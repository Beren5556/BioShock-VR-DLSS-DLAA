// Include the actual implementation to seed test-owned transport resources.
// Never call prepare(): no host launch, runtime staging or personal file access.
#include "core/gfx/dlss45_client.cpp"
#include "core/vr/deferred_eye_lease.h"
#include <d3d11sdklayers.h>
#include <cstdlib>
#include <thread>

namespace bvr::log {
const wchar_t* data_dir() { return L""; } // no personal profile fallback in tests
void write(const char* fmt, ...) {
    va_list args; va_start(args,fmt); std::vprintf(fmt,args); va_end(args); std::puts("");
}
}
namespace bvr::game { HostGame detect_host_game() { return HostGame::Bioshock1; } }
namespace {
// These transport fixtures exercise the shared path, never a running game.
unsigned checks=0, failures=0;
void expect(bool ok,const char* text) {
    ++checks; if(!ok) ++failures; std::printf("%s: %s\n",ok?"PASS":"FAIL",text);
}
void require(bool ok,const char* text) { expect(ok,text); if(!ok) std::exit(2); }
void bounded_wait_tests(ID3D11DeviceContext4* context) {
    using namespace bvr::dlss45;
    EyeState probe{};
    require(SUCCEEDED(g_device5->CreateFence(0, D3D11_FENCE_FLAG_NONE, IID_PPV_ARGS(&probe.fenceOut))),
            "stereo regression fixture has a private output fence");
    probe.process = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    probe.completionEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    require(probe.process && probe.completionEvent, "stereo regression uses test events, not helper processes");
    bool guardedAfterOldLimit = false;
    std::thread worker([&] {
        Sleep(1400);
        guardedAfterOldLimit = bounded_wait_active();
        context->Signal(probe.fenceOut, 1); context->Flush();
    });
    const bool completed = wait_for_output(probe, 1);
    worker.join();
    expect(completed && guardedAfterOldLimit, "actual 1.4-second fence wait is visible as bounded activity to watchdog");
    expect(!bounded_wait_active(), "successful real fence wait clears watchdog protection immediately");
    SetEvent(probe.process);
    expect(!wait_for_output(probe, 2) && !bounded_wait_active(),
           "dead-helper exit preserves failure and does not leave watchdog suppressed");
    context->Signal(probe.fenceOut, 2); context->Flush();
    require(WaitForSingleObject(probe.completionEvent, 1000) == WAIT_OBJECT_0,
            "retire fixture's outstanding event registration");
    release_one(probe.fenceOut);
    CloseHandle(probe.process); CloseHandle(probe.completionEvent);
}
#ifdef BVR_CRITICAL_PATH_PROBE
void input_event_tests(ID3D11DeviceContext4* ctx4) {
    using namespace bvr::dlss45;
    EyeState probe{}; probe.index=0;
    require(SUCCEEDED(g_device5->CreateFence(0,D3D11_FENCE_FLAG_NONE,IID_PPV_ARGS(&probe.fenceIn))) &&
            SUCCEEDED(g_device5->CreateFence(0,D3D11_FENCE_FLAG_NONE,IID_PPV_ARGS(&probe.fenceOut))),
            "sparse wait fixture has private WARP fences");
    probe.process=CreateEventW(nullptr,TRUE,FALSE,nullptr);
    probe.completionEvent=CreateEventW(nullptr,TRUE,FALSE,nullptr);
    probe.inputProbeEvent=CreateEventW(nullptr,FALSE,FALSE,nullptr);
    require(probe.process && probe.completionEvent && probe.inputProbeEvent,"sparse wait fixture has isolated events");
    // The worker alone uses this immediate context while the caller waits on
    // thread-safe fence/event objects. No shared context calls overlap.
    g_inputWaitCosts[0]={};
    std::thread worker([&] {
        Sleep(20); ctx4->Signal(probe.fenceIn,16); ctx4->Flush();
        Sleep(40); ctx4->Signal(probe.fenceOut,16); ctx4->Flush();
    });
    const bool resolved=wait_for_output(probe,16,true); worker.join();
    const auto separated=g_inputWaitCosts[0];
    expect(resolved && separated.sampled==1 && separated.inputPending==1 &&
           separated.split+separated.coalesced==1 && !separated.unsuccessful,
           "actual sparse input/output event path completes (late scheduler wake may coalesce)");
    expect(!separated.split || (separated.beforeInputMs>=0 && separated.afterInputMs>=0 &&
           std::abs(separated.sampledTotalMs-separated.beforeInputMs-separated.afterInputMs)<0.001),
           "observed sparse partition conserves elapsed output wait");
    g_inputWaitCosts[0]={};
    expect(wait_for_output(probe,16,true) && g_inputWaitCosts[0].outputReady==1 &&
           g_inputWaitCosts[0].inputReady==1,"already-complete WARP frame needs no additional wake");

    const HANDLE optional=probe.inputProbeEvent; probe.inputProbeEvent=nullptr;
    g_inputWaitCosts[0]={};
    std::thread noEvent([&] {
        Sleep(20); ctx4->Signal(probe.fenceIn,32); ctx4->Signal(probe.fenceOut,32); ctx4->Flush();
    });
    const bool withoutEvent=wait_for_output(probe,32,true); noEvent.join();
    expect(withoutEvent && g_inputWaitCosts[0].registrationFailed==1 &&
           g_inputWaitCosts[0].unobserved==1,"missing optional input event preserves actual output completion");
    probe.inputProbeEvent=optional;

    SetEvent(probe.process); g_inputWaitCosts[0]={};
    const auto before=GetTickCount64();
    expect(!wait_for_output(probe,48,true) && GetTickCount64()-before<1000 &&
           g_inputWaitCosts[0].unsuccessful==1,"sampled wait preserves immediate dead-helper escape");
    // Retire the private optional registration before releasing its event.
    ctx4->Signal(probe.fenceIn,48); ctx4->Signal(probe.fenceOut,48); ctx4->Flush();
    ResetEvent(probe.completionEvent);
    require(SUCCEEDED(probe.fenceOut->SetEventOnCompletion(48,probe.completionEvent)) &&
            WaitForSingleObject(probe.completionEvent,1000)==WAIT_OBJECT_0,
            "private fixture retires all registered fence values");
    expect(wait_for_output(probe,48),"output retains priority once current private output is complete");
    release_one(probe.fenceIn); release_one(probe.fenceOut);
    CloseHandle(probe.process); CloseHandle(probe.completionEvent); CloseHandle(probe.inputProbeEvent);
    g_inputWaitCosts[0]={};
}
#endif
void lease_tests() {
    bvr::DeferredEyeLease<unsigned,unsigned> lease;
    expect(!lease.arm(0,2,1)&&!lease.arm(1,0,1)&&!lease.arm(1,2,0)&&
           !lease.arm(1,2,UINT64_MAX),"invalid leases rejected");
    expect(lease.arm(7,8,11)&&!lease.arm(7,8,11)&&lease.sibling(12)&&
           !lease.sibling(11)&&!lease.sibling(13),"exact sibling and single-owner lease");
    std::string order;
    auto complete=[&](bool publish,unsigned image){order+=publish?'C':'D'; return image==8;};
    auto restore=[&](unsigned image){order+='F'; return image==8;};
    auto release=[&](unsigned chain){order+='R'; return chain==7;};
    expect(lease.close(true,complete,restore,release)&&order=="CR"&&!lease.held(),
           "complete matching output before the only release");
    expect(!lease.close(true,complete,restore,release)&&order=="CR","no double release");
    lease.arm(7,8,21); order.clear();
    expect(!lease.close(false,complete,restore,release)&&order=="DFR",
           "abort drains input, restores original eye, then releases");
    lease.arm(7,8,31); order.clear();
    expect(!lease.close(true,[&](bool,unsigned){order+='X';return false;},restore,release)&&order=="XFR",
           "failed completion restores before release");
    lease.arm(7,8,41); order.clear();
    expect(!lease.close(false,complete,[&](unsigned){order+='X';return false;},release)&&order=="DXR",
           "failed restoration still closes the XR lease exactly once");
    lease.arm(7,8,51); order.clear();
    expect(!lease.close(true,complete,restore,[&](unsigned){order+='X';return false;})&&order=="CX",
           "release failure cannot be reported as success");
    lease.arm(7,8,61); order.clear();
    expect(lease.close(true,[&](bool,unsigned){
        return !lease.held()&&!lease.close(false,complete,restore,release);
    },restore,release)&&order=="R","reentrant teardown cannot release twice");
}
}

