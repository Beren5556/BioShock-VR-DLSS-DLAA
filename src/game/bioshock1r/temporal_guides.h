#pragma once

// BioShock 1 temporal inputs for a future DLSS 4.5 SR/DLAA client.  This
// module deliberately stops at producing D3D11 depth and motion-vector
// textures: it does not know about IPC, NGX, OpenXR swapchains or process_eye.

#include <d3d11.h>

#include <cstdint>

namespace bvr::b1r::temporal_guides {

struct PrepareDesc {
    UINT width = 0;
    UINT height = 0;

    // Unreal units. BioShockHD.exe's finite D3D projection builder loads 10 uu.
    float nearPlane = 10.0f;

    // 0 keeps the generic infinite-far compatibility path. BioShock selects a
    // finite 1024/65536 uu far plane; the runtime caller supplies the branch it
    // can prove (diagnostic v0.2 uses the normal gameplay 65536 branch).
    float farPlane = 0.0f;

    // False is the normal D3D convention (near=0, far=1). Kept explicit so a
    // reversed-Z discovery does not require a shader/interface rewrite.
    bool depthInverted = false;
};

struct Projection {
    float tanHalfFovX = 0.0f;
    float tanHalfFovY = 0.0f;

    // Monotonic id popped with this eye's Present tag. It must identify an
    // exact camera publication; zero is deliberately rejected by the
    // diagnostic temporal path rather than falling back to a merely-latest
    // camera.
    std::uint64_t buildId = 0;

    // Optional independent observation decoded from the live WORLD ray block.
    // It is diagnostic and a safety check only; the caller's rendered
    // projection above remains the reconstruction source.
    float observedTanHalfFovX = 0.0f;
    float observedTanHalfFovY = 0.0f;
    std::uint32_t observedAgeMs = 0;
    bool observedValid = false;
};

enum class RejectReason : std::uint32_t {
    None = 0,
    InvalidCall,
    MissingBuildTag,
    MissingDepth,
    MissingCamera,
    AmbiguousCamera,
    OutOfOrderBuild,
    ProjectionMismatch,
};

// Borrowed views/textures: the module owns every COM reference. They remain
// valid until prepare() is called again or shutdown(). Each eye owns distinct
// output resources, so generating the other eye does not overwrite these.
struct EyeGuides {
    ID3D11Texture2D* depthTexture = nullptr;       // DXGI_FORMAT_R32_FLOAT
    ID3D11ShaderResourceView* depthSrv = nullptr;
    ID3D11Texture2D* motionTexture = nullptr;      // DXGI_FORMAT_R16G16_FLOAT
    ID3D11ShaderResourceView* motionSrv = nullptr;
    UINT width = 0;
    UINT height = 0;
    std::uint64_t generation = 0;

    // Motion is current-pixel -> previous-pixel, in pixels. A future NGX call
    // should therefore publish MV scales 1,1 and MVJittered=false.
    float motionVectorScaleX = 1.0f;
    float motionVectorScaleY = 1.0f;
    bool cameraValid = false;
    bool historyValid = false;
    bool resetRequired = true;
    bool depthInverted = false;
    bool coherent = false;

    std::uint64_t buildId = 0;
    std::uint64_t cameraBuildId = 0;
    std::uint64_t captureId = 0;
    std::uintptr_t depthTextureIdentity = 0;
    std::uintptr_t depthViewIdentity = 0;
    std::uint32_t depthDraws = 0;
    std::uint32_t depthClears = 0;
    float depthClearValue = 0.0f;
    bool depthClearSeen = false;
};

struct EyeDiagnostics {
    std::uint64_t generated = 0;
    std::uint64_t rejected = 0;
    std::uint64_t resetFrames = 0;
    std::uint64_t sequenceDiscontinuities = 0;
    std::uint64_t lastBuildId = 0;
    std::uint64_t lastCameraBuildId = 0;
    std::uint64_t lastCaptureId = 0;
    std::uintptr_t lastDepthTextureIdentity = 0;
    std::uintptr_t lastDepthViewIdentity = 0;
    std::uint32_t lastDepthDraws = 0;
    std::uint32_t lastDepthClears = 0;
    float lastDepthClearValue = 0.0f;
    bool lastDepthClearSeen = false;
    bool lastHistoryValid = false;
    bool lastCoherent = false;
    float lastTanX = 0.0f;
    float lastTanY = 0.0f;
    float lastObservedTanX = 0.0f;
    float lastObservedTanY = 0.0f;
    std::uint32_t lastObservedAgeMs = 0;
    bool lastObservedValid = false;
    float lastCameraLocation[3] = {};
    std::int32_t lastCameraRotation[3] = {};
    std::uint32_t lastCameraAgeMs = 0;
    std::uint32_t lastCameraPublications = 0;
    RejectReason lastReject = RejectReason::None;
};

struct Diagnostics {
    EyeDiagnostics eyes[2];
    std::uint64_t depthCopies = 0;
    float nearPlane = 0.0f;
    float farPlane = 0.0f;
    bool depthInverted = false;
};

// Creates the format-independent output textures and compute shader. The
// source D24 resource is discovered from the live DSV bindings, not retained
// from engine memory beyond one capture interval.
bool prepare(ID3D11Device* device, const PrepareDesc& desc);

// Hook taps. on_setrt() runs AFTER the real OMSetRenderTargets call; a changed
// DSV can then be copied safely. If BioShock keeps the winning scene DSV bound
// through tonemap/HUD, generate_eye() temporarily detaches it while preserving
// and restoring the complete RTV/DSV state. Taps from deferred/foreign contexts
// are ignored. on_draw_indexed() only increments the current DSV's vote.
void on_setrt(ID3D11DeviceContext* context, UINT numViews,
              ID3D11RenderTargetView* const* rtvs, ID3D11DepthStencilView* dsv);
void on_draw_indexed(ID3D11DeviceContext* context);
void on_clear_dsv(ID3D11DeviceContext* context, ID3D11DepthStencilView* dsv,
                  UINT clearFlags, FLOAT depth, UINT8 stencil);

// Ends the current capture interval, converts the winning D24 depth to R32F,
// and generates this eye's camera-only MV field. The diagnostic path fails
// soft (returns false for spatial fallback) when the exact Build camera/tag is
// absent or ambiguous; it never submits a merely-latest camera to DLSS.
bool generate_eye(ID3D11DeviceContext* context, int eye,
                  const Projection& projection, EyeGuides* out);

// Returns the last successfully generated resources for one eye without
// touching GPU state. False means that eye has not generated since prepare /
// invalidate. The pointers follow the borrowed-lifetime rule above.
bool get_eye(int eye, EyeGuides* out);
void get_diagnostics(Diagnostics* out);
const char* reject_reason_name(RejectReason reason);

// Loads, teleports, FOV/projection changes and device-resize paths must reset
// both temporal histories. invalidate_eye() is useful for a one-eye failure.
void invalidate();
void invalidate_eye(int eye);

bool ready();
void shutdown();

} // namespace bvr::b1r::temporal_guides
