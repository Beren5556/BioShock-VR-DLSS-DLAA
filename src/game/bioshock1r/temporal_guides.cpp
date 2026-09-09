#include "game/bioshock1r/temporal_guides.h"
#include "core/gfx/sampled_gpu_timer.h"

#include "core/util/log.h"
#include "game/bioshock1r/camera.h"
#include "game/shared/ue_math.h"

#include <d3dcompiler.h>

#include <atomic>
#include <cmath>
#include <cstring>

namespace bvr::b1r::temporal_guides {
namespace {

constexpr int kEyeCount = 2;
constexpr int kMaxCandidates = 8;

const char* kGuideShader = R"(
Texture2D<float> capturedDepth : register(t0);
RWTexture2D<float> convertedDepth : register(u0);
RWTexture2D<float2> motionVectors : register(u1);

cbuffer GuideCB : register(b0) {
    float4 currentLocationNear;
    float4 currentForwardTanX;
    float4 currentRightTanY;
    float4 currentUpHasHistory;
    float4 previousLocationFar;
    float4 previousForwardTanX;
    float4 previousRightTanY;
    float4 previousUp;
    float4 imageAndFlags; // width, height, depthInverted, unused
};

[numthreads(8, 8, 1)]
void cs_main(uint3 dispatchId : SV_DispatchThreadID) {
    uint2 pixel = dispatchId.xy;
    uint width = (uint)imageAndFlags.x;
    uint height = (uint)imageAndFlags.y;
    if (pixel.x >= width || pixel.y >= height) return;

    // Sampling R24_UNORM_X8_TYPELESS performs the required 24-bit normalized
    // conversion. Store the hardware-depth value unchanged for NGX; only the
    // reconstruction below converts it into view distance.
    float rawDepth = capturedDepth.Load(int3(pixel, 0));
    convertedDepth[pixel] = rawDepth;
    if (currentUpHasHistory.w < 0.5f) {
        motionVectors[pixel] = 0.0f;
        return;
    }

    float conventionalDepth = imageAndFlags.z > 0.5f ? 1.0f - rawDepth : rawDepth;
    conventionalDepth = saturate(conventionalDepth);
    float nearPlane = max(currentLocationNear.w, 0.0001f);
    float farPlane = previousLocationFar.w;
    float viewZ;
    if (farPlane > nearPlane) {
        viewZ = (nearPlane * farPlane) /
                max(farPlane - conventionalDepth * (farPlane - nearPlane), 0.0001f);
    } else {
        // Standard D3D infinite-far projection: z = 1 - near / viewZ.
        viewZ = nearPlane / max(1.0f - conventionalDepth, 0.00001f);
    }

    float2 currentPixel = float2(pixel) + 0.5f;
    float2 currentNdc;
    currentNdc.x = currentPixel.x * (2.0f / imageAndFlags.x) - 1.0f;
    currentNdc.y = 1.0f - currentPixel.y * (2.0f / imageAndFlags.y);

    float3 worldPosition = currentLocationNear.xyz +
        currentForwardTanX.xyz * viewZ +
        currentRightTanY.xyz * (currentNdc.x * currentForwardTanX.w * viewZ) +
        currentUpHasHistory.xyz * (currentNdc.y * currentRightTanY.w * viewZ);

    float3 fromPreviousCamera = worldPosition - previousLocationFar.xyz;
    float previousZ = dot(fromPreviousCamera, previousForwardTanX.xyz);
    if (previousZ <= max(nearPlane * 0.01f, 0.0001f)) {
        motionVectors[pixel] = 0.0f;
        return;
    }

    float2 previousNdc;
    previousNdc.x = dot(fromPreviousCamera, previousRightTanY.xyz) /
                    (previousZ * previousForwardTanX.w);
    previousNdc.y = dot(fromPreviousCamera, previousUp.xyz) /
                    (previousZ * previousRightTanY.w);
    float2 previousPixel;
    previousPixel.x = (previousNdc.x + 1.0f) * 0.5f * imageAndFlags.x;
    previousPixel.y = (1.0f - previousNdc.y) * 0.5f * imageAndFlags.y;

    // DLSS convention used by the bridge: where this current pixel came from
    // in the previous frame, expressed in pixels (scale = 1,1).
    motionVectors[pixel] = previousPixel - currentPixel;
}
)";

template <typename T>
void release_one(T*& object) {
    if (object) {
        object->Release();
        object = nullptr;
    }
}

struct CameraState {
    float location[3] = {};
    bvr::ue::FRotator rotation{};
    float tanX = 0.0f;
    float tanY = 0.0f;
    std::uint64_t buildId = 0;
    std::uint64_t stampMs = 0;
    std::uint32_t publications = 0;
    bool valid = false;
};

struct EyeState {
    ID3D11Texture2D* depth = nullptr;
    ID3D11ShaderResourceView* depthSrv = nullptr;
    ID3D11UnorderedAccessView* depthUav = nullptr;
    ID3D11Texture2D* motion = nullptr;
    ID3D11ShaderResourceView* motionSrv = nullptr;
    ID3D11UnorderedAccessView* motionUav = nullptr;
    CameraState previous{};
    std::uint64_t generation = 0;
    bool generated = false;
    bool generatedCameraValid = false;
    bool generatedHistoryValid = false;
};

struct Candidate {
    ID3D11Texture2D* texture = nullptr;
    std::uintptr_t dsvIdentity = 0;
    unsigned draws = 0;
    unsigned depthClears = 0;
    float lastDepthClear = 0.0f;
    bool depthClearSeen = false;
};

struct CaptureMetadata {
    std::uint64_t captureId = 0;
    std::uintptr_t textureIdentity = 0;
    std::uintptr_t dsvIdentity = 0;
    unsigned draws = 0;
    unsigned depthClears = 0;
    float lastDepthClear = 0.0f;
    bool depthClearSeen = false;
};

ID3D11Device* g_device = nullptr;
ID3D11DeviceContext* g_immediateContext = nullptr;
ID3D11ComputeShader* g_shader = nullptr;
ID3D11Buffer* g_constants = nullptr;
ID3D11Texture2D* g_depthCapture = nullptr;
ID3D11ShaderResourceView* g_depthCaptureSrv = nullptr;
EyeState g_eyes[kEyeCount];
Candidate g_candidates[kMaxCandidates];
int g_candidateCount = 0;
Candidate* g_current = nullptr;
unsigned g_currentPassDraws = 0;
Candidate* g_best = nullptr;
unsigned g_bestDraws = 0;
bool g_haveCapture = false;
std::uint64_t g_depthCopies = 0;
#ifdef BVR_DEPTH_COPY_REUSE
constexpr bool kDepthCopyReuse = true;
#else
constexpr bool kDepthCopyReuse = false;
#endif
std::atomic<bool> g_copyTrackingAvailable{false}; // hook lifetime, survives prepare/shutdown
bool g_depthWritesEnabled = true; // null/default D3D11 state writes depth
uint64_t g_depthWriteSerial = 0, g_copiedWriteSerial = 0;
uint64_t g_unchangedCopiesSkipped = 0, g_skippedWindow = 0;
bvr::SampledGpuTimer g_depthCopyTimer;
uint64_t g_copyWindowCount = 0;
double g_copyCpuTotalMs = 0, g_copyCpuMaxMs = 0;
CaptureMetadata g_captureMetadata{};
Diagnostics g_diagnostics{};
UINT g_width = 0;
UINT g_height = 0;
float g_nearPlane = 10.0f;
float g_farPlane = 0.0f;
bool g_depthInverted = false;
bool g_loggedUnsupportedFormat = false;
bool g_loggedUnsupportedShape = false;
bool g_loggedUnsupportedView = false;
bool g_loggedCandidateOverflow = false;
bool g_loggedBoundFinalize = false;
bool g_loggedReject[kEyeCount][8] = {};
std::atomic<bool> g_ready{false};

struct alignas(16) GuideConstants {
    float currentLocationNear[4];
    float currentForwardTanX[4];
    float currentRightTanY[4];
    float currentUpHasHistory[4];
    float previousLocationFar[4];
    float previousForwardTanX[4];
    float previousRightTanY[4];
    float previousUp[4];
    float imageAndFlags[4];
};
static_assert(sizeof(GuideConstants) % 16 == 0);

void release_eye_resources(EyeState& eye) {
    release_one(eye.depthUav);
    release_one(eye.depthSrv);
    release_one(eye.depth);
    release_one(eye.motionUav);
    release_one(eye.motionSrv);
    release_one(eye.motion);
    eye = {};
}

void reset_interval() {
    for (int i = 0; i < g_candidateCount; ++i) {
        release_one(g_candidates[i].texture);
        g_candidates[i] = {};
    }
    g_candidateCount = 0;
    g_current = nullptr;
    g_currentPassDraws = 0;
    g_best = nullptr;
    g_bestDraws = 0;
    g_haveCapture = false;
    g_depthWriteSerial = g_copiedWriteSerial = 0;
    g_captureMetadata = {};
}

bool dsv_is_compatible(ID3D11DepthStencilView* dsv, ID3D11Texture2D* texture) {
    if (!dsv || !texture) return false;
    D3D11_DEPTH_STENCIL_VIEW_DESC viewDesc{};
    D3D11_TEXTURE2D_DESC textureDesc{};
    dsv->GetDesc(&viewDesc);
    texture->GetDesc(&textureDesc);

    const bool viewFormat = viewDesc.Format == DXGI_FORMAT_D24_UNORM_S8_UINT;
    const bool resourceFormat = textureDesc.Format == DXGI_FORMAT_R24G8_TYPELESS ||
                                textureDesc.Format == DXGI_FORMAT_D24_UNORM_S8_UINT;
    if (!viewFormat || !resourceFormat) {
        if (!g_loggedUnsupportedFormat) {
            BVR_LOG("[dlss45-guides] unsupported depth format (DSV %u, resource %u); "
                    "guide capture stays off for that pass",
                    static_cast<unsigned>(viewDesc.Format),
                    static_cast<unsigned>(textureDesc.Format));
            g_loggedUnsupportedFormat = true;
        }
        return false;
    }

    // CopySubresourceRegion below copies source subresource 0. Do not silently
    // accept a view of another mip/slice and then copy unrelated depth data.
    // BioShock's principal scene DSV is a plain mip-0 Texture2D view.
    if (viewDesc.ViewDimension != D3D11_DSV_DIMENSION_TEXTURE2D ||
        viewDesc.Texture2D.MipSlice != 0) {
        if (!g_loggedUnsupportedView) {
            BVR_LOG("[dlss45-guides] ignored DSV view dimension=%u mip=%u "
                    "(requires Texture2D mip 0)",
                    static_cast<unsigned>(viewDesc.ViewDimension),
                    viewDesc.ViewDimension == D3D11_DSV_DIMENSION_TEXTURE2D
                        ? viewDesc.Texture2D.MipSlice
                        : 0u);
            g_loggedUnsupportedView = true;
        }
        return false;
    }

    if (textureDesc.Width != g_width || textureDesc.Height != g_height ||
        textureDesc.ArraySize != 1 || textureDesc.SampleDesc.Count != 1 ||
        textureDesc.SampleDesc.Quality != 0) {
        if (!g_loggedUnsupportedShape) {
            BVR_LOG("[dlss45-guides] ignored depth shape %ux%u array=%u samples=%u "
                    "(expected %ux%u, array=1, samples=1)",
                    textureDesc.Width, textureDesc.Height, textureDesc.ArraySize,
                    textureDesc.SampleDesc.Count, g_width, g_height);
            g_loggedUnsupportedShape = true;
        }
        return false;
    }
    return true;
}

Candidate* candidate_for(ID3D11DepthStencilView* dsv) {
    if (!dsv) return nullptr;
    ID3D11Resource* resource = nullptr;
    dsv->GetResource(&resource);
    if (!resource) return nullptr;
    ID3D11Texture2D* texture = nullptr;
    const HRESULT qi = resource->QueryInterface(IID_PPV_ARGS(&texture));
    resource->Release();
    if (FAILED(qi) || !texture) return nullptr;

    for (int i = 0; i < g_candidateCount; ++i) {
        if (g_candidates[i].texture == texture) {
            g_candidates[i].dsvIdentity = reinterpret_cast<std::uintptr_t>(dsv);
            texture->Release();
            return &g_candidates[i];
        }
    }
    if (!dsv_is_compatible(dsv, texture)) {
        texture->Release();
        return nullptr;
    }
    if (g_candidateCount == kMaxCandidates) {
        if (!g_loggedCandidateOverflow) {
            BVR_LOG("[dlss45-guides] more than %d compatible DSVs in one interval; "
                    "extra candidates ignored", kMaxCandidates);
            g_loggedCandidateOverflow = true;
        }
        texture->Release();
        return nullptr;
    }
    Candidate& result = g_candidates[g_candidateCount++];
    result.texture = texture; // retain the QI reference through this interval
    result.dsvIdentity = reinterpret_cast<std::uintptr_t>(dsv);
    result.draws = 0;
    return &result;
}

void finish_current_pass(ID3D11DeviceContext* context) {
    if (!g_current || !g_currentPassDraws) {
        g_currentPassDraws = 0;
        return;
    }

    // Copy only when this DSV becomes the interval leader, or when a later
    // sub-pass updates the already-leading DSV. on_setrt is called after the
    // real bind, so a different/null next DSV has made this source safe to copy.
    if (g_current == g_best || g_current->draws > g_bestDraws) {
        // Keep the original pass/winner policy. Only skip an identical source
        // with no possible DEPTH write since its existing capture. Any depth-
        // writing draw (even to another target), clear, command list or direct
        // write to the selected resource dirties the serial conservatively.
        // Stencil-only changes do not matter: conversion reads R24, not X8.
        const bool unchanged = kDepthCopyReuse &&
            g_copyTrackingAvailable.load(std::memory_order_acquire) &&
            g_haveCapture && g_current == g_best &&
            g_depthWriteSerial == g_copiedWriteSerial;
        if (unchanged) {
            ++g_unchangedCopiesSkipped; ++g_skippedWindow;
        } else {
            // Whole-subresource copy is required for BIND_DEPTH_STENCIL.
            const double copyStarted = bvr::diagnostic_clock_ms();
            g_depthCopyTimer.begin(context);
            context->CopySubresourceRegion(g_depthCapture, 0, 0, 0, 0,
                                           g_current->texture, 0, nullptr);
            g_depthCopyTimer.end(context);
            const double copyCpuMs = bvr::diagnostic_clock_ms() - copyStarted;
            ++g_copyWindowCount; g_copyCpuTotalMs += copyCpuMs;
            if (copyCpuMs > g_copyCpuMaxMs) g_copyCpuMaxMs = copyCpuMs;
            ++g_depthCopies;
            g_copiedWriteSerial = g_depthWriteSerial;
        }
        g_best = g_current;
        g_bestDraws = g_current->draws;
        g_haveCapture = true;
        g_captureMetadata.captureId = g_depthCopies;
        g_captureMetadata.textureIdentity =
            reinterpret_cast<std::uintptr_t>(g_current->texture);
        g_captureMetadata.dsvIdentity = g_current->dsvIdentity;
        g_captureMetadata.draws = g_current->draws;
        g_captureMetadata.depthClears = g_current->depthClears;
        g_captureMetadata.lastDepthClear = g_current->lastDepthClear;
        g_captureMetadata.depthClearSeen = g_current->depthClearSeen;
    }
    g_currentPassDraws = 0;
}

// Present normally arrives after BioShock has rebound the tonemap RTV while
// deliberately leaving the scene DSV attached (depth testing is disabled for
// that pass). Preserve the complete RTV set and the DSV, detach only depth long
// enough to make the copy legal, then restore the exact OM bindings. KEEP_UAV
// prevents the temporary bind from perturbing any pixel UAV slots.
struct SavedOutputMergerState {
    ID3D11DeviceContext* context = nullptr; // borrowed
    ID3D11RenderTargetView* rtvs[D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT] = {};
    ID3D11DepthStencilView* dsv = nullptr;
    UINT rtvCount = 0;
    bool depthDetached = false;

