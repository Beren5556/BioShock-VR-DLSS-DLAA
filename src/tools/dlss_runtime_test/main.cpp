// Opt-in NVIDIA integration test of the ACTUAL mod client, not a second IPC
// implementation. Only explicit test-owned data directories are used. No game,
// launcher, OpenXR session or personal configuration is opened.
#include "core/gfx/dlss45_client.cpp"
#include "core/gfx/image_control_policy.h"
#include <wrl/client.h>

namespace {
std::wstring testDataDirectory;
bvr::game::HostGame testGame = bvr::game::HostGame::Unknown;
unsigned checks = 0, failures = 0;
bool expect(bool ok, const char* what) {
    ++checks;
    if (!ok) ++failures;
    std::printf("%s: %s\n", ok ? "PASS" : "FAIL", what);
    return ok;
}
}
namespace bvr::log {
const wchar_t* data_dir() { return testDataDirectory.c_str(); }
void write(const char* format, ...) {
    va_list args; va_start(args, format); std::vprintf(format, args); va_end(args);
    std::puts("");
}
}
namespace bvr::game { HostGame detect_host_game() { return testGame; } }

namespace {
using Microsoft::WRL::ComPtr;
struct Surface {
    ComPtr<ID3D11Texture2D> texture;
    ComPtr<ID3D11RenderTargetView> rtv;
};
bool surface(ID3D11Device* device, UINT width, UINT height, DXGI_FORMAT format, Surface& out) {
    D3D11_TEXTURE2D_DESC desc{};
    desc.Width = width; desc.Height = height; desc.MipLevels = desc.ArraySize = 1;
    desc.Format = format; desc.SampleDesc.Count = 1; desc.Usage = D3D11_USAGE_DEFAULT;
    desc.BindFlags = D3D11_BIND_RENDER_TARGET;
    return SUCCEEDED(device->CreateTexture2D(&desc, nullptr, &out.texture)) &&
        SUCCEEDED(device->CreateRenderTargetView(out.texture.Get(), nullptr, &out.rtv));
}
bool frames(ID3D11Device* device, ID3D11DeviceContext* context,
            const bvr::image_controls::Settings& geometry) {
    Surface color, depth, motion, outputs[2];
    if (!surface(device, geometry.renderWidth, geometry.renderHeight, DXGI_FORMAT_R8G8B8A8_UNORM, color) ||
        !surface(device, geometry.renderWidth, geometry.renderHeight, DXGI_FORMAT_R32_FLOAT, depth) ||
        !surface(device, geometry.renderWidth, geometry.renderHeight, DXGI_FORMAT_R16G16_FLOAT, motion) ||
        !surface(device, geometry.outputWidth, geometry.outputHeight, DXGI_FORMAT_R8G8B8A8_UNORM, outputs[0]) ||
        !surface(device, geometry.outputWidth, geometry.outputHeight, DXGI_FORMAT_R8G8B8A8_UNORM, outputs[1]))
        return false;
    D3D11_TEXTURE2D_DESC readDesc{};
    readDesc.Width = readDesc.Height = 16; readDesc.MipLevels = readDesc.ArraySize = 1;
    readDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM; readDesc.SampleDesc.Count = 1;
    readDesc.Usage = D3D11_USAGE_STAGING; readDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    ComPtr<ID3D11Texture2D> readback;
    if (FAILED(device->CreateTexture2D(&readDesc, nullptr, &readback))) return false;
    const float depthValue[4] = {0.95f, 0, 0, 0}, noMotion[4] = {};
    context->ClearRenderTargetView(depth.rtv.Get(), depthValue);
    context->ClearRenderTargetView(motion.rtv.Get(), noMotion);
    for (unsigned frame = 0; frame < 12; ++frame) {
        for (int eye = 0; eye < 2; ++eye) {
            const float pixel[4] = {eye == 0 ? 0.8f : 0.02f, 0.04f, eye == 1 ? 0.8f : 0.02f, 1};
            context->ClearRenderTargetView(color.rtv.Get(), pixel);
            if (!bvr::dlss45::process_eye(context, eye, outputs[eye].texture.Get(), color.texture.Get(),
                                         depth.texture.Get(), motion.texture.Get(), frame == 0, 0, 0))
                return false;
            const UINT x = geometry.outputWidth / 2 - 8, y = geometry.outputHeight / 2 - 8;
            const D3D11_BOX box{x, y, 0, x + 16, y + 16, 1};
            context->CopySubresourceRegion(readback.Get(), 0, 0, 0, 0, outputs[eye].texture.Get(), 0, &box);
            D3D11_MAPPED_SUBRESOURCE mapped{};
            if (FAILED(context->Map(readback.Get(), 0, D3D11_MAP_READ, 0, &mapped))) return false;
            const BYTE* actual = static_cast<const BYTE*>(mapped.pData);
            const bool correctEye = eye == 0 ? actual[0] > 100 && actual[2] < 60
                                            : actual[2] > 100 && actual[0] < 60;
            context->Unmap(readback.Get(), 0);
            if (!correctEye) return false;
        }
    }
    return bvr::dlss45::gpu_retired();
}
}

