#include "core/gfx/dlss45_client.h"

#include "core/util/log.h"

#include <d3d11_4.h>
#include <dxgi1_2.h>
#include <winver.h>

#include <cstdarg>
#include <cstdint>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

#pragma comment(lib, "version.lib")

namespace bvr::dlss45 {
namespace {

constexpr std::uint32_t kIpcMagic = 0x35534C44u; // 'DLS5', inherited wire magic
constexpr std::uint32_t kIpcVersion = 8u;
constexpr DWORD kStartupTimeoutMs = 30000;
constexpr DWORD kHelloTimeoutMs = 15000;
constexpr DWORD kBuildTimeoutMs = 20000;
constexpr DWORD kFramePipeTimeoutMs = 1500;
constexpr DWORD kFrameFenceTimeoutMs = 5000;
constexpr DWORD kShutdownTimeoutMs = 3000;

enum FeedSlot : int {
    FeedColor = 0,
    FeedOutput,
    FeedDepth,
    FeedMotion,
    FeedSlotCount,
};

constexpr std::uint32_t kClientD3D11 = 0;
constexpr std::uint32_t kAckSrActive = 1u;
constexpr std::uint32_t kAckSrUnavailable = 2u;

#pragma pack(push, 1)
struct FeedHello {
    std::uint32_t magic;
    std::uint32_t version;
    std::uint32_t pid;
    std::uint32_t clientKind;
    std::uint64_t selfProcess;
};

struct FeedHelloAck {
    std::uint32_t magic;
    std::uint32_t version;
    std::uint32_t panelWidth;
    std::uint32_t panelHeight;
};

struct FeedBuild {
    std::uint32_t width;
    std::uint32_t height;
    std::uint32_t colorFormat;
    std::uint32_t outputFormat;
    std::int32_t hdr;
    std::int32_t depthInverted;
    std::int32_t flagsOverride;
    std::int32_t transport;
    float motionScaleX;
    float motionScaleY;
    std::uint64_t textures[FeedSlotCount];
    std::uint32_t clientFlags;
    std::uint32_t targetWidth;
    std::uint32_t targetHeight;
    std::uint64_t panelTexture;
};

struct FeedBuildAck {
    std::int32_t ok;
    std::uint32_t ngxResult;
    std::uint64_t fenceIn;
    std::uint64_t fenceOut;
    std::uint64_t textures[FeedSlotCount];
    std::uint64_t textureSizes[FeedSlotCount];
    std::uint32_t outputFormat;
    std::uint32_t flags;
    std::uint32_t srQuality;
    std::uint64_t panelTexture;
    std::uint64_t panelSize;
};

struct FeedFrame {
    std::uint64_t fenceValue;
    std::uint32_t reset;
    float jitterX;
    float jitterY;
};
#pragma pack(pop)

static_assert(sizeof(FeedHello) == 24);
static_assert(sizeof(FeedHelloAck) == 16);
static_assert(sizeof(FeedBuild) == 92);
static_assert(sizeof(FeedBuildAck) == 116);
static_assert(sizeof(FeedFrame) == 20);

template <typename T>
void release_one(T*& value) noexcept {
    if (value) {
        value->Release();
        value = nullptr;
    }
}

struct EyeState {
    int index = -1;
    std::wstring directory;
    std::wstring executable;
    HANDLE process = nullptr;
    DWORD processId = 0;
    HANDLE pipe = INVALID_HANDLE_VALUE;
    HANDLE pipeEvent = nullptr;
    HANDLE completionEvent = nullptr;
    ID3D11Texture2D* textures[FeedSlotCount] = {};
    HANDLE textureHandles[FeedSlotCount] = {};
    ID3D11Fence* fenceIn = nullptr;
    ID3D11Fence* fenceOut = nullptr;
    std::uint64_t frame = 0;
};

EyeState g_eyes[2];
ID3D11Device* g_device = nullptr;
ID3D11Device5* g_device5 = nullptr;
HANDLE g_job = nullptr;
UINT g_renderWidth = 0;
UINT g_renderHeight = 0;
UINT g_outputWidth = 0;
UINT g_outputHeight = 0;
DXGI_FORMAT g_backbufferFormat = DXGI_FORMAT_UNKNOWN;
DXGI_FORMAT g_colorFormat = DXGI_FORMAT_UNKNOWN;
DXGI_FORMAT g_outputFormat = DXGI_FORMAT_UNKNOWN;
Mode g_mode = Mode::Off;
bool g_depthInverted = false;
bool g_isReady = false;
bool g_faulted = false;
char g_status[512] = "desactivado";

void set_status_v(const char* format, va_list args) noexcept {
    _vsnprintf_s(g_status, sizeof(g_status), _TRUNCATE, format, args);
    g_status[sizeof(g_status) - 1] = '\0';
}

void set_status(const char* format, ...) noexcept {
    va_list args;
    va_start(args, format);
    set_status_v(format, args);
    va_end(args);
}

std::wstring join_path(const std::wstring& left, const std::wstring& right) {
    if (left.empty()) return right;
    if (left.back() == L'\\' || left.back() == L'/') return left + right;
    return left + L"\\" + right;
}

std::wstring parent_path(const std::wstring& path) {
    const std::wstring::size_type slash = path.find_last_of(L"\\/");
    return slash == std::wstring::npos ? std::wstring() : path.substr(0, slash);
}

std::wstring absolute_path(const wchar_t* path) {
    if (!path || !*path) return {};
    const DWORD required = GetFullPathNameW(path, 0, nullptr, nullptr);
    if (!required) return {};
    std::vector<wchar_t> buffer(required);
    if (!GetFullPathNameW(path, required, buffer.data(), nullptr)) return {};
    return std::wstring(buffer.data());
}

bool file_exists(const std::wstring& path) noexcept {
    const DWORD attributes = GetFileAttributesW(path.c_str());
    return attributes != INVALID_FILE_ATTRIBUTES &&
           (attributes & FILE_ATTRIBUTE_DIRECTORY) == 0;
}

bool ensure_directory(const std::wstring& path) noexcept {
    const DWORD attributes = GetFileAttributesW(path.c_str());
    if (attributes != INVALID_FILE_ATTRIBUTES)
        return (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
    if (CreateDirectoryW(path.c_str(), nullptr)) return true;
    return GetLastError() == ERROR_ALREADY_EXISTS;
}

bool copy_unless_same(const std::wstring& source, const std::wstring& destination) noexcept {
    if (_wcsicmp(source.c_str(), destination.c_str()) == 0) return true;
    return CopyFileW(source.c_str(), destination.c_str(), FALSE) != FALSE;
}

bool is_x64_pe(const std::wstring& path) noexcept {
    HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
                              nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;

    IMAGE_DOS_HEADER dos{};
    DWORD read = 0;
    bool ok = ReadFile(file, &dos, sizeof(dos), &read, nullptr) &&
              read == sizeof(dos) && dos.e_magic == IMAGE_DOS_SIGNATURE && dos.e_lfanew > 0;
    DWORD signature = 0;
    IMAGE_FILE_HEADER header{};
    if (ok) {
        LARGE_INTEGER offset{};
        offset.QuadPart = dos.e_lfanew;
        ok = SetFilePointerEx(file, offset, nullptr, FILE_BEGIN) &&
             ReadFile(file, &signature, sizeof(signature), &read, nullptr) &&
             read == sizeof(signature) && signature == IMAGE_NT_SIGNATURE &&
             ReadFile(file, &header, sizeof(header), &read, nullptr) &&
             read == sizeof(header) && header.Machine == IMAGE_FILE_MACHINE_AMD64;
    }
    CloseHandle(file);
    return ok;
}

bool is_dlss_310_7_0(const std::wstring& path,
                     DWORD* actualMajor = nullptr, DWORD* actualMinor = nullptr,
                     DWORD* actualBuild = nullptr, DWORD* actualRevision = nullptr) {
    DWORD ignored = 0;
    const DWORD size = GetFileVersionInfoSizeW(path.c_str(), &ignored);
    if (!size) return false;
    std::vector<BYTE> data(size);
    if (!GetFileVersionInfoW(path.c_str(), 0, size, data.data())) return false;

    VS_FIXEDFILEINFO* version = nullptr;
    UINT versionSize = 0;
    if (!VerQueryValueW(data.data(), L"\\", reinterpret_cast<void**>(&version),
                        &versionSize) || !version ||
        versionSize < sizeof(VS_FIXEDFILEINFO) ||
        version->dwSignature != VS_FFI_SIGNATURE)
        return false;

    const DWORD major = HIWORD(version->dwFileVersionMS);
    const DWORD minor = LOWORD(version->dwFileVersionMS);
    const DWORD build = HIWORD(version->dwFileVersionLS);
    const DWORD revision = LOWORD(version->dwFileVersionLS);
    if (actualMajor) *actualMajor = major;
    if (actualMinor) *actualMinor = minor;
    if (actualBuild) *actualBuild = build;
    if (actualRevision) *actualRevision = revision;
    return major == 310 && minor == 7 && build == 0 && revision == 0;
}

bool capabilities_are_dlss45(const std::wstring& path) noexcept {
    wchar_t phase[32]{};
    wchar_t runtime[32]{};
    GetPrivateProfileStringW(L"backend", L"phase", L"", phase,
                             static_cast<DWORD>(_countof(phase)), path.c_str());
    GetPrivateProfileStringW(L"backend", L"runtime", L"", runtime,
                             static_cast<DWORD>(_countof(runtime)), path.c_str());
    const UINT eyeHosts = GetPrivateProfileIntW(L"backend", L"eyeHosts", 0, path.c_str());
    const UINT protocol = GetPrivateProfileIntW(L"backend", L"protocol", 0, path.c_str());
    return _wcsicmp(phase, L"DLSS45") == 0 && eyeHosts >= 2 &&
           wcscmp(runtime, L"310.7.0") == 0 && protocol == kIpcVersion;
}

bool package_is_clean(const std::wstring& directory, const wchar_t** offending) {
    static const wchar_t* kForbidden[] = {
        L"nvngx_dlssnr.dll",
        L"renodx-dlss5.addon64",
        L"dlss5-feed.addon64",
        L"dxgi.dll",
        L"d3d11.dll",
        L"ReShade64.dll",
        L"ReShade32.dll",
    };
    for (const wchar_t* name : kForbidden) {
        if (file_exists(join_path(directory, name))) {
            if (offending) *offending = name;
            return false;
        }
    }

    // The feeder accepts version-suffixed RenoDX add-ons too. Reject the same
    // wildcard so renaming an old phase-5 file cannot bypass the phase-1 gate.
    const std::wstring wildcard = join_path(directory, L"renodx-dlss5*.addon64");
    WIN32_FIND_DATAW found{};
    HANDLE find = FindFirstFileW(wildcard.c_str(), &found);
    if (find != INVALID_HANDLE_VALUE) {
        FindClose(find);
        if (offending) *offending = L"renodx-dlss5*.addon64";
        return false;
    }
    return true;
}

DXGI_FORMAT typed_color_format(DXGI_FORMAT format) noexcept {
    switch (format) {
        case DXGI_FORMAT_R8G8B8A8_TYPELESS:
        case DXGI_FORMAT_R8G8B8A8_UNORM:
        case DXGI_FORMAT_R8G8B8A8_UNORM_SRGB:
            return DXGI_FORMAT_R8G8B8A8_UNORM;
        case DXGI_FORMAT_B8G8R8A8_TYPELESS:
        case DXGI_FORMAT_B8G8R8A8_UNORM:
        case DXGI_FORMAT_B8G8R8A8_UNORM_SRGB:
            return DXGI_FORMAT_B8G8R8A8_UNORM;
        case DXGI_FORMAT_R10G10B10A2_TYPELESS:
        case DXGI_FORMAT_R10G10B10A2_UNORM:
            return DXGI_FORMAT_R10G10B10A2_UNORM;
        case DXGI_FORMAT_R16G16B16A16_TYPELESS:
        case DXGI_FORMAT_R16G16B16A16_FLOAT:
            return DXGI_FORMAT_R16G16B16A16_FLOAT;
        case DXGI_FORMAT_R11G11B10_FLOAT:
            return DXGI_FORMAT_R11G11B10_FLOAT;
        default:
            return DXGI_FORMAT_UNKNOWN;
    }
}

DXGI_FORMAT output_format_for(DXGI_FORMAT color) noexcept {
    switch (color) {
        case DXGI_FORMAT_R16G16B16A16_FLOAT:
        case DXGI_FORMAT_R11G11B10_FLOAT:
            return DXGI_FORMAT_R16G16B16A16_FLOAT;
        case DXGI_FORMAT_R10G10B10A2_UNORM:
            return DXGI_FORMAT_R10G10B10A2_UNORM;
        default:
            // NGX needs a typed UAV. R8G8B8A8 is guaranteed on the target GPU
            // and is also the OpenXR swapchain family used by BioShock VR.
            return DXGI_FORMAT_R8G8B8A8_UNORM;
    }
}

int format_family(DXGI_FORMAT format) noexcept {
    switch (format) {
        case DXGI_FORMAT_R8G8B8A8_TYPELESS:
        case DXGI_FORMAT_R8G8B8A8_UNORM:
        case DXGI_FORMAT_R8G8B8A8_UNORM_SRGB: return 1;
        case DXGI_FORMAT_B8G8R8A8_TYPELESS:
        case DXGI_FORMAT_B8G8R8A8_UNORM:
        case DXGI_FORMAT_B8G8R8A8_UNORM_SRGB: return 2;
        case DXGI_FORMAT_R10G10B10A2_TYPELESS:
        case DXGI_FORMAT_R10G10B10A2_UNORM: return 3;
        case DXGI_FORMAT_R16G16B16A16_TYPELESS:
        case DXGI_FORMAT_R16G16B16A16_FLOAT: return 4;
        case DXGI_FORMAT_R11G11B10_FLOAT: return 5;
        default: return 0;
    }
}

bool format_compatible(DXGI_FORMAT left, DXGI_FORMAT right) noexcept {
    const int family = format_family(left);
    return family != 0 && family == format_family(right);
}

void close_pipe(EyeState& eye) noexcept {
    if (eye.pipe != INVALID_HANDLE_VALUE) {
        CancelIoEx(eye.pipe, nullptr);
        CloseHandle(eye.pipe);
        eye.pipe = INVALID_HANDLE_VALUE;
    }
}

void close_all_pipes() noexcept {
    close_pipe(g_eyes[0]);
    close_pipe(g_eyes[1]);
}

void stop_helpers_bounded() noexcept {
    close_all_pipes();

    HANDLE processes[2] = {};
    DWORD count = 0;
    for (EyeState& eye : g_eyes)
        if (eye.process) processes[count++] = eye.process;

    bool exited = count == 0;
    if (count)
        exited = WaitForMultipleObjects(count, processes, TRUE, kShutdownTimeoutMs) == WAIT_OBJECT_0;

    if (!exited) {
        BVR_LOG("[dlss45] helpers did not drain in %lu ms; terminating owned child processes",
                static_cast<unsigned long>(kShutdownTimeoutMs));
        for (EyeState& eye : g_eyes)
            if (eye.process && WaitForSingleObject(eye.process, 0) == WAIT_TIMEOUT)
                TerminateProcess(eye.process, 0xD145u);
        if (count) WaitForMultipleObjects(count, processes, TRUE, 1000);
    }

    // KILL_ON_JOB_CLOSE is the final guard for a helper stuck below user mode.
    if (g_job) {
        CloseHandle(g_job);
        g_job = nullptr;
    }
    for (EyeState& eye : g_eyes) {
        if (eye.process) CloseHandle(eye.process);
        eye.process = nullptr;
        eye.processId = 0;
    }
}

void release_resources() noexcept {
    for (EyeState& eye : g_eyes) {
        release_one(eye.fenceIn);
        release_one(eye.fenceOut);
        for (int slot = 0; slot < FeedSlotCount; ++slot) {
            release_one(eye.textures[slot]);
            if (eye.textureHandles[slot]) CloseHandle(eye.textureHandles[slot]);
            eye.textureHandles[slot] = nullptr;
        }
        if (eye.pipeEvent) CloseHandle(eye.pipeEvent);
        if (eye.completionEvent) CloseHandle(eye.completionEvent);
        eye.pipeEvent = nullptr;
        eye.completionEvent = nullptr;
        eye.frame = 0;
        eye.directory.clear();
        eye.executable.clear();
    }
    release_one(g_device5);
    release_one(g_device);
    g_renderWidth = g_renderHeight = 0;
    g_outputWidth = g_outputHeight = 0;
    g_backbufferFormat = DXGI_FORMAT_UNKNOWN;
    g_colorFormat = DXGI_FORMAT_UNKNOWN;
    g_outputFormat = DXGI_FORMAT_UNKNOWN;
    g_mode = Mode::Off;
    g_depthInverted = false;
    g_isReady = false;
}

void cleanup(bool resetStatus) noexcept {
    stop_helpers_bounded();
    release_resources();
    g_faulted = false;
    if (resetStatus) set_status("desactivado");
}

bool prepare_failure(const char* format, ...) noexcept {
    va_list args;
    va_start(args, format);
    set_status_v(format, args);
    va_end(args);
    BVR_LOG("[dlss45] prepare failed: %s", g_status);
    cleanup(false);
    return false;
}

void runtime_failure(const char* format, ...) noexcept {
    if (g_faulted) return;
    va_list args;
    va_start(args, format);
    set_status_v(format, args);
    va_end(args);
    g_faulted = true;
    g_isReady = false;
    BVR_LOG("[dlss45] disabled fail-soft: %s", g_status);

    // process_eye has not queued a fence wait unless the host already completed
    // it. Closing the streams and stopping both owned helpers therefore cannot
    // leave an unsatisfied wait in the game's D3D11 queue.
    close_all_pipes();
    for (EyeState& eye : g_eyes)
        if (eye.process && WaitForSingleObject(eye.process, 0) == WAIT_TIMEOUT)
            TerminateProcess(eye.process, 0xD146u);
}

bool create_job() noexcept {
    g_job = CreateJobObjectW(nullptr, nullptr);
    if (!g_job) return false;
    JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};
    limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
    if (!SetInformationJobObject(g_job, JobObjectExtendedLimitInformation,
                                 &limits, sizeof(limits))) {
        CloseHandle(g_job);
        g_job = nullptr;
        return false;
    }
    return true;
}

bool stage_runtime(const std::wstring& hostSource) {
    const std::wstring packageDirectory = parent_path(hostSource);
    const std::wstring dlssSource = join_path(packageDirectory, L"nvngx_dlss.dll");
    const std::wstring capabilitiesSource =
        join_path(packageDirectory, L"dlss-capabilities.ini");
    if (!file_exists(hostSource))
        return prepare_failure("no se encuentra BioShockVR-DLSS45-Host64.exe");
    if (!file_exists(dlssSource))
        return prepare_failure("falta nvngx_dlss.dll junto al host DLSS 4.5");
    if (!file_exists(capabilitiesSource))
        return prepare_failure("falta dlss-capabilities.ini junto al host DLSS 4.5");
    if (!is_x64_pe(hostSource))
        return prepare_failure("el host indicado no es un ejecutable x64");
    if (!is_x64_pe(dlssSource))
        return prepare_failure("nvngx_dlss.dll no es la version x64 requerida");
    DWORD versionMajor = 0, versionMinor = 0, versionBuild = 0, versionRevision = 0;
    if (!is_dlss_310_7_0(dlssSource, &versionMajor, &versionMinor,
                         &versionBuild, &versionRevision)) {
        BVR_LOG("[dlss45] rejected nvngx_dlss.dll FileVersion %lu.%lu.%lu.%lu "
                "(required 310.7.0.0)",
                static_cast<unsigned long>(versionMajor),
                static_cast<unsigned long>(versionMinor),
                static_cast<unsigned long>(versionBuild),
                static_cast<unsigned long>(versionRevision));
        return prepare_failure("nvngx_dlss.dll no tiene FileVersion 310.7.0.0");
    }
    if (!capabilities_are_dlss45(capabilitiesSource))
        return prepare_failure("dlss-capabilities.ini no declara DLSS45, dos hosts, runtime 310.7.0 e IPC v8");

    const wchar_t* offending = nullptr;
    if (!package_is_clean(packageDirectory, &offending)) {
        BVR_LOG("[dlss45] rejected contaminated source package: %ls", offending);
        return prepare_failure("paquete rechazado: contiene componentes de Neural Rendering/"
                               "ReShade incompatibles con la fase DLSS 4.5");
    }

    const DWORD required = GetEnvironmentVariableW(L"LOCALAPPDATA", nullptr, 0);
    if (!required) return prepare_failure("LOCALAPPDATA no esta disponible para aislar los hosts");
    std::vector<wchar_t> localBuffer(required);
    if (!GetEnvironmentVariableW(L"LOCALAPPDATA", localBuffer.data(), required))
        return prepare_failure("no se pudo leer LOCALAPPDATA");

    const std::wstring bioshockDir = join_path(localBuffer.data(), L"BioshockVR");
    const std::wstring runtimeRoot = join_path(bioshockDir, L"DLSS45Host");
    if (!ensure_directory(bioshockDir) || !ensure_directory(runtimeRoot))
        return prepare_failure("no se pudo crear el runtime local aislado para DLSS 4.5");

    for (int index = 0; index < 2; ++index) {
        EyeState& eye = g_eyes[index];
        eye.index = index;
        eye.directory = join_path(runtimeRoot, index == 0 ? L"eye0" : L"eye1");
        eye.executable = join_path(eye.directory, L"BioShockVR-DLSS45-Host64.exe");
        if (!ensure_directory(eye.directory))
            return prepare_failure("no se pudo crear la carpeta aislada de un ojo");

        offending = nullptr;
        if (!package_is_clean(eye.directory, &offending)) {
            BVR_LOG("[dlss45] rejected contaminated eye%d runtime: %ls", index, offending);
            return prepare_failure("runtime local rechazado: conserva componentes de Neural Rendering/ReShade");
        }
        if (!copy_unless_same(hostSource, eye.executable) ||
            !copy_unless_same(dlssSource, join_path(eye.directory, L"nvngx_dlss.dll")) ||
            !copy_unless_same(capabilitiesSource,
                              join_path(eye.directory, L"dlss-capabilities.ini"))) {
            BVR_LOG("[dlss45] CopyFile failed for eye%d: %lu", index,
                    static_cast<unsigned long>(GetLastError()));
            return prepare_failure("no se pudieron preparar los binarios limpios de los hosts");
        }
    }
    return true;
}

bool launch_host(EyeState& eye, DWORD gamePid) {
    std::wstring command = L"\"" + eye.executable + L"\" " +
                           std::to_wstring(gamePid) + L" --eye " +
                           std::to_wstring(eye.index) + L" --hide";
    std::vector<wchar_t> mutableCommand(command.begin(), command.end());
    mutableCommand.push_back(L'\0');

    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(eye.executable.c_str(), mutableCommand.data(), nullptr, nullptr,
                        FALSE, CREATE_NO_WINDOW | CREATE_SUSPENDED, nullptr,
                        eye.directory.c_str(), &startup, &process)) {
        BVR_LOG("[dlss45] eye%d CreateProcess failed: %lu", eye.index,
                static_cast<unsigned long>(GetLastError()));
        return false;
    }

