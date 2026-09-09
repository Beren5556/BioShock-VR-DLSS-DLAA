#include "core/gfx/dlss_sharpen.h"
#include <d3dcompiler.h>
#include <cstring>

namespace bvr::dlss_sharpen {
namespace {
const char* shader = R"(
Texture2D<float4> source : register(t0);
RWTexture2D<float4> target : register(u0);
cbuffer Parameters : register(b0) { uint width; uint height; float strength; uint padding; };
[numthreads(8,8,1)]
void main(uint3 id : SV_DispatchThreadID) {
    if (id.x >= width || id.y >= height) return;
    int2 p = int2(id.xy), limit = int2(width - 1, height - 1);
    float4 c = source.Load(int3(p, 0));
    float3 n = source.Load(int3(clamp(p + int2(0,-1), int2(0,0), limit), 0)).rgb;
    float3 s = source.Load(int3(clamp(p + int2(0, 1), int2(0,0), limit), 0)).rgb;
    float3 e = source.Load(int3(clamp(p + int2(1, 0), int2(0,0), limit), 0)).rgb;
    float3 w = source.Load(int3(clamp(p + int2(-1,0), int2(0,0), limit), 0)).rgb;
    float3 lo = min(c.rgb, min(min(n,s), min(e,w)));
    float3 hi = max(c.rgb, max(max(n,s), max(e,w)));
    target[p] = float4(clamp(c.rgb + strength * (c.rgb - (n+s+e+w)*0.25), lo, hi), c.a);
}
)";
ID3D11ComputeShader* g_shader = nullptr;
ID3D11Buffer* g_constants = nullptr;
ID3D11Texture2D* g_output = nullptr;
ID3D11UnorderedAccessView* g_uav = nullptr;
ID3D11ShaderResourceView* g_inputs[2]{};
ID3D11Texture2D* g_sources[2]{}; // retained by the SRVs
UINT g_width = 0, g_height = 0;
template<class T> void drop(T*& p) { if (p) { p->Release(); p = nullptr; } }
bool rgba8(DXGI_FORMAT f) {
    return f == DXGI_FORMAT_R8G8B8A8_TYPELESS || f == DXGI_FORMAT_R8G8B8A8_UNORM ||
           f == DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;
}
}
void release() {
    for (auto& input : g_inputs) drop(input);
    g_sources[0] = g_sources[1] = nullptr;
    drop(g_shader); drop(g_constants); drop(g_uav); drop(g_output);
    g_width = g_height = 0;
}
bool ready() { return g_shader && g_constants && g_output && g_uav && g_inputs[0] && g_inputs[1]; }
bool prepare(ID3D11Device* device, ID3D11Texture2D* left, ID3D11Texture2D* right) {
    if (ready() && g_sources[0] == left && g_sources[1] == right) return true;
    release();
    if (!device || !left || !right || left == right) return false;
    D3D11_TEXTURE2D_DESC desc[2]{};
    left->GetDesc(&desc[0]); right->GetDesc(&desc[1]);
    for (const auto& d : desc) {
        if (!d.Width || !d.Height || d.Width != desc[0].Width || d.Height != desc[0].Height ||
            d.MipLevels != 1 || d.ArraySize != 1 || d.SampleDesc.Count != 1 ||
            !(d.BindFlags & D3D11_BIND_SHADER_RESOURCE) ||
            (d.Format != DXGI_FORMAT_R8G8B8A8_TYPELESS && d.Format != DXGI_FORMAT_R8G8B8A8_UNORM))
            return false;
    }
    ID3DBlob* code = nullptr;
    if (FAILED(D3DCompile(shader, std::strlen(shader), "dlss_sharpen.hlsl", nullptr, nullptr,
        "main", "cs_5_0", D3DCOMPILE_ENABLE_STRICTNESS | D3DCOMPILE_WARNINGS_ARE_ERRORS,
        0, &code, nullptr))) return false;
    bool ok = SUCCEEDED(device->CreateComputeShader(code->GetBufferPointer(), code->GetBufferSize(),
                                                    nullptr, &g_shader));
    code->Release();
    D3D11_SHADER_RESOURCE_VIEW_DESC view{};
    // Preserve the encoded SDR byte domain at both ends; no additional
    // sRGB decode/encode or color/exposure change is introduced.
    view.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    view.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
    view.Texture2D.MipLevels = 1;
    if (ok) ok = SUCCEEDED(device->CreateShaderResourceView(left, &view, &g_inputs[0])) &&
                 SUCCEEDED(device->CreateShaderResourceView(right, &view, &g_inputs[1]));
    D3D11_TEXTURE2D_DESC output = desc[0];
    output.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    output.BindFlags = D3D11_BIND_UNORDERED_ACCESS;
    output.MiscFlags = 0;
    output.Usage = D3D11_USAGE_DEFAULT; output.CPUAccessFlags = 0;
    if (ok) ok = SUCCEEDED(device->CreateTexture2D(&output, nullptr, &g_output)) &&
                 SUCCEEDED(device->CreateUnorderedAccessView(g_output, nullptr, &g_uav));
    D3D11_BUFFER_DESC buffer{};
    buffer.ByteWidth = 16; buffer.Usage = D3D11_USAGE_DYNAMIC;
    buffer.BindFlags = D3D11_BIND_CONSTANT_BUFFER; buffer.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    if (ok) ok = SUCCEEDED(device->CreateBuffer(&buffer, nullptr, &g_constants));
    if (!ok) { release(); return false; }
    g_width = output.Width; g_height = output.Height;
    g_sources[0] = left; g_sources[1] = right;
    return true;
}
bool render(ID3D11DeviceContext* context, int eye, ID3D11Texture2D* destination, unsigned percent) {
    if (!context || !destination || eye < 0 || eye > 1 || percent > 100 || !ready()) return false;
    if (destination == g_sources[0] || destination == g_sources[1] || destination == g_output) return false;
    D3D11_TEXTURE2D_DESC d{}; destination->GetDesc(&d);
    if (d.Width != g_width || d.Height != g_height || d.MipLevels != 1 ||
        d.ArraySize != 1 || d.SampleDesc.Count != 1 || !rgba8(d.Format)) return false;
    if (!percent) { context->CopyResource(destination, g_sources[eye]); return true; }
    struct Values { UINT width, height; float strength; UINT padding; } values{g_width, g_height, percent / 100.0f, 0};
    D3D11_MAPPED_SUBRESOURCE map{};
    if (FAILED(context->Map(g_constants, 0, D3D11_MAP_WRITE_DISCARD, 0, &map))) return false;
    std::memcpy(map.pData, &values, sizeof(values)); context->Unmap(g_constants, 0);
    ID3D11ComputeShader* oldShader = nullptr;
    ID3D11ClassInstance* classes[256]{}; UINT classCount = 256;
    ID3D11Buffer* oldBuffer = nullptr;
    ID3D11ShaderResourceView* oldSrv = nullptr;
    ID3D11UnorderedAccessView* oldUav = nullptr;
    context->CSGetShader(&oldShader, classes, &classCount);
    context->CSGetConstantBuffers(0, 1, &oldBuffer);
    context->CSGetShaderResources(0, 1, &oldSrv);
    context->CSGetUnorderedAccessViews(0, 1, &oldUav);
    const UINT keepCounter = UINT(-1);
    context->CSSetShader(g_shader, nullptr, 0);
    context->CSSetConstantBuffers(0, 1, &g_constants);
    context->CSSetShaderResources(0, 1, &g_inputs[eye]);
    context->CSSetUnorderedAccessViews(0, 1, &g_uav, &keepCounter);
    context->Dispatch((g_width + 7) / 8, (g_height + 7) / 8, 1);
    context->CSSetUnorderedAccessViews(0, 1, &oldUav, &keepCounter);
    context->CSSetShaderResources(0, 1, &oldSrv);
    context->CSSetConstantBuffers(0, 1, &oldBuffer);
    context->CSSetShader(oldShader, classes, classCount);
    drop(oldShader); drop(oldBuffer); drop(oldSrv); drop(oldUav);
    for (UINT i = 0; i < classCount; ++i) drop(classes[i]);
    context->CopyResource(destination, g_output);
    return true;
}
}