    explicit SavedOutputMergerState(ID3D11DeviceContext* value) : context(value) {
        context->OMGetRenderTargets(D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT, rtvs, &dsv);
        for (UINT i = 0; i < D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT; ++i)
            if (rtvs[i]) rtvCount = i + 1;
    }

    void bind_depth(ID3D11DepthStencilView* value) {
        context->OMSetRenderTargetsAndUnorderedAccessViews(
            rtvCount, rtvCount ? rtvs : nullptr, value, rtvCount,
            D3D11_KEEP_UNORDERED_ACCESS_VIEWS, nullptr, nullptr);
    }

    void detach_depth() {
        bind_depth(nullptr);
        depthDetached = true;
    }

    void restore() {
        if (!depthDetached) return;
        bind_depth(dsv);
        depthDetached = false;
    }

    ~SavedOutputMergerState() {
        restore();
        for (ID3D11RenderTargetView*& rtv : rtvs) release_one(rtv);
        release_one(dsv);
    }
};

bool compile_shader(ID3DBlob** bytecode) {
    ID3DBlob* errors = nullptr;
    const HRESULT hr = D3DCompile(kGuideShader, std::strlen(kGuideShader),
                                  "bioshock1r_temporal_guides.hlsl", nullptr, nullptr,
                                  "cs_main", "cs_5_0", D3DCOMPILE_ENABLE_STRICTNESS,
                                  0, bytecode, &errors);
    if (errors) {
        BVR_LOG("[dlss45-guides] shader compile: %s",
                static_cast<const char*>(errors->GetBufferPointer()));
        errors->Release();
    }
    return SUCCEEDED(hr);
}

bool create_eye_resources(ID3D11Device* device, EyeState& eye) {
    D3D11_TEXTURE2D_DESC desc{};
    desc.Width = g_width;
    desc.Height = g_height;
    desc.MipLevels = 1;
    desc.ArraySize = 1;
    desc.SampleDesc.Count = 1;
    desc.Usage = D3D11_USAGE_DEFAULT;
    desc.BindFlags = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_UNORDERED_ACCESS;

    desc.Format = DXGI_FORMAT_R32_FLOAT;
    if (FAILED(device->CreateTexture2D(&desc, nullptr, &eye.depth)) ||
        FAILED(device->CreateShaderResourceView(eye.depth, nullptr, &eye.depthSrv)) ||
        FAILED(device->CreateUnorderedAccessView(eye.depth, nullptr, &eye.depthUav)))
        return false;

    desc.Format = DXGI_FORMAT_R16G16_FLOAT;
    if (FAILED(device->CreateTexture2D(&desc, nullptr, &eye.motion)) ||
        FAILED(device->CreateShaderResourceView(eye.motion, nullptr, &eye.motionSrv)) ||
        FAILED(device->CreateUnorderedAccessView(eye.motion, nullptr, &eye.motionUav)))
        return false;
    return true;
}

struct SavedComputeState {
    static constexpr UINT kMaxClasses = 256;
    ID3D11ComputeShader* shader = nullptr;
    ID3D11ClassInstance* classes[kMaxClasses] = {};
    UINT classCount = kMaxClasses;
    ID3D11Buffer* constantBuffer = nullptr;
    ID3D11ShaderResourceView* srvs[1] = {};
    ID3D11UnorderedAccessView* uavs[2] = {};

