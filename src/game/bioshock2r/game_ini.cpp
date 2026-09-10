#include "game/bioshock2r/game_ini.h"

#include "core/util/log.h"
#include "core/util/config_batch.h"

#include <windows.h>
#include <shlobj.h>

#include <cstdio>
#include <cstring>
#include <cerrno>
#include <string>

namespace bvr::b2r::game_ini {
namespace {

// THE section that governs (Shared.ini). Measured, not assumed - see the
// header's truth table.
constexpr char kSharedSection[] = "[SharedOptions]";

// The PC driver section of Bioshock2SP.ini. FOUR others carry identical
// viewport key names in the shipped BS2 file - [XeDrv.XenonClient] (509),
// [PS3Drv.PS3Client] (541), [DurangoDrv.DurangoClient] (573) and
// [OrbisDrv.OrbisClient] (605), all verified present at those lines on the live
// install. BS1 has only three decoys; the PS3 one is BS2's addition, and it is
// also the marker that tells the two games' config files apart (BS1 ships
// GNMDrv, never PS3Drv). Never touch any of them. These keys do NOT drive the
// engine on BS2 - they are kept in sync, not relied on.
constexpr char kPcSection[] = "[WinDrv.WindowsClient]";
constexpr char kBs2Marker[] = "[PS3Drv.PS3Client]";

// Shared.ini is the governing file; Bioshock2SP.ini sits beside it.
wchar_t g_path[MAX_PATH] = {};   // Shared.ini
wchar_t g_spPath[MAX_PATH] = {}; // Bioshock2SP.ini ("" if absent)
bool g_searched = false;

bool file_exists(const wchar_t* p) {
    DWORD a = GetFileAttributesW(p);
    return a != INVALID_FILE_ATTRIBUTES && !(a & FILE_ATTRIBUTE_DIRECTORY);
}

bool read_all(const wchar_t* p, std::string& out) {
    HANDLE h = CreateFileW(p, GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
                           FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) return false;
    LARGE_INTEGER size{};
    if (!GetFileSizeEx(h, &size) || size.QuadPart <= 0 || size.QuadPart > (8 << 20)) {
        CloseHandle(h);
        return false;
    }
    out.resize(static_cast<size_t>(size.QuadPart));
    DWORD got = 0;
    bool ok = ReadFile(h, out.data(), static_cast<DWORD>(out.size()), &got, nullptr) &&
              got == out.size();
    CloseHandle(h);
    if (!ok) out.clear();
    return ok;
}

// Match complete, unique sections/keys; retain whitespace, comments and CRLF.
std::string trimmed(std::string value) {
    auto first = value.find_first_not_of(" \t\r");
    if (first == std::string::npos) return {};
    return value.substr(first, value.find_last_not_of(" \t\r") - first + 1);
}
bool section_span(const std::string& text, const char* section, size_t& start, size_t& end) {
    if (text.find('\0') != std::string::npos) return false; // never rewrite UTF-16 as ANSI
    bool found = false, inSection = false;
    for (size_t p = 0; p < text.size();) {
        size_t nl = text.find('\n', p);
        const size_t next = nl == std::string::npos ? text.size() : nl + 1;
        std::string line = text.substr(p, (nl == std::string::npos ? text.size() : nl) - p);
        if (p == 0 && line.compare(0, 3, "\xEF\xBB\xBF") == 0) line.erase(0, 3);
        line = trimmed(line.substr(0, line.find(';')));
        if (!line.empty() && line.front() == '[') {
            if (inSection) { end = p; inSection = false; }
            if (line == section) {
                if (found) return false;
                found = inSection = true; start = next; end = text.size();
            }
        }
        p = next;
    }
    return found;
}
bool key_span(const std::string& text, size_t start, size_t end, const char* key,
              size_t& valueStart, size_t& valueEnd) {
    bool found = false;
    for (size_t p = start; p < end;) {
        const size_t nl = text.find('\n', p);
        const size_t lineEnd = nl == std::string::npos || nl > end ? end : nl;
        size_t eq = text.find('=', p);
        if (eq < lineEnd && trimmed(text.substr(p, eq - p)) == key) {
            if (found) return false;
            found = true; valueStart = eq + 1; valueEnd = lineEnd;
            size_t comment = text.find(';', valueStart);
            if (comment < valueEnd) valueEnd = comment;
            while (valueStart < valueEnd && (text[valueStart] == ' ' || text[valueStart] == '\t')) ++valueStart;
            while (valueEnd > valueStart && (text[valueEnd-1] == ' ' || text[valueEnd-1] == '\t' || text[valueEnd-1] == '\r')) --valueEnd;
        }
        p = lineEnd == text.size() ? lineEnd : lineEnd + 1;
    }
    return found && valueEnd > valueStart;
}
long section_value(const std::string& text, size_t start, size_t end, const char* key) {
    size_t a = 0, b = 0;
    if (!key_span(text, start, end, key, a, b)) return -1;
    char* tail = nullptr; errno = 0;
    const long value = strtol(text.c_str() + a, &tail, 10);
    return errno == 0 && tail == text.c_str() + b ? value : -1;
}
bool set_section_value(std::string& text, size_t start, size_t& end, const char* key, uint32_t value) {
    size_t a = 0, b = 0;
    if (section_value(text, start, end, key) < 0 || !key_span(text, start, end, key, a, b)) return false;
    const auto number = std::to_string(value);
    text.replace(a, b - a, number);
    end = end - (b - a) + number.size();
    return true;
}
bool section_bool(const std::string& text, size_t start, size_t end, const char* key) {
    size_t a = 0, b = 0;
    if (!key_span(text, start, end, key, a, b)) return false;
    const auto value = text.substr(a, b - a);
    return _stricmp(value.c_str(), "True") == 0 || value == "1";
}

// A candidate only wins if it EXISTS and actually carries the named section.
bool candidate_has(const wchar_t* p, const char* section) {
    if (!file_exists(p)) return false;
    std::string text;
    if (!read_all(p, text)) return false;
    size_t start = 0, end = 0;
    return section_span(text, section, start, end);
}

void search() {
    g_searched = true;
#ifdef BVR_BS2_TEST_ISOLATION
    // Test-only physical game copies must never discover a user profile.
    wchar_t sp[MAX_PATH]{};
    const DWORD count = GetEnvironmentVariableW(L"BVR_LAB_GAME_INI", sp, MAX_PATH);
    const bool absolute = (count > 3 && sp[1] == L':' && sp[2] == L'\\') ||
                          (count > 2 && sp[0] == L'\\' && sp[1] == L'\\');
    if (!count || count >= MAX_PATH || !absolute || !candidate_has(sp, kPcSection)) {
        BVR_LOG("[b2r] test isolation: BVR_LAB_GAME_INI missing/invalid; config access disabled");
        return;
    }
    wchar_t shared[MAX_PATH]{};
    wcscpy_s(shared, sp);
    wchar_t* last = wcsrchr(shared, L'\\');
    if (!last || _wcsicmp(last + 1, L"Bioshock2SP.ini") != 0) {
        BVR_LOG("[b2r] test isolation: expected absolute Bioshock2SP.ini path");
        return;
    }
    *(last + 1) = L'\0';
    if (wcscat_s(shared, L"Shared.ini") != 0 || !candidate_has(shared, kSharedSection)) {
        BVR_LOG("[b2r] test isolation: sibling Shared.ini missing; config access disabled");
        return;
    }
    wcscpy_s(g_spPath, sp);
    wcscpy_s(g_path, shared);
    BVR_LOG("[b2r] test isolation: config files restricted to %ls", g_path);
    return;
#else
    wchar_t roaming[MAX_PATH]{}, docs[MAX_PATH]{};
    SHGetFolderPathW(nullptr, CSIDL_APPDATA, nullptr, 0, roaming);
    SHGetFolderPathW(nullptr, CSIDL_PERSONAL, nullptr, 0, docs);

    // Verified on the live Steam install: %APPDATA%\BioshockHD\Bioshock2\ holds
    // Shared.ini (the pair that governs), Bioshock2SP.ini and User.ini; the
    // matching Documents\BioshockHD\BioShock2\ holds ONLY SaveGames, so there is
    // no competing candidate there today. The Documents entries cover layouts
    // other installs may use. The game directory is deliberately NOT searched:
    // under Program Files a write there is silently redirected to VirtualStore,
    // which reads back as success and changes nothing.
    const wchar_t* dirs[][2] = {
        {roaming, L"\\BioshockHD\\Bioshock2"},
        {roaming, L"\\Bioshock2"},
        {docs, L"\\BioshockHD\\Bioshock2"},
        {docs, L"\\BioShock 2 Remastered"},
    };
    for (const auto& d : dirs) {
        if (!d[0][0]) continue;
        wchar_t shared[MAX_PATH], sp[MAX_PATH];
        if (_snwprintf_s(shared, MAX_PATH, _TRUNCATE, L"%s%s\\Shared.ini", d[0], d[1]) < 0)
            continue;
        if (!candidate_has(shared, kSharedSection)) {
            BVR_LOG("[b2r] game ini: not here - %ls", shared);
            continue;
        }
        wcscpy_s(g_path, shared);
        // The secondary file is optional. If present but malformed, retain its
        // path so a save is rejected before either file is changed.
        if (_snwprintf_s(sp, MAX_PATH, _TRUNCATE, L"%s%s\\Bioshock2SP.ini", d[0], d[1]) > 0 &&
            file_exists(sp)) {
            wcscpy_s(g_spPath, sp);
        }
        bool marker = g_spPath[0] && candidate_has(g_spPath, kBs2Marker);
        BVR_LOG("[b2r] game ini: %ls (governs) | %ls%s", g_path,
                g_spPath[0] ? g_spPath : L"<no Bioshock2SP.ini - not needed>",
                g_spPath[0] && !marker
                    ? " (note: no [PS3Drv.PS3Client] - the BS2 section layout may have "
                      "changed; writes stay section-scoped regardless)"
                    : "");
        return;
    }
    BVR_LOG("[b2r] game ini: Shared.ini NOT FOUND - resolution cannot be set from the mod");
#endif
}

bool prepare_section(config_batch::Batch& batch, const wchar_t* file, const char* section,
                     const char* const* keys, const uint32_t* values, int count) {
    std::string before; bool exists = false;
    if (!config_batch::read(file, before, exists) || !exists) return false;
    std::string after = before;
    size_t start = 0, end = 0;
    if (!section_span(after, section, start, end)) return false;
    for (int i = 0; i < count; ++i)
        if (!set_section_value(after, start, end, keys[i], values[i])) return false;
    return batch.add(file, true, before, after, L".bvr-bak-res");
}
bool prepare_viewport(config_batch::Batch& batch, uint32_t w, uint32_t h) {
    if (w < 640 || h < 480 || w > 16384 || h > 16384 || (w & 1) || (h & 1) || !path()[0]) return false;
    const char* sharedKeys[] = {"ViewportX", "ViewportY"};
    const uint32_t sharedValues[] = {w, h};
    if (!prepare_section(batch, g_path, kSharedSection, sharedKeys, sharedValues, 2)) return false;
    if (g_spPath[0]) {
        const char* keys[] = {"WindowedViewportX", "WindowedViewportY", "FullscreenViewportX", "FullscreenViewportY"};
        const uint32_t values[] = {w, h, w, h};
        if (!prepare_section(batch, g_spPath, kPcSection, keys, values, 4)) return false;
    }
    return true;
}

} // namespace

const wchar_t* path() {
    if (!g_searched) search();
    return g_path;
}
const wchar_t* sp_path() {
    if (!g_searched) search();
    return g_spPath;
}

Viewport read_viewport() {
    Viewport v{};
    if (!path()[0]) return v;

    // The governing pair.
    std::string text;
    if (!read_all(g_path, text)) return v;
    size_t start = 0, end = 0;
    if (!section_span(text, kSharedSection, start, end)) return v;
    long sx = section_value(text, start, end, "ViewportX");
    long sy = section_value(text, start, end, "ViewportY");
    if (sx <= 0 || sy <= 0) return v;
    v.w = static_cast<uint32_t>(sx);
    v.h = static_cast<uint32_t>(sy);
    v.startupFullscreen = section_bool(text, start, end, "StartupFullscreen");
    v.valid = true;

    // The ignored-but-kept-in-sync pair, reported only.
    std::string sp;
    if (g_spPath[0] && read_all(g_spPath, sp)) {
        size_t s2 = 0, e2 = 0;
        if (section_span(sp, kPcSection, s2, e2)) {
            long wx = section_value(sp, s2, e2, "WindowedViewportX");
            long wy = section_value(sp, s2, e2, "WindowedViewportY");
            long fx = section_value(sp, s2, e2, "FullscreenViewportX");
            long fy = section_value(sp, s2, e2, "FullscreenViewportY");
            v.windowedW = wx > 0 ? static_cast<uint32_t>(wx) : 0;
            v.windowedH = wy > 0 ? static_cast<uint32_t>(wy) : 0;
            v.fullscreenW = fx > 0 ? static_cast<uint32_t>(fx) : 0;
            v.fullscreenH = fy > 0 ? static_cast<uint32_t>(fy) : 0;
        }
    }
    return v;
}

bool write_viewport(uint32_t w, uint32_t h) {
    config_batch::Batch batch;
    const bool ok = prepare_viewport(batch, w, h) && batch.commit();
    BVR_LOG("[b2r] game ini: coordinated viewport save %ux%u %s", w, h, ok ? "verified" : "FAILED");
    return ok;
}
bool write_viewport_and_settings(uint32_t w, uint32_t h, const std::wstring& settings,
                                 bool existed, const std::string& before, const std::wstring& staged) {
    config_batch::Batch batch;
    std::string after; bool hasStage = false;
    const bool ok = prepare_viewport(batch, w, h) &&
        config_batch::read(staged, after, hasStage) && hasStage &&
        batch.add(settings, existed, before, after, L".bvr-bak-controls") && batch.commit();
    BVR_LOG("[b2r] game ini: Shared/SP/DLSS save %s", ok ? "verified" : "FAILED; see recovery log if present");
    return ok;
}

void log_status(uint32_t liveW, uint32_t liveH) {
    Viewport v = read_viewport();
    if (!v.valid) {
        BVR_LOG("[b2r] game ini: Shared.ini unreadable or missing %s", kSharedSection);
        return;
    }
    const bool agrees = liveW == 0 || (v.w == liveW && v.h == liveH);
    BVR_LOG("[b2r] game ini: Shared.ini %ux%u (GOVERNS) startupFullscreen=%s | "
            "Bioshock2SP.ini windowed %ux%u fullscreen %ux%u (ignored by the engine, synced) | "
            "live backbuffer %ux%u%s",
            v.w, v.h, v.startupFullscreen ? "True" : "False", v.windowedW, v.windowedH,
            v.fullscreenW, v.fullscreenH, liveW, liveH,
            agrees ? "" : " - NOT HONOURED YET (a change takes effect on the next launch; if it "
                          "persists past a relaunch, something else is winning - measure, do "
                          "not assume)");
}

} // namespace bvr::b2r::game_ini
