#pragma once

// Shared transport/diagnostic values, never game camera or projection producers.
#include <d3d11.h>
#include <cstdint>

namespace bvr::temporal_types {
struct Projection {
    float tanHalfFovX = 0.0f;
    float tanHalfFovY = 0.0f;

    // Monotonic id popped with this eye's Present tag. It must identify an
    // exact camera publication; zero is deliberately rejected by the
    // diagnostic temporal path rather than falling back to a merely-latest
    // camera.
    std::uint64_t buildId = 0;

    // Optional independent observation decoded from the live WORLD ray block.
    // It is diagnostic and a safety check only. Both it and the caller's
    // tangents must agree with the exact root-WORLD matrix captured by the
    // adapter, which is the reconstruction source.
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
    MissingProjection,
    AmbiguousProjection,
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
    float lastNearPlane = 0.0f;
    float lastFarPlane = 0.0f;
    uint32_t lastProjectionPublications = 0;
    uint64_t lastHistoryEpoch = 0;
    float lastCameraLocation[3] = {};
    std::int32_t lastCameraRotation[3] = {};
    std::uint32_t lastCameraAgeMs = 0;
    std::uint32_t lastCameraPublications = 0;
    RejectReason lastReject = RejectReason::None;
};

struct Diagnostics {
    EyeDiagnostics eyes[2];
    std::uint64_t depthCopies = 0;
    std::uint64_t unchangedCopiesSkipped = 0;
    float nearPlane = 0.0f;
    float farPlane = 0.0f;
    bool depthInverted = false;
};
} // namespace bvr::temporal_types
