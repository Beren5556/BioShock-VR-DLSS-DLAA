#include "game/bioshock1r/temporal_guides.h"
#include "game/bioshock1r/camera.h"

#include <d3d11.h>
#include <d3d11sdklayers.h>

#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>

namespace test_camera {
float locations[2][3] = {};
int32_t rotations[2][3] = {};
bool valid[2] = {true, true};
std::uint64_t buildIds[2] = {};
std::uint32_t publications[2] = {1, 1};
}

// temporal_guides.cpp deliberately consumes the adapter's public publication
// seam. The WARP test supplies a deterministic implementation instead of
// linking the whole injected camera/hook graph.
namespace bvr::b1r::camera {
bool driven_eye_cam(int eye, float loc[3], int32_t rot[3]) {
    if (eye < 0 || eye > 1 || !test_camera::valid[eye]) return false;
    std::memcpy(loc, test_camera::locations[eye], sizeof(test_camera::locations[eye]));
    std::memcpy(rot, test_camera::rotations[eye], sizeof(test_camera::rotations[eye]));
    return true;
}

bool driven_eye_cam_for_build(int eye, std::uint64_t buildId, DrivenEyeCamera* out) {
    if (out) *out = {};
    if (!out || eye < 0 || eye > 1 || !test_camera::valid[eye] || !buildId ||
        test_camera::buildIds[eye] != buildId) {
        return false;
    }
    std::memcpy(out->location, test_camera::locations[eye],
                sizeof(test_camera::locations[eye]));
    std::memcpy(out->rotation, test_camera::rotations[eye],
                sizeof(test_camera::rotations[eye]));
    out->buildId = buildId;
    out->stampMs = GetTickCount64();
    out->publications = test_camera::publications[eye];
    return true;
}
} // namespace bvr::b1r::camera

