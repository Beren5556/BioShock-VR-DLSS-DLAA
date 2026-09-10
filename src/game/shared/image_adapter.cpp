#include "game/shared/image_adapter.h"
#include "game/adapter_registry.h"
#include "game/bioshock1r/camera.h"
#include "game/bioshock2r/camera.h"
#include "game/bioshock2r/game_ini.h"
#include <windows.h>
#include <cstring>
#include <string>

namespace bvr::active_image {
namespace {
bool bs1() { return game::detect_host_game() == game::HostGame::Bioshock1; }
bool bs2() { return game::detect_host_game() == game::HostGame::Bioshock2; }
}
bool enqueue_resolution(uint32_t w, uint32_t h) {
    if (bs1()) return b1r::camera::enqueue_resolution(w, h);
    if (bs2()) return b2r::camera::enqueue_resolution(w, h);
    return false;
}
ResolutionRequestStatus resolution_request_status() {
    if (bs2()) return b2r::camera::resolution_request_status();
    if (!bs1()) return ResolutionRequestStatus::Unavailable;
    using One = b1r::camera::ResolutionRequestStatus;
    switch (b1r::camera::resolution_request_status()) {
    case One::Pending: return ResolutionRequestStatus::Pending;
    case One::Dispatched: return ResolutionRequestStatus::Dispatched;
    case One::Fault: return ResolutionRequestStatus::Fault;
    default: return ResolutionRequestStatus::Unavailable;
    }
}
bool cancel_pending_resolution() {
    if (bs1()) return b1r::camera::cancel_pending_resolution();
    if (bs2()) return b2r::camera::cancel_pending_resolution();
    return false;
}
Viewport read_viewport() {
    if (bs1()) return b1r::game_ini::read_viewport();
    if (bs2()) {
        const auto v = b2r::game_ini::read_viewport();
        // Do not substitute SP's ignored WinDrv values for Shared.ini.
        return {v.w, v.h, v.w, v.h, v.startupFullscreen, v.valid};
    }
    return {};
}
bool write_viewport(uint32_t w, uint32_t h) {
    if (bs1()) return b1r::game_ini::write_viewport(w, h);
    if (bs2()) return b2r::game_ini::write_viewport(w, h);
    return false;
}
bool save_configuration(uint32_t w, uint32_t h, const std::wstring& ini,
                        const std::wstring& staged, const std::string& before, bool existed) {
    if (bs2()) return b2r::game_ini::write_viewport_and_settings(w, h, ini, existed, before, staged);
    if (!bs1()) return false;
    // Retain the accepted BS1 save path; BS1's shipped MSI is frozen separately.
    const auto previous = read_viewport();
    const bool changed = !previous.valid || previous.windowedW != w || previous.windowedH != h ||
                         previous.fullscreenW != w || previous.fullscreenH != h;
    if (changed && !write_viewport(w, h)) return false;
    const bool ok = MoveFileExW(staged.c_str(), ini.c_str(),
                               MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH) != FALSE;
    if (!ok && changed && previous.valid && previous.windowedW == previous.fullscreenW &&
        previous.windowedH == previous.fullscreenH)
        write_viewport(previous.windowedW, previous.windowedH);
    return ok;
}
namespace graphics {
Values read() {
    if (bs1()) return b1r::graphics_options::read();
    Values result{};
    if (!bs2()) return result;
    const wchar_t* ini = b2r::game_ini::sp_path();
    if (!ini || !*ini) return result;
    for (size_t i = 0; i < kCount; ++i) {
        const auto* key = kOptions[i].key;
        std::wstring wideKey(key, key + strlen(key));
        wchar_t value[64]{};
        GetPrivateProfileStringW(L"Engine.RenderConfig", wideKey.c_str(), L"", value, 64, ini);
        std::wstring text(value);
        auto end = text.find_first_of(L";\r\n");
        if (end != std::wstring::npos) text.resize(end);
        auto first = text.find_first_not_of(L" \t");
        if (first == std::wstring::npos) continue;
        text = text.substr(first, text.find_last_not_of(L" \t") - first + 1);
        const bool fluid = i == kCount - 1;
        if (_wcsicmp(text.c_str(), fluid ? L"High" : L"True") == 0 || (!fluid && text == L"1"))
            result[i] = {true, true, false};
        else if (_wcsicmp(text.c_str(), fluid ? L"Low" : L"False") == 0 || (!fluid && text == L"0"))
            result[i] = {false, true, false};
    }
    return result;
}
Change toggle(size_t index, Value& observed) {
    if (index >= kCount) return Change::Failed;
    if (bs1()) return b1r::graphics_options::toggle(index, observed);
    // BS2's engine Exec address/calling convention has NOT been derived.
    // Never invoke BS1 RVAs or claim a disk value is live engine readback.
    if (bs2()) { observed = read()[index]; return Change::RestartRequired; }
    return Change::Failed;
}
}
}