int main() {
    lease_tests();
    using namespace bvr::dlss45;
    ID3D11Device* device=nullptr; ID3D11DeviceContext* context=nullptr;
    UINT flags=D3D11_CREATE_DEVICE_DEBUG;
    HRESULT hr=D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,flags,nullptr,0,
        D3D11_SDK_VERSION,&device,nullptr,&context);
    if(FAILED(hr)) {flags=0;hr=D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,0,
        nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&context);}
    require(SUCCEEDED(hr),"WARP device created without game or NVIDIA");
    ID3D11DeviceContext4* ctx4=nullptr;
    require(SUCCEEDED(context->QueryInterface(IID_PPV_ARGS(&ctx4))),"real D3D11 context4 fences");
    g_device=device; device->AddRef();
    require(SUCCEEDED(device->QueryInterface(IID_PPV_ARGS(&g_device5))),"real device5");
    bounded_wait_tests(ctx4);
#ifdef BVR_CRITICAL_PATH_PROBE
    input_event_tests(ctx4);
#endif
    g_renderWidth=g_renderHeight=g_outputWidth=g_outputHeight=16;
    g_backbufferFormat=g_colorFormat=g_outputFormat=DXGI_FORMAT_R8G8B8A8_UNORM;
    g_mode=Mode::Dlaa;
    auto texture=[&](DXGI_FORMAT format,bool staging=false) {
        D3D11_TEXTURE2D_DESC d{};d.Width=d.Height=16;
        d.MipLevels=d.ArraySize=d.SampleDesc.Count=1;d.Format=format;
        d.Usage=staging?D3D11_USAGE_STAGING:D3D11_USAGE_DEFAULT;
        d.CPUAccessFlags=staging?D3D11_CPU_ACCESS_READ:0;
        ID3D11Texture2D* result=nullptr;
        if(FAILED(device->CreateTexture2D(&d,nullptr,&result))) std::exit(2);
        return result;
    };
    ID3D11Texture2D *color=texture(g_colorFormat), *depth=texture(DXGI_FORMAT_R32_FLOAT),
        *motion=texture(DXGI_FORMAT_R16G16_FLOAT),*destination[2]={texture(g_outputFormat),texture(g_outputFormat)},
        *readback=texture(g_outputFormat,true);
    auto fill=[&](ID3D11Texture2D* tex,UINT value) {
        UINT pixels[256]; for(auto& pixel:pixels) pixel=value;
        context->UpdateSubresource(tex,0,nullptr,pixels,64,0);
    };
    auto pixel=[&](ID3D11Texture2D* tex) {
        context->CopyResource(readback,tex); D3D11_MAPPED_SUBRESOURCE mapped{};
        if(FAILED(context->Map(readback,0,D3D11_MAP_READ,0,&mapped))) std::exit(2);
        UINT value=*static_cast<UINT*>(mapped.pData);context->Unmap(readback,0);return value;
    };
    HANDLE servers[2]{};
    for(int i=0;i<2;++i) {
        auto& eye=g_eyes[i];eye.index=i;
        eye.textures[FeedColor]=texture(g_colorFormat);eye.textures[FeedOutput]=texture(g_outputFormat);
        eye.textures[FeedDepth]=texture(DXGI_FORMAT_R32_FLOAT);eye.textures[FeedMotion]=texture(DXGI_FORMAT_R16G16_FLOAT);
        require(SUCCEEDED(g_device5->CreateFence(0,D3D11_FENCE_FLAG_NONE,IID_PPV_ARGS(&eye.fenceIn)))&&
                SUCCEEDED(g_device5->CreateFence(0,D3D11_FENCE_FLAG_NONE,IID_PPV_ARGS(&eye.fenceOut))),
                "independent input/output fences per eye");
        // A test-owned event models helper liveness; it is never a real process handle.
        eye.process=CreateEventW(nullptr,TRUE,FALSE,nullptr);
        eye.pipeEvent=CreateEventW(nullptr,TRUE,FALSE,nullptr);
        eye.completionEvent=CreateEventW(nullptr,TRUE,FALSE,nullptr);
        wchar_t name[128];swprintf_s(name,L"\\\\.\\pipe\\bvr-overlap-test-%lu-%d",GetCurrentProcessId(),i);
        servers[i]=CreateNamedPipeW(name,PIPE_ACCESS_INBOUND,PIPE_TYPE_BYTE|PIPE_WAIT,1,4096,4096,0,nullptr);
        eye.pipe=CreateFileW(name,GENERIC_WRITE,0,nullptr,OPEN_EXISTING,FILE_FLAG_OVERLAPPED,nullptr);
        require(servers[i]!=INVALID_HANDLE_VALUE&&eye.pipe!=INVALID_HANDLE_VALUE,"local isolated IPC pipe");
        const BOOL connected=ConnectNamedPipe(servers[i],nullptr);
        require(connected||GetLastError()==ERROR_PIPE_CONNECTED,"test pipe connected");
        fill(destination[i],0);fill(eye.textures[FeedOutput],i?0xFF222222u:0xFF111111u);
    }
    g_isReady=true;g_faulted=false;
    auto submit=[&](int eye){return submit_eye(context,eye,destination[eye],color,depth,motion,false,0,0);};
    auto message=[&](int eye,UINT64 value) {
        BYTE bytes[1+sizeof(FeedFrame)]{};DWORD count=0;
        const BOOL read=ReadFile(servers[eye],bytes,sizeof(bytes),&count,nullptr);
        FeedFrame frame{};std::memcpy(&frame,bytes+1,sizeof(frame));
        return read&&count==sizeof(bytes)&&bytes[0]=='F'&&frame.fenceValue==value&&
            frame.reset==(value==1?1u:0u)&&frame.jitterX==0&&frame.jitterY==0;
    };
    fill(color,0xFFABCDEFu);
    expect(!retain_submitted_color(0,destination[0]) && !retain_submitted_color(-1,destination[0]) &&
           !retain_submitted_color(2,destination[0]) && !retain_submitted_color(0,nullptr),
           "raw backup requires a valid pending eye and exact destination");
    expect(submit(0),"left submit returns without any output fence signal");
    auto* leftRaw = retain_submitted_color(0,destination[0]);
    expect(leftRaw == g_eyes[0].textures[FeedColor] &&
           !retain_submitted_color(0,destination[1]),
           "backup retains the exact submitted color without making another texture/copy");
    expect(message(0,1),"unchanged v8 frame/reset/jitter message");
    expect(pixel(destination[0])==0,"submit never publishes incomplete left output");
    expect(pixel(g_eyes[0].textures[FeedColor])==0xFFABCDEFu&&g_eyes[0].fenceIn->GetCompletedValue()>=1,
           "submitted inputs copied and fenced");
    fill(color,0xFF123456u); // Stand-in for the subsequent RIGHT scene.
    expect(submit(1)&&message(1,1),"right can submit while left remains pending");
    expect(pixel(g_eyes[0].textures[FeedColor])==0xFFABCDEFu&&pixel(g_eyes[1].textures[FeedColor])==0xFF123456u,
           "right scene cannot overwrite left inputs");
    expect(leftRaw && pixel(leftRaw)==0xFFABCDEFu,"retained left backup is unchanged by the right scene");
    ctx4->Signal(g_eyes[1].fenceOut,1);context->Flush();
    expect(resolve_eye(context,1,destination[1])&&pixel(destination[1])==0xFF222222u,
           "right resolves independently before left");
    expect(pixel(destination[0])==0&&g_eyes[0].pendingDestination==destination[0],
           "left destination still leased and unpublished");
    ctx4->Signal(g_eyes[0].fenceOut,1);context->Flush();
    expect(!gpu_retired(),"completed GPU fence alone cannot retire unpublished destination");
    expect(resolve_eye(context,0,destination[0])&&pixel(destination[0])==0xFF111111u&&gpu_retired(),
           "matching left resolves and both eyes retire");
    expect(g_cpuCosts[0].count==1&&g_cpuCosts[1].count==1,"one completion sample per eye");
    expect(!retain_submitted_color(0,destination[0]),"completed destination cannot obtain a stale snapshot");
    release_one(leftRaw);

    auto* auxiliary = texture(g_colorFormat);
    unsigned tailCalls=0; bool tailOrdered=false;
    fill(g_eyes[1].textures[FeedOutput],0xFF444444u);
    auto tail = [&] {
        ++tailCalls;
        tailOrdered = g_eyes[1].pendingDestination==destination[1] &&
            g_eyes[1].fenceOut->GetCompletedValue()<2 && pixel(destination[1])==0xFF222222u;
        fill(auxiliary,0xFF102030u); // Independent work, no eye/backbuffer mutation.
        ctx4->Signal(g_eyes[1].fenceOut,2); context->Flush();
    };
    using Tail = decltype(tail);
    expect(process_eye(context,1,destination[1],color,depth,motion,false,0,0,
            {[](void* p){(*static_cast<Tail*>(p))();},&tail}) && message(1,2),
           "right wrapper accepts work between submit and resolve with unchanged wire message");
    expect(tailCalls==1 && tailOrdered && pixel(auxiliary)==0xFF102030u &&
           pixel(destination[1])==0xFF444444u && gpu_retired(),
           "tail runs once before output exists; only the completed current eye is then published");
    release_one(auxiliary);

    fill(g_eyes[0].textures[FeedOutput],0xFF333333u);
    expect(submit(0)&&message(0,2),"next frame reuses input only after previous resolve");
    ctx4->Signal(g_eyes[0].fenceOut,2);context->Flush();
    expect(discard_pending()&&pixel(destination[0])==0xFF111111u&&!g_eyes[0].pendingDestination,
           "discard waits for input ownership but never copies unwanted output");
    ctx4->Signal(g_eyes[0].fenceOut,3);context->Flush();
    expect(process_eye(context,0,destination[0],color,depth,motion,false,0,0)&&message(0,3)&&
           pixel(destination[0])==0xFF333333u,"original synchronous wrapper still works");
    expect(submit(0)&&message(0,4),"pending frame prepared for reuse rejection");
    auto* faultBackup = retain_submitted_color(0,destination[0]);
    require(faultBackup!=nullptr,"retain raw frame before simulated host failure");
    const auto rawBeforeFailure=pixel(faultBackup);
    for(auto& eye:g_eyes) SetEvent(eye.process); // No real process can ever be terminated by failure tests.
    expect(!submit(0)&&!ready(),"reject pending input reuse and fail soft");
    unsigned unexpectedWork=0;
    expect(!process_eye(context,1,destination[1],color,depth,motion,false,0,0,
            {[](void* p){++*static_cast<unsigned*>(p);},&unexpectedWork}) && !unexpectedWork,
           "failed submit never runs auxiliary work");
    expect(!discard_pending()&&!g_eyes[0].pendingDestination,"faulted cleanup drops logical ownership");
    g_isReady=true;g_faulted=false;g_eyes[0].pendingDestination=destination[0];
    const auto start=GetTickCount64();
    expect(!resolve_eye(context,0,destination[0])&&GetTickCount64()-start<1000,
           "dead helper exits bounded CPU wait without enqueueing an unsatisfied GPU wait");
    // Map must still complete: a mistaken GPU Wait on fence value 4 would hang this test.
    expect(pixel(destination[0])==0xFF333333u,"GPU queue remains usable after helper failure");
    discard_pending();release();
    expect(pixel(faultBackup)==rawBeforeFailure,
           "retained current input survives bridge resource release for exact fallback");
    release_one(faultBackup);
    for(auto handle:servers) CloseHandle(handle);
    ID3D11InfoQueue* queue=nullptr;
    if(SUCCEEDED(device->QueryInterface(IID_PPV_ARGS(&queue)))) {
        unsigned errors=0;
        for(UINT64 i=0;i<queue->GetNumStoredMessagesAllowedByRetrievalFilter();++i) {
            SIZE_T size=0;queue->GetMessage(i,nullptr,&size);std::vector<BYTE> bytes(size);
            auto* m=reinterpret_cast<D3D11_MESSAGE*>(bytes.data());queue->GetMessage(i,m,&size);
            if(m->Severity<=D3D11_MESSAGE_SEVERITY_WARNING) {++errors;std::printf("D3D: %s\n",m->pDescription);}
        }
        expect(errors==0,"D3D11 debug: no warnings, errors or corruption");release_one(queue);
    }
    release_one(color);release_one(depth);release_one(motion);release_one(readback);
    for(auto& dst:destination) release_one(dst);
    release_one(ctx4);release_one(context);release_one(device);
    std::printf("Overlap checks: %u/%u passed; debug=%u\n",checks-failures,checks,flags);
    return failures?1:0;
}
