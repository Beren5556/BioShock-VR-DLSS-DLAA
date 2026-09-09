// Real client, optional real GPU/NGX helpers. Never launches a game or OpenXR.
#include "core/gfx/dlss45_client.cpp"
#include "core/vr/frame_submission_stats.h"
#include <d3d11sdklayers.h>
#include <stdexcept>

namespace bvr::log {
void write(const char* format, ...) {
    va_list args; va_start(args,format); std::vprintf(format,args); va_end(args); std::puts("");
}
}
namespace {
unsigned checks = 0, failures = 0;
void expect(bool ok, const char* label) {
    ++checks; if (!ok) ++failures;
    std::printf("%s: %s\n",ok ? "PASS" : "FAIL", label);
}
void require(bool ok, const char* label) {
    expect(ok,label); if (!ok) throw std::runtime_error(label);
}
void policy_tests() {
    using namespace bvr_latency_probe;
    expect(valid_copy(3072,3072,3072,3072,28,28),"native full-image transport accepted");
    expect(!valid_copy(0,3072,0,3072,28,28),"empty image rejected");
    expect(!valid_copy(2150,2150,3072,3072,28,28),"SR cannot enter copy-only transport");
    expect(!valid_copy(3072,3072,3072,3072,28,10),"incompatible formats rejected");
    expect(!matching_ack(true,0),"legacy half-image helper cannot acknowledge this probe");
    expect(matching_ack(true,kFullImageAck),"new full-image acknowledgement accepted");
    expect(!matching_ack(false,kFullImageAck),"copy-only output cannot masquerade as DLAA");
    expect(matching_ack(false,0),"ordinary DLAA contract unchanged");
    using namespace bvr::submission_probe;
    const auto s=sample(1000,1007,1008,1014,1000,13888889,true);
    expect(s.valid&&s.predictionValid&&s.afterWaitMs==7&&s.endCallMs==1,"CPU intervals measured separately");
    expect(s.waitLeadMs==14&&s.submitLeadMs==7,"prediction uses converted QPC origin");
    expect(s.periodMs>13.88&&s.periodMs<13.90,"72 Hz period retains fractional milliseconds");
    expect(!sample(1000,1007,1008,1014,0,1,true).valid,"unavailable QPC frequency is not a measurement");
    expect(!sample(1007,1000,1008,1014,1000,1,true).valid,"stale/out-of-order frame rejected");
    auto absent=sample(1000,1007,1008,0,1000,0,false);
    expect(absent.valid&&!absent.predictionValid&&absent.periodMs==0,"absent runtime conversion stays unavailable");
    Window w; w.add(s); w.add(absent); w.add(sample(1000,1015,1016,1014,1000,13888889,true));
    expect(w.count==3&&w.leadSamples==2&&w.pastPrediction==1&&w.minLead==-1,
           "CPU past-prediction count excludes unknown samples, not mislabeled dropped frames");
    w={}; expect(w.count==0&&w.leadSamples==0,"phase windows can be reset without mixing settings");
}
void integration(const wchar_t* host, UINT width = 1024) {
    using namespace bvr::dlss45;
    wchar_t temp[MAX_PATH]{}, fixture[MAX_PATH]{};
    require(GetTempPathW(MAX_PATH,temp)>0,"private fixture base available");
    swprintf_s(fixture,L"%sBVR-Latency1-Test-%lu-%llu",temp,GetCurrentProcessId(),GetTickCount64());
    require(CreateDirectoryW(fixture,nullptr)!=FALSE,"unique private fixture created");
    require(SetEnvironmentVariableW(L"LOCALAPPDATA",fixture)!=FALSE,"helpers inherit private appdata, not user profile");
    std::printf("Private fixture retained: %ls\n",fixture);
    ID3D11Device* device=nullptr; ID3D11DeviceContext* context=nullptr;
    UINT flags=D3D11_CREATE_DEVICE_DEBUG;
    HRESULT hr=D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,flags,nullptr,0,
        D3D11_SDK_VERSION,&device,nullptr,&context);
    if(FAILED(hr)){ flags=0;hr=D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,0,nullptr,0,
        D3D11_SDK_VERSION,&device,nullptr,&context); }
    require(SUCCEEDED(hr),"real D3D11 GPU device created without a game");
    require(width>=256 && width<=3072 && width%8==0,"bounded native test size");
    const UINT W=width;
    auto texture=[&](DXGI_FORMAT fmt,UINT width,bool staging=false) {
        D3D11_TEXTURE2D_DESC d{}; d.Width=d.Height=width;d.MipLevels=d.ArraySize=d.SampleDesc.Count=1;
        d.Format=fmt;d.Usage=staging?D3D11_USAGE_STAGING:D3D11_USAGE_DEFAULT;
        d.CPUAccessFlags=staging?D3D11_CPU_ACCESS_READ:0;
        ID3D11Texture2D* tex=nullptr;
        require(SUCCEEDED(device->CreateTexture2D(&d,nullptr,&tex)),"test texture created");return tex;
    };
    auto* color=texture(DXGI_FORMAT_R8G8B8A8_UNORM,W);
    auto* depth=texture(DXGI_FORMAT_R32_FLOAT,W);
    auto* motion=texture(DXGI_FORMAT_R16G16_FLOAT,W);
    auto* rawReadback=texture(DXGI_FORMAT_R8G8B8A8_UNORM,W,true);
    auto* auxiliary=texture(DXGI_FORMAT_R8G8B8A8_UNORM,W);
    std::vector<UINT> pixels(W*W,0x3f000000u);
    context->UpdateSubresource(depth,0,nullptr,pixels.data(),W*4,0);
    std::fill(pixels.begin(),pixels.end(),0);context->UpdateSubresource(motion,0,nullptr,pixels.data(),W*4,0);
    for (int variant=0;variant<3;++variant) {
        const bool bridge=variant==0, sr=variant==2; const UINT out=sr?W*3/2:W;
        ID3D11Texture2D* dst[2]={texture(DXGI_FORMAT_R8G8B8A8_UNORM,out),texture(DXGI_FORMAT_R8G8B8A8_UNORM,out)};
        auto* readback=texture(DXGI_FORMAT_R8G8B8A8_UNORM,out,true);
        require(prepare(device,W,W,DXGI_FORMAT_R8G8B8A8_UNORM,out,out,
                sr?Mode::SuperResolution:Mode::Dlaa,false,host,bridge),
                bridge?"two real bridge-only hosts acknowledged":sr?"two real DLSS hosts prepared":"two real DLAA hosts prepared");
        for(UINT frame=0;frame<8;++frame) {
            const UINT colors[2]={0xff2030d0u+frame,0xffd03020u+(frame<<16)};
            std::fill(pixels.begin(),pixels.end(),colors[0]);
            context->UpdateSubresource(color,0,nullptr,pixels.data(),W*4,0);
            require(submit_eye(context,0,dst[0],color,depth,motion,frame==0,0,0),"left inputs submitted");
            auto* rawLeft=retain_submitted_color(0,dst[0]);
            require(rawLeft!=nullptr,"real shared input retained without duplicate snapshot");
            std::fill(pixels.begin(),pixels.end(),colors[1]);
            context->UpdateSubresource(color,0,nullptr,pixels.data(),W*4,0);
            unsigned workCalls=0;
            auto tail=[&] {
                ++workCalls;
                // Stand-in for independent HUD preparation, never a game/VR call.
                context->CopyResource(auxiliary,color);
                require(g_eyes[1].pendingDestination==dst[1] && !gpu_retired(),
                        "tail work runs with an unpublished right-eye result");
            };
            using Tail=decltype(tail);
            require(process_eye(context,1,dst[1],color,depth,motion,frame==0,0,0,
                    {[](void* p){(*static_cast<Tail*>(p))();},&tail}) && workCalls==1 &&
                    resolve_eye(context,0,dst[0])&&gpu_retired(),
                    "both real hosts complete current pair in overlap order");
            context->CopyResource(rawReadback,rawLeft);
            D3D11_MAPPED_SUBRESOURCE rawMapped{};
            require(SUCCEEDED(context->Map(rawReadback,0,D3D11_MAP_READ,0,&rawMapped)),"retained raw color readback");
            bool exactRaw=true;
            for(UINT y: {0u,W/2,W-1})for(UINT x: {0u,W/2,W-1})
                exactRaw &= reinterpret_cast<UINT*>(static_cast<BYTE*>(rawMapped.pData)+y*rawMapped.RowPitch)[x]==colors[0];
            context->Unmap(rawReadback,0);
            require(exactRaw,"current left raw backup survives right input and real NVIDIA evaluation");
            release_one(rawLeft);
            for(int eye=0;eye<2;++eye){
                context->CopyResource(readback,dst[eye]); D3D11_MAPPED_SUBRESOURCE mapped{};
                require(SUCCEEDED(context->Map(readback,0,D3D11_MAP_READ,0,&mapped)),"readback after real host completion");
                bool ok=true;
                for(UINT y: {0u,out/2,out-1})for(UINT x: {0u,out/2,out-1}){
                    const UINT p=reinterpret_cast<UINT*>(static_cast<BYTE*>(mapped.pData)+y*mapped.RowPitch)[x];
                    const UINT red=p&255u, blue=(p>>16)&255u;
                    ok &= bridge?p==colors[eye]:(eye==0?red>blue:blue>red);
                }
                context->Unmap(readback,0);
                require(ok,bridge?"full image including right edge is exact current eye/frame":"NGX output preserves independent eye colors");
            }
        }
        log_performance(); release();
        for(auto*& t:dst)release_one(t);release_one(readback);
    }
    ID3D11InfoQueue* queue=nullptr;
    if(SUCCEEDED(device->QueryInterface(IID_PPV_ARGS(&queue)))) {
        unsigned errors=0;
        for(UINT64 i=0;i<queue->GetNumStoredMessagesAllowedByRetrievalFilter();++i){
            SIZE_T bytes=0;queue->GetMessage(i,nullptr,&bytes);std::vector<BYTE> data(bytes);
            auto* message=reinterpret_cast<D3D11_MESSAGE*>(data.data());queue->GetMessage(i,message,&bytes);
            if(message->Severity<=D3D11_MESSAGE_SEVERITY_WARNING){++errors;std::printf("D3D: %s\n",message->pDescription);}
        }
        expect(!errors,"D3D11 debug has no warning, error or corruption");release_one(queue);
    }
    release_one(rawReadback);release_one(auxiliary);
    release_one(color);release_one(depth);release_one(motion);release_one(context);release_one(device);
}
}
int wmain(int argc,wchar_t** argv) {
    try {policy_tests();if(argc>=2)integration(argv[1],argc>=3?static_cast<UINT>(_wtoi(argv[2])):1024);}
    catch(const std::exception& e){std::printf("FAILED: %s\n",e.what());bvr::dlss45::release();return 1;}
    std::printf("Latency probe checks: %u/%u passed; game/OpenXR not launched.\n",checks-failures,checks);
    return failures?1:0;
}
