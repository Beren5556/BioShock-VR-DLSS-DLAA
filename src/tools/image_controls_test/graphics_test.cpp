// Real graphics backend + private INI, simulated console (never launches a game).
#include "game/bioshock1r/graphics_options.h"
#include "game/bioshock1r/console_exec.h"
#include "game/bioshock1r/game_ini.h"
#include <windows.h>
#include <objbase.h>
#include <cstdio>
#include <cstring>
#include <string>
#pragma comment(lib, "ole32.lib")

namespace {
namespace g = bvr::b1r::graphics_options;
std::wstring ini;
std::array<bool, g::kCount> engine{};
unsigned checks = 0, failures = 0, sets = 0;
bool unavailable = false, rejected = false, ignored = false, malformed = false;
void expect(bool ok, const char* label) {
    ++checks; if (!ok) ++failures;
    std::printf("%s %u: %s\n", ok ? "PASS" : "FAIL", checks, label);
}
size_t index_of(const char* key) {
    for (size_t i = 0; i < g::kCount; ++i) if (!strcmp(key, g::kOptions[i].key)) return i;
    return g::kCount;
}
std::wstring wide(const char* key) { return std::wstring(key, key + strlen(key)); }
std::wstring saved(const wchar_t* section, const wchar_t* key) {
    wchar_t text[128]{};
    GetPrivateProfileStringW(section, key, L"", text, 128, ini.c_str());
    return text;
}
std::string bytes() {
    HANDLE f = CreateFileW(ini.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, 0, nullptr);
    if (f == INVALID_HANDLE_VALUE) return {};
    std::string text(GetFileSize(f, nullptr), '\0'); DWORD count = 0;
    ReadFile(f, text.data(), DWORD(text.size()), &count, nullptr); CloseHandle(f);
    return count == text.size() ? text : std::string();
}
}
namespace bvr::log { void write(const char*, ...) {} }
namespace bvr::b1r::game_ini { const wchar_t* path() { return ini.c_str(); } }
namespace bvr::b1r::console_exec {
bool read_render_option(const char* key, std::wstring& value) {
    auto i = index_of(key); if (unavailable || i >= g::kCount) return false;
    if (malformed) { value = L"Unrecognized property"; return true; }
    value = i == g::kCount - 1 ? (engine[i] ? L" High " : L"Low")
                               : (engine[i] ? L" TrUe; engine\r\n" : L"FALSE");
    return true;
}
bool set_render_option(const char* key, const char* value) {
    ++sets; auto i = index_of(key);
    if (rejected || i >= g::kCount) return false;
    if (!ignored) engine[i] = !strcmp(value, "True") || !strcmp(value, "High");
    return true;
}
}
int main() {
    wchar_t temp[MAX_PATH]{}, suffix[40]{}; GUID guid{};
    DWORD count = GetTempPathW(MAX_PATH, temp);
    if (!count || count >= MAX_PATH || FAILED(CoCreateGuid(&guid)) || !StringFromGUID2(guid, suffix, 40)) return 2;
    const auto dir = std::wstring(temp) + L"bvr-graphics-test-" + suffix;
    if (!CreateDirectoryW(dir.c_str(), nullptr)) return 2;
    ini = dir + L"\\Bioshock.ini";
    for (size_t i = 0; i < g::kCount; ++i) {
        engine[i] = g::kOptions[i].defaultOn;
        WritePrivateProfileStringW(L"Engine.RenderConfig", wide(g::kOptions[i].key).c_str(),
            i == g::kCount - 1 ? L"High" : (engine[i] ? L"True" : L"False"), ini.c_str());
    }
    WritePrivateProfileStringW(L"WinDrv.WindowsClient", L"WindowedViewportX", L"3072", ini.c_str());
    WritePrivateProfileStringW(L"Unrelated", L"RealTimeReflection", L"KeepThis", ini.c_str());
    const auto original = bytes();
    auto values = g::read();
    expect(values.size() == 9 && values[0].live && values[8].on && !values[2].on && !values[4].on,
           "all nine engine values parsed; performance defaults correct");
    expect(bytes() == original && sets == 0, "opening graphics page never changes engine or INI");
    for (size_t i = 0; i < g::kCount; ++i) {
        const bool before = engine[i]; g::Value observed{};
        const auto result = g::toggle(i, observed);
        const auto key = wide(g::kOptions[i].key);
        const auto desired = i == g::kCount - 1 ? (!before ? L"High" : L"Low") : (!before ? L"True" : L"False");
        expect(result == g::Change::Applied && observed.known && observed.live && observed.on != before &&
               saved(L"Engine.RenderConfig", key.c_str()) == desired, g::kOptions[i].key);
    }
    expect(saved(L"WinDrv.WindowsClient", L"WindowedViewportX") == L"3072" &&
           saved(L"Unrelated", L"RealTimeReflection") == L"KeepThis", "only intended section/key changed");
    const auto beforeFailure = bytes(); unsigned beforeSets = sets;
    g::Value observed{};
    unavailable = true; values = g::read();
    expect(values[0].known && !values[0].live && values[0].on == engine[0], "unavailable GET falls back to INI, marked not live");
    expect(g::toggle(0, observed) == g::Change::RestartRequired && sets == beforeSets && bytes() == beforeFailure,
           "unavailable GET cannot SET or save");
    unavailable = false; malformed = true;
    expect(g::toggle(0, observed) == g::Change::RestartRequired && sets == beforeSets, "error output is not parsed as a value");
    malformed = false; rejected = true;
    expect(g::toggle(0, observed) == g::Change::Failed && bytes() == beforeFailure, "rejected SET does not persist");
    rejected = false; ignored = true; beforeSets = sets; const bool current = engine[0];
    expect(g::toggle(0, observed) == g::Change::Failed && sets == beforeSets + 2 &&
           observed.known && observed.on == current && bytes() == beforeFailure,
           "handled but ignored SET fails readback, restores old value, does not save");
    ignored = false;
    HANDLE lock = CreateFileW(ini.c_str(), GENERIC_READ, 0, nullptr, OPEN_EXISTING, 0, nullptr);
    if (lock == INVALID_HANDLE_VALUE) return 2;
    expect(g::toggle(0, observed) == g::Change::AppliedNotSaved && observed.on != current,
           "locked INI reports applied but not saved");
    CloseHandle(lock);
    expect(bytes() == beforeFailure, "failed persistence leaves original INI intact");
    expect(g::toggle(g::kCount, observed) == g::Change::Failed, "out of range option rejected");
    expect(GetFileAttributesW((ini + L".bvr-graphics.tmp").c_str()) == INVALID_FILE_ATTRIBUTES,
           "no staging file left after successful or rejected operation");
    std::printf("Checks: %u; failures: %u. Private fixture retained: %ls\n", checks, failures, ini.c_str());
    return failures ? 1 : 0;
}
