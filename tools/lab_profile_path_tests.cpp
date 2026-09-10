#include "../src/proxy/lab_profile_redirect.h"
#include <cstdio>
#include <string>

int wmain(int argc, wchar_t** argv) {
    if (argc != 3) return 2;
    using namespace bvr_lab_profile;
    int failures = 0;
    auto check = [&](bool ok, const char* name) {
        std::printf("%s %s\n", ok ? "PASS" : "FAIL", name);
        if (!ok) ++failures;
    };
    const std::wstring parent = L"D:\\BioShock2VR-DLSS-Lab\\game-11111111-1111-1111-1111-111111111111";
    check(approved_root(parent.c_str()), "GUID root accepted");
    check(!approved_root((parent + L"suffix").c_str()), "root suffix rejected");
    check(!approved_root(L"D:\\BioShock2VR-DLSS-Lab\\game-invalid"), "invalid GUID rejected");
    check(within((parent + L"\\runs\\data").c_str(), parent.c_str()), "descendant accepted");
    check(!within(parent.c_str(), parent.c_str()), "root itself is not descendant");
    check(!within((parent + L"-sibling\\data").c_str(), parent.c_str()), "sibling prefix rejected");
    check(!within((parent + L"\\..\\..\\outside").c_str(), parent.c_str()), "dot-dot escape rejected");
    check(!within((parent + L"\\.. \\outside").c_str(), parent.c_str()), "Win32 trimmed dot-dot rejected");
    check(within((parent + L"\\runs\\tmp\\..\\data").c_str(), parent.c_str()), "internal canonical path accepted");
    check(!within(L"C:relative", parent.c_str()), "drive relative rejected");
    check(!within(L"\\\\server\\share\\data", parent.c_str()), "UNC rejected");
    check(!within((parent + L"\\file:stream").c_str(), parent.c_str()), "alternate stream rejected");
    check(physical_without_reparse(argv[1]), "physical directory accepted");
    check(!physical_without_reparse(argv[2]), "junction leaf rejected");
    check(!physical_without_reparse((std::wstring(argv[2]) + L"\\child").c_str()), "junction ancestor rejected");
    check(!physical_without_reparse((std::wstring(argv[1]) + L"\\missing").c_str()), "missing target rejected");
    wchar_t path[MAX_PATH]{};
    SetEnvironmentVariableW(L"BVR_PATH_GUARD_SELFTEST", argv[1]);
    check(environment(L"BVR_PATH_GUARD_SELFTEST", path), "physical environment path accepted");
    SetEnvironmentVariableW(L"BVR_PATH_GUARD_SELFTEST", argv[2]);
    check(!environment(L"BVR_PATH_GUARD_SELFTEST", path), "junction environment rejected");
    SetEnvironmentVariableW(L"BVR_PATH_GUARD_SELFTEST", nullptr);
    check(!environment(L"BVR_PATH_GUARD_SELFTEST", path), "missing environment rejected");
    return failures ? 1 : 0;
}
