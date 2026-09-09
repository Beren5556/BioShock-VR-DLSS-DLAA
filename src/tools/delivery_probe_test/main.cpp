#include "core/vr/delivery_probe.h"
#include <windows.h>
#include <d3d11.h>
#include <d3d11sdklayers.h>
#include <MinHook.h>
#include <cstdio>
#include <cstdarg>
#include <cstring>
#include <cmath>
#include <limits>
#include <string>
#include <thread>
#include <vector>

namespace {
unsigned checks=0, failures=0;
std::vector<std::string> logs;
void expect(bool ok, const char* label) {
    ++checks; if (!ok) ++failures;
    std::printf("%s: %s\n", ok?"PASS":"FAIL", label);
}
template<class T> void drop(T*& p) { if(p) p->Release(); p=nullptr; }
void* observedSession=nullptr;
const char* observedProperty=nullptr;
float observedFloat=0;
unsigned originalCalls=0;
char originalReturn=1;
__declspec(noinline) char __cdecl fake_set_float(void* session,const char* property,float value) {
    observedSession=session; observedProperty=property; observedFloat=value;
    ++originalCalls; return originalReturn;
}
}
namespace bvr::log {
void write(const char* format, ...) {
    char line[4096]; va_list args; va_start(args,format);
    vsnprintf_s(line,_TRUNCATE,format,args); va_end(args); logs.emplace_back(line);
    std::printf("%s\n",line);
}
}
int main() {
    namespace P=bvr::delivery_probe;
    std::puts("Standalone observer/order tests. No game, real VD DLL, headset or INI access.");
    expect(P::defer_desktop(true,true,true,true,true),"healthy paired temporal projection uses post-XR desktop");
    for (unsigned absent=0;absent<5;++absent) {
        bool v[5]={true,true,true,true,true}; v[absent]=false;
        expect(!P::defer_desktop(v[0],v[1],v[2],v[3],v[4]),"other game/NORMAL/left/menu/unfocused retain original ordering");
    }
    for (bool deferred:{false,true}) for(bool xrSucceeded:{false,true}) {
        std::string order;
        P::DesktopTail tail(deferred);
        const auto work=[&] {order+='D';};
        tail.run(false,work); order+='X';
        // Caller must still execute the desktop work before handling XR failure.
        tail.run(true,work); tail.run(false,work); tail.run(true,work); order+='P';
        expect(order==(deferred?"XDP":"DXP"),xrSucceeded?
            "desktop once, correct side of XR, always before DXGI Present":
            "failed XR submission still performs exactly one desktop composite");
    }

    expect(MH_Initialize()==MH_OK,"MinHook initialized only in this test process");
    expect(P::install_test(&fake_set_float),"actual x86 cdecl hook installed on private fixture");
    auto invoke=static_cast<P::SetFloatFn>(&fake_set_float);
    auto* session=reinterpret_cast<void*>(std::uintptr_t(0x12345678));
    const char property[]="AppGpuTime";
    expect(invoke(session,property,0.0045f)==1,"unobserved call preserves char return");
    expect(P::finish_end_frame().calls==0,"out-of-scope VD calls ignored");
    P::begin_end_frame();
    const auto before=originalCalls;
    expect(invoke(session,property,0.0045f)==1,"in-scope call preserves result");
    expect(observedSession==session&&observedProperty==property&&observedFloat==0.0045f&&originalCalls==before+1,
        "same pointer arguments and float forwarded exactly once");
    auto sample=P::finish_end_frame();
    expect(sample.calls==1&&sample.positive==1&&sample.accepted==1&&std::abs(sample.sumMs-4.5)<0.0001,
        "records runtime seconds as milliseconds without changing them");
    P::begin_end_frame();
    invoke(session,"AppGpuTimes",2.0f); invoke(session,"OtherProperty",3.0f); invoke(session,nullptr,4.0f);
    expect(P::finish_end_frame().calls==0,"only exact AppGpuTime property is measured");
    P::begin_end_frame(); invoke(session,property,0); invoke(session,property,-1);
    const float nan=std::numeric_limits<float>::quiet_NaN(); invoke(session,property,nan);
    expect(std::isnan(observedFloat),"invalid runtime float still passes through unchanged");
    sample=P::finish_end_frame();
    expect(sample.calls==3&&sample.zero==1&&sample.invalid==2&&sample.positive==0,
        "zero warmup and invalid values do not become positive measured GPU samples");
    originalReturn=0; P::begin_end_frame();
    expect(invoke(session,property,0.008f)==0,"unsupported property failure remains false");
    sample=P::finish_end_frame();
    expect(sample.calls==1&&sample.accepted==0&&sample.positive==1,"records proposed value and acceptance separately");
    originalReturn=1; P::begin_end_frame();
    std::thread other([&]{invoke(session,property,0.01f);}); other.join();
    expect(P::finish_end_frame().calls==0,"foreign thread cannot contaminate this frame's sample");
    P::begin_end_frame(); invoke(session,property,0.006f);
    sample=P::finish_end_frame();
    P::FrameInfo info{3,2,2150,2150,2150,2150,true};
    P::observe(info,sample,0.08,0.01); P::desktop_present(0.2,0);
    LARGE_INTEGER now{}; QueryPerformanceCounter(&now); P::after_end(now.QuadPart); P::before_wait(); P::end_phase();
    bool logged=false;
    for(const auto& s:logs) logged |= s.find("vdGpuMs=6.000")!=std::string::npos &&
        s.find("desktopAfterXr=1")!=std::string::npos&&s.find("notTotalVdLatency=1")!=std::string::npos;
    expect(logged,"report preserves phase/geometry and distinguishes runtime GPU from total VD latency");
    const auto count=logs.size(); P::end_phase();
    expect(logs.size()==count,"repeated phase close does not duplicate old measurements");
    P::remove_test();
    expect(invoke(session,property,0.007f)==1,"unhook restores the original call path");
    expect(MH_Uninitialize()==MH_OK,"private hook infrastructure released");

    ID3D11Device* device=nullptr; ID3D11DeviceContext* context=nullptr;
    UINT flags=D3D11_CREATE_DEVICE_DEBUG;
    HRESULT hr=D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,flags,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&context);
    if(FAILED(hr)){flags=0;hr=D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&context);}
    expect(SUCCEEDED(hr),"WARP device for image ordering proof");
    if(FAILED(hr)) return 2;
    constexpr UINT W=64,H=64;
    std::vector<unsigned> left(W*H,0xFF223344),right(W*H,0xFF556677);
    ID3D11Texture2D *bb=nullptr,*held=nullptr,*xrL=nullptr,*xrR=nullptr,*read=nullptr;
    D3D11_TEXTURE2D_DESC d{};d.Width=W;d.Height=H;d.MipLevels=d.ArraySize=d.SampleDesc.Count=1;
    d.Format=DXGI_FORMAT_R8G8B8A8_UNORM;
    bool textures=true;
    for(auto** p:{&bb,&held,&xrL,&xrR}) textures &= SUCCEEDED(device->CreateTexture2D(&d,nullptr,p));
    d.Usage=D3D11_USAGE_STAGING;d.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
    textures &= SUCCEEDED(device->CreateTexture2D(&d,nullptr,&read));
    expect(textures,"separate desktop/left/right/captured textures");
    if(!textures) return 2;
    const auto verify=[&](ID3D11Texture2D* source,unsigned value,bool hud=false) {
        context->CopyResource(read,source);
        D3D11_MAPPED_SUBRESOURCE m{};
        if(FAILED(context->Map(read,0,D3D11_MAP_READ,0,&m))) return false;
        bool same=true;
        for(UINT y=0;y<H;++y)for(UINT x=0;x<W;++x)
            same &= reinterpret_cast<unsigned*>(static_cast<unsigned char*>(m.pData)+y*m.RowPitch)[x]
                ==(hud&&x==0&&y==0?0xFFFFFFFF:value);
        context->Unmap(read,0);return same;
    };
    for(bool deferred:{false,true}) {
        context->UpdateSubresource(bb,0,nullptr,left.data(),W*4,0);
        context->CopyResource(held,bb);context->CopyResource(xrL,bb);
        context->UpdateSubresource(bb,0,nullptr,right.data(),W*4,0);context->CopyResource(xrR,bb);
        P::DesktopTail tail(deferred);
        const auto composite=[&]{
            context->CopyResource(bb,held);
            const unsigned white=0xFFFFFFFF;D3D11_BOX pixel{0,0,0,1,1,1};
            context->UpdateSubresource(bb,0,&pixel,&white,4,0);
        };
        tail.run(false,composite);
        expect(verify(xrL,left[0])&&verify(xrR,right[0]),"both captured XR eyes exact at simulated submission seam");
        tail.run(true,composite);
        expect(verify(xrL,left[0])&&verify(xrR,right[0]),"post-XR desktop work cannot alter either submitted eye");
        expect(verify(bb,left[0],true),"desktop mirror remains left eye with its own HUD after either ordering");
    }
    ID3D11InfoQueue* queue=nullptr;
    if(SUCCEEDED(device->QueryInterface(IID_PPV_ARGS(&queue)))) {
        unsigned warnings=0;
        for(UINT64 i=0;i<queue->GetNumStoredMessagesAllowedByRetrievalFilter();++i){
            SIZE_T size=0;queue->GetMessage(i,nullptr,&size);std::vector<unsigned char> bytes(size);
            auto* message=reinterpret_cast<D3D11_MESSAGE*>(bytes.data());queue->GetMessage(i,message,&size);
            if(message->Severity<=D3D11_MESSAGE_SEVERITY_WARNING){++warnings;std::printf("D3D: %s\n",message->pDescription);}
        }
        expect(warnings==0,"D3D11 debug no warnings/errors/corruption");drop(queue);
    }
    drop(read);drop(bb);drop(held);drop(xrL);drop(xrR);drop(context);drop(device);
    std::printf("Delivery checks: %u/%u passed\n",checks-failures,checks);
    return failures?1:0;
}