    void capture(ID3D11DeviceContext* context) {
        context->CSGetShader(&shader, classes, &classCount);
        if (classCount > kMaxClasses) classCount = kMaxClasses;
        context->CSGetConstantBuffers(0, 1, &constantBuffer);
        context->CSGetShaderResources(0, 1, srvs);
        context->CSGetUnorderedAccessViews(0, 2, uavs);
    }

    void restore(ID3D11DeviceContext* context) {
        context->CSSetShader(shader, classes, classCount);
        context->CSSetConstantBuffers(0, 1, &constantBuffer);
        context->CSSetShaderResources(0, 1, srvs);
        context->CSSetUnorderedAccessViews(0, 2, uavs, nullptr);
    }

    ~SavedComputeState() {
        release_one(shader);
        for (UINT i = 0; i < classCount; ++i) release_one(classes[i]);
        release_one(constantBuffer);
        release_one(srvs[0]);
        release_one(uavs[0]);
        release_one(uavs[1]);
    }
};

void basis_for(const CameraState& cameraState, float forward[3], float right[3], float up[3]) {
    bvr::ue::ue_rot_basis(cameraState.rotation, forward, right, up);
}

void copy3(float destination[4], const float source[3]) {
    destination[0] = source[0];
    destination[1] = source[1];
    destination[2] = source[2];
}

CameraState current_camera(int eye, const Projection& projection) {
    CameraState result{};
    result.tanX = projection.tanHalfFovX;
    result.tanY = projection.tanHalfFovY;
    camera::DrivenEyeCamera published{};
    result.valid = camera::driven_eye_cam_for_build(eye, projection.buildId, &published);
    if (result.valid) {
        std::memcpy(result.location, published.location, sizeof(result.location));
        result.rotation.pitch = published.rotation[0];
        result.rotation.yaw = published.rotation[1];
        result.rotation.roll = published.rotation[2];
        result.buildId = published.buildId;
        result.stampMs = published.stampMs;
        result.publications = published.publications;
    }
    return result;
}

void fill_output(int eye, EyeGuides* out) {
    if (!out || eye < 0 || eye >= kEyeCount) return;
    const EyeState& state = g_eyes[eye];
    EyeGuides value{};
    value.depthTexture = state.depth;
    value.depthSrv = state.depthSrv;
    value.motionTexture = state.motion;
    value.motionSrv = state.motionSrv;
    value.width = g_width;
    value.height = g_height;
    value.generation = state.generation;
    value.cameraValid = state.generatedCameraValid;
    value.historyValid = state.generatedHistoryValid;
    value.resetRequired = !state.generatedHistoryValid;
    value.depthInverted = g_depthInverted;
    value.coherent = state.generatedCameraValid;
    const EyeDiagnostics& diag = g_diagnostics.eyes[eye];
    value.buildId = diag.lastBuildId;
    value.cameraBuildId = diag.lastCameraBuildId;
    value.captureId = diag.lastCaptureId;
    value.depthTextureIdentity = diag.lastDepthTextureIdentity;
    value.depthViewIdentity = diag.lastDepthViewIdentity;
    value.depthDraws = diag.lastDepthDraws;
    value.depthClears = diag.lastDepthClears;
    value.depthClearValue = diag.lastDepthClearValue;
    value.depthClearSeen = diag.lastDepthClearSeen;
    *out = value;
}

void seed_projection_diagnostics(EyeDiagnostics& diag, const Projection& projection) {
    diag.lastBuildId = projection.buildId;
    diag.lastTanX = projection.tanHalfFovX;
    diag.lastTanY = projection.tanHalfFovY;
    diag.lastObservedTanX = projection.observedTanHalfFovX;
    diag.lastObservedTanY = projection.observedTanHalfFovY;
    diag.lastObservedAgeMs = projection.observedAgeMs;
    diag.lastObservedValid = projection.observedValid;
    diag.lastCameraBuildId = 0;
    diag.lastCameraAgeMs = 0;
    diag.lastCameraPublications = 0;
    std::memset(diag.lastCameraLocation, 0, sizeof(diag.lastCameraLocation));
    std::memset(diag.lastCameraRotation, 0, sizeof(diag.lastCameraRotation));
    diag.lastCaptureId = 0;
    diag.lastDepthTextureIdentity = 0;
    diag.lastDepthViewIdentity = 0;
    diag.lastDepthDraws = 0;
    diag.lastDepthClears = 0;
    diag.lastDepthClearValue = 0.0f;
    diag.lastDepthClearSeen = false;
}

void seed_capture_diagnostics(EyeDiagnostics& diag) {
    diag.lastCaptureId = g_captureMetadata.captureId;
    diag.lastDepthTextureIdentity = g_captureMetadata.textureIdentity;
    diag.lastDepthViewIdentity = g_captureMetadata.dsvIdentity;
    diag.lastDepthDraws = g_captureMetadata.draws;
    diag.lastDepthClears = g_captureMetadata.depthClears;
    diag.lastDepthClearValue = g_captureMetadata.lastDepthClear;
    diag.lastDepthClearSeen = g_captureMetadata.depthClearSeen;
}

void reject_frame(int eye, RejectReason reason) {
    if (eye < 0 || eye >= kEyeCount) return;
    EyeDiagnostics& diag = g_diagnostics.eyes[eye];
    ++diag.rejected;
    diag.lastReject = reason;
    diag.lastCoherent = false;
    diag.lastHistoryValid = false;
    const unsigned reasonIndex = static_cast<unsigned>(reason);
    if (reasonIndex < 8 && !g_loggedReject[eye][reasonIndex]) {
        g_loggedReject[eye][reasonIndex] = true;
        BVR_LOG("[dlss45] rejected eye=%d build=%llu camera=%llu capture=%llu "
                "reason=%s tan=%.6f/%.6f observed=%.6f/%.6f age=%ums",
                eye, static_cast<unsigned long long>(diag.lastBuildId),
                static_cast<unsigned long long>(diag.lastCameraBuildId),
                static_cast<unsigned long long>(diag.lastCaptureId),
                reject_reason_name(reason), diag.lastTanX, diag.lastTanY,
                diag.lastObservedTanX, diag.lastObservedTanY,
                diag.lastObservedAgeMs);
    }
    g_eyes[eye].previous = {};
    g_eyes[eye].generated = false;
    g_eyes[eye].generatedCameraValid = false;
    g_eyes[eye].generatedHistoryValid = false;
}

bool projection_matches_observation(const Projection& projection) {
    if (!projection.observedValid) return true;
    const float toleranceX = std::fmax(0.002f, std::fabs(projection.tanHalfFovX) * 0.005f);
    const float toleranceY = std::fmax(0.002f, std::fabs(projection.tanHalfFovY) * 0.005f);
    return std::isfinite(projection.observedTanHalfFovX) &&
           std::isfinite(projection.observedTanHalfFovY) &&
           projection.observedTanHalfFovX > 0.0f &&
           projection.observedTanHalfFovY > 0.0f &&
           std::fabs(projection.observedTanHalfFovX - projection.tanHalfFovX) <= toleranceX &&
           std::fabs(projection.observedTanHalfFovY - projection.tanHalfFovY) <= toleranceY;
}

} // namespace