namespace {

template <typename T>
void release_one(T*& value) {
    if (value) {
        value->Release();
        value = nullptr;
    }
}

struct DepthSurface {
    ID3D11Texture2D* texture = nullptr;
    ID3D11DepthStencilView* dsv = nullptr;
};

struct ColorSurface {
    ID3D11Texture2D* texture = nullptr;
    ID3D11RenderTargetView* rtv = nullptr;
};

bool make_depth(ID3D11Device* device, UINT width, UINT height,
                DXGI_FORMAT resourceFormat, DXGI_FORMAT dsvFormat,
                DepthSurface* out) {
    D3D11_TEXTURE2D_DESC desc{};
    desc.Width = width;
    desc.Height = height;
    desc.MipLevels = 1;
    desc.ArraySize = 1;
    desc.Format = resourceFormat;
    desc.SampleDesc.Count = 1;
    desc.Usage = D3D11_USAGE_DEFAULT;
    desc.BindFlags = D3D11_BIND_DEPTH_STENCIL;
    HRESULT hr = device->CreateTexture2D(&desc, nullptr, &out->texture);
    D3D11_DEPTH_STENCIL_VIEW_DESC view{};
    view.Format = dsvFormat;
    view.ViewDimension = D3D11_DSV_DIMENSION_TEXTURE2D;
    if (SUCCEEDED(hr)) hr = device->CreateDepthStencilView(out->texture, &view, &out->dsv);
    return SUCCEEDED(hr);
}

bool make_mip1_depth(ID3D11Device* device, UINT width, UINT height,
                     DepthSurface* out) {
    D3D11_TEXTURE2D_DESC desc{};
    desc.Width = width;
    desc.Height = height;
    desc.MipLevels = 2;
    desc.ArraySize = 1;
    desc.Format = DXGI_FORMAT_R24G8_TYPELESS;
    desc.SampleDesc.Count = 1;
    desc.Usage = D3D11_USAGE_DEFAULT;
    desc.BindFlags = D3D11_BIND_DEPTH_STENCIL;
    HRESULT hr = device->CreateTexture2D(&desc, nullptr, &out->texture);
    D3D11_DEPTH_STENCIL_VIEW_DESC view{};
    view.Format = DXGI_FORMAT_D24_UNORM_S8_UINT;
    view.ViewDimension = D3D11_DSV_DIMENSION_TEXTURE2D;
    view.Texture2D.MipSlice = 1;
    if (SUCCEEDED(hr)) hr = device->CreateDepthStencilView(out->texture, &view, &out->dsv);
    return SUCCEEDED(hr);
}

bool make_color(ID3D11Device* device, UINT width, UINT height, ColorSurface* out) {
    D3D11_TEXTURE2D_DESC desc{};
    desc.Width = width;
    desc.Height = height;
    desc.MipLevels = 1;
    desc.ArraySize = 1;
    desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.Usage = D3D11_USAGE_DEFAULT;
    desc.BindFlags = D3D11_BIND_RENDER_TARGET;
    HRESULT hr = device->CreateTexture2D(&desc, nullptr, &out->texture);
    if (SUCCEEDED(hr)) hr = device->CreateRenderTargetView(out->texture, nullptr, &out->rtv);
    return SUCCEEDED(hr);
}

void destroy_depth(DepthSurface& surface) {
    release_one(surface.dsv);
    release_one(surface.texture);
}

void destroy_color(ColorSurface& surface) {
    release_one(surface.rtv);
    release_one(surface.texture);
}

void bind_color_depth(ID3D11DeviceContext* context, ID3D11RenderTargetView* rtv,
                      ID3D11DepthStencilView* dsv) {
    context->OMSetRenderTargets(1, &rtv, dsv);
    bvr::b1r::temporal_guides::on_setrt(context, 1, &rtv, dsv);
}

void bind_depth(ID3D11DeviceContext* context, ID3D11DepthStencilView* dsv) {
    // Match the production hook's post-original ordering.
    context->OMSetRenderTargets(0, nullptr, dsv);
    bvr::b1r::temporal_guides::on_setrt(context, 0, nullptr, dsv);
}

void vote(unsigned count, ID3D11DeviceContext* context) {
    for (unsigned i = 0; i < count; ++i)
        bvr::b1r::temporal_guides::on_draw_indexed(context);
}

void clear_depth(ID3D11DeviceContext* context, DepthSurface& surface, float depth) {
    context->ClearDepthStencilView(surface.dsv, D3D11_CLEAR_DEPTH, depth, 0);
    bvr::b1r::temporal_guides::on_clear_dsv(
        context, surface.dsv, D3D11_CLEAR_DEPTH, depth, 0);
}

bvr::b1r::temporal_guides::Projection tagged_projection(
    int eye, std::uint64_t buildId, float tanX = 1.0f, float tanY = 0.75f) {
    test_camera::buildIds[eye] = buildId;
    test_camera::publications[eye] = 1;
    bvr::b1r::temporal_guides::Projection result{};
    result.tanHalfFovX = tanX;
    result.tanHalfFovY = tanY;
    result.buildId = buildId;
    result.observedTanHalfFovX = tanX;
    result.observedTanHalfFovY = tanY;
    result.observedValid = true;
    return result;
}

bool capture_interval(ID3D11DeviceContext* context, DepthSurface& minor,
                      float minorDepth, unsigned minorVotes, DepthSurface& main,
                      float mainDepth, unsigned mainVotes) {
    bind_depth(context, minor.dsv);
    clear_depth(context, minor, minorDepth);
    vote(minorVotes, context);
    bind_depth(context, main.dsv);
    clear_depth(context, main, mainDepth);
    vote(mainVotes, context);
    bind_depth(context, nullptr); // queues the safe copy of the winning DSV
    return true;
}

float half_to_float(std::uint16_t half) {
    const std::uint32_t sign = static_cast<std::uint32_t>(half & 0x8000u) << 16;
    std::uint32_t exponent = (half >> 10) & 0x1fu;
    std::uint32_t mantissa = half & 0x03ffu;
    std::uint32_t bits = 0;
    if (exponent == 0) {
        if (mantissa == 0) {
            bits = sign;
        } else {
            int shift = 0;
            while ((mantissa & 0x0400u) == 0) {
                mantissa <<= 1;
                ++shift;
            }
            mantissa &= 0x03ffu;
            const std::uint32_t floatExp = static_cast<std::uint32_t>(127 - 15 - shift);
            bits = sign | (floatExp << 23) | (mantissa << 13);
        }
    } else if (exponent == 31) {
        bits = sign | 0x7f800000u | (mantissa << 13);
    } else {
        bits = sign | ((exponent + (127 - 15)) << 23) | (mantissa << 13);
    }
    float value = 0.0f;
    std::memcpy(&value, &bits, sizeof(value));
    return value;
}

bool read_depth(ID3D11Device* device, ID3D11DeviceContext* context,
                ID3D11Texture2D* source, UINT x, UINT y, float* value) {
    D3D11_TEXTURE2D_DESC desc{};
    source->GetDesc(&desc);
    desc.Usage = D3D11_USAGE_STAGING;
    desc.BindFlags = 0;
    desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    ID3D11Texture2D* staging = nullptr;
    HRESULT hr = device->CreateTexture2D(&desc, nullptr, &staging);
    if (SUCCEEDED(hr)) {
        context->CopyResource(staging, source);
        D3D11_MAPPED_SUBRESOURCE mapped{};
        hr = context->Map(staging, 0, D3D11_MAP_READ, 0, &mapped);
        if (SUCCEEDED(hr)) {
            const auto* row = reinterpret_cast<const std::uint8_t*>(mapped.pData) +
                              y * mapped.RowPitch;
            *value = reinterpret_cast<const float*>(row)[x];
            context->Unmap(staging, 0);
        }
    }
    release_one(staging);
    return SUCCEEDED(hr);
}

bool read_motion(ID3D11Device* device, ID3D11DeviceContext* context,
                 ID3D11Texture2D* source, UINT x, UINT y, float* mx, float* my) {
    D3D11_TEXTURE2D_DESC desc{};
    source->GetDesc(&desc);
    desc.Usage = D3D11_USAGE_STAGING;
    desc.BindFlags = 0;
    desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    ID3D11Texture2D* staging = nullptr;
    HRESULT hr = device->CreateTexture2D(&desc, nullptr, &staging);
    if (SUCCEEDED(hr)) {
        context->CopyResource(staging, source);
        D3D11_MAPPED_SUBRESOURCE mapped{};
        hr = context->Map(staging, 0, D3D11_MAP_READ, 0, &mapped);
        if (SUCCEEDED(hr)) {
            const auto* row = reinterpret_cast<const std::uint8_t*>(mapped.pData) +
                              y * mapped.RowPitch;
            const auto* pair = reinterpret_cast<const std::uint16_t*>(row) + x * 2;
            *mx = half_to_float(pair[0]);
            *my = half_to_float(pair[1]);
            context->Unmap(staging, 0);
        }
    }
    release_one(staging);
    return SUCCEEDED(hr);
}

bool almost(float a, float b, float tolerance = 0.002f) {
    return std::fabs(a - b) <= tolerance;
}

bool debug_layer_clean(ID3D11Device* device, bool printAll) {
    ID3D11InfoQueue* queue = nullptr;
    if (FAILED(device->QueryInterface(IID_PPV_ARGS(&queue))) || !queue) return true;
    bool clean = true;
    const UINT64 count = queue->GetNumStoredMessagesAllowedByRetrievalFilter();
    for (UINT64 i = 0; i < count; ++i) {
        SIZE_T bytes = 0;
        queue->GetMessage(i, nullptr, &bytes);
        auto* storage = new std::uint8_t[bytes];
        auto* message = reinterpret_cast<D3D11_MESSAGE*>(storage);
        if (SUCCEEDED(queue->GetMessage(i, message, &bytes))) {
            const bool actionable = message->Severity == D3D11_MESSAGE_SEVERITY_CORRUPTION ||
                                    message->Severity == D3D11_MESSAGE_SEVERITY_ERROR ||
                                    message->Severity == D3D11_MESSAGE_SEVERITY_WARNING;
            if (actionable) clean = false;
            if (printAll || actionable) std::printf("D3D11: %s\n", message->pDescription);
        }
        delete[] storage;
    }
    queue->Release();
    return clean;
}

bool generate_frame(ID3D11DeviceContext* context, DepthSurface& minor,
                    DepthSurface& main, int eye, std::uint64_t buildId,
                    float rawDepth,
                    bvr::b1r::temporal_guides::EyeGuides* guides) {
    capture_interval(context, minor, 0.10f, 1, main, rawDepth, 5);
    const auto projection = tagged_projection(eye, buildId);
    return bvr::b1r::temporal_guides::generate_eye(
        context, eye, projection, guides);
}

bool rotation_case(ID3D11Device* device, ID3D11DeviceContext* context,
                   DepthSurface& minor, DepthSurface& main,
                   std::uint64_t buildBase, int rotationAxis,
                   UINT sampleX, UINT sampleY, int expectedComponent,
                   int expectedSign, const char* name) {
    bvr::b1r::temporal_guides::invalidate_eye(0);
    std::memset(test_camera::locations[0], 0, sizeof(test_camera::locations[0]));
    std::memset(test_camera::rotations[0], 0, sizeof(test_camera::rotations[0]));
    test_camera::valid[0] = true;

    bvr::b1r::temporal_guides::EyeGuides baseline{};
    if (!generate_frame(context, minor, main, 0, buildBase, 0.50f, &baseline))
        return false;

    // Ten degrees in Unreal's 16-bit FRotator units.
    test_camera::rotations[0][rotationAxis] = 1820;
    bvr::b1r::temporal_guides::EyeGuides rotated{};
    float mx = 0.0f, my = 0.0f;
    const bool generated = generate_frame(context, minor, main, 0, buildBase + 2,
                                          0.50f, &rotated);
    const bool read = generated && read_motion(device, context, rotated.motionTexture,
                                               sampleX, sampleY, &mx, &my);
    const float component = expectedComponent == 0 ? mx : my;
    const bool ok = read && rotated.historyValid &&
                    component * static_cast<float>(expectedSign) > 1.0f;
    if (!ok)
        std::printf("FAIL: %s reprojection direction (mv %.3f %.3f)\n", name, mx, my);
    return ok;
}

bool finite_depth_case(ID3D11Device* device, ID3D11DeviceContext* context,
                       DepthSurface& minor, DepthSurface& main,
                       UINT width, UINT height, float nearPlane, float farPlane,
                       bool inverted, float rawDepth, std::uint64_t buildBase,
                       const char* name) {
    bvr::b1r::temporal_guides::PrepareDesc prepare{};
    prepare.width = width;
    prepare.height = height;
    prepare.nearPlane = nearPlane;
    prepare.farPlane = farPlane;
    prepare.depthInverted = inverted;
    if (!bvr::b1r::temporal_guides::prepare(device, prepare)) return false;

    std::memset(test_camera::locations[0], 0, sizeof(test_camera::locations[0]));
    std::memset(test_camera::rotations[0], 0, sizeof(test_camera::rotations[0]));
    test_camera::valid[0] = true;
    bvr::b1r::temporal_guides::EyeGuides baseline{};
    if (!generate_frame(context, minor, main, 0, buildBase, rawDepth, &baseline))
        return false;

    test_camera::locations[0][1] = 2.0f;
    bvr::b1r::temporal_guides::EyeGuides moved{};
    float depth = 0.0f, mx = 0.0f, my = 0.0f;
    const bool generated = generate_frame(context, minor, main, 0, buildBase + 2,
                                          rawDepth, &moved);
    const bool read = generated &&
        read_depth(device, context, moved.depthTexture, width / 2, height / 2, &depth) &&
        read_motion(device, context, moved.motionTexture, width / 2, height / 2,
                    &mx, &my);
    const float conventional = inverted ? 1.0f - rawDepth : rawDepth;
    const float viewZ = nearPlane * farPlane /
        (farPlane - conventional * (farPlane - nearPlane));
    const float expectedX = static_cast<float>(width) / viewZ;
    const bool ok = read && moved.historyValid && almost(depth, rawDepth, 0.002f) &&
                    almost(mx, expectedX, 0.035f) && almost(my, 0.0f, 0.025f);
    if (!ok)
        std::printf("FAIL: %s finite-depth reprojection expected %.4f got "
                    "depth %.4f mv %.4f %.4f\n",
                    name, expectedX, depth, mx, my);
    return ok;
}

} // namespace

