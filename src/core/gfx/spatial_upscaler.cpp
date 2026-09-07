#include "core/gfx/spatial_upscaler.h"

#include "core/util/log.h"

#include <d3dcompiler.h>

#include <algorithm>
#include <cmath>
#include <cstring>

namespace bvr::spatial_upscaler {
namespace {

const char* kShader = R"(
Texture2D<float4> sourceTex : register(t0);
SamplerState linearClamp : register(s0);

cbuffer UpscaleCB : register(b0) {
    float2 sourceTexel;
    float sharpness;
    float _padding;
};

struct VSOut {
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

VSOut vs_main(uint id : SV_VertexID) {
    VSOut o;
    float2 uv = float2((id << 1) & 2, id & 2);
    o.position = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
    o.uv = uv;
    return o;
}

float4 ps_linear(VSOut i) : SV_Target {
    return sourceTex.SampleLevel(linearClamp, i.uv, 0);
}

// A small, bounded unsharp pass after the hardware-linear resize.  Clamping
// to the four-neighbour range avoids the bright/dark ringing common in an
// unconstrained sharpen. This improves perceived texture definition but is
// intentionally not presented as temporal anti-aliasing.
float4 ps_sharpen(VSOut i) : SV_Target {
    float4 c = sourceTex.SampleLevel(linearClamp, i.uv, 0);
    float3 n = sourceTex.SampleLevel(linearClamp, i.uv + float2(0, -sourceTexel.y), 0).rgb;
    float3 s = sourceTex.SampleLevel(linearClamp, i.uv + float2(0,  sourceTexel.y), 0).rgb;
    float3 e = sourceTex.SampleLevel(linearClamp, i.uv + float2( sourceTexel.x, 0), 0).rgb;
    float3 w = sourceTex.SampleLevel(linearClamp, i.uv + float2(-sourceTexel.x, 0), 0).rgb;
    float3 lo = min(c.rgb, min(min(n, s), min(e, w)));
    float3 hi = max(c.rgb, max(max(n, s), max(e, w)));
    float3 neighbourAverage = (n + s + e + w) * 0.25;
    float3 filtered = clamp(c.rgb + (c.rgb - neighbourAverage) * sharpness, lo, hi);
    return float4(filtered, c.a);
}
)";

ID3D11Texture2D* g_input = nullptr;
ID3D11ShaderResourceView* g_inputSrv = nullptr;
ID3D11Texture2D* g_output = nullptr;
ID3D11RenderTargetView* g_outputRtv = nullptr;
ID3D11VertexShader* g_vs = nullptr;
ID3D11PixelShader* g_psLinear = nullptr;
ID3D11PixelShader* g_psSharpen = nullptr;
ID3D11Buffer* g_cb = nullptr;
ID3D11SamplerState* g_sampler = nullptr;
ID3D11RasterizerState* g_raster = nullptr;
ID3D11DepthStencilState* g_depth = nullptr;
UINT g_sourceW = 0, g_sourceH = 0;
UINT g_outputW = 0, g_outputH = 0;
DXGI_FORMAT g_outputViewFormat = DXGI_FORMAT_UNKNOWN;

template <typename T>
void release_one(T*& object) {
    if (object) {
        object->Release();
        object = nullptr;
    }
}

bool rgba8_family(DXGI_FORMAT format) {
    return format == DXGI_FORMAT_R8G8B8A8_TYPELESS ||
           format == DXGI_FORMAT_R8G8B8A8_UNORM ||
           format == DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;
}

struct SavedState {
    ID3D11RenderTargetView* rtvs[D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT] = {};
    UINT rtvCount = 0;
    ID3D11DepthStencilView* dsv = nullptr;
    ID3D11BlendState* blend = nullptr;
    FLOAT blendFactor[4] = {};
    UINT sampleMask = 0;
    ID3D11DepthStencilState* depth = nullptr;
    UINT stencilRef = 0;
    ID3D11RasterizerState* raster = nullptr;
    UINT viewportCount = D3D11_VIEWPORT_AND_SCISSORRECT_OBJECT_COUNT_PER_PIPELINE;
    D3D11_VIEWPORT viewports[D3D11_VIEWPORT_AND_SCISSORRECT_OBJECT_COUNT_PER_PIPELINE] = {};
    ID3D11VertexShader* vs = nullptr;
    ID3D11PixelShader* ps = nullptr;
    ID3D11GeometryShader* gs = nullptr;
    ID3D11HullShader* hs = nullptr;
    ID3D11DomainShader* ds = nullptr;
    ID3D11Buffer* psCb = nullptr;
    ID3D11ShaderResourceView* psSrv = nullptr;
    ID3D11SamplerState* psSampler = nullptr;
    ID3D11InputLayout* inputLayout = nullptr;
    D3D11_PRIMITIVE_TOPOLOGY topology = D3D11_PRIMITIVE_TOPOLOGY_UNDEFINED;