bool prepare(ID3D11Device* device, const PrepareDesc& desc) {
    shutdown();
    if (!device || !desc.width || !desc.height || !std::isfinite(desc.nearPlane) ||
        desc.nearPlane <= 0.0f || !std::isfinite(desc.farPlane) ||
        (desc.farPlane != 0.0f && desc.farPlane <= desc.nearPlane)) {
        BVR_LOG("[dlss45-guides] invalid prepare: %ux%u near=%.4f far=%.4f",
                desc.width, desc.height, desc.nearPlane, desc.farPlane);
        return false;
    }

    g_width = desc.width;
    g_height = desc.height;
    g_nearPlane = desc.nearPlane;
    g_farPlane = desc.farPlane;
    g_depthInverted = desc.depthInverted;
    g_device = device;
    g_device->AddRef();
    g_device->GetImmediateContext(&g_immediateContext);

    ID3DBlob* bytecode = nullptr;
    bool ok = g_immediateContext != nullptr && compile_shader(&bytecode);
    if (ok) {
        ok = SUCCEEDED(device->CreateComputeShader(bytecode->GetBufferPointer(),
                                                   bytecode->GetBufferSize(), nullptr,
                                                   &g_shader));
    }
    release_one(bytecode);

    D3D11_BUFFER_DESC constantDesc{};
    constantDesc.ByteWidth = sizeof(GuideConstants);
    constantDesc.Usage = D3D11_USAGE_DYNAMIC;
    constantDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    constantDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    if (ok) ok = SUCCEEDED(device->CreateBuffer(&constantDesc, nullptr, &g_constants));

    D3D11_TEXTURE2D_DESC captureDesc{};
    captureDesc.Width = g_width;
    captureDesc.Height = g_height;
    captureDesc.MipLevels = 1;
    captureDesc.ArraySize = 1;
    captureDesc.Format = DXGI_FORMAT_R24G8_TYPELESS;
    captureDesc.SampleDesc.Count = 1;
    captureDesc.Usage = D3D11_USAGE_DEFAULT;
    // Keep DEPTH_STENCIL in the bind contract even though we never create a
    // DSV for this texture. The D3D11 copy rules require a depth resource's
    // destination to be depth-compatible; TYPELESS then permits the R24 SRV.
    captureDesc.BindFlags = D3D11_BIND_DEPTH_STENCIL | D3D11_BIND_SHADER_RESOURCE;
    if (ok) ok = SUCCEEDED(device->CreateTexture2D(&captureDesc, nullptr, &g_depthCapture));

    D3D11_SHADER_RESOURCE_VIEW_DESC depthSrvDesc{};
    depthSrvDesc.Format = DXGI_FORMAT_R24_UNORM_X8_TYPELESS;
    depthSrvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
    depthSrvDesc.Texture2D.MipLevels = 1;
    if (ok) {
        ok = SUCCEEDED(device->CreateShaderResourceView(g_depthCapture, &depthSrvDesc,
                                                        &g_depthCaptureSrv));
    }

    for (int eye = 0; ok && eye < kEyeCount; ++eye)
        ok = create_eye_resources(device, g_eyes[eye]);

    if (!ok) {
        BVR_LOG("[dlss45-guides] D3D11 resource/SRV creation failed; temporal guides disabled");
        shutdown();
        return false;
    }

    BVR_LOG("[dlss45-guides] ready %ux%u, near=%.3f uu, far=%.3f (%s), depth=%s",
            g_width, g_height, g_nearPlane, g_farPlane,
            g_farPlane > g_nearPlane ? "finite" : "infinite",
            g_depthInverted ? "reversed" : "normal");
    g_ready.store(true, std::memory_order_release);
    on_context_reset(g_immediateContext); // capture real state; no per-draw Get* calls
    BVR_LOG("[depth-reuse] enabled=%d tracking=%d same-source/unchanged-depth only",
            kDepthCopyReuse?1:0, g_copyTrackingAvailable.load()?1:0);
    g_depthCopyTimer.prepare(device);
    return true;
}