    eye.process = process.hProcess;
    eye.processId = process.dwProcessId;
    if (!AssignProcessToJobObject(g_job, eye.process)) {
        BVR_LOG("[dlss45] eye%d AssignProcessToJobObject failed: %lu", eye.index,
                static_cast<unsigned long>(GetLastError()));
        TerminateProcess(eye.process, 0xD147u);
        CloseHandle(process.hThread);
        return false;
    }
    if (ResumeThread(process.hThread) == static_cast<DWORD>(-1)) {
        BVR_LOG("[dlss45] eye%d ResumeThread failed: %lu", eye.index,
                static_cast<unsigned long>(GetLastError()));
        TerminateProcess(eye.process, 0xD148u);
        CloseHandle(process.hThread);
        return false;
    }
    CloseHandle(process.hThread);
    BVR_LOG("[dlss45] eye%d host started pid=%lu from %ls", eye.index,
            static_cast<unsigned long>(eye.processId), eye.directory.c_str());
    return true;
}

bool connect_pipe(EyeState& eye, DWORD gamePid) noexcept {
    wchar_t pipeName[128]{};
    _snwprintf_s(pipeName, _countof(pipeName), _TRUNCATE,
                 L"\\\\.\\pipe\\bvr-dlss45.%lu.eye%d",
                 static_cast<unsigned long>(gamePid), eye.index);

    eye.pipeEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    eye.completionEvent = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    if (!eye.pipeEvent || !eye.completionEvent) return false;

    const ULONGLONG deadline = GetTickCount64() + kStartupTimeoutMs;
    while (GetTickCount64() < deadline) {
        eye.pipe = CreateFileW(pipeName, GENERIC_READ | GENERIC_WRITE, 0, nullptr,
                               OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr);
        if (eye.pipe != INVALID_HANDLE_VALUE) {
            BVR_LOG("[dlss45] eye%d connected to %ls", eye.index, pipeName);
            return true;
        }
        if (WaitForSingleObject(eye.process, 0) == WAIT_OBJECT_0) {
            DWORD code = 0;
            GetExitCodeProcess(eye.process, &code);
            BVR_LOG("[dlss45] eye%d host exited before pipe connection (code %lu)", eye.index,
                    static_cast<unsigned long>(code));
            return false;
        }
        if (GetLastError() == ERROR_PIPE_BUSY) WaitNamedPipeW(pipeName, 100);
        else Sleep(50);
    }
    return false;
}

bool pipe_transfer(EyeState& eye, bool write, void* bytes, DWORD size,
                   DWORD timeoutMs) noexcept {
    BYTE* cursor = static_cast<BYTE*>(bytes);
    while (size) {
        OVERLAPPED operation{};
        operation.hEvent = eye.pipeEvent;
        ResetEvent(eye.pipeEvent);
        DWORD moved = 0;
        const BOOL immediate = write
            ? WriteFile(eye.pipe, cursor, size, &moved, &operation)
            : ReadFile(eye.pipe, cursor, size, &moved, &operation);
        if (!immediate) {
            if (GetLastError() != ERROR_IO_PENDING) return false;
            HANDLE waits[2] = {eye.pipeEvent, eye.process};
            const DWORD result = WaitForMultipleObjects(2, waits, FALSE, timeoutMs);
            if (result != WAIT_OBJECT_0) {
                CancelIoEx(eye.pipe, &operation);
                GetOverlappedResult(eye.pipe, &operation, &moved, TRUE);
                return false;
            }
            if (!GetOverlappedResult(eye.pipe, &operation, &moved, FALSE)) return false;
        }
        if (!moved) return false;
        cursor += moved;
        size -= moved;
    }
    return true;
}

bool pipe_write(EyeState& eye, const void* bytes, DWORD size, DWORD timeoutMs) noexcept {
    return pipe_transfer(eye, true, const_cast<void*>(bytes), size, timeoutMs);
}

bool pipe_read(EyeState& eye, void* bytes, DWORD size, DWORD timeoutMs) noexcept {
    return pipe_transfer(eye, false, bytes, size, timeoutMs);
}

bool hello_host(EyeState& eye, DWORD gamePid) noexcept {
    HANDLE selfInHost = nullptr;
    if (!DuplicateHandle(GetCurrentProcess(), GetCurrentProcess(), eye.process,
                         &selfInHost,
                         PROCESS_DUP_HANDLE | PROCESS_QUERY_LIMITED_INFORMATION,
                         FALSE, 0)) {
        BVR_LOG("[dlss45] eye%d could not duplicate game process handle: %lu", eye.index,
                static_cast<unsigned long>(GetLastError()));
        return false;
    }

    FeedHello hello{};
    hello.magic = kIpcMagic;
    hello.version = kIpcVersion;
    hello.pid = gamePid;
    hello.clientKind = kClientD3D11;
    hello.selfProcess = static_cast<std::uint64_t>(reinterpret_cast<std::uintptr_t>(selfInHost));
    FeedHelloAck ack{};
    if (!pipe_write(eye, &hello, sizeof(hello), kHelloTimeoutMs) ||
        !pipe_read(eye, &ack, sizeof(ack), kHelloTimeoutMs))
        return false;
    if (ack.magic != kIpcMagic || ack.version != kIpcVersion) {
        BVR_LOG("[dlss45] eye%d protocol mismatch: host v%u, client v%u", eye.index,
                ack.version, kIpcVersion);
        return false;
    }
    return true;
}

bool make_shared_texture(ID3D11Device* device, UINT width, UINT height,
                         DXGI_FORMAT format, UINT bindFlags,
                         ID3D11Texture2D** texture, HANDLE* sharedHandle) noexcept {
    D3D11_TEXTURE2D_DESC desc{};
    desc.Width = width;
    desc.Height = height;
    desc.MipLevels = 1;
    desc.ArraySize = 1;
    desc.Format = format;
    desc.SampleDesc.Count = 1;
    desc.Usage = D3D11_USAGE_DEFAULT;
    desc.BindFlags = bindFlags;
    desc.MiscFlags = D3D11_RESOURCE_MISC_SHARED_NTHANDLE |
                     D3D11_RESOURCE_MISC_SHARED;
    HRESULT result = device->CreateTexture2D(&desc, nullptr, texture);
    if (FAILED(result)) return false;

    IDXGIResource1* resource = nullptr;
    result = (*texture)->QueryInterface(IID_PPV_ARGS(&resource));
    if (SUCCEEDED(result)) {
        result = resource->CreateSharedHandle(
            nullptr, DXGI_SHARED_RESOURCE_READ | DXGI_SHARED_RESOURCE_WRITE,
            nullptr, sharedHandle);
        resource->Release();
    }
    return SUCCEEDED(result);
}

bool create_eye_resources(EyeState& eye) noexcept {
    constexpr UINT kInputBind = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_RENDER_TARGET;
    return make_shared_texture(g_device, g_renderWidth, g_renderHeight, g_colorFormat,
                               kInputBind, &eye.textures[FeedColor],
                               &eye.textureHandles[FeedColor]) &&
           make_shared_texture(g_device, g_outputWidth, g_outputHeight, g_outputFormat,
                               D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_UNORDERED_ACCESS,
                               &eye.textures[FeedOutput],
                               &eye.textureHandles[FeedOutput]) &&
           make_shared_texture(g_device, g_renderWidth, g_renderHeight,
                               DXGI_FORMAT_R32_FLOAT, kInputBind,
                               &eye.textures[FeedDepth],
                               &eye.textureHandles[FeedDepth]) &&
           make_shared_texture(g_device, g_renderWidth, g_renderHeight,
                               DXGI_FORMAT_R16G16_FLOAT, kInputBind,
                               &eye.textures[FeedMotion],
                               &eye.textureHandles[FeedMotion]);
}

bool build_host(EyeState& eye) noexcept {
    FeedBuild build{};
    build.width = g_renderWidth;
    build.height = g_renderHeight;
    build.colorFormat = static_cast<std::uint32_t>(g_colorFormat);
    build.outputFormat = static_cast<std::uint32_t>(g_outputFormat);
    build.hdr = g_colorFormat == DXGI_FORMAT_R16G16B16A16_FLOAT ||
                g_colorFormat == DXGI_FORMAT_R11G11B10_FLOAT;
    // This is part of feature creation, not per-frame evaluation. Keep it in
    // the prepare contract so the host describes the same hardware-depth
    // convention temporal_guides copied from the game.
    build.depthInverted = g_depthInverted ? 1 : 0;
    build.flagsOverride = -1;
    build.transport = 0;
    build.motionScaleX = 1.0f;
    build.motionScaleY = 1.0f;
    for (int slot = 0; slot < FeedSlotCount; ++slot)
        build.textures[slot] = static_cast<std::uint64_t>(
            reinterpret_cast<std::uintptr_t>(eye.textureHandles[slot]));
    // Zero deliberately means same-frame. FEED_BUILD_ASYNC_HOME is never set.
    build.clientFlags = 0;
    if (g_mode == Mode::SuperResolution) {
        build.targetWidth = g_outputWidth;
        build.targetHeight = g_outputHeight;
    }

    const BYTE tag = 'B';
    FeedBuildAck ack{};
    if (!pipe_write(eye, &tag, 1, kBuildTimeoutMs) ||
        !pipe_write(eye, &build, sizeof(build), kBuildTimeoutMs) ||
        !pipe_read(eye, &ack, sizeof(ack), kBuildTimeoutMs))
        return false;

    auto close_ack_handles = [&ack]() noexcept {
        HANDLE in = reinterpret_cast<HANDLE>(static_cast<std::uintptr_t>(ack.fenceIn));
        HANDLE out = reinterpret_cast<HANDLE>(static_cast<std::uintptr_t>(ack.fenceOut));
        if (in) CloseHandle(in);
        if (out) CloseHandle(out);
        if (ack.panelTexture)
            CloseHandle(reinterpret_cast<HANDLE>(static_cast<std::uintptr_t>(ack.panelTexture)));
        for (std::uint64_t texture : ack.textures)
            if (texture) CloseHandle(reinterpret_cast<HANDLE>(static_cast<std::uintptr_t>(texture)));
    };

    const bool wantSr = g_mode == Mode::SuperResolution;
    const bool gotSr = (ack.flags & kAckSrActive) != 0;
    if (!ack.ok || wantSr != gotSr ||
        ack.outputFormat != static_cast<std::uint32_t>(g_outputFormat)) {
        BVR_LOG("[dlss45] eye%d build rejected: ok=%d ngx=0x%08X flags=0x%X "
                "output=%u expected=%u", eye.index, ack.ok, ack.ngxResult, ack.flags,
                ack.outputFormat, static_cast<unsigned>(g_outputFormat));
        if (ack.flags & kAckSrUnavailable)
            BVR_LOG("[dlss45] requested SR ratio is not offered by the DLSS runtime");
        close_ack_handles();
        return false;
    }

    HANDLE inHandle = reinterpret_cast<HANDLE>(static_cast<std::uintptr_t>(ack.fenceIn));
    HANDLE outHandle = reinterpret_cast<HANDLE>(static_cast<std::uintptr_t>(ack.fenceOut));
    const HRESULT inResult = g_device5->OpenSharedFence(
        inHandle, IID_PPV_ARGS(&eye.fenceIn));
    const HRESULT outResult = g_device5->OpenSharedFence(
        outHandle, IID_PPV_ARGS(&eye.fenceOut));
    if (inHandle) CloseHandle(inHandle);
    if (outHandle) CloseHandle(outHandle);
    if (ack.panelTexture)
        CloseHandle(reinterpret_cast<HANDLE>(static_cast<std::uintptr_t>(ack.panelTexture)));
    if (FAILED(inResult) || FAILED(outResult) || !eye.fenceIn || !eye.fenceOut) {
        BVR_LOG("[dlss45] eye%d OpenSharedFence failed: 0x%08X / 0x%08X", eye.index,
                static_cast<unsigned>(inResult), static_cast<unsigned>(outResult));
        return false;
    }

    BVR_LOG("[dlss45] eye%d ready: %ux%u -> %ux%u, %s, depth=%s, IPC v%u, "
            "same-frame",
            eye.index, g_renderWidth, g_renderHeight, g_outputWidth, g_outputHeight,
            wantSr ? "DLSS 4.5 SR" : "DLAA 4.5",
            g_depthInverted ? "reversed" : "normal", kIpcVersion);
    return true;
}

bool object_uses_device(ID3D11DeviceChild* object) noexcept {
    if (!object || !g_device) return false;
    ID3D11Device* owner = nullptr;
    object->GetDevice(&owner);
    const bool same = owner == g_device;
    release_one(owner);
    return same;
}

bool context_uses_device(ID3D11DeviceContext* context) noexcept {
    if (!context || !g_device) return false;
    ID3D11Device* owner = nullptr;
    context->GetDevice(&owner);
    const bool same = owner == g_device;
    release_one(owner);
    return same;
}

bool simple_texture(const D3D11_TEXTURE2D_DESC& desc, UINT width, UINT height) noexcept {
    return desc.Width == width && desc.Height == height && desc.MipLevels == 1 &&
           desc.ArraySize == 1 && desc.SampleDesc.Count == 1 &&
           desc.SampleDesc.Quality == 0;
}

bool validate_frame_resources(ID3D11Texture2D* destination,
                              ID3D11Texture2D* color,
                              ID3D11Texture2D* depth,
                              ID3D11Texture2D* motion) noexcept {
    if (!destination || !color || !depth || !motion) return false;
    if (!object_uses_device(destination) || !object_uses_device(color) ||
        !object_uses_device(depth) || !object_uses_device(motion))
        return false;

    D3D11_TEXTURE2D_DESC destinationDesc{}, colorDesc{}, depthDesc{}, motionDesc{};
    destination->GetDesc(&destinationDesc);
    color->GetDesc(&colorDesc);
    depth->GetDesc(&depthDesc);
    motion->GetDesc(&motionDesc);
    return simple_texture(destinationDesc, g_outputWidth, g_outputHeight) &&
           format_compatible(destinationDesc.Format, g_outputFormat) &&
           simple_texture(colorDesc, g_renderWidth, g_renderHeight) &&
           format_compatible(colorDesc.Format, g_backbufferFormat) &&
           simple_texture(depthDesc, g_renderWidth, g_renderHeight) &&
           depthDesc.Format == DXGI_FORMAT_R32_FLOAT &&
           simple_texture(motionDesc, g_renderWidth, g_renderHeight) &&
           motionDesc.Format == DXGI_FORMAT_R16G16_FLOAT;
}

bool wait_for_output(EyeState& eye, std::uint64_t value) noexcept {
    if (eye.fenceOut->GetCompletedValue() >= value) return true;
    ResetEvent(eye.completionEvent);
    if (FAILED(eye.fenceOut->SetEventOnCompletion(value, eye.completionEvent)))
        return false;
    HANDLE waits[2] = {eye.completionEvent, eye.process};
    return WaitForMultipleObjects(2, waits, FALSE, kFrameFenceTimeoutMs) == WAIT_OBJECT_0;
}

bool prepare_impl(ID3D11Device* device,
                  UINT renderWidth, UINT renderHeight, DXGI_FORMAT backbufferFormat,
                  UINT outputWidth, UINT outputHeight, Mode mode, bool depthInverted,
                  const wchar_t* hostExePath) {
    cleanup(false);
    set_status("preparando DLSS 4.5");

    if (mode == Mode::Off) return prepare_failure("DLSS 4.5 desactivado");
    if (mode != Mode::Dlaa && mode != Mode::SuperResolution)
        return prepare_failure("modo DLSS 4.5 no valido");
    if (!device || !renderWidth || !renderHeight || !outputWidth || !outputHeight)
        return prepare_failure("parametros D3D11 o dimensiones no validos");
    if (renderWidth > D3D11_REQ_TEXTURE2D_U_OR_V_DIMENSION ||
        renderHeight > D3D11_REQ_TEXTURE2D_U_OR_V_DIMENSION ||
        outputWidth > D3D11_REQ_TEXTURE2D_U_OR_V_DIMENSION ||
        outputHeight > D3D11_REQ_TEXTURE2D_U_OR_V_DIMENSION)
        return prepare_failure("las dimensiones superan el limite D3D11");
    if (mode == Mode::Dlaa && (renderWidth != outputWidth || renderHeight != outputHeight))
        return prepare_failure("DLAA requiere salida igual a la resolucion de render");
    if (mode == Mode::SuperResolution &&
        (outputWidth <= renderWidth || outputHeight <= renderHeight))
        return prepare_failure("DLSS SR requiere una salida mayor que el render");
    if (static_cast<std::uint64_t>(renderWidth) * outputHeight !=
        static_cast<std::uint64_t>(renderHeight) * outputWidth)
        return prepare_failure("render y salida deben conservar la relacion de aspecto");

    const DXGI_FORMAT colorFormat = typed_color_format(backbufferFormat);
    if (colorFormat == DXGI_FORMAT_UNKNOWN)
        return prepare_failure("formato de backbuffer no compatible con DLSS 4.5");

    g_device = device;
    g_device->AddRef();
    if (FAILED(device->QueryInterface(IID_PPV_ARGS(&g_device5))) || !g_device5)
        return prepare_failure("ID3D11Device5 no disponible: no se pueden compartir fences");
    g_renderWidth = renderWidth;
    g_renderHeight = renderHeight;
    g_outputWidth = outputWidth;
    g_outputHeight = outputHeight;
    g_backbufferFormat = backbufferFormat;
    g_colorFormat = colorFormat;
    g_outputFormat = output_format_for(colorFormat);
    g_mode = mode;
    g_depthInverted = depthInverted;
    g_eyes[0].index = 0;
    g_eyes[1].index = 1;

    const std::wstring hostSource = absolute_path(hostExePath);
    if (hostSource.empty()) return prepare_failure("ruta del host DLSS 4.5 no valida");
    if (!stage_runtime(hostSource)) return false;

    if (!create_eye_resources(g_eyes[0]) || !create_eye_resources(g_eyes[1]))
        return prepare_failure("fallo al crear las texturas D3D11 compartidas por ojo");
    if (!create_job())
        return prepare_failure("no se pudo crear el guard de procesos de los hosts");

    const DWORD gamePid = GetCurrentProcessId();
    if (!launch_host(g_eyes[0], gamePid) || !launch_host(g_eyes[1], gamePid))
        return prepare_failure("no se pudieron iniciar los dos hosts x64 aislados");
    if (!connect_pipe(g_eyes[0], gamePid) || !connect_pipe(g_eyes[1], gamePid))
        return prepare_failure("timeout conectando los pipes independientes de ambos ojos");
    if (!hello_host(g_eyes[0], gamePid) || !hello_host(g_eyes[1], gamePid))
        return prepare_failure("handshake IPC v8 rechazado por uno de los hosts");
    if (!build_host(g_eyes[0]) || !build_host(g_eyes[1]))
        return prepare_failure("NGX no pudo crear dos features temporales independientes");

    g_isReady = true;
    g_faulted = false;
    set_status("%s activo: %ux%u -> %ux%u, dos ojos, same-frame",
               mode == Mode::Dlaa ? "DLAA 4.5" : "DLSS 4.5 SR",
               renderWidth, renderHeight, outputWidth, outputHeight);
    BVR_LOG("[dlss45] %s", g_status);
    return true;
}

} // namespace