    void capture(ID3D11DeviceContext* context) {
        context->OMGetRenderTargets(D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT, rtvs, &dsv);
        for (UINT i = 0; i < D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT; ++i)
            if (rtvs[i]) rtvCount = i + 1;
        context->OMGetBlendState(&blend, blendFactor, &sampleMask);
        context->OMGetDepthStencilState(&depth, &stencilRef);
        context->RSGetState(&raster);
        context->RSGetViewports(&viewportCount, viewports);
        context->VSGetShader(&vs, nullptr, nullptr);
        context->PSGetShader(&ps, nullptr, nullptr);
        context->GSGetShader(&gs, nullptr, nullptr);
        context->HSGetShader(&hs, nullptr, nullptr);
        context->DSGetShader(&ds, nullptr, nullptr);
        context->PSGetConstantBuffers(0, 1, &psCb);
        context->PSGetShaderResources(0, 1, &psSrv);
        context->PSGetSamplers(0, 1, &psSampler);
        context->IAGetInputLayout(&inputLayout);
        context->IAGetPrimitiveTopology(&topology);
    }

    void restore(ID3D11DeviceContext* context) {
        // Restore only through the last originally-bound RTV. Passing the
        // maximum slot count here can disturb unrelated OM UAV bindings.
        context->OMSetRenderTargets(rtvCount, rtvCount ? rtvs : nullptr, dsv);
        context->OMSetBlendState(blend, blendFactor, sampleMask);
        context->OMSetDepthStencilState(depth, stencilRef);
        context->RSSetState(raster);
        context->RSSetViewports(viewportCount, viewportCount ? viewports : nullptr);
        context->VSSetShader(vs, nullptr, 0);
        context->PSSetShader(ps, nullptr, 0);
        context->GSSetShader(gs, nullptr, 0);
        context->HSSetShader(hs, nullptr, 0);
        context->DSSetShader(ds, nullptr, 0);
        context->PSSetConstantBuffers(0, 1, &psCb);
        context->PSSetShaderResources(0, 1, &psSrv);
        context->PSSetSamplers(0, 1, &psSampler);
        context->IASetInputLayout(inputLayout);
        context->IASetPrimitiveTopology(topology);
    }