bool ready() {
    return g_ready.load(std::memory_order_acquire);
}

void log_performance() {
    if (!ready()) return;
    BVR_LOG("[depth-perf] size=%ux%u copyCalls=%llu copiedMiB=%.1f cpuSubmitAvgMs=%.4f "
            "cpuSubmitMaxMs=%.3f gpuSamples=%llu gpuCopyAvgMs=%.3f gpuCopyMaxMs=%.3f "
            "skippedUnchanged=%llu reuseActive=%d",
            g_width, g_height, static_cast<unsigned long long>(g_copyWindowCount),
            double(g_copyWindowCount) * double(g_width) * double(g_height) * 4.0 / 1048576.0,
            g_copyWindowCount ? g_copyCpuTotalMs / double(g_copyWindowCount) : 0,
            g_copyCpuMaxMs, static_cast<unsigned long long>(g_depthCopyTimer.samples()),
            g_depthCopyTimer.average_ms(), g_depthCopyTimer.max_ms(),
            static_cast<unsigned long long>(g_skippedWindow),
            kDepthCopyReuse && g_copyTrackingAvailable.load() ? 1 : 0);
    g_copyWindowCount = 0; g_copyCpuTotalMs = g_copyCpuMaxMs = 0;
    g_skippedWindow = 0;
    g_depthCopyTimer.clear_stats();
}