bool prepare(ID3D11Device* device,
             UINT renderWidth, UINT renderHeight, DXGI_FORMAT backbufferFormat,
             UINT outputWidth, UINT outputHeight, Mode mode, bool depthInverted,
             const wchar_t* hostExePath) noexcept {
    try {
        return prepare_impl(device, renderWidth, renderHeight, backbufferFormat,
                            outputWidth, outputHeight, mode, depthInverted,
                            hostExePath);
    } catch (...) {
        set_status("excepcion preparando el puente DLSS 4.5");
        BVR_LOG("[dlss45] %s", g_status);
        cleanup(false);
        return false;
    }
}

bool process_eye(ID3D11DeviceContext* context, int eyeIndex,
                 ID3D11Texture2D* destination, ID3D11Texture2D* backbuffer,
                 ID3D11Texture2D* depth, ID3D11Texture2D* motion,
                 bool reset, float jitterX, float jitterY) noexcept {
    try {
        if (!g_isReady || g_faulted) return false;
        if (eyeIndex < 0 || eyeIndex > 1) {
            runtime_failure("indice de ojo no valido: %d", eyeIndex);
            return false;
        }
        if (!context_uses_device(context) ||
            !validate_frame_resources(destination, backbuffer, depth, motion)) {
            runtime_failure("recursos incompatibles en process_eye(%d)", eyeIndex);
            return false;
        }
        if (!std::isfinite(jitterX) || !std::isfinite(jitterY) ||
            std::abs(jitterX) > 1.0f || std::abs(jitterY) > 1.0f) {
            runtime_failure("jitter no valido en process_eye(%d)", eyeIndex);
            return false;
        }

        ID3D11DeviceContext4* context4 = nullptr;
        if (FAILED(context->QueryInterface(IID_PPV_ARGS(&context4))) || !context4) {
            runtime_failure("ID3D11DeviceContext4 no disponible");
            return false;
        }

        EyeState& eye = g_eyes[eyeIndex];
        context->CopyResource(eye.textures[FeedColor], backbuffer);
        context->CopyResource(eye.textures[FeedDepth], depth);
        context->CopyResource(eye.textures[FeedMotion], motion);

        const std::uint64_t value = ++eye.frame;
        const HRESULT signalResult = context4->Signal(eye.fenceIn, value);
        if (FAILED(signalResult)) {
            release_one(context4);
            runtime_failure("fallo Signal de entrada en ojo %d: 0x%08X", eyeIndex,
                            static_cast<unsigned>(signalResult));
            return false;
        }
        context->Flush();

        FeedFrame frame{};
        frame.fenceValue = value;
        frame.reset = (reset || value == 1) ? 1u : 0u;
        frame.jitterX = jitterX;
        frame.jitterY = jitterY;
        const BYTE tag = 'F';
        if (!pipe_write(eye, &tag, 1, kFramePipeTimeoutMs) ||
            !pipe_write(eye, &frame, sizeof(frame), kFramePipeTimeoutMs)) {
            release_one(context4);
            runtime_failure("se perdio el pipe del host del ojo %d", eyeIndex);
            return false;
        }

        // Wait on the CPU with a process/timeout escape before recording any GPU
        // dependency. A crashed helper can therefore never leave the game's queue
        // waiting forever. Once complete, retain the explicit D3D11 fence wait so
        // Output(n) -> dstXR is ordered by the GPU contract, not CPU timing luck.
        if (!wait_for_output(eye, value)) {
            release_one(context4);
            runtime_failure("timeout esperando DLSS 4.5 en ojo %d, frame %llu",
                            eyeIndex, static_cast<unsigned long long>(value));
            return false;
        }
        const HRESULT waitResult = context4->Wait(eye.fenceOut, value);
        if (FAILED(waitResult)) {
            release_one(context4);
            runtime_failure("fallo Wait de salida en ojo %d: 0x%08X", eyeIndex,
                            static_cast<unsigned>(waitResult));
            return false;
        }
        context->CopyResource(destination, eye.textures[FeedOutput]);
        release_one(context4);
        return true;
    } catch (...) {
        runtime_failure("excepcion procesando un ojo con DLSS 4.5");
        return false;
    }
}

void release() noexcept {
    cleanup(true);
}

bool ready() noexcept {
    return g_isReady && !g_faulted;
}

const char* status() noexcept {
    return g_status;
}

} // namespace bvr::dlss45