    ~SavedState() {
        for (ID3D11RenderTargetView*& rtv : rtvs) release_one(rtv);
        release_one(dsv);
        release_one(blend);
        release_one(depth);
        release_one(raster);
        release_one(vs);
        release_one(ps);
        release_one(gs);
        release_one(hs);
        release_one(ds);
        release_one(psCb);
        release_one(psSrv);
        release_one(psSampler);
        release_one(inputLayout);
    }
};

bool compile_shader(const char* entry, const char* target, ID3DBlob** blob) {
    ID3DBlob* errors = nullptr;
    const HRESULT hr = D3DCompile(kShader, std::strlen(kShader), "spatial_upscaler.hlsl",
                                  nullptr, nullptr, entry, target,
                                  D3DCOMPILE_ENABLE_STRICTNESS, 0, blob, &errors);
    if (errors) {
        BVR_LOG("[upscaler] shader %s: %s", entry,
                static_cast<const char*>(errors->GetBufferPointer()));
        errors->Release();
    }
    return SUCCEEDED(hr);
}

} // namespace

void release() {
    release_one(g_inputSrv);
    release_one(g_input);
    release_one(g_outputRtv);
    release_one(g_output);
    release_one(g_vs);
    release_one(g_psLinear);
    release_one(g_psSharpen);
    release_one(g_cb);
    release_one(g_sampler);
    release_one(g_raster);
    release_one(g_depth);
    g_sourceW = g_sourceH = g_outputW = g_outputH = 0;
    g_outputViewFormat = DXGI_FORMAT_UNKNOWN;
}

bool ready() {
    return g_input && g_inputSrv && g_output && g_outputRtv && g_vs && g_psLinear &&
           g_psSharpen && g_cb && g_sampler && g_raster && g_depth;
}

bool prepare(ID3D11Device* device, UINT sourceWidth, UINT sourceHeight,
             DXGI_FORMAT sourceFormat, UINT outputWidth, UINT outputHeight,
             DXGI_FORMAT outputViewFormat) {
    release();
    if (!device || !sourceWidth || !sourceHeight || !outputWidth || !outputHeight ||
        !rgba8_family(sourceFormat) ||
        (outputViewFormat != DXGI_FORMAT_R8G8B8A8_UNORM &&
         outputViewFormat != DXGI_FORMAT_R8G8B8A8_UNORM_SRGB)) {
        BVR_LOG("[upscaler] unsupported setup (source %ux%u fmt %u, output %ux%u fmt %u)",
                sourceWidth, sourceHeight, static_cast<unsigned>(sourceFormat), outputWidth,
                outputHeight, static_cast<unsigned>(outputViewFormat));
        return false;
    }

    ID3DBlob* vsBlob = nullptr;
    ID3DBlob* linearBlob = nullptr;
    ID3DBlob* sharpenBlob = nullptr;
    bool ok = compile_shader("vs_main", "vs_4_0", &vsBlob) &&
              compile_shader("ps_linear", "ps_4_0", &linearBlob) &&
              compile_shader("ps_sharpen", "ps_4_0", &sharpenBlob);
    if (ok)
        ok = SUCCEEDED(device->CreateVertexShader(vsBlob->GetBufferPointer(),
                                                   vsBlob->GetBufferSize(), nullptr, &g_vs)) &&
             SUCCEEDED(device->CreatePixelShader(linearBlob->GetBufferPointer(),
                                                  linearBlob->GetBufferSize(), nullptr,
                                                  &g_psLinear)) &&
             SUCCEEDED(device->CreatePixelShader(sharpenBlob->GetBufferPointer(),
                                                  sharpenBlob->GetBufferSize(), nullptr,
                                                  &g_psSharpen));
    release_one(vsBlob);
    release_one(linearBlob);
    release_one(sharpenBlob);

    D3D11_TEXTURE2D_DESC inputDesc{};
    inputDesc.Width = sourceWidth;
    inputDesc.Height = sourceHeight;
    inputDesc.MipLevels = 1;
    inputDesc.ArraySize = 1;
    inputDesc.Format = DXGI_FORMAT_R8G8B8A8_TYPELESS;
    inputDesc.SampleDesc.Count = 1;
    inputDesc.Usage = D3D11_USAGE_DEFAULT;
    inputDesc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
    if (ok) ok = SUCCEEDED(device->CreateTexture2D(&inputDesc, nullptr, &g_input));

    // Match the view interpretation at both ends. With an sRGB XR swapchain,
    // sampling decodes the game's gamma-encoded bytes and the output RTV
    // encodes them again; with UNORM, both operations stay in byte space.
    D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc{};
    srvDesc.Format = outputViewFormat;
    srvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
    srvDesc.Texture2D.MipLevels = 1;
    if (ok) ok = SUCCEEDED(device->CreateShaderResourceView(g_input, &srvDesc, &g_inputSrv));

    D3D11_TEXTURE2D_DESC outputDesc = inputDesc;
    outputDesc.Width = outputWidth;
    outputDesc.Height = outputHeight;
    outputDesc.BindFlags = D3D11_BIND_RENDER_TARGET;
    if (ok) ok = SUCCEEDED(device->CreateTexture2D(&outputDesc, nullptr, &g_output));
    D3D11_RENDER_TARGET_VIEW_DESC rtvDesc{};
    rtvDesc.Format = outputViewFormat;
    rtvDesc.ViewDimension = D3D11_RTV_DIMENSION_TEXTURE2D;
    if (ok) ok = SUCCEEDED(device->CreateRenderTargetView(g_output, &rtvDesc, &g_outputRtv));

    D3D11_BUFFER_DESC cbDesc{};
    cbDesc.ByteWidth = 16;
    cbDesc.Usage = D3D11_USAGE_DYNAMIC;
    cbDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    cbDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    if (ok) ok = SUCCEEDED(device->CreateBuffer(&cbDesc, nullptr, &g_cb));

    D3D11_SAMPLER_DESC samplerDesc{};
    samplerDesc.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
    samplerDesc.AddressU = samplerDesc.AddressV = samplerDesc.AddressW =
        D3D11_TEXTURE_ADDRESS_CLAMP;
    samplerDesc.MaxLOD = D3D11_FLOAT32_MAX;
    if (ok) ok = SUCCEEDED(device->CreateSamplerState(&samplerDesc, &g_sampler));

    D3D11_RASTERIZER_DESC rasterDesc{};
    rasterDesc.FillMode = D3D11_FILL_SOLID;
    rasterDesc.CullMode = D3D11_CULL_NONE;
    rasterDesc.DepthClipEnable = TRUE;
    if (ok) ok = SUCCEEDED(device->CreateRasterizerState(&rasterDesc, &g_raster));

    D3D11_DEPTH_STENCIL_DESC depthDesc{};
    depthDesc.DepthEnable = FALSE;
    depthDesc.StencilEnable = FALSE;
    if (ok) ok = SUCCEEDED(device->CreateDepthStencilState(&depthDesc, &g_depth));

    if (!ok) {
        BVR_LOG("[upscaler] D3D11 resource creation failed - falling back to direct copy");
        release();
        return false;
    }

    g_sourceW = sourceWidth;
    g_sourceH = sourceHeight;
    g_outputW = outputWidth;
    g_outputH = outputHeight;
    g_outputViewFormat = outputViewFormat;
    BVR_LOG("[upscaler] spatial pipeline ready: %ux%u -> %ux%u, %s transfer",
            g_sourceW, g_sourceH, g_outputW, g_outputH,
            outputViewFormat == DXGI_FORMAT_R8G8B8A8_UNORM_SRGB ? "sRGB" : "UNORM");
    return true;
}

bool render(ID3D11DeviceContext* context, ID3D11Texture2D* destination,
            ID3D11Texture2D* source, float sharpness) {
    if (!context || !destination || !source || !ready()) return false;

    D3D11_TEXTURE2D_DESC sourceDesc{};
    D3D11_TEXTURE2D_DESC destinationDesc{};
    source->GetDesc(&sourceDesc);
    destination->GetDesc(&destinationDesc);
    if (sourceDesc.Width != g_sourceW || sourceDesc.Height != g_sourceH ||
        sourceDesc.MipLevels != 1 || sourceDesc.ArraySize != 1 ||
        sourceDesc.SampleDesc.Count != 1 || !rgba8_family(sourceDesc.Format) ||
        destinationDesc.Width != g_outputW || destinationDesc.Height != g_outputH ||
        destinationDesc.MipLevels != 1 || destinationDesc.ArraySize != 1 ||
        destinationDesc.SampleDesc.Count != 1 ||
        !rgba8_family(destinationDesc.Format)) {
        BVR_LOG("[upscaler] frame resource mismatch - refusing an unsafe GPU copy");
        return false;
    }

    sharpness = std::clamp(sharpness, 0.0f, 1.0f);
    D3D11_MAPPED_SUBRESOURCE mapped{};
    if (FAILED(context->Map(g_cb, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) return false;
    float* values = static_cast<float*>(mapped.pData);
    values[0] = 1.0f / static_cast<float>(g_sourceW);
    values[1] = 1.0f / static_cast<float>(g_sourceH);
    values[2] = sharpness;
    values[3] = 0.0f;
    context->Unmap(g_cb, 0);

    SavedState saved;
    saved.capture(context);

    // Avoid a read/write hazard if a previous caller happened to leave slot 0
    // pointing at our input (the normal state restoration makes this rare).
    ID3D11ShaderResourceView* nullSrv = nullptr;
    context->PSSetShaderResources(0, 1, &nullSrv);
    context->CopyResource(g_input, source);

    D3D11_VIEWPORT viewport{};
    viewport.Width = static_cast<FLOAT>(g_outputW);
    viewport.Height = static_cast<FLOAT>(g_outputH);
    viewport.MaxDepth = 1.0f;
    context->OMSetRenderTargets(1, &g_outputRtv, nullptr);
    context->OMSetBlendState(nullptr, nullptr, 0xffffffffu);
    context->OMSetDepthStencilState(g_depth, 0);
    context->RSSetState(g_raster);
    context->RSSetViewports(1, &viewport);
    context->IASetInputLayout(nullptr);
    context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    context->VSSetShader(g_vs, nullptr, 0);
    context->GSSetShader(nullptr, nullptr, 0);
    context->HSSetShader(nullptr, nullptr, 0);
    context->DSSetShader(nullptr, nullptr, 0);
    context->PSSetShader(sharpness > 0.001f ? g_psSharpen : g_psLinear, nullptr, 0);
    context->PSSetConstantBuffers(0, 1, &g_cb);
    context->PSSetShaderResources(0, 1, &g_inputSrv);
    context->PSSetSamplers(0, 1, &g_sampler);
    context->Draw(3, 0);

    // The output must no longer be bound as an RTV when it becomes the source
    // of the final GPU copy. Restoring the game state performs that unbind.
    saved.restore(context);
    context->CopyResource(destination, g_output);
    return true;
}

} // namespace bvr::spatial_upscaler