void set_copy_tracking_available(bool available) {
    g_copyTrackingAvailable.store(available, std::memory_order_release);
}

void on_depth_state(ID3D11DeviceContext* context, ID3D11DepthStencilState* state) {
    if (!kDepthCopyReuse || !ready() || context != g_immediateContext) return;
    g_depthWritesEnabled = true;
    if (state) {
        D3D11_DEPTH_STENCIL_DESC desc{}; state->GetDesc(&desc);
        g_depthWritesEnabled = desc.DepthEnable && desc.DepthWriteMask != D3D11_DEPTH_WRITE_MASK_ZERO;
    }
}

void on_untracked_draw(ID3D11DeviceContext* context) {
    if (kDepthCopyReuse && ready() && context == g_immediateContext && g_depthWritesEnabled)
        ++g_depthWriteSerial; // no change to the original indexed-draw voting
}

void on_resource_write(ID3D11DeviceContext* context, ID3D11Resource* destination) {
    if (kDepthCopyReuse && ready() && context == g_immediateContext && g_best &&
        destination == static_cast<ID3D11Resource*>(g_best->texture))
        ++g_depthWriteSerial;
}

void on_context_reset(ID3D11DeviceContext* context) {
    if (!kDepthCopyReuse || !ready() || context != g_immediateContext) return;
    ++g_depthWriteSerial; // command lists may have changed captured depth
    ID3D11DepthStencilState* state = nullptr; UINT stencil = 0;
    context->OMGetDepthStencilState(&state, &stencil);
    on_depth_state(context, state); release_one(state);
}

void on_setrt(ID3D11DeviceContext* context, UINT, ID3D11RenderTargetView* const*,
              ID3D11DepthStencilView* dsv) {
    if (!ready() || !context || context != g_immediateContext) return;

    Candidate* next = candidate_for(dsv);
    if (next == g_current) return; // RTV-only rebind; one continuous depth pass
    finish_current_pass(context);
    g_current = next;
}

void on_draw_indexed(ID3D11DeviceContext* context) {
    if (!ready() || context != g_immediateContext) return;
    if (kDepthCopyReuse && g_depthWritesEnabled) ++g_depthWriteSerial;
    if (!g_current) return;
    ++g_current->draws;
    ++g_currentPassDraws;
}

