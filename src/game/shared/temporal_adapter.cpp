#include "game/shared/temporal_adapter.h"
#include "game/adapter_registry.h"
#include "game/bioshock1r/temporal_guides.h"
#include "game/bioshock2r/temporal_guides.h"

namespace bvr::active_temporal {
namespace one = bvr::b1r::temporal_guides;
namespace two = bvr::b2r::temporal_guides;
using game::HostGame;
bool supported() {
    const auto game = game::detect_host_game();
    return game == HostGame::Bioshock1 || game == HostGame::Bioshock2;
}
bool prepare(ID3D11Device* device, const PrepareDesc& desc) {
    switch (game::detect_host_game()) {
    case HostGame::Bioshock1: {
        one::PrepareDesc native{};
        native.width=desc.width; native.height=desc.height;
        native.nearPlane=desc.nearPlane; native.farPlane=desc.farPlane;
        native.depthInverted=desc.depthInverted;
        return one::prepare(device,native);
    }
    case HostGame::Bioshock2: {
        two::PrepareDesc native{};
        native.width=desc.width; native.height=desc.height;
        // The initial hints never select a BS2 projection branch.
        native.nearPlane=0.0f; native.farPlane=0.0f;
        native.depthInverted=desc.depthInverted;
        return two::prepare(device,native);
    }
    default: return false;
    }
}
bool ready() {
    switch (game::detect_host_game()) {
    case HostGame::Bioshock1: return one::ready();
    case HostGame::Bioshock2: return two::ready();
    default: return false;
    }
}
void shutdown() {
    switch (game::detect_host_game()) {
    case HostGame::Bioshock1: return one::shutdown();
    case HostGame::Bioshock2: return two::shutdown();
    default: return;
    }
}
void invalidate() {
    switch (game::detect_host_game()) {
    case HostGame::Bioshock1: return one::invalidate();
    case HostGame::Bioshock2: return two::invalidate();
    default: return;
    }
}
void invalidate_eye(int eye) {
    switch (game::detect_host_game()) {
    case HostGame::Bioshock1: return one::invalidate_eye(eye);
    case HostGame::Bioshock2: return two::invalidate_eye(eye);
    default: return;
    }
}
void get_diagnostics(Diagnostics* out) {
    switch (game::detect_host_game()) {
    case HostGame::Bioshock1: return one::get_diagnostics(out);
    case HostGame::Bioshock2: return two::get_diagnostics(out);
    default: if (out) *out = {}; return;
    }
}
void log_performance() {
    switch (game::detect_host_game()) {
    case HostGame::Bioshock1: return one::log_performance();
    case HostGame::Bioshock2: return two::log_performance();
    default: return;
    }
}
const char* reject_reason_name(RejectReason reason) {
    switch (game::detect_host_game()) {
    case HostGame::Bioshock1: return one::reject_reason_name(reason);
    case HostGame::Bioshock2: return two::reject_reason_name(reason);
    default: return "unsupported-game";
    }
}
bool generate_eye(ID3D11DeviceContext* context, int eye, const Projection& projection, EyeGuides* out) {
    switch (game::detect_host_game()) {
    case HostGame::Bioshock1: return one::generate_eye(context, eye, projection, out);
    case HostGame::Bioshock2: return two::generate_eye(context, eye, projection, out);
    default: return false;
    }
}
void on_setrt(ID3D11DeviceContext* context, UINT count, ID3D11RenderTargetView* const* rtvs, ID3D11DepthStencilView* dsv) {
    switch (game::detect_host_game()) {
    case HostGame::Bioshock1: return one::on_setrt(context, count, rtvs, dsv);
    case HostGame::Bioshock2: return two::on_setrt(context, count, rtvs, dsv);
    default: return;
    }
}
void on_draw_indexed(ID3D11DeviceContext* context) {
    switch (game::detect_host_game()) {
    case HostGame::Bioshock1: return one::on_draw_indexed(context);
    case HostGame::Bioshock2: return two::on_draw_indexed(context);
    default: return;
    }
}
void on_clear_dsv(ID3D11DeviceContext* context, ID3D11DepthStencilView* dsv, UINT flags, FLOAT depth, UINT8 stencil) {
    switch (game::detect_host_game()) {
    case HostGame::Bioshock1: return one::on_clear_dsv(context, dsv, flags, depth, stencil);
    case HostGame::Bioshock2: return two::on_clear_dsv(context, dsv, flags, depth, stencil);
    default: return;
    }
}
void set_copy_tracking_available(bool available) {
    switch (game::detect_host_game()) {
    case HostGame::Bioshock1: return one::set_copy_tracking_available(available);
    case HostGame::Bioshock2: return two::set_copy_tracking_available(available);
    default: return;
    }
}
void on_depth_state(ID3D11DeviceContext* context, ID3D11DepthStencilState* state) {
    switch (game::detect_host_game()) {
    case HostGame::Bioshock1: return one::on_depth_state(context, state);
    case HostGame::Bioshock2: return two::on_depth_state(context, state);
    default: return;
    }
}
void on_untracked_draw(ID3D11DeviceContext* context) {
    switch (game::detect_host_game()) {
    case HostGame::Bioshock1: return one::on_untracked_draw(context);
    case HostGame::Bioshock2: return two::on_untracked_draw(context);
    default: return;
    }
}
void on_resource_write(ID3D11DeviceContext* context, ID3D11Resource* destination) {
    switch (game::detect_host_game()) {
    case HostGame::Bioshock1: return one::on_resource_write(context, destination);
    case HostGame::Bioshock2: return two::on_resource_write(context, destination);
    default: return;
    }
}
void on_context_reset(ID3D11DeviceContext* context) {
    switch (game::detect_host_game()) {
    case HostGame::Bioshock1: return one::on_context_reset(context);
    case HostGame::Bioshock2: return two::on_context_reset(context);
    default: return;
    }
}
} // namespace bvr::active_temporal