int main() {
    constexpr UINT kWidth = 64;
    constexpr UINT kHeight = 48;
    ID3D11Device* device = nullptr;
    ID3D11DeviceContext* context = nullptr;
    D3D_FEATURE_LEVEL level{};
    const D3D_FEATURE_LEVEL requested[] = {D3D_FEATURE_LEVEL_11_0};
    HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr,
                                   D3D11_CREATE_DEVICE_DEBUG,
                                   requested, 1, D3D11_SDK_VERSION,
                                   &device, &level, &context);
    if (FAILED(hr)) {
        hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, 0,
                               requested, 1, D3D11_SDK_VERSION,
                               &device, &level, &context);
    }
    if (FAILED(hr)) {
        std::printf("FAIL: WARP device 0x%08X\n", static_cast<unsigned>(hr));
        return 1;
    }

    DepthSurface minor{}, main{}, unsupported{}, unsupportedMip{};
    ColorSurface sentinelColor{};
    bool ok = make_depth(device, kWidth, kHeight, DXGI_FORMAT_R24G8_TYPELESS,
                         DXGI_FORMAT_D24_UNORM_S8_UINT, &minor) &&
              make_depth(device, kWidth, kHeight, DXGI_FORMAT_R24G8_TYPELESS,
                         DXGI_FORMAT_D24_UNORM_S8_UINT, &main) &&
              make_depth(device, kWidth, kHeight, DXGI_FORMAT_R32_TYPELESS,
                         DXGI_FORMAT_D32_FLOAT, &unsupported) &&
              make_mip1_depth(device, kWidth, kHeight, &unsupportedMip) &&
              make_color(device, kWidth, kHeight, &sentinelColor);

    bvr::b1r::temporal_guides::PrepareDesc prepare{};
    prepare.width = kWidth;
    prepare.height = kHeight;
    prepare.nearPlane = 10.0f;
    prepare.farPlane = 65536.0f;
    prepare.depthInverted = false;
    ok = ok && bvr::b1r::temporal_guides::prepare(device, prepare);

    // Keep a sentinel CS constant buffer bound; generate_eye must restore it.
    D3D11_BUFFER_DESC sentinelDesc{};
    sentinelDesc.ByteWidth = 16;
    sentinelDesc.Usage = D3D11_USAGE_DEFAULT;
    sentinelDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    ID3D11Buffer* sentinel = nullptr;
    if (ok) ok = SUCCEEDED(device->CreateBuffer(&sentinelDesc, nullptr, &sentinel));
    if (ok) context->CSSetConstantBuffers(0, 1, &sentinel);

    const bvr::b1r::temporal_guides::Projection projection =
        tagged_projection(0, 1);
    bvr::b1r::temporal_guides::EyeGuides left0{};
    if (ok) {
        // BioShock's real tonemap/HUD path rebinds its color target but leaves
        // the scene DSV attached. generate_eye must temporarily detach that DSV
        // for the copy and restore both bindings exactly afterwards.
        bind_depth(context, minor.dsv);
        clear_depth(context, minor, 0.20f);
        vote(2, context);
        bind_color_depth(context, sentinelColor.rtv, main.dsv);
        clear_depth(context, main, 0.70f);
        vote(9, context);
        ok = bvr::b1r::temporal_guides::generate_eye(context, 0, projection, &left0);
    }
    if (ok) {
        ID3D11RenderTargetView* afterRtv = nullptr;
        ID3D11DepthStencilView* afterDsv = nullptr;
        context->OMGetRenderTargets(1, &afterRtv, &afterDsv);
        ok = afterRtv == sentinelColor.rtv && afterDsv == main.dsv;
        release_one(afterDsv);
        release_one(afterRtv);
        if (!ok) std::printf("FAIL: bound DSV/RTV state was not restored\n");
    }
    float depth = 0.0f, mx = 0.0f, my = 0.0f;
    if (ok) {
        ok = left0.depthTexture && left0.motionTexture && left0.resetRequired &&
             !left0.historyValid && left0.cameraValid && left0.generation == 1 &&
             left0.coherent && left0.buildId == 1 && left0.cameraBuildId == 1 &&
             left0.captureId != 0 && left0.depthDraws == 9 &&
             left0.depthClears == 1 && left0.depthClearSeen &&
             almost(left0.depthClearValue, 0.70f) &&
             left0.depthViewIdentity ==
                 reinterpret_cast<std::uintptr_t>(main.dsv) &&
             read_depth(device, context, left0.depthTexture, kWidth / 2, kHeight / 2,
                        &depth) &&
             read_motion(device, context, left0.motionTexture, kWidth / 2, kHeight / 2,
                         &mx, &my) &&
             almost(depth, 0.70f) && almost(mx, 0.0f) && almost(my, 0.0f);
        if (!ok)
            std::printf("FAIL: DSV vote/clear/build telemetry / D24 conversion/reset "
                        "(depth %.5f mv %.3f %.3f)\n", depth, mx, my);
    }

    // Move the left camera two UE units to its right. Static geometry should
    // map to a positive previous-frame X offset; the exact centre value is
    // 64 * 0.5 * 2 / viewZ (viewZ=20 at depth .5) = 3.2 pixels.
    bvr::b1r::temporal_guides::EyeGuides left1{};
    if (ok) {
        test_camera::locations[0][1] = 2.0f;
        capture_interval(context, minor, 0.10f, 1, main, 0.50f, 5);
        const auto left1Projection = tagged_projection(0, 3);
        ok = bvr::b1r::temporal_guides::generate_eye(
                 context, 0, left1Projection, &left1) &&
             read_motion(device, context, left1.motionTexture, kWidth / 2, kHeight / 2,
                         &mx, &my) &&
             left1.historyValid && !left1.resetRequired && left1.generation == 2 &&
             almost(mx, 3.2f, 0.05f) && almost(my, 0.0f, 0.02f);
        if (!ok)
            std::printf("FAIL: camera reprojection (expected +3.2,0; got %.3f,%.3f)\n",
                        mx, my);
    }

    // Right eye has never generated, so it must not inherit the left history.
    bvr::b1r::temporal_guides::EyeGuides right0{};
    if (ok) {
        capture_interval(context, minor, 0.30f, 1, main, 0.60f, 4);
        const auto right0Projection = tagged_projection(1, 2);
        ok = bvr::b1r::temporal_guides::generate_eye(
                 context, 1, right0Projection, &right0) &&
             read_motion(device, context, right0.motionTexture, kWidth / 2, kHeight / 2,
                         &mx, &my) &&
             !right0.historyValid && right0.resetRequired && right0.generation == 1 &&
             almost(mx, 0.0f) && almost(my, 0.0f);
        if (!ok) std::printf("FAIL: right eye inherited left-eye history\n");
    }

    // Generating right must not overwrite left's resources or metadata.
    bvr::b1r::temporal_guides::EyeGuides leftAgain{};
    if (ok) {
        ok = bvr::b1r::temporal_guides::get_eye(0, &leftAgain) &&
             leftAgain.motionTexture == left1.motionTexture &&
             leftAgain.motionTexture != right0.motionTexture &&
             leftAgain.generation == 2 && leftAgain.historyValid &&
             read_motion(device, context, leftAgain.motionTexture,
                         kWidth / 2, kHeight / 2, &mx, &my) &&
             almost(mx, 3.2f, 0.05f);
        if (!ok) std::printf("FAIL: per-eye output resources are not independent\n");
    }

    if (ok) {
        ID3D11Buffer* after = nullptr;
        context->CSGetConstantBuffers(0, 1, &after);
        ok = after == sentinel;
        release_one(after);
        if (!ok) std::printf("FAIL: compute state was not restored\n");
    }

    if (ok) {
        bvr::b1r::temporal_guides::Diagnostics diagnostics{};
        bvr::b1r::temporal_guides::get_diagnostics(&diagnostics);
        ok = diagnostics.depthCopies >= 3 && diagnostics.eyes[0].generated == 2 &&
             diagnostics.eyes[1].generated == 1 &&
             diagnostics.eyes[0].lastBuildId == 3 &&
             diagnostics.eyes[1].lastBuildId == 2 &&
             diagnostics.nearPlane == 10.0f && diagnostics.farPlane == 65536.0f &&
             !diagnostics.depthInverted;
        if (!ok) std::printf("FAIL: bounded guide diagnostics are inconsistent\n");
    }

    // Projection changes are a mandatory temporal reset even if the caller
    // misses the explicit invalidate() edge.
    if (ok) {
        const auto changedProjection = tagged_projection(0, 5, 1.10f, 0.75f);
        bvr::b1r::temporal_guides::EyeGuides fovReset{};
        capture_interval(context, minor, 0.20f, 1, main, 0.50f, 4);
        ok = bvr::b1r::temporal_guides::generate_eye(
                 context, 0, changedProjection, &fovReset) &&
             !fovReset.historyValid && fovReset.resetRequired &&
             read_motion(device, context, fovReset.motionTexture,
                         kWidth / 2, kHeight / 2, &mx, &my) &&
             almost(mx, 0.0f) && almost(my, 0.0f);
        if (!ok) std::printf("FAIL: projection change did not reset history\n");
    }

    // The diagnostic contract never guesses across an absent tag, missing or
    // multiply-published camera, or a WORLD-projection disagreement.
    if (ok) {
        capture_interval(context, minor, 0.20f, 1, main, 0.50f, 4);
        bvr::b1r::temporal_guides::Projection untagged{};
        untagged.tanHalfFovX = 1.0f;
        untagged.tanHalfFovY = 0.75f;
        bvr::b1r::temporal_guides::EyeGuides rejected{};
        bvr::b1r::temporal_guides::Diagnostics diagnostics{};
        ok = !bvr::b1r::temporal_guides::generate_eye(
            context, 0, untagged, &rejected);
        bvr::b1r::temporal_guides::get_diagnostics(&diagnostics);
        ok = ok && diagnostics.eyes[0].lastReject ==
                       bvr::b1r::temporal_guides::RejectReason::MissingBuildTag;
        if (!ok) std::printf("FAIL: missing Build tag was not rejected/diagnosed\n");
    }
    if (ok) {
        capture_interval(context, minor, 0.20f, 1, main, 0.50f, 4);
        const auto missingCamera = tagged_projection(0, 7);
        test_camera::valid[0] = false;
        bvr::b1r::temporal_guides::EyeGuides rejected{};
        bvr::b1r::temporal_guides::Diagnostics diagnostics{};
        ok = !bvr::b1r::temporal_guides::generate_eye(
            context, 0, missingCamera, &rejected);
        test_camera::valid[0] = true;
        bvr::b1r::temporal_guides::get_diagnostics(&diagnostics);
        ok = ok && diagnostics.eyes[0].lastReject ==
                       bvr::b1r::temporal_guides::RejectReason::MissingCamera;
        if (!ok) std::printf("FAIL: missing exact camera was not rejected/diagnosed\n");
    }
    if (ok) {
        capture_interval(context, minor, 0.20f, 1, main, 0.50f, 4);
        const auto ambiguousCamera = tagged_projection(0, 9);
        test_camera::publications[0] = 2;
        bvr::b1r::temporal_guides::EyeGuides rejected{};
        bvr::b1r::temporal_guides::Diagnostics diagnostics{};
        ok = !bvr::b1r::temporal_guides::generate_eye(
            context, 0, ambiguousCamera, &rejected);
        test_camera::publications[0] = 1;
        bvr::b1r::temporal_guides::get_diagnostics(&diagnostics);
        ok = ok && diagnostics.eyes[0].lastReject ==
                       bvr::b1r::temporal_guides::RejectReason::AmbiguousCamera;
        if (!ok) std::printf("FAIL: duplicate camera publication was not rejected\n");
    }
    if (ok) {
        capture_interval(context, minor, 0.20f, 1, main, 0.50f, 4);
        auto mismatchedProjection = tagged_projection(0, 11);
        mismatchedProjection.observedTanHalfFovX = 1.10f;
        bvr::b1r::temporal_guides::EyeGuides rejected{};
        bvr::b1r::temporal_guides::Diagnostics diagnostics{};
        ok = !bvr::b1r::temporal_guides::generate_eye(
            context, 0, mismatchedProjection, &rejected);
        bvr::b1r::temporal_guides::get_diagnostics(&diagnostics);
        ok = ok && diagnostics.eyes[0].lastReject ==
                       bvr::b1r::temporal_guides::RejectReason::ProjectionMismatch;
        if (!ok) std::printf("FAIL: live projection mismatch was not rejected\n");
    }

    // A dropped same-eye generation is accepted only as a Reset frame; a
    // repeated/out-of-order id is rejected outright.
    if (ok) {
        bvr::b1r::temporal_guides::EyeGuides seeded{}, gap{};
        ok = generate_frame(context, minor, main, 0, 13, 0.50f, &seeded) &&
             generate_frame(context, minor, main, 0, 17, 0.50f, &gap) &&
             gap.resetRequired && !gap.historyValid;
        bvr::b1r::temporal_guides::Diagnostics diagnostics{};
        bvr::b1r::temporal_guides::get_diagnostics(&diagnostics);
        ok = ok && diagnostics.eyes[0].sequenceDiscontinuities != 0;
        if (!ok) std::printf("FAIL: Build sequence gap was not reset/diagnosed\n");
    }
    if (ok) {
        capture_interval(context, minor, 0.20f, 1, main, 0.50f, 4);
        const auto repeated = tagged_projection(0, 17);
        bvr::b1r::temporal_guides::EyeGuides rejected{};
        bvr::b1r::temporal_guides::Diagnostics diagnostics{};
        ok = !bvr::b1r::temporal_guides::generate_eye(
            context, 0, repeated, &rejected);
        bvr::b1r::temporal_guides::get_diagnostics(&diagnostics);
        ok = ok && diagnostics.eyes[0].lastReject ==
                       bvr::b1r::temporal_guides::RejectReason::OutOfOrderBuild;
        if (!ok) std::printf("FAIL: repeated Build id was not rejected\n");
    }

    if (ok)
        ok = rotation_case(device, context, minor, main, 101, 1,
                           kWidth / 2, kHeight / 2, 0, +1, "yaw");
    if (ok)
        ok = rotation_case(device, context, minor, main, 105, 0,
                           kWidth / 2, kHeight / 2, 1, -1, "pitch");
    if (ok)
        ok = rotation_case(device, context, minor, main, 109, 2,
                           3 * kWidth / 4, kHeight / 2, 1, +1, "roll");

    // Unsupported D32 is ignored, not sampled through an invalid SRV/copy.
    if (ok) {
        bind_depth(context, unsupported.dsv);
        vote(20, context);
        bind_depth(context, nullptr);
        bvr::b1r::temporal_guides::EyeGuides rejected{};
        const auto rejectedProjection = tagged_projection(0, 201);
        ok = !bvr::b1r::temporal_guides::generate_eye(
            context, 0, rejectedProjection, &rejected);
        if (!ok) std::printf("FAIL: unsupported D32 pass was accepted\n");
    }

    // The implementation copies source subresource 0, so a DSV selecting mip 1
    // must fail soft instead of silently converting the wrong depth image.
    if (ok) {
        bind_depth(context, unsupportedMip.dsv);
        vote(20, context);
        bind_depth(context, nullptr);
        bvr::b1r::temporal_guides::EyeGuides rejected{};
        const auto rejectedProjection = tagged_projection(0, 203);
        ok = !bvr::b1r::temporal_guides::generate_eye(
            context, 0, rejectedProjection, &rejected);
        if (!ok) std::printf("FAIL: mip-1 DSV view was accepted\n");
    }

    // Global D3D hooks also see deferred contexts. Their taps must not pollute
    // the immediate-context interval used by Present.
    if (ok) {
        ID3D11DeviceContext* deferred = nullptr;
        ok = SUCCEEDED(device->CreateDeferredContext(0, &deferred)) && deferred;
        if (ok) {
            bvr::b1r::temporal_guides::on_setrt(deferred, 0, nullptr, main.dsv);
            bvr::b1r::temporal_guides::on_draw_indexed(deferred);
            bvr::b1r::temporal_guides::EyeGuides rejected{};
            const auto rejectedProjection = tagged_projection(0, 205);
            ok = !bvr::b1r::temporal_guides::generate_eye(
                context, 0, rejectedProjection, &rejected);
        }
        release_one(deferred);
        if (!ok) std::printf("FAIL: deferred-context tap polluted the interval\n");
    }

    // Binary audit of BioShockHD.exe found the finite D3D projection branches
    // at far=1024 and far=65536, with near=10. Exercise both, a changed near,
    // and the explicit inverted compatibility switch (production uses normal).
    if (ok)
        ok = finite_depth_case(device, context, minor, main, kWidth, kHeight,
                               10.0f, 1024.0f, false, 0.99f, 301,
                               "standard far=1024");
    if (ok)
        ok = finite_depth_case(device, context, minor, main, kWidth, kHeight,
                               10.0f, 65536.0f, false, 0.99f, 305,
                               "standard far=65536");
    if (ok)
        ok = finite_depth_case(device, context, minor, main, kWidth, kHeight,
                               5.0f, 1024.0f, false, 0.50f, 309,
                               "finite near=5");
    if (ok)
        ok = finite_depth_case(device, context, minor, main, kWidth, kHeight,
                               10.0f, 1024.0f, true, 0.25f, 313,
                               "explicit reversed compatibility");

    if (ok) {
        bvr::b1r::temporal_guides::invalidate();
        bvr::b1r::temporal_guides::EyeGuides stale{};
        ok = !bvr::b1r::temporal_guides::get_eye(0, &stale) &&
             !bvr::b1r::temporal_guides::get_eye(1, &stale);
        if (!ok) std::printf("FAIL: invalidate retained a generated eye\n");
    }

    const bool d3dClean = debug_layer_clean(device, !ok);
    if (ok && !d3dClean) {
        std::printf("FAIL: D3D11 debug layer reported a warning/error\n");
        ok = false;
    }
    bvr::b1r::temporal_guides::shutdown();
    context->OMSetRenderTargets(0, nullptr, nullptr);
    context->CSSetConstantBuffers(0, 0, nullptr);
    release_one(sentinel);
    destroy_color(sentinelColor);
    destroy_depth(unsupportedMip);
    destroy_depth(unsupported);
    destroy_depth(main);
    destroy_depth(minor);
    release_one(context);
    release_one(device);

    std::printf("%s: BioShock 1 temporal-guides WARP test\n", ok ? "PASS" : "FAIL");
    return ok ? 0 : 2;
}