void on_clear_dsv(ID3D11DeviceContext* context, ID3D11DepthStencilView* dsv,
                  UINT clearFlags, FLOAT depth, UINT8) {
    if (!ready() || !context || context != g_immediateContext || !dsv ||
        (clearFlags & D3D11_CLEAR_DEPTH) == 0 || !std::isfinite(depth)) {
        return;
    }
    Candidate* candidate = candidate_for(dsv);
    if (!candidate) return;
    if (kDepthCopyReuse) ++g_depthWriteSerial; // DSV clears bypass the depth-write mask

    // generate_eye() temporarily detaches and then restores the scene DSV
    // before reset_interval() forgets the previous interval.  BioShock may
    // start the sibling eye by clearing that still-bound view without issuing
    // another OMSetRenderTargets call.  Re-arm it only when Direct3D confirms
    // that the cleared view is the one currently bound; a clear of an
    // unrelated/off-screen DSV must not become the active draw candidate.
    if (!g_current) {
        ID3D11DepthStencilView* boundDsv = nullptr;
        context->OMGetRenderTargets(0, nullptr, &boundDsv);
        if (boundDsv == dsv) {
            g_current = candidate;
            g_currentPassDraws = 0;
        }
        release_one(boundDsv);
    }
    ++candidate->depthClears;
    candidate->lastDepthClear = depth;
    candidate->depthClearSeen = true;
}

