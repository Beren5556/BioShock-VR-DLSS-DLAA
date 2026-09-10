#pragma once
#include "game/shared/temporal_types.h"

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

using bvr::temporal_types::Projection;
using bvr::temporal_types::RejectReason;
using bvr::temporal_types::EyeGuides;
using bvr::temporal_types::EyeDiagnostics;
using bvr::temporal_types::Diagnostics;

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

// perf2: do not alter DSV voting or when copies are selected. Reuse only a
// same-interval, same-source capture with no intervening potential depth write.
// The installer of the complete hook set must opt in; missing hooks leave the
// old copy behavior intact. Context state taps run AFTER their real D3D call.
void set_copy_tracking_available(bool available);
void on_depth_state(ID3D11DeviceContext* context, ID3D11DepthStencilState* state);
void on_untracked_draw(ID3D11DeviceContext* context); // no new votes
void on_resource_write(ID3D11DeviceContext* context, ID3D11Resource* destination);
void on_context_reset(ID3D11DeviceContext* context); // ClearState / ExecuteCommandList

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
void log_performance(); // sampled GPU queries, no forced flush or query waits
const char* reject_reason_name(RejectReason reason);

// Loads, teleports, FOV/projection changes and device-resize paths must reset
// both temporal histories. invalidate_eye() is useful for a one-eye failure.
void invalidate();
void invalidate_eye(int eye);

bool ready();
void shutdown();

} // namespace bvr::b1r::temporal_guides
