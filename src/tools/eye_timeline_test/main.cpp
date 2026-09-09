#include "core/gfx/eye_timeline.h"
#include "game/bioshock1r/performance_probe.h"
#include "game/bioshock1r/camera.h"
#include <d3d11sdklayers.h>
#include <cstdarg>
#include <cstdio>
#include <cmath>
#include <string>
#include <thread>
#include <vector>

namespace {
unsigned checks = 0, failures = 0;
std::vector<std::string> lines;
void expect(bool ok, const char* message) {
    ++checks; if (!ok) ++failures;
    std::printf("%s: %s\n", ok ? "PASS" : "FAIL", message);
}
template<class T> void drop(T*& p) { if (p) p->Release(); p = nullptr; }
}
namespace bvr::log {
void write(const char* fmt, ...) {
    char text[2048]; va_list args; va_start(args, fmt);
    vsnprintf_s(text, _TRUNCATE, fmt, args); va_end(args); lines.emplace_back(text);
}
}
namespace bvr::b1r::camera {
bool driven_eye_cam_for_build(int eye, uint64_t build, DrivenEyeCamera* out) {
    if (!out || eye < 0 || eye > 1 || !build) return false;
    *out = {}; out->buildId=build; out->rotation[1]=eye ? 2000 : 1000;
    out->location[0]=100; out->publications=1; return true;
}
}
int main() {
    ID3D11Device* device=nullptr; ID3D11DeviceContext* context=nullptr;
    UINT flags=D3D11_CREATE_DEVICE_DEBUG;
    HRESULT hr=D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,flags,nullptr,0,
                                 D3D11_SDK_VERSION,&device,nullptr,&context);
    if (FAILED(hr)) { flags=0; hr=D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,0,nullptr,0,
                                 D3D11_SDK_VERSION,&device,nullptr,&context); }
    if (FAILED(hr)) return 2;
    std::printf("WARP debug=%u; no game, host, NVIDIA, headset or personal INI access.\n",flags);
    ID3D11Texture2D *input=nullptr,*output=nullptr;
    D3D11_TEXTURE2D_DESC desc{}; desc.Width=desc.Height=128;
    desc.MipLevels=desc.ArraySize=desc.SampleDesc.Count=1;
    desc.Format=DXGI_FORMAT_R8G8B8A8_UNORM;
    if (FAILED(device->CreateTexture2D(&desc,nullptr,&input)) ||
        FAILED(device->CreateTexture2D(&desc,nullptr,&output))) return 2;
    bvr::EyeTimeline timer;
    using T=bvr::EyeTimeline;
    expect(timer.prepare(device,1),"prepare one sparse timeline ring");
    expect(!timer.begin(-1,1)&&!timer.begin(2,1)&&!timer.begin(0,0),"reject invalid eye/build");
    bool foreign=false;
    std::thread other([&]{foreign=timer.begin(0,2);}); other.join();
    expect(!foreign,"foreign thread never issues immediate-context queries");
    std::vector<T::Result> results;
    auto receive=[&](const T::Result& result){results.push_back(result);};
    auto drain=[&] {
        context->Flush(); // Explicit waiting is confined to this standalone test.
        const auto deadline=GetTickCount64()+3000;
        const auto before=results.size();
        do { timer.poll(receive); if (results.size()>before) break; Sleep(1); }
        while (GetTickCount64()<deadline);
    };
    expect(timer.begin(0,11),"begin left eye");
    expect(!timer.begin(1,12)&&timer.matches(0,11),"nested begin cannot steal a pending eye");
    context->CopyResource(output,input); timer.mark(T::SceneEnd);
    Sleep(3); // Deliberately put pacing OUTSIDE the scene interval.
    timer.mark(T::CaptureStart);
    context->CopyResource(output,input); timer.mark(T::GuidesEnd);
    context->CopyResource(output,input); timer.finish(2); drain();
    expect(results.size()==1&&results[0].eye==0&&results[0].build==11&&results[0].outcome==2,
           "results preserve exact eye, build and capture outcome");
    bool valid=results.size()==1;
    if (valid) for (int i=0;i<4;++i) valid&=std::isfinite(results[0].gpuMs[i])&&results[0].gpuMs[i]>=0;
    expect(valid,"four ordered GPU elapsed intervals are valid");
    expect(!results.empty()&&results[0].cpuMs[1]>=2.0,"pacing is separated into pre-capture, not scene or output");
    expect(timer.begin(1,12),"begin right eye"); timer.mark(T::CaptureStart);
    expect(!timer.active()&&timer.dropped()==1,"out-of-order stages cancel without inventing a sample");
    expect(timer.begin(1,13),"fresh eye after cancellation"); timer.mark(T::SceneEnd); timer.cancel();
    expect(!timer.active()&&timer.dropped()==2,"missing capture cancels without waiting");
    timer.release(); expect(!timer.ready()&&!timer.active(),"release closes incomplete query and drops old-phase data");
    expect(timer.prepare(device,1),"reprepare after cancelled frames");
    for (int frame=1;frame<=8;++frame) {
        expect(timer.begin(frame%2,frame),"available bounded ring slot");
        timer.mark(T::SceneEnd); timer.mark(T::CaptureStart); timer.mark(T::GuidesEnd); timer.finish(1);
    }
    const auto dropped=timer.dropped();
    expect(!timer.begin(0,100)&&timer.dropped()==dropped+1,"full ring drops measurements without polling/spinning");
    drain(); timer.release();

    namespace P=bvr::b1r::performance_probe;
    bvr::image_controls::Settings settings{};
    settings.renderWidth=settings.renderHeight=settings.outputWidth=settings.outputHeight=2560;
    for (int phase=1;phase<=4;++phase) {
        settings.probe=static_cast<bvr::image_controls::Probe>(phase);
        settings.mode=phase>=3 ? bvr::image_controls::RenderMode::Dlaa : bvr::image_controls::RenderMode::Normal;
        P::configure(device,settings);
        expect(bvr::diagnostic_timeline_owns_queries,"probe suspends the older disjoint samplers");
        bvr::SampledGpuTimer legacy; legacy.prepare(device,1);
        legacy.begin(context); legacy.end(context);
        context->Flush(); legacy.poll(context);
        expect(legacy.samples()==0,"legacy timer cannot nest queries while the probe owns them");
        legacy.release();
        for (uint64_t pair=1;pair<=48;++pair) for (int eye=0;eye<2;++eye) {
            const auto id=pair*2+eye;
            P::scene_begin(eye,id); context->CopyResource(output,input); P::scene_end();
            P::configure(device,settings); // Present-tail's unchanged configuration must not cancel the scene.
            P::capture_begin(eye,id); context->CopyResource(output,input); P::guides_end();
            context->CopyResource(output,input); P::capture_end(phase==1?1:phase==2?3:eye==0?4:2); P::build_end(eye,id);
            if (pair%16==0) { context->Flush(); Sleep(5); }
        }
        context->Flush(); Sleep(20); P::shutdown();
        bool left=false,right=false;
        const auto key="[scene-probe] phase="+std::to_string(phase);
        for (const auto& line:lines) if (line.find(key)!=std::string::npos && line.find("samples=3")!=std::string::npos) {
            left|=line.find("eye=0")!=std::string::npos; right|=line.find("eye=1")!=std::string::npos;
        }
        expect(left&&right,"real wrapper reports both eyes in the correct diagnostic phase");
        if (phase>=3) {
            bool deferred=false;
            for (const auto& line:lines) if (line.find(key)!=std::string::npos &&
                line.find("eye=0")!=std::string::npos && line.find("deferred=3")!=std::string::npos &&
                line.find("fallback=0")!=std::string::npos) deferred=true;
            expect(deferred,"deferred left output is labelled, not misreported as fallback or complete");
        }
        expect(!bvr::diagnostic_timeline_owns_queries,"shutdown restores old sampler ownership");
    }
    settings.probe=bvr::image_controls::Probe::Off; P::configure(device,settings);
    expect(!bvr::diagnostic_timeline_owns_queries,"OFF disables the new timeline"); P::shutdown();
    ID3D11InfoQueue* queue=nullptr;
    if (SUCCEEDED(device->QueryInterface(IID_PPV_ARGS(&queue)))) {
        unsigned errors=0;
        for (UINT64 i=0;i<queue->GetNumStoredMessagesAllowedByRetrievalFilter();++i) {
            SIZE_T size=0; queue->GetMessage(i,nullptr,&size); std::vector<unsigned char> bytes(size);
            auto* message=reinterpret_cast<D3D11_MESSAGE*>(bytes.data()); queue->GetMessage(i,message,&size);
            if (message->Severity<=D3D11_MESSAGE_SEVERITY_WARNING) { ++errors; std::printf("D3D: %s\n",message->pDescription); }
        }
        expect(errors==0,"D3D11 debug: no warnings/errors/corruption"); drop(queue);
    }
    drop(input); drop(output); drop(context); drop(device);
    std::printf("Timeline checks: %u/%u passed\n",checks-failures,checks);
    return failures?1:0;
}