bool generate_eye(ID3D11DeviceContext* context, int eye,
                  const Projection& projection, EyeGuides* out) {
    if (out) *out = {};
    if (!ready() || !context || context != g_immediateContext ||
        eye < 0 || eye >= kEyeCount ||
        !std::isfinite(projection.tanHalfFovX) || !std::isfinite(projection.tanHalfFovY) ||
        projection.tanHalfFovX <= 0.0f || projection.tanHalfFovY <= 0.0f) {
        if (ready() && eye >= 0 && eye < kEyeCount) {
            seed_projection_diagnostics(g_diagnostics.eyes[eye], projection);
            reject_frame(eye, RejectReason::InvalidCall);
        }
        reset_interval();
        return false;
    }

    EyeDiagnostics& diag = g_diagnostics.eyes[eye];
    seed_projection_diagnostics(diag, projection);
    if (!projection.buildId) {
        reject_frame(eye, RejectReason::MissingBuildTag);
        reset_interval();
        return false;
    }

    // BioShock keeps the scene DSV bound through tonemap/HUD even though depth
    // testing is disabled. Temporarily detach it while preserving/restoring the
    // complete OM state so the interval can still be captured without a D3D11
    // read/write hazard.
    if (g_current && g_currentPassDraws) {
        if (!g_loggedBoundFinalize) {
            BVR_LOG("[dlss45-guides] scene DSV remained bound at generate_eye; "
                    "temporarily detaching it for a safe depth copy");
            g_loggedBoundFinalize = true;
        }
        SavedOutputMergerState outputMerger(context);
        outputMerger.detach_depth();
        finish_current_pass(context);
        outputMerger.restore();
    }
    if (!g_haveCapture) {
        reject_frame(eye, RejectReason::MissingDepth);
        reset_interval();
        return false;
    }
    seed_capture_diagnostics(diag);

    EyeState& eyeState = g_eyes[eye];
    CameraState current = current_camera(eye, projection);
    if (!current.valid) {
        reject_frame(eye, RejectReason::MissingCamera);
        reset_interval();
        return false;
    }
    diag.lastCameraBuildId = current.buildId;
    diag.lastCameraPublications = current.publications;
    const std::uint64_t cameraAge = GetTickCount64() - current.stampMs;
    diag.lastCameraAgeMs = static_cast<std::uint32_t>(
        cameraAge > 0xFFFFFFFFull ? 0xFFFFFFFFull : cameraAge);
    std::memcpy(diag.lastCameraLocation, current.location,
                sizeof(diag.lastCameraLocation));
    diag.lastCameraRotation[0] = current.rotation.pitch;
    diag.lastCameraRotation[1] = current.rotation.yaw;
    diag.lastCameraRotation[2] = current.rotation.roll;
    if (current.buildId != projection.buildId) {
        reject_frame(eye, RejectReason::MissingCamera);
        reset_interval();
        return false;
    }
    if (current.publications != 1) {
        reject_frame(eye, RejectReason::AmbiguousCamera);
        reset_interval();
        return false;
    }
    if (eyeState.previous.valid && current.buildId <= eyeState.previous.buildId) {
        reject_frame(eye, RejectReason::OutOfOrderBuild);
        reset_interval();
        return false;
    }
    if (!projection_matches_observation(projection)) {
        reject_frame(eye, RejectReason::ProjectionMismatch);
        reset_interval();
        return false;
    }

    // A projection discontinuity invalidates reprojection even if the caller
    // forgot to issue invalidate() on an FOV-mode edge.
    const bool sameProjection =
        eyeState.previous.valid &&
        std::fabs(eyeState.previous.tanX - current.tanX) <= 0.0001f &&
        std::fabs(eyeState.previous.tanY - current.tanY) <= 0.0001f;
    // Build ids are global and the established producer alternates L,R, so a
    // continuous history advances by exactly two for one eye. A gap is not a
    // fatal frame mismatch: accept the current inputs with Reset and seed a
    // fresh history, while making the discontinuity visible in telemetry.
    const bool sequenceContinuous =
        eyeState.previous.valid && current.buildId == eyeState.previous.buildId + 2;
    if (eyeState.previous.valid && !sequenceContinuous)
        ++diag.sequenceDiscontinuities;
    const bool historyValid = sameProjection && sequenceContinuous;

    float currentForward[3] = {1.0f, 0.0f, 0.0f};
    float currentRight[3] = {0.0f, 1.0f, 0.0f};
    float currentUp[3] = {0.0f, 0.0f, 1.0f};
    float previousForward[3] = {1.0f, 0.0f, 0.0f};
    float previousRight[3] = {0.0f, 1.0f, 0.0f};
    float previousUp[3] = {0.0f, 0.0f, 1.0f};
    if (current.valid) basis_for(current, currentForward, currentRight, currentUp);
    if (eyeState.previous.valid)
        basis_for(eyeState.previous, previousForward, previousRight, previousUp);

    GuideConstants constants{};
    copy3(constants.currentLocationNear, current.location);
    constants.currentLocationNear[3] = g_nearPlane;
    copy3(constants.currentForwardTanX, currentForward);
    constants.currentForwardTanX[3] = current.tanX;
    copy3(constants.currentRightTanY, currentRight);
    constants.currentRightTanY[3] = current.tanY;
    copy3(constants.currentUpHasHistory, currentUp);
    constants.currentUpHasHistory[3] = historyValid ? 1.0f : 0.0f;
    copy3(constants.previousLocationFar, eyeState.previous.location);
    constants.previousLocationFar[3] = g_farPlane;
    copy3(constants.previousForwardTanX, previousForward);
    constants.previousForwardTanX[3] = eyeState.previous.tanX;
    copy3(constants.previousRightTanY, previousRight);
    constants.previousRightTanY[3] = eyeState.previous.tanY;
    copy3(constants.previousUp, previousUp);
    constants.imageAndFlags[0] = static_cast<float>(g_width);
    constants.imageAndFlags[1] = static_cast<float>(g_height);
    constants.imageAndFlags[2] = g_depthInverted ? 1.0f : 0.0f;

    D3D11_MAPPED_SUBRESOURCE mapped{};
    if (FAILED(context->Map(g_constants, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
        reject_frame(eye, RejectReason::InvalidCall);
        reset_interval();
        return false;
    }
    std::memcpy(mapped.pData, &constants, sizeof(constants));
    context->Unmap(g_constants, 0);

    SavedComputeState saved;
    saved.capture(context);
    ID3D11UnorderedAccessView* outputs[2] = {eyeState.depthUav, eyeState.motionUav};
    context->CSSetShader(g_shader, nullptr, 0);
    context->CSSetConstantBuffers(0, 1, &g_constants);
    context->CSSetShaderResources(0, 1, &g_depthCaptureSrv);
    context->CSSetUnorderedAccessViews(0, 2, outputs, nullptr);
    context->Dispatch((g_width + 7) / 8, (g_height + 7) / 8, 1);

    // Explicitly break our read/write bindings before restoring arbitrary game
    // compute state. This also leaves the generated textures copyable by a
    // future IPC client immediately after this call.
    ID3D11ShaderResourceView* nullSrv = nullptr;
    ID3D11UnorderedAccessView* nullUavs[2] = {};
    context->CSSetShaderResources(0, 1, &nullSrv);
    context->CSSetUnorderedAccessViews(0, 2, nullUavs, nullptr);
    saved.restore(context);

    if (current.valid) eyeState.previous = current;
    else eyeState.previous = {};
    ++eyeState.generation;
    eyeState.generated = true;
    eyeState.generatedCameraValid = current.valid;
    eyeState.generatedHistoryValid = historyValid;
    ++diag.generated;
    if (!historyValid) ++diag.resetFrames;
    diag.lastReject = RejectReason::None;
    diag.lastHistoryValid = historyValid;
    diag.lastCoherent = true;
    fill_output(eye, out);
    reset_interval();
    return true;
}

bool get_eye(int eye, EyeGuides* out) {
    if (out) *out = {};
    if (!ready() || !out || eye < 0 || eye >= kEyeCount || !g_eyes[eye].generated)
        return false;
    fill_output(eye, out);
    return true;
}

void get_diagnostics(Diagnostics* out) {
    if (!out) return;
    g_diagnostics.depthCopies = g_depthCopies;
    g_diagnostics.unchangedCopiesSkipped = g_unchangedCopiesSkipped;
    g_diagnostics.nearPlane = g_nearPlane;
    g_diagnostics.farPlane = g_farPlane;
    g_diagnostics.depthInverted = g_depthInverted;
    *out = g_diagnostics;
}

const char* reject_reason_name(RejectReason reason) {
    switch (reason) {
    case RejectReason::None:               return "none";
    case RejectReason::InvalidCall:        return "invalid-call";
    case RejectReason::MissingBuildTag:    return "missing-build-tag";
    case RejectReason::MissingDepth:       return "missing-depth";
    case RejectReason::MissingCamera:      return "missing-camera";
    case RejectReason::AmbiguousCamera:    return "ambiguous-camera";
    case RejectReason::OutOfOrderBuild:    return "out-of-order-build";
    case RejectReason::ProjectionMismatch: return "projection-mismatch";
    }
    return "unknown";
}

void invalidate_eye(int eye) {
    if (eye < 0 || eye >= kEyeCount) return;
    g_eyes[eye].previous = {};
    g_eyes[eye].generated = false;
    g_eyes[eye].generatedCameraValid = false;
    g_eyes[eye].generatedHistoryValid = false;
}

void invalidate() {
    reset_interval();
    for (int eye = 0; eye < kEyeCount; ++eye) invalidate_eye(eye);
}

void shutdown() {
    g_ready.store(false, std::memory_order_release);
    reset_interval();
    for (EyeState& eye : g_eyes) release_eye_resources(eye);
    release_one(g_depthCaptureSrv);
    release_one(g_depthCapture);
    release_one(g_constants);
    release_one(g_shader);
    release_one(g_immediateContext);
    release_one(g_device);
    g_width = g_height = 0;
    g_nearPlane = 10.0f;
    g_farPlane = 0.0f;
    g_depthInverted = false;
    g_depthCopies = 0;
    g_unchangedCopiesSkipped = g_skippedWindow = 0;
    g_depthWritesEnabled = true;
    g_depthCopyTimer.release();
    g_copyWindowCount = 0; g_copyCpuTotalMs = g_copyCpuMaxMs = 0;
    g_captureMetadata = {};
    g_diagnostics = {};
    g_loggedUnsupportedFormat = false;
    g_loggedUnsupportedShape = false;
    g_loggedUnsupportedView = false;
    g_loggedCandidateOverflow = false;
    g_loggedBoundFinalize = false;
    std::memset(g_loggedReject, 0, sizeof(g_loggedReject));
}

} // namespace bvr::b1r::temporal_guides