int wmain(int argc, wchar_t** argv) {
    if (argc != 4 || (wcscmp(argv[1], L"bs1") && wcscmp(argv[1], L"bs2"))) {
        std::puts("Usage: dlss_runtime_test32 bs1|bs2 <verified-host-exe> <EXISTING-test-data-directory>");
        return 2;
    }
    testGame = wcscmp(argv[1], L"bs2") == 0 ? bvr::game::HostGame::Bioshock2 : bvr::game::HostGame::Bioshock1;
    wchar_t absolute[MAX_PATH]{};
    const DWORD count = GetFullPathNameW(argv[3], MAX_PATH, absolute, nullptr);
    if (!count || count >= MAX_PATH || !(GetFileAttributesW(absolute) & FILE_ATTRIBUTE_DIRECTORY) ||
        GetFileAttributesW(absolute) == INVALID_FILE_ATTRIBUTES) return 2;
    testDataDirectory = absolute;
    const std::wstring hostPath = argv[2];
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    D3D_FEATURE_LEVEL level{};
    if (!expect(SUCCEEDED(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0,
        nullptr, 0, D3D11_SDK_VERSION, &device, &level, &context)), "hardware D3D11 device")) return 1;
    using namespace bvr::image_controls;
    for (RenderMode mode : {RenderMode::Dlaa, RenderMode::Normal, RenderMode::Dlss,
                            RenderMode::Dlaa, RenderMode::Dlss}) {
        if (mode == RenderMode::Normal) {
            bvr::dlss45::release();
            if (!expect(!bvr::dlss45::ready() && !bvr::dlss45::bounded_wait_active(),
                        "NORMAL releases both helpers and ends bounded activity")) break;
            // Deliberately send the old invalid size to the actual client.
            const bool invalid = bvr::dlss45::prepare(device.Get(), 1474, 1474, DXGI_FORMAT_R8G8B8A8_UNORM,
                2950, 2950, bvr::dlss45::Mode::SuperResolution, false, hostPath.c_str());
            if (!expect(!invalid && !bvr::dlss45::ready() &&
                std::strstr(bvr::dlss45::status(), "outside the NVIDIA supported range") != nullptr,
                "out-of-range input is rejected with the specific NVIDIA range message")) break;
            continue;
        }
        Settings geometry{};
        geometry.mode = mode; geometry.outputWidth = geometry.outputHeight = 2950;
        geometry.srScale = {1, 2};
        if (!expect(bvr::image_controls::geometry(geometry), "production geometry is valid")) break;
        const auto bridgeMode = mode == RenderMode::Dlss ? bvr::dlss45::Mode::SuperResolution : bvr::dlss45::Mode::Dlaa;
        if (!expect(bvr::dlss45::prepare(device.Get(), geometry.renderWidth, geometry.renderHeight,
            DXGI_FORMAT_R8G8B8A8_UNORM, geometry.outputWidth, geometry.outputHeight, bridgeMode, false, hostPath.c_str()),
            mode == RenderMode::Dlss ? "actual mod client rebuilds DLSS" : "actual mod client rebuilds DLAA")) break;
        if (!expect(frames(device.Get(), context.Get(), geometry), "12 L/R pairs read back correctly; GPU retired")) break;
    }
    bvr::dlss45::release();
    expect(!bvr::dlss45::ready() && !bvr::dlss45::bounded_wait_active(), "final clean release");
    std::printf("Actual mod/NGX transitions (%ls): %u/%u checks passed. No game or OpenXR launched.\n",
                argv[1], checks - failures, checks);
    return failures ? 1 : 0;
}
