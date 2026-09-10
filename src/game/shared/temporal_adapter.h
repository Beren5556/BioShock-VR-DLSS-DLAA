#pragma once
#include "game/shared/temporal_types.h"

// Selects only the matching game's producer. Shared rendering code must not
// directly depend on BS1's camera/projection when processing BS2 (or vice versa).
namespace bvr::active_temporal {
using namespace bvr::temporal_types;
struct PrepareDesc {
    UINT width = 0, height = 0;
    float nearPlane = 10.0f, farPlane = 0.0f;
    bool depthInverted = false;
};
bool supported();
bool prepare(ID3D11Device*, const PrepareDesc&);
bool ready();
void shutdown();
void invalidate();
void invalidate_eye(int eye);
void get_diagnostics(Diagnostics*);
void log_performance();
const char* reject_reason_name(RejectReason);
bool generate_eye(ID3D11DeviceContext*, int, const Projection&, EyeGuides*);
void on_setrt(ID3D11DeviceContext*, UINT, ID3D11RenderTargetView* const*, ID3D11DepthStencilView*);
void on_draw_indexed(ID3D11DeviceContext*);
void on_clear_dsv(ID3D11DeviceContext*, ID3D11DepthStencilView*, UINT, FLOAT, UINT8);
void set_copy_tracking_available(bool);
void on_depth_state(ID3D11DeviceContext*, ID3D11DepthStencilState*);
void on_untracked_draw(ID3D11DeviceContext*);
void on_resource_write(ID3D11DeviceContext*, ID3D11Resource*);
void on_context_reset(ID3D11DeviceContext*);
} // namespace bvr::active_temporal
