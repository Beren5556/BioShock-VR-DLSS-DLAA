#include "core/gfx/dlss_sharpen.h"
#include "core/gfx/sampled_gpu_timer.h"
#include <d3d11.h>
#include <d3d11sdklayers.h>
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <vector>

namespace {
unsigned checks=0, failures=0;
void expect(bool ok,const char* name){++checks;if(!ok)++failures;std::printf("%s: %s\n",ok?"PASS":"FAIL",name);}
template<class T> void drop(T*& p){if(p){p->Release();p=nullptr;}}
int channel(uint32_t p,int c){return int((p>>(8*c))&255);}
void copy_benchmark(ID3D11Device* d,ID3D11DeviceContext* c) {
    for(UINT size:{2048u,3072u,3584u}) {
        D3D11_TEXTURE2D_DESC desc{};
        desc.Width=desc.Height=size;desc.MipLevels=desc.ArraySize=desc.SampleDesc.Count=1;
        desc.Format=DXGI_FORMAT_R24G8_TYPELESS;desc.Usage=D3D11_USAGE_DEFAULT;
        desc.BindFlags=D3D11_BIND_DEPTH_STENCIL|D3D11_BIND_SHADER_RESOURCE;
        ID3D11Texture2D *a=nullptr,*b=nullptr;
        ID3D11DepthStencilView* depth=nullptr;
        D3D11_DEPTH_STENCIL_VIEW_DESC view{};view.Format=DXGI_FORMAT_D24_UNORM_S8_UINT;
        view.ViewDimension=D3D11_DSV_DIMENSION_TEXTURE2D;
        if(FAILED(d->CreateTexture2D(&desc,nullptr,&a))||FAILED(d->CreateTexture2D(&desc,nullptr,&b))||
           FAILED(d->CreateDepthStencilView(a,&view,&depth))){drop(depth);drop(a);drop(b);return;}
        c->ClearDepthStencilView(depth,D3D11_CLEAR_DEPTH,0.75f,0);
        for(int dirty:{0,1})for(int copies:{1,4,8,12}) {
            ID3D11Query *disjoint=nullptr,*start=nullptr,*end=nullptr;
            D3D11_QUERY_DESC q{D3D11_QUERY_TIMESTAMP_DISJOINT,0};
            d->CreateQuery(&q,&disjoint);q.Query=D3D11_QUERY_TIMESTAMP;
            d->CreateQuery(&q,&start);d->CreateQuery(&q,&end);
            if(!disjoint||!start||!end){drop(disjoint);drop(start);drop(end);continue;}
            c->Begin(disjoint);c->End(start);
            constexpr int iterations=24;
            for(int n=0;n<iterations;++n)for(int j=0;j<copies;++j){
                if(dirty)c->ClearDepthStencilView(depth,D3D11_CLEAR_DEPTH,(j&1)?0.25f:0.75f,0);
                c->CopySubresourceRegion(b,0,0,0,0,a,0,nullptr);
            }
            c->End(end);c->End(disjoint);c->Flush(); // standalone benchmark only
            D3D11_QUERY_DATA_TIMESTAMP_DISJOINT result{};UINT64 t0=0,t1=0;
            const auto deadline=GetTickCount64()+5000;
            while(c->GetData(disjoint,&result,sizeof(result),D3D11_ASYNC_GETDATA_DONOTFLUSH)==S_FALSE&&GetTickCount64()<deadline)Sleep(1);
            if(!result.Disjoint&&result.Frequency&&c->GetData(start,&t0,sizeof(t0),0)==S_OK&&c->GetData(end,&t1,sizeof(t1),0)==S_OK)
                std::printf("DEPTH_BENCH size=%u copiesPerEye=%d withClear=%d gpuMsPerEye=%.4f iterations=%d\n",
                            size,copies,dirty,1000.0*double(t1-t0)/double(result.Frequency)/iterations,iterations);
            drop(disjoint);drop(start);drop(end);
        }
        drop(depth);drop(a);drop(b);
    }
}
}
int main(int argc,char** argv) {
    bool hardware=argc>1&&std::strcmp(argv[1],"--hardware-benchmark")==0;
    ID3D11Device* d=nullptr;ID3D11DeviceContext* c=nullptr;
    D3D_FEATURE_LEVEL feature{};
    UINT flags=hardware?0:D3D11_CREATE_DEVICE_DEBUG;
    const auto driver=hardware?D3D_DRIVER_TYPE_HARDWARE:D3D_DRIVER_TYPE_WARP;
    HRESULT hr=D3D11CreateDevice(nullptr,driver,nullptr,flags,nullptr,0,D3D11_SDK_VERSION,&d,&feature,&c);
    if(FAILED(hr)&&!hardware){flags=0;hr=D3D11CreateDevice(nullptr,driver,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&d,&feature,&c);}
    if(FAILED(hr)){std::printf("Device creation failed: %08x\n",unsigned(hr));return 2;}
    std::printf("Adapter=%s debug=%u feature=%x. No game, NGX or profile access.\n",hardware?"hardware":"WARP",flags,unsigned(feature));
    constexpr UINT width=17,height=11;
    std::vector<uint32_t> pixels[2];
    ID3D11Texture2D* inputs[2]{};
    D3D11_TEXTURE2D_DESC desc{};desc.Width=width;desc.Height=height;
    desc.MipLevels=desc.ArraySize=desc.SampleDesc.Count=1;
    desc.Format=DXGI_FORMAT_R8G8B8A8_UNORM;desc.Usage=D3D11_USAGE_DEFAULT;
    desc.BindFlags=D3D11_BIND_SHADER_RESOURCE;
    for(int eye=0;eye<2;++eye){
        pixels[eye].resize(width*height);
        for(UINT p=0;p<width*height;++p){
            uint32_t value=0;for(int k=0;k<4;++k)value|=((p*37+eye*73+k*29)%256)<<(8*k);
            pixels[eye][p]=value;
        }
        D3D11_SUBRESOURCE_DATA data{pixels[eye].data(),width*4,0};
        if(FAILED(d->CreateTexture2D(&desc,&data,&inputs[eye])))return 2;
    }
    ID3D11Texture2D *output=nullptr,*staging=nullptr,*marker=nullptr;
    desc.BindFlags=0;if(FAILED(d->CreateTexture2D(&desc,nullptr,&output)))return 2;
    desc.Usage=D3D11_USAGE_STAGING;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
    if(FAILED(d->CreateTexture2D(&desc,nullptr,&staging)))return 2;
    desc.Usage=D3D11_USAGE_DEFAULT;desc.CPUAccessFlags=0;desc.BindFlags=D3D11_BIND_UNORDERED_ACCESS;
    if(FAILED(d->CreateTexture2D(&desc,nullptr,&marker)))return 2;
    ID3D11UnorderedAccessView* markerUav=nullptr;
    ID3D11ShaderResourceView* markerSrv=nullptr;
    ID3D11Buffer* markerCb=nullptr;
    d->CreateUnorderedAccessView(marker,nullptr,&markerUav);
    d->CreateShaderResourceView(inputs[0],nullptr,&markerSrv);
    D3D11_BUFFER_DESC bd{};bd.ByteWidth=16;bd.BindFlags=D3D11_BIND_CONSTANT_BUFFER;
    d->CreateBuffer(&bd,nullptr,&markerCb);
    if(!markerUav||!markerSrv||!markerCb)return 2;
    c->CSSetUnorderedAccessViews(0,1,&markerUav,nullptr);
    c->CSSetShaderResources(0,1,&markerSrv);c->CSSetConstantBuffers(0,1,&markerCb);
    expect(bvr::dlss_sharpen::prepare(d,inputs[0],inputs[1]),"prepare distinct stereo inputs");
    for(unsigned percent=0;percent<=100;percent+=5)for(int eye=0;eye<2;++eye){
        bool ok=bvr::dlss_sharpen::render(c,eye,output,percent);
        c->CopyResource(staging,output);
        D3D11_MAPPED_SUBRESOURCE map{};
        if(FAILED(c->Map(staging,0,D3D11_MAP_READ,0,&map)))return 2;
        for(UINT y=0;y<height;++y)for(UINT x=0;x<width;++x){
            uint32_t actual=reinterpret_cast<const uint32_t*>(static_cast<const uint8_t*>(map.pData)+y*map.RowPitch)[x];
            uint32_t center=pixels[eye][y*width+x];
            const uint32_t neighbors[]={pixels[eye][(y?y-1:y)*width+x],pixels[eye][std::min(y+1,height-1)*width+x],
                pixels[eye][y*width+(x?x-1:x)],pixels[eye][y*width+std::min(x+1,width-1)]};
            if(!percent)ok=ok&&actual==center;
            for(int k=0;k<4;++k){
                int v=channel(center,k),lo=v,hi=v,sum=0;
                for(auto n:neighbors){int t=channel(n,k);lo=std::min(lo,t);hi=std::max(hi,t);sum+=t;}
                double reference=k==3?v:std::clamp(v+(v-sum/4.0)*(percent/100.0),double(lo),double(hi));
                ok=ok&&std::abs(channel(actual,k)-reference)<1.01;
                if(k==3)ok=ok&&channel(actual,k)==v;
            }
        }
        c->Unmap(staging,0);
        expect(ok,"GPU equals bounded CPU reference, correct eye, alpha and 0% identity");
    }
    ID3D11UnorderedAccessView* seenUav=nullptr;ID3D11ShaderResourceView* seenSrv=nullptr;ID3D11Buffer* seenCb=nullptr;
    c->CSGetUnorderedAccessViews(0,1,&seenUav);c->CSGetShaderResources(0,1,&seenSrv);c->CSGetConstantBuffers(0,1,&seenCb);
    expect(seenUav==markerUav&&seenSrv==markerSrv&&seenCb==markerCb,"compute bindings restored");
    drop(seenUav);drop(seenSrv);drop(seenCb);
    expect(!bvr::dlss_sharpen::render(c,2,output,10)&&!bvr::dlss_sharpen::render(c,0,output,101)&&
           !bvr::dlss_sharpen::render(c,0,inputs[0],10),"invalid eye, amount and aliases rejected");
    bvr::SampledGpuTimer timer;timer.prepare(d,1);
    for(int i=0;i<8;++i){timer.begin(c);c->CopyResource(output,inputs[0]);timer.end(c);}
    c->Flush(); // Tests may wait for their own GPU; production sampler never does.
    const auto deadline=GetTickCount64()+3000;
    do{timer.poll(c);if(timer.samples())break;Sleep(1);}while(GetTickCount64()<deadline);
    expect(timer.samples()>0&&timer.average_ms()>=0,"non-blocking diagnostic sampler returns valid GPU timestamps");
    timer.clear_stats();expect(timer.samples()==0,"sampler statistics reset without changing GPU work");
    timer.release();
    c->ClearState();bvr::dlss_sharpen::release();
    drop(markerCb);drop(markerUav);drop(markerSrv);drop(marker);drop(output);drop(staging);
    for(auto& input:inputs)drop(input);
    if(hardware)copy_benchmark(d,c);
    ID3D11InfoQueue* queue=nullptr;
    if(SUCCEEDED(d->QueryInterface(IID_PPV_ARGS(&queue)))){
        unsigned errors=0;
        for(UINT64 i=0;i<queue->GetNumStoredMessagesAllowedByRetrievalFilter();++i){
            SIZE_T n=0;queue->GetMessage(i,nullptr,&n);std::vector<uint8_t> bytes(n);
            auto* message=reinterpret_cast<D3D11_MESSAGE*>(bytes.data());queue->GetMessage(i,message,&n);
            if(message->Severity<=D3D11_MESSAGE_SEVERITY_WARNING){++errors;std::printf("D3D: %s\n",message->pDescription);}
        }
        expect(errors==0,"D3D11 debug: no warning/error/corruption messages");drop(queue);
    }
    drop(c);drop(d);
    std::printf("Sharpness checks: %u/%u passed\n",checks-failures,checks);return failures?1:0;
}
