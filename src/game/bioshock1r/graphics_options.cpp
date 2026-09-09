#include "game/bioshock1r/graphics_options.h"
#include "game/bioshock1r/console_exec.h"
#include "game/bioshock1r/game_ini.h"
#include "core/util/log.h"
#include <windows.h>
#include <cwchar>
#include <cstring>
#include <string>

namespace bvr::b1r::graphics_options {
namespace {
std::wstring wide(const char* text) { return std::wstring(text, text + strlen(text)); }
bool parse(std::wstring text, bool fluid, bool& on) {
    auto end = text.find_first_of(L";\r\n");
    if (end != std::wstring::npos) text.resize(end);
    const auto first = text.find_first_not_of(L" \t");
    if (first == std::wstring::npos) return false;
    text = text.substr(first, text.find_last_not_of(L" \t") - first + 1);
    if (_wcsicmp(text.c_str(), fluid ? L"High" : L"True") == 0 || (!fluid && text == L"1")) { on = true; return true; }
    if (_wcsicmp(text.c_str(), fluid ? L"Low" : L"False") == 0 || (!fluid && text == L"0")) { on = false; return true; }
    return false;
}
bool query(size_t index, bool& value) {
    std::wstring text;
    return console_exec::read_render_option(kOptions[index].key, text) &&
           parse(text, index == kCount - 1, value);
}
bool persist(size_t index, bool on) {
    const wchar_t* ini = game_ini::path();
    if (!ini || !ini[0]) return false;
    const std::wstring key = wide(kOptions[index].key);
    const wchar_t* value = index == kCount - 1 ? (on ? L"High" : L"Low") : (on ? L"True" : L"False");
    const std::wstring staged = std::wstring(ini) + L".bvr-graphics.tmp";
    if (!CopyFileW(ini, staged.c_str(), FALSE)) return false;
    bool ok = WritePrivateProfileStringW(L"Engine.RenderConfig", key.c_str(), value, staged.c_str()) != FALSE;
    WritePrivateProfileStringW(nullptr, nullptr, nullptr, staged.c_str());
    if (ok) ok = ReplaceFileW(ini, staged.c_str(), nullptr, REPLACEFILE_IGNORE_MERGE_ERRORS, nullptr, nullptr) != FALSE;
    if (!ok) DeleteFileW(staged.c_str());
    return ok;
}
}
Values read() {
    Values values{};
    for (size_t i = 0; i < kCount; ++i) {
        bool on = false;
        if (query(i, on)) { values[i] = {on, true, true}; continue; }
        wchar_t text[64]{};
        const auto key = wide(kOptions[i].key);
        const auto* ini = game_ini::path();
        if (ini && ini[0]) GetPrivateProfileStringW(L"Engine.RenderConfig", key.c_str(), L"", text, 64, ini);
        if (parse(text, i == kCount - 1, on)) values[i] = {on, true, false};
    }
    return values;
}
Change toggle(size_t index, Value& observed) {
    if (index >= kCount) return Change::Failed;
    bool previous = false;
    if (!query(index, previous)) return Change::RestartRequired;
    observed = {previous, true, true};
    const bool wanted = !previous;
    const char* value = index == kCount - 1 ? (wanted ? "High" : "Low") : (wanted ? "True" : "False");
    if (!console_exec::set_render_option(kOptions[index].key, value)) return Change::Failed;
    bool after = previous;
    if (!query(index, after) || after != wanted) {
        // Never display a successful switch merely because Exec handled SET.
        console_exec::set_render_option(kOptions[index].key,
            index == kCount - 1 ? (previous ? "High" : "Low") : (previous ? "True" : "False"));
        observed.live = query(index, after);
        observed.known = observed.live;
        if (observed.known) observed.on = after;
        return Change::Failed;
    }
    observed = {after, true, true};
    const bool saved = persist(index, after);
    BVR_LOG("[graphics-controls] %s=%s engine readback=OK saved=%s", kOptions[index].key, value, saved ? "yes" : "no");
    return saved ? Change::Applied : Change::AppliedNotSaved;
}
} // namespace bvr::b1r::graphics_options
