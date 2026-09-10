// Runs the shipping BS2 INI writer against generated files only.
#include "game/bioshock2r/game_ini.h"
#include "core/util/config_batch.h"
#include <windows.h>
#include <objbase.h>
#include <cstdio>
#include <string>
#pragma comment(lib, "ole32.lib")
namespace bvr::log { void write(const char*, ...) {} }
namespace {
unsigned checks = 0, failures = 0;
std::wstring root, shared, sp, settings, staged;
bool expect(bool ok, const char* label) {
    ++checks; if (!ok) ++failures;
    std::printf("%s %u: %s\n", ok ? "PASS" : "FAIL", checks, label); return ok;
}
void put(const std::wstring& path, const std::string& text) {
    HANDLE h = CreateFileW(path.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    DWORD wrote = 0;
    if (h == INVALID_HANDLE_VALUE || !WriteFile(h, text.data(), DWORD(text.size()), &wrote, nullptr) || wrote != text.size()) {
        std::printf("Fixture write failed\n"); ExitProcess(2);
    }
    CloseHandle(h);
}
std::string get(const std::wstring& path) {
    std::string bytes; bool exists = false;
    return bvr::config_batch::read(path, bytes, exists) && exists ? bytes : std::string();
}
const std::string sharedOriginal =
    "\xEF\xBB\xBF; [SharedOptions] is only a comment\r\n[Other]\r\nViewportX=7\r\n"
    " [SharedOptions] ; keep header\r\n ViewportX = 1920 ; width\r\nViewportY=1080\r\nStartupFullscreen=False\r\n"
    "Unrelated=Leave me\r\n[After]\r\nViewportX=13\r\n";
const std::string spOriginal =
    "[XeDrv.XenonClient]\r\nWindowedViewportX=320\r\nWindowedViewportY=200\r\n"
    "[WinDrv.WindowsClient]\r\nWindowedViewportX=1920\r\nWindowedViewportY=1080\r\n"
    "FullscreenViewportX=1920\r\nFullscreenViewportY=1080\r\nMenuViewportX=800\r\n"
    "[PS3Drv.PS3Client]\r\nWindowedViewportX=12\r\nWindowedViewportY=14\r\n"
    "[Engine.RenderConfig]\r\nRealTimeReflection=True\r\n";
void reset() { put(shared, sharedOriginal); put(sp, spOriginal); put(settings, "[dlss]\r\nmode=off\r\nKeep=123\r\n"); }
bool unchanged() { return get(shared) == sharedOriginal && get(sp) == spOriginal; }
bool failThird(size_t i) { return i != 2; }
bool conflict(size_t i) { if (i == 1) { put(shared, "EXTERNAL EDIT"); return false; } return true; }
}
int main() {
    using namespace bvr;
    wchar_t temp[MAX_PATH]{}, guidText[40]{}; GUID guid{};
    if (!GetTempPathW(MAX_PATH,temp) || FAILED(CoCreateGuid(&guid)) || !StringFromGUID2(guid,guidText,40)) return 2;
    root = std::wstring(temp) + L"bvr-bs2-config-test-" + guidText;
    if (!CreateDirectoryW(root.c_str(), nullptr)) return 2;
    shared=root+L"\\Shared.ini"; sp=root+L"\\Bioshock2SP.ini"; settings=root+L"\\dlss.ini"; staged=root+L"\\dlss-staged.ini";
    reset(); put(staged, "[dlss]\r\nmode=sr\r\nKeep=123\r\n");
    SetEnvironmentVariableW(L"BVR_LAB_GAME_INI", sp.c_str());
    namespace ini = b2r::game_ini;
    expect(std::wstring(ini::path()) == shared && std::wstring(ini::sp_path()) == sp, "isolated paths; no discovery of personal profile");
    auto v = ini::read_viewport();
    expect(v.valid && v.w == 1920 && v.h == 1080, "Shared.ini governs; comments are not sections");
    expect(ini::write_viewport(2400,2400), "both INIs saved and verified");
    const auto a = get(shared), b = get(sp);
    expect(a.find(" ViewportX = 2400 ; width") != std::string::npos && a.find("ViewportY=2400\r\n") != std::string::npos &&
           a.substr(0,3) == sharedOriginal.substr(0,3) && a.find("Unrelated=Leave me") != std::string::npos,
           "BOM, comments, whitespace, CRLF and unknown settings retained");
    expect(a.find("ViewportX=7\r\n") != std::string::npos && a.find("ViewportX=13\r\n") != std::string::npos &&
           b.find("WindowedViewportX=320") != std::string::npos && b.find("WindowedViewportX=12") != std::string::npos &&
           b.find("MenuViewportX=800") != std::string::npos && b.find("RealTimeReflection=True") != std::string::npos,
           "other sections, console drivers, menu and graphics untouched");
    expect(get(shared+L".bvr-bak-res")==sharedOriginal && get(sp+L".bvr-bak-res")==spOriginal, "verified first originals retained");
    expect(ini::write_viewport(2600,2600) && get(shared+L".bvr-bak-res")==sharedOriginal, "later save never replaces first backup");
    reset();
    expect(!ini::write_viewport(639,480) && !ini::write_viewport(2401,2400) && !ini::write_viewport(16386,2400) && unchanged(), "invalid dimensions write nothing");
    auto bad = spOriginal;
    bad.replace(bad.find("FullscreenViewportY=1080"), 23, "MissingViewportY=1080");
    put(sp,bad);
    expect(!ini::write_viewport(2400,2400) && get(shared)==sharedOriginal && get(sp)==bad, "missing secondary key prevents either write");
    reset(); bad=sharedOriginal; bad.insert(bad.find("StartupFullscreen"), "ViewportX=777\r\n"); put(shared,bad);
    expect(!ini::write_viewport(2400,2400) && get(shared)==bad && get(sp)==spOriginal, "duplicate governing key rejected");
    reset(); bad=sharedOriginal+"[SharedOptions]\r\nViewportX=555\r\nViewportY=555\r\n"; put(shared,bad);
    expect(!ini::write_viewport(2400,2400) && get(shared)==bad && get(sp)==spOriginal, "duplicate section rejected");
    reset(); SetFileAttributesW(sp.c_str(), FILE_ATTRIBUTE_READONLY);
    expect(!ini::write_viewport(2400,2400) && unchanged(), "read-only secondary file prevents partial save");
    SetFileAttributesW(sp.c_str(), FILE_ATTRIBUTE_NORMAL);
    const auto backup=sp+L".bvr-bak-res";
    DeleteFileW(backup.c_str()); CreateDirectoryW(backup.c_str(), nullptr);
    expect(!ini::write_viewport(2400,2400) && unchanged(), "unavailable backup aborts before replacement");
    RemoveDirectoryW(backup.c_str()); put(backup, spOriginal);
    const auto dlssBefore=get(settings);
    expect(ini::write_viewport_and_settings(2400,2400,settings,true,dlssBefore,staged) &&
           get(settings)==get(staged) && ini::read_viewport().w==2400, "Shared, SP and DLSS committed together");
    reset(); config_batch::before_replace=failThird;
    expect(!ini::write_viewport_and_settings(2400,2400,settings,true,dlssBefore,staged) &&
           unchanged() && get(settings)==dlssBefore, "failure at third replacement rolls back both INIs byte for byte");
    config_batch::before_replace=nullptr;
    expect(!ini::write_viewport_and_settings(2400,2400,settings,true,"stale snapshot",staged) &&
           unchanged() && get(settings)==dlssBefore, "concurrent DLSS edit is not overwritten");
    {
        const auto fresh=root+L"\\new.ini";
        config_batch::Batch batch;
        expect(batch.add(fresh,false,{},"new") && !batch.add(fresh,false,{},"duplicate"), "duplicate target rejected");
        batch.add(sp,true,spOriginal,"changed"); batch.add(settings,true,dlssBefore,"changed");
        config_batch::before_replace=failThird;
        expect(!batch.commit() && GetFileAttributesW(fresh.c_str())==INVALID_FILE_ATTRIBUTES &&
               unchanged() && get(settings)==dlssBefore, "rollback removes only newly-created file and restores existing files");
        config_batch::before_replace=nullptr;
    }
    config_batch::before_replace=conflict;
    expect(!ini::write_viewport_and_settings(2400,2400,settings,true,dlssBefore,staged) &&
           get(shared)=="EXTERNAL EDIT" && get(sp)==spOriginal, "rollback never clobbers a later external edit");
    config_batch::before_replace=nullptr;
    WIN32_FIND_DATAW data{};
    HANDLE found=FindFirstFileW((shared+L".bvr-*.rollback").c_str(), &data);
    expect(found!=INVALID_HANDLE_VALUE, "conflicting rollback retains recovery bytes");
    if(found!=INVALID_HANDLE_VALUE) FindClose(found);
    std::printf("BS2 config checks: %u/%u. Generated fixture retained: %ls\n", checks-failures, checks, root.c_str());
    return failures ? 1 : 0;
}
