#include "core/gfx/spatial_upscaler.h"

#include <d3d11.h>

#include <cmath>
#include <cstdio>
#include <cstdint>
#include <cstring>

namespace {

template <typename T>
void release_one(T*& value) {
    if (value) {
        value->Release();
        value = nullptr;
    }
}

bool nearly_equal(float a, float b) {
    return std::fabs(a - b) < 0.001f;
}

} // namespace

int main(int argc, char** argv) {
    ID3D11Device* device = nullptr;
    ID3D11DeviceContext* context = nullptr;
    D3D_FEATURE_LEVEL featureLevel{};
    const D3D_FEATURE_LEVEL requested[] = {D3D_FEATURE_LEVEL_11_0,
                                           D3D_FEATURE_LEVEL_10_0};
    const bool benchmark = argc > 1 && std::strcmp(argv[1], "--benchmark") == 0;
    const bool hardware = benchmark ||
                          (argc > 1 && std::strcmp(argv[1], "--hardware") == 0);
    HRESULT hr = D3D11CreateDevice(nullptr,
                                   hardware ? D3D_DRIVER_TYPE_HARDWARE
                                            : D3D_DRIVER_TYPE_WARP,
                                   nullptr, 0, requested,
                                   static_cast<UINT>(_countof(requested)),
                                   D3D11_SDK_VERSION, &device, &featureLevel, &context);
    if (FAILED(hr)) {
        std::printf("FAIL: D3D11 %s device 0x%08X\n",
                    hardware ? "hardware" : "WARP", static_cast<unsigned>(hr));
        return 1;
    }

    constexpr UINT kInputW = 4, kInputH = 4, kOutputW = 9, kOutputH = 7;
    std::uint32_t pixels[kInputW * kInputH];
    for (UINT y = 0; y < kInputH; ++y) {
        for (UINT x = 0; x < kInputW; ++x) {
            const std::uint8_t r = static_cast<std::uint8_t>(20 + x * 55);
            const std::uint8_t g = static_cast<std::uint8_t>(15 + y * 65);
            const std::uint8_t b = ((x + y) & 1) ? 210 : 35;
            pixels[y * kInputW + x] = 0xff000000u |
                                       (static_cast<std::uint32_t>(b) << 16) |
                                       (static_cast<std::uint32_t>(g) << 8) | r;
        }
    }

    D3D11_TEXTURE2D_DESC sourceDesc{};
    sourceDesc.Width = kInputW;
    sourceDesc.Height = kInputH;
    sourceDesc.MipLevels = 1;
    sourceDesc.ArraySize = 1;
    sourceDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    sourceDesc.SampleDesc.Count = 1;
    sourceDesc.Usage = D3D11_USAGE_DEFAULT;
    D3D11_SUBRESOURCE_DATA sourceData{pixels, kInputW * 4, 0};
    ID3D11Texture2D* source = nullptr;
    hr = device->CreateTexture2D(&sourceDesc, &sourceData, &source);

    D3D11_TEXTURE2D_DESC destinationDesc = sourceDesc;
    destinationDesc.Width = kOutputW;
    destinationDesc.Height = kOutputH;
    ID3D11Texture2D* destination = nullptr;
    if (SUCCEEDED(hr)) hr = device->CreateTexture2D(&destinationDesc, nullptr, &destination);

    D3D11_TEXTURE2D_DESC stagingDesc = destinationDesc;
    stagingDesc.Usage = D3D11_USAGE_STAGING;
    stagingDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    ID3D11Texture2D* staging = nullptr;
    if (SUCCEEDED(hr)) hr = device->CreateTexture2D(&stagingDesc, nullptr, &staging);
    if (FAILED(hr)) {
        std::printf("FAIL: test textures 0x%08X\n", static_cast<unsigned>(hr));
        release_one(staging);
        release_one(destination);
        release_one(source);
        release_one(context);
        release_one(device);
        return 2;
    }

    // Sentinel state verifies that the injected pass is transparent to the
    // game pipeline at the two most error-prone state points.
    D3D11_VIEWPORT sentinel{};
    sentinel.TopLeftX = 2.0f;
    sentinel.TopLeftY = 3.0f;
    sentinel.Width = 17.0f;
    sentinel.Height = 19.0f;
    sentinel.MinDepth = 0.1f;
    sentinel.MaxDepth = 0.9f;
    context->RSSetViewports(1, &sentinel);
    context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_LINELIST);
    D3D11_TEXTURE2D_DESC sentinelRtDesc = sourceDesc;
    sentinelRtDesc.Width = sentinelRtDesc.Height = 1;
    sentinelRtDesc.BindFlags = D3D11_BIND_RENDER_TARGET;
    ID3D11Texture2D* sentinelRtTextures[2] = {};
    ID3D11RenderTargetView* sentinelRtvs[2] = {};
    for (int i = 0; i < 2 && SUCCEEDED(hr); ++i) {
        hr = device->CreateTexture2D(&sentinelRtDesc, nullptr, &sentinelRtTextures[i]);
        if (SUCCEEDED(hr))
            hr = device->CreateRenderTargetView(sentinelRtTextures[i], nullptr,
                                                &sentinelRtvs[i]);
    }
    if (SUCCEEDED(hr)) context->OMSetRenderTargets(2, sentinelRtvs, nullptr);

    bool ok = SUCCEEDED(hr) && bvr::spatial_upscaler::prepare(
                  device, kInputW, kInputH, DXGI_FORMAT_R8G8B8A8_UNORM,
                  kOutputW, kOutputH, DXGI_FORMAT_R8G8B8A8_UNORM) &&
              bvr::spatial_upscaler::render(context, destination, source, 0.20f);
    if (ok) {
        UINT count = 1;
        D3D11_VIEWPORT after{};
        context->RSGetViewports(&count, &after);
        D3D11_PRIMITIVE_TOPOLOGY topology{};
        context->IAGetPrimitiveTopology(&topology);
        ID3D11RenderTargetView* afterRtvs[2] = {};
        context->OMGetRenderTargets(2, afterRtvs, nullptr);
        ok = count == 1 && topology == D3D11_PRIMITIVE_TOPOLOGY_LINELIST &&
             afterRtvs[0] == sentinelRtvs[0] && afterRtvs[1] == sentinelRtvs[1] &&
             nearly_equal(after.TopLeftX, sentinel.TopLeftX) &&
             nearly_equal(after.TopLeftY, sentinel.TopLeftY) &&
             nearly_equal(after.Width, sentinel.Width) &&
             nearly_equal(after.Height, sentinel.Height) &&
             nearly_equal(after.MinDepth, sentinel.MinDepth) &&
             nearly_equal(after.MaxDepth, sentinel.MaxDepth);
        release_one(afterRtvs[0]);
        release_one(afterRtvs[1]);
        if (!ok) std::printf("FAIL: D3D11 state was not restored\n");
    }

    // A second eye/pass reuses the same owned intermediates. Also exercise the
    // legal zero-viewport state; leaving our output viewport bound would alter
    // the game after a capture on such a frame.
    if (ok) {
        context->RSSetViewports(0, nullptr);
        ok = bvr::spatial_upscaler::render(context, destination, source, 0.10f);
        UINT afterCount = D3D11_VIEWPORT_AND_SCISSORRECT_OBJECT_COUNT_PER_PIPELINE;
        D3D11_VIEWPORT after[D3D11_VIEWPORT_AND_SCISSORRECT_OBJECT_COUNT_PER_PIPELINE] = {};
        context->RSGetViewports(&afterCount, after);
        ok = ok && afterCount == 0;
        if (!ok) std::printf("FAIL: consecutive pass / zero viewport restoration\n");
    }

    if (ok) {
        context->CopyResource(staging, destination);
        D3D11_MAPPED_SUBRESOURCE mapped{};
        hr = context->Map(staging, 0, D3D11_MAP_READ, 0, &mapped);
        ok = SUCCEEDED(hr);
        bool varied = false;
        std::uint32_t first = 0;
        if (ok) {
            for (UINT y = 0; y < kOutputH; ++y) {
                const auto* row = reinterpret_cast<const std::uint32_t*>(
                    static_cast<const std::uint8_t*>(mapped.pData) + y * mapped.RowPitch);
                for (UINT x = 0; x < kOutputW; ++x) {
                    if ((row[x] >> 24) != 0xffu) ok = false;
                    if (x == 0 && y == 0)
                        first = row[x];
                    else if (row[x] != first)
                        varied = true;
                }
            }
            context->Unmap(staging, 0);
            ok = ok && varied;
        }
        if (!ok) std::printf("FAIL: scaled output validation\n");
    }

    // At 1:1, the sRGB decode/encode path should preserve the game's stored
    // bytes (allow one code value for hardware rounding). This catches both a
    // missing decode and the much more visible double-gamma mistake.
    ID3D11Texture2D* gammaDestination = nullptr;
    ID3D11Texture2D* gammaStaging = nullptr;
    if (ok) {
        D3D11_TEXTURE2D_DESC gammaDesc = sourceDesc;
        gammaDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;
        hr = device->CreateTexture2D(&gammaDesc, nullptr, &gammaDestination);
        D3D11_TEXTURE2D_DESC gammaStagingDesc = gammaDesc;
        gammaStagingDesc.Usage = D3D11_USAGE_STAGING;
        gammaStagingDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        if (SUCCEEDED(hr))
            hr = device->CreateTexture2D(&gammaStagingDesc, nullptr, &gammaStaging);
        ok = SUCCEEDED(hr) && bvr::spatial_upscaler::prepare(
                                  device, kInputW, kInputH,
                                  DXGI_FORMAT_R8G8B8A8_UNORM, kInputW, kInputH,
                                  DXGI_FORMAT_R8G8B8A8_UNORM_SRGB) &&
             bvr::spatial_upscaler::render(context, gammaDestination, source, 0.0f);
        if (ok) {
            context->CopyResource(gammaStaging, gammaDestination);
            D3D11_MAPPED_SUBRESOURCE mapped{};
            hr = context->Map(gammaStaging, 0, D3D11_MAP_READ, 0, &mapped);
            ok = SUCCEEDED(hr);
            if (ok) {
                for (UINT y = 0; y < kInputH; ++y) {
                    const auto* row = reinterpret_cast<const std::uint32_t*>(
                        static_cast<const std::uint8_t*>(mapped.pData) + y * mapped.RowPitch);
                    for (UINT x = 0; x < kInputW; ++x) {
                        const std::uint32_t actual = row[x];
                        const std::uint32_t expected = pixels[y * kInputW + x];
                        for (int channel = 0; channel < 4; ++channel) {
                            const int a = static_cast<int>((actual >> (channel * 8)) & 0xffu);
                            const int e = static_cast<int>((expected >> (channel * 8)) & 0xffu);
                            if (std::abs(a - e) > 1) ok = false;
                        }
                    }
                }
                context->Unmap(gammaStaging, 0);
            }
        }
        if (!ok) std::printf("FAIL: 1:1 sRGB round-trip validation\n");
    }

    if (ok && benchmark) {
        constexpr UINT kLargeInput = 4056;
        constexpr UINT kLargeOutput = 4504;
        constexpr int kPasses = 8;
        D3D11_TEXTURE2D_DESC largeSourceDesc{};
        largeSourceDesc.Width = kLargeInput;
        largeSourceDesc.Height = kLargeInput;
        largeSourceDesc.MipLevels = 1;
        largeSourceDesc.ArraySize = 1;
        largeSourceDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        largeSourceDesc.SampleDesc.Count = 1;
        largeSourceDesc.Usage = D3D11_USAGE_DEFAULT;
        ID3D11Texture2D* largeSource = nullptr;
        ID3D11Texture2D* largeDestination = nullptr;
        hr = device->CreateTexture2D(&largeSourceDesc, nullptr, &largeSource);
        D3D11_TEXTURE2D_DESC largeDestinationDesc = largeSourceDesc;
        largeDestinationDesc.Width = kLargeOutput;
        largeDestinationDesc.Height = kLargeOutput;
        largeDestinationDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;
        if (SUCCEEDED(hr))
            hr = device->CreateTexture2D(&largeDestinationDesc, nullptr, &largeDestination);

        ID3D11Query* finished = nullptr;
        D3D11_QUERY_DESC queryDesc{D3D11_QUERY_EVENT, 0};
        if (SUCCEEDED(hr)) hr = device->CreateQuery(&queryDesc, &finished);
        ok = SUCCEEDED(hr) && bvr::spatial_upscaler::prepare(
                                  device, kLargeInput, kLargeInput,
                                  DXGI_FORMAT_R8G8B8A8_UNORM, kLargeOutput, kLargeOutput,
                                  DXGI_FORMAT_R8G8B8A8_UNORM_SRGB) &&
             bvr::spatial_upscaler::render(context, largeDestination, largeSource, 0.20f);
        if (ok) {
            auto waitForGpu = [&]() {
                const ULONGLONG deadline = GetTickCount64() + 10000;
                HRESULT queryResult = S_FALSE;
                while ((queryResult = context->GetData(finished, nullptr, 0, 0)) == S_FALSE &&
                       GetTickCount64() < deadline)
                    Sleep(0);
                return queryResult == S_OK;
            };
            context->End(finished);
            ok = waitForGpu();
            LARGE_INTEGER frequency{}, start{}, end{};
            QueryPerformanceFrequency(&frequency);
            QueryPerformanceCounter(&start);
            for (int pass = 0; pass < kPasses && ok; ++pass)
                ok = bvr::spatial_upscaler::render(
                    context, largeDestination, largeSource, 0.20f);
            context->End(finished);
            context->Flush();
            ok = ok && waitForGpu();
            QueryPerformanceCounter(&end);
            if (FAILED(device->GetDeviceRemovedReason())) ok = false;
            if (ok) {
                const double elapsedMs =
                    (end.QuadPart - start.QuadPart) * 1000.0 / frequency.QuadPart;
                std::printf("BENCH: %ux%u -> %ux%u sharpen 0.20: %.3f ms/eye "
                            "(GPU-complete wall time, %d passes)\n",
                            kLargeInput, kLargeInput, kLargeOutput, kLargeOutput,
                            elapsedMs / kPasses, kPasses);
            }
        }
        if (!ok) std::printf("FAIL: representative-resolution hardware pass\n");
        release_one(finished);
        release_one(largeDestination);
        release_one(largeSource);
    }

    // Fail-closed contract: an unrelated pixel family must never be accepted.
    if (ok && bvr::spatial_upscaler::prepare(
                  device, kInputW, kInputH, DXGI_FORMAT_B8G8R8A8_UNORM,
                  kOutputW, kOutputH, DXGI_FORMAT_R8G8B8A8_UNORM)) {
        std::printf("FAIL: unsupported source format was accepted\n");
        ok = false;
    }

    bvr::spatial_upscaler::release();
    context->OMSetRenderTargets(0, nullptr, nullptr);
    for (int i = 0; i < 2; ++i) {
        release_one(sentinelRtvs[i]);
        release_one(sentinelRtTextures[i]);
    }
    release_one(gammaStaging);
    release_one(gammaDestination);
    release_one(staging);
    release_one(destination);
    release_one(source);
    release_one(context);
    release_one(device);

    if (ok)
        std::printf("PASS: spatial upscaler %s test\n", hardware ? "hardware" : "WARP");
    else
        std::printf("FAIL: spatial upscaler %s test\n", hardware ? "hardware" : "WARP");
    return ok ? 0 : 3;
}
