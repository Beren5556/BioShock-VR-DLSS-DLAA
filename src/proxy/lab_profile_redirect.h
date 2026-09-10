#pragma once

// Test-copy portability only. The approved game/mod binaries are never edited.
// Redirect the game's observed SHGetFolderPathW import before its entry point,
// since APPDATA/LOCALAPPDATA environment overrides alone do not redirect CSIDLs.
#include <windows.h>
#include <shlobj.h>
#include <cstring>
#include <cwchar>

namespace bvr_lab_profile {
using FolderPath = HRESULT(WINAPI*)(HWND, int, HANDLE, DWORD, LPWSTR);
inline FolderPath original = nullptr;
inline wchar_t root[MAX_PATH]{};
inline wchar_t roaming[MAX_PATH]{}, local[MAX_PATH]{}, documents[MAX_PATH]{}, profile[MAX_PATH]{};
inline wchar_t data[MAX_PATH]{}, gameIni[MAX_PATH]{};

// Only regular, existing drive paths are admissible. These Win32 filesystem
// calls do not load a DLL, which matters because install runs under loader lock.
inline bool canonical_path(const wchar_t* path, wchar_t (&out)[MAX_PATH]) {
    out[0] = L'\0';
    if (!path || wcslen(path) < 3 || path[1] != L':' || path[2] != L'\\' ||
        !((path[0] >= L'A' && path[0] <= L'Z') || (path[0] >= L'a' && path[0] <= L'z')))
        return false;
    const DWORD length = GetFullPathNameW(path, MAX_PATH, out, nullptr);
    if (length < 3 || length >= MAX_PATH) { out[0] = L'\0'; return false; }
    size_t n = wcslen(out);
    while (n > 3 && out[n - 1] == L'\\') out[--n] = L'\0';
    for (size_t i = 3; i <= n; ++i) {
        if (out[i] == L':' || out[i] == L'/') return false;
        // Win32 can trim a component's dots/spaces after canonicalization.
        // Reject those alternate spellings before testing a directory prefix.
        if ((out[i] == L'\\' || out[i] == L'\0') &&
            (out[i - 1] == L'.' || out[i - 1] == L' ')) return false;
    }
    return true;
}
inline bool physical_without_reparse(const wchar_t* path) {
    wchar_t normalized[MAX_PATH]{};
    if (!canonical_path(path, normalized)) return false;
    const size_t n = wcslen(normalized);
    // Inspect every ancestor as well as the leaf: checking the leaf alone
    // would miss a junction higher up the profile/save path.
    for (size_t i = 3; i <= n; ++i) {
        if (normalized[i] != L'\\' && normalized[i] != L'\0') continue;
        const wchar_t separator = normalized[i];
        normalized[i] = L'\0';
        const DWORD attributes = GetFileAttributesW(normalized);
        normalized[i] = separator;
        if (attributes == INVALID_FILE_ATTRIBUTES || (attributes & FILE_ATTRIBUTE_REPARSE_POINT) ||
            (i < n && !(attributes & FILE_ATTRIBUTE_DIRECTORY))) return false;
    }
    return true;
}
inline bool within(const wchar_t* path, const wchar_t* parent) {
    wchar_t normalizedPath[MAX_PATH]{}, normalizedParent[MAX_PATH]{};
    if (!canonical_path(path, normalizedPath) || !canonical_path(parent, normalizedParent)) return false;
    const size_t length = wcslen(normalizedParent);
    return wcslen(normalizedPath) > length &&
        _wcsnicmp(normalizedPath, normalizedParent, length) == 0 && normalizedPath[length] == L'\\';
}
inline bool approved_root(const wchar_t* path) {
    constexpr wchar_t prefix[] = L"D:\\BioShock2VR-DLSS-Lab\\game-";
    wchar_t normalized[MAX_PATH]{};
    if (!canonical_path(path, normalized)) return false;
    const size_t prefixLength = _countof(prefix) - 1;
    if (wcslen(normalized) != prefixLength + 36 || _wcsnicmp(normalized, prefix, prefixLength) != 0)
        return false;
    const wchar_t* guid = normalized + prefixLength;
    for (size_t i = 0; i < 36; ++i) {
        if (i == 8 || i == 13 || i == 18 || i == 23) { if (guid[i] != L'-') return false; }
        else if (!((guid[i] >= L'0' && guid[i] <= L'9') ||
                   (guid[i] >= L'a' && guid[i] <= L'f') || (guid[i] >= L'A' && guid[i] <= L'F')))
            return false;
    }
    return true;
}
inline bool environment(const wchar_t* name, wchar_t (&value)[MAX_PATH]) {
    wchar_t raw[MAX_PATH]{};
    const DWORD n = GetEnvironmentVariableW(name, raw, MAX_PATH);
    return n > 3 && n < MAX_PATH && canonical_path(raw, value) && physical_without_reparse(value);
}
inline HRESULT WINAPI redirected(HWND window, int csidl, HANDLE token, DWORD flags, LPWSTR out) {
    if (!out) return E_INVALIDARG;
    const wchar_t* target = nullptr;
    switch (csidl & 0xff) {
        case CSIDL_APPDATA: target = roaming; break;
        case CSIDL_LOCAL_APPDATA: target = local; break;
        case CSIDL_PERSONAL: target = documents; break;
        case CSIDL_PROFILE: target = profile; break;
        default: return original ? original(window, csidl, token, flags, out) : E_FAIL;
    }
    wcscpy_s(out, MAX_PATH, target);
    return S_OK;
}
inline bool install() {
    wchar_t executable[MAX_PATH]{};
    const DWORD exeLength = GetModuleFileNameW(nullptr, executable, MAX_PATH);
    if (!environment(L"BVR_LAB_GAME_ROOT", root) || !exeLength || exeLength >= MAX_PATH ||
        !within(executable, root) || !physical_without_reparse(executable) || !approved_root(root) ||
        !environment(L"BVR_LAB_ROAMING", roaming) || !within(roaming, root) ||
        !environment(L"BVR_LAB_LOCAL", local) || !within(local, root) ||
        !environment(L"BVR_LAB_DOCUMENTS", documents) || !within(documents, root) ||
        !environment(L"BVR_LAB_PROFILE", profile) || !within(profile, root) ||
        !environment(L"BVR_LAB_DATA_DIR", data) || !within(data, root) ||
        !environment(L"BVR_LAB_GAME_INI", gameIni) || !within(gameIni, root)) return false;
    auto* base = reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
    auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew <= 0 || dos->e_lfanew > 0x100000) return false;
    auto* nt = reinterpret_cast<IMAGE_NT_HEADERS32*>(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE || nt->FileHeader.Machine != IMAGE_FILE_MACHINE_I386 ||
        nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR32_MAGIC) return false;
    const auto directory = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (!directory.VirtualAddress || directory.Size < sizeof(IMAGE_IMPORT_DESCRIPTOR) ||
        directory.VirtualAddress >= nt->OptionalHeader.SizeOfImage) return false;
    auto* imports = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(base + directory.VirtualAddress);
    for (size_t i = 0; i < directory.Size / sizeof(*imports) && imports[i].Name; ++i) {
        if (_stricmp(reinterpret_cast<char*>(base + imports[i].Name), "SHELL32.dll")) continue;
        if (!imports[i].OriginalFirstThunk || !imports[i].FirstThunk) return false;
        auto* names = reinterpret_cast<IMAGE_THUNK_DATA32*>(base + imports[i].OriginalFirstThunk);
        auto* slots = reinterpret_cast<IMAGE_THUNK_DATA32*>(base + imports[i].FirstThunk);
        for (size_t j = 0; j < 4096 && names[j].u1.AddressOfData; ++j) {
            if (IMAGE_SNAP_BY_ORDINAL32(names[j].u1.Ordinal)) continue;
            auto* name = reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(base + names[j].u1.AddressOfData);
            if (strcmp(reinterpret_cast<char*>(name->Name), "SHGetFolderPathW")) continue;
            DWORD protection = 0;
            if (!VirtualProtect(&slots[j].u1.Function, sizeof(DWORD), PAGE_READWRITE, &protection)) return false;
            original = reinterpret_cast<FolderPath>(slots[j].u1.Function);
            InterlockedExchange(reinterpret_cast<volatile LONG*>(&slots[j].u1.Function),
                                reinterpret_cast<LONG>(&redirected));
            DWORD discarded = 0;
            return VirtualProtect(&slots[j].u1.Function, sizeof(DWORD), protection, &discarded) != FALSE;
        }
    }
    return false; // A different executable/import table must not silently escape isolation.
}
} // namespace bvr_lab_profile
