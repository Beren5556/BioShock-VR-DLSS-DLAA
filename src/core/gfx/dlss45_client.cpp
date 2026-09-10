#include "core/gfx/dlss45_client.h"
#include "game/adapter_registry.h"
#include "core/gfx/dlss_sharpen.h"
#include "core/gfx/sampled_gpu_timer.h"
#include "core/gfx/input_wait_probe.h"
#include "core/vr/critical_path_probe.h"
#include "../../../components/dlss-host/src/latency_probe_protocol.h"

#include "core/util/log.h"
#include "core/util/bounded_activity.h"

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

namespace CP = bvr::critical_path_probe;

constexpr std::uint32_t kIpcMagic = 0x35534C44u; // 'DLS5', inherited wire magic
constexpr std::uint32_t kIpcVersion = 8u;
constexpr DWORD kStartupTimeoutMs = 30000;
constexpr DWORD kHelloTimeoutMs = 15000;
constexpr DWORD kBuildTimeoutMs = 20000;
constexpr DWORD kFramePipeTimeoutMs = 1500;
constexpr DWORD kFrameFenceTimeoutMs = 5000;
constexpr DWORD kShutdownTimeoutMs = 3000;
// A small scheduling margin, not a new rendering timeout. Once the original
// bound expires, an actually stuck API remains visible to both watchdogs.
constexpr DWORD kWatchdogWaitMarginMs = 250;
bvr::BoundedActivity g_boundedWait;

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
#ifdef BVR_CRITICAL_PATH_PROBE
    HANDLE inputProbeEvent = nullptr; // optional, auto-reset; never changes output ownership
#endif
    ID3D11Texture2D* textures[FeedSlotCount] = {};
    HANDLE textureHandles[FeedSlotCount] = {};
    ID3D11Fence* fenceIn = nullptr;
    ID3D11Fence* fenceOut = nullptr;
    std::uint64_t frame = 0;
    ID3D11Texture2D* pendingDestination = nullptr; // borrowed XR lease; never a DXGI backbuffer ref
    double pendingSubmitMs = 0;
};

EyeState g_eyes[2];
unsigned g_sharpnessPercent = 0;
struct CpuCosts { uint64_t count = 0; double submit = 0, wait = 0, total = 0, maxTotal = 0; };
CpuCosts g_cpuCosts[2];
#ifdef BVR_CRITICAL_PATH_PROBE
bvr::input_wait_probe::Counters g_inputWaitCosts[2];
#endif
bvr::SampledGpuTimer g_inputTimers[2];
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
bool g_diagnosticTransport = false;
bool g_isReady = false;
bool g_faulted = false;
char g_status[512] = "disabled";
bool g_srRangeRejected = false;

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
    wchar_t game[32]{};
    wchar_t adapter[32]{};
    GetPrivateProfileStringW(L"backend", L"phase", L"", phase,
                             static_cast<DWORD>(_countof(phase)), path.c_str());
    GetPrivateProfileStringW(L"backend", L"runtime", L"", runtime,
                             static_cast<DWORD>(_countof(runtime)), path.c_str());
    GetPrivateProfileStringW(L"backend", L"game", L"", game,
                             static_cast<DWORD>(_countof(game)), path.c_str());
    GetPrivateProfileStringW(L"backend", L"adapter", L"", adapter,
                             static_cast<DWORD>(_countof(adapter)), path.c_str());
    const UINT eyeHosts = GetPrivateProfileIntW(L"backend", L"eyeHosts", 0, path.c_str());
    const UINT protocol = GetPrivateProfileIntW(L"backend", L"protocol", 0, path.c_str());
    const auto hostGame = bvr::game::detect_host_game();
    // The accepted BS1 payload predates explicit game/adapter keys. Missing
    // identity remains a BS1-only legacy contract, never valid for BS2.
    const bool identity = hostGame == bvr::game::HostGame::Bioshock2
        ? wcscmp(game, L"bs2") == 0 && wcscmp(adapter, L"bioshock2r") == 0
        : hostGame == bvr::game::HostGame::Bioshock1 &&
          ((!game[0] && !adapter[0]) ||
           (wcscmp(game, L"bs1") == 0 && wcscmp(adapter, L"bioshock1r") == 0));
    return identity && _wcsicmp(phase, L"DLSS45") == 0 && eyeHosts >= 2 &&
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
    bvr::BoundedActivity::Scope activity(g_boundedWait, GetTickCount64(),
        kShutdownTimeoutMs + 1000 + kWatchdogWaitMarginMs);
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
#ifdef BVR_CRITICAL_PATH_PROBE
        if (eye.inputProbeEvent) CloseHandle(eye.inputProbeEvent);
        eye.inputProbeEvent = nullptr;
#endif
        eye.pipeEvent = nullptr;
        eye.completionEvent = nullptr;
        eye.frame = 0;
        eye.directory.clear();
        eye.pendingDestination = nullptr;
        eye.pendingSubmitMs = 0;
        eye.executable.clear();
    }
    bvr::dlss_sharpen::release();
    g_sharpnessPercent = 0;
    for (int i=0; i<2; ++i) { g_inputTimers[i].release(); g_cpuCosts[i] = {}; }
#ifdef BVR_CRITICAL_PATH_PROBE
    for (auto& costs : g_inputWaitCosts) costs = {};
#endif
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
    if (resetStatus) set_status("disabled");
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
        return prepare_failure("BioShockVR-DLSS45-Host64.exe was not found");
    if (!file_exists(dlssSource))
        return prepare_failure("nvngx_dlss.dll missing beside the DLSS 4.5 host");
    if (!file_exists(capabilitiesSource))
        return prepare_failure("dlss-capabilities.ini missing beside the DLSS 4.5 host");
    if (!is_x64_pe(hostSource))
        return prepare_failure("the selected host is not an x64 executable");
    if (!is_x64_pe(dlssSource))
        return prepare_failure("nvngx_dlss.dll is not the required x64 version");
    DWORD versionMajor = 0, versionMinor = 0, versionBuild = 0, versionRevision = 0;
    if (!is_dlss_310_7_0(dlssSource, &versionMajor, &versionMinor,
                         &versionBuild, &versionRevision)) {
        BVR_LOG("[dlss45] WARNING: untested nvngx_dlss.dll FileVersion "
                "%lu.%lu.%lu.%lu; continuing at the user's risk "
                "(tested runtime 310.7.0.0)",
                static_cast<unsigned long>(versionMajor),
                static_cast<unsigned long>(versionMinor),
                static_cast<unsigned long>(versionBuild),
                static_cast<unsigned long>(versionRevision));
    } else {
        BVR_LOG("[dlss45] nvngx_dlss.dll FileVersion 310.7.0.0 matches the tested runtime");
    }
    if (!capabilities_are_dlss45(capabilitiesSource))
        return prepare_failure("dlss-capabilities.ini does not match the game, DLSS45, two hosts, runtime 310.7.0 and IPC v8");

    const wchar_t* offending = nullptr;
    if (!package_is_clean(packageDirectory, &offending)) {
        BVR_LOG("[dlss45] rejected contaminated source package: %ls", offending);
        return prepare_failure("package rejected: contains Neural Rendering/"
                               "ReShade components incompatible with DLSS 4.5");
    }

    const wchar_t* gameDataDir = bvr::log::data_dir();
    if (!gameDataDir || !*gameDataDir)
        return prepare_failure("could not resolve the game data directory");
    const std::wstring bioshockDir(gameDataDir);
#ifdef BVR_LATENCY_PROBE
    const std::wstring runtimeRoot = join_path(bioshockDir, L"DLSS45Host-Latency1");
#else
    const std::wstring runtimeRoot = join_path(bioshockDir, L"DLSS45Host");
#endif
    if (!ensure_directory(bioshockDir) || !ensure_directory(runtimeRoot))
        return prepare_failure("could not create the isolated local DLSS 4.5 runtime");

    for (int index = 0; index < 2; ++index) {
        EyeState& eye = g_eyes[index];
        eye.index = index;
        eye.directory = join_path(runtimeRoot, g_diagnosticTransport
            ? (index == 0 ? L"bridge-eye0" : L"bridge-eye1")
            : (index == 0 ? L"eye0" : L"eye1"));
        eye.executable = join_path(eye.directory, L"BioShockVR-DLSS45-Host64.exe");
        if (!ensure_directory(eye.directory))
            return prepare_failure("could not create an isolated eye directory");

        offending = nullptr;
        if (!package_is_clean(eye.directory, &offending)) {
            BVR_LOG("[dlss45] rejected contaminated eye%d runtime: %ls", index, offending);
            return prepare_failure("local runtime rejected: Neural Rendering/ReShade components remain");
        }
        if (!copy_unless_same(hostSource, eye.executable) ||
            !copy_unless_same(dlssSource, join_path(eye.directory, L"nvngx_dlss.dll")) ||
            !copy_unless_same(capabilitiesSource,
                              join_path(eye.directory, L"dlss-capabilities.ini"))) {
            BVR_LOG("[dlss45] CopyFile failed for eye%d: %lu", index,
                    static_cast<unsigned long>(GetLastError()));
            return prepare_failure("could not prepare clean host binaries");
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
#ifdef BVR_CRITICAL_PATH_PROBE
    // Optional diagnostic allocation: failure disables only the extra input
    // wake, never installation, host startup or current-frame rendering.
    eye.inputProbeEvent = CreateEventW(nullptr, FALSE, FALSE, nullptr);
#endif
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
    bvr::BoundedActivity::Scope activity(g_boundedWait, GetTickCount64(),
        timeoutMs + kWatchdogWaitMarginMs);
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
    build.transport = g_diagnosticTransport ? bvr_latency_probe::kFullImageTransport : 0;
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
        !bvr_latency_probe::matching_ack(g_diagnosticTransport, ack.flags) ||
        ack.outputFormat != static_cast<std::uint32_t>(g_outputFormat)) {
        BVR_LOG("[dlss45] eye%d build rejected: ok=%d ngx=0x%08X flags=0x%X "
                "output=%u expected=%u", eye.index, ack.ok, ack.ngxResult, ack.flags,
                ack.outputFormat, static_cast<unsigned>(g_outputFormat));
        if (ack.flags & kAckSrUnavailable) {
            g_srRangeRejected = true;
            BVR_LOG("[dlss45] requested SR ratio is not offered by the DLSS runtime");
        }
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
            g_diagnosticTransport ? "DIAGNOSTIC BRIDGE / NO DLAA" : wantSr ? "DLSS 4.5 SR" : "DLAA 4.5",
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

bool wait_for_output(EyeState& eye, std::uint64_t value, bool observe = false) noexcept {
    bvr::BoundedActivity::Scope activity(g_boundedWait, GetTickCount64(),
        kFrameFenceTimeoutMs + kWatchdogWaitMarginMs);
#ifdef BVR_CRITICAL_PATH_PROBE
    if (observe) {
        struct Hooks {
            EyeState& eye;
            double now_ms() { return bvr::diagnostic_clock_ms(); }
            std::uint64_t input_value() { return eye.fenceIn->GetCompletedValue(); }
            std::uint64_t output_value() { return eye.fenceOut->GetCompletedValue(); }
            bool register_output(std::uint64_t target) {
                ResetEvent(eye.completionEvent);
                return SUCCEEDED(eye.fenceOut->SetEventOnCompletion(target, eye.completionEvent));
            }
            bool register_input(std::uint64_t target) {
                if (!eye.inputProbeEvent) return false;
                ResetEvent(eye.inputProbeEvent);
                return SUCCEEDED(eye.fenceIn->SetEventOnCompletion(target, eye.inputProbeEvent));
            }
            bvr::input_wait_probe::Wake wait(bool input, std::uint32_t remaining) {
                using Wake = bvr::input_wait_probe::Wake;
                HANDLE handles[3] = {eye.completionEvent, eye.process, eye.inputProbeEvent};
                const DWORD result = WaitForMultipleObjects(input ? 3u : 2u, handles, FALSE, remaining);
                if (result == WAIT_OBJECT_0) return Wake::Output;
                if (result == WAIT_OBJECT_0 + 1) return Wake::Process;
                if (input && result == WAIT_OBJECT_0 + 2) return Wake::Input;
                return result == WAIT_TIMEOUT ? Wake::Timeout : Wake::Failed;
            }
        } hooks{eye};
        bvr::input_wait_probe::Observation observation{};
        const bool ok = bvr::input_wait_probe::wait(hooks, value, kFrameFenceTimeoutMs,
                                                   bvr::input_wait_probe::sampled_frame(value), observation);
        g_inputWaitCosts[eye.index].add(observation);
        return ok;
    }
#else
    (void)observe;
#endif
    const auto completed = eye.fenceOut->GetCompletedValue();
    if (completed == UINT64_MAX) return false; // Device removal is not completion.
    if (completed >= value) return true;
    ResetEvent(eye.completionEvent);
    if (FAILED(eye.fenceOut->SetEventOnCompletion(value, eye.completionEvent)))
        return false;
    HANDLE waits[2] = {eye.completionEvent, eye.process};
    if (WaitForMultipleObjects(2, waits, FALSE, kFrameFenceTimeoutMs) != WAIT_OBJECT_0) return false;
    const auto finished = eye.fenceOut->GetCompletedValue();
    return finished != UINT64_MAX && finished >= value;
}

bool prepare_impl(ID3D11Device* device,
                  UINT renderWidth, UINT renderHeight, DXGI_FORMAT backbufferFormat,
                  UINT outputWidth, UINT outputHeight, Mode mode, bool depthInverted,
                  const wchar_t* hostExePath) {
    cleanup(false);
    set_status("preparing DLSS 4.5");
    g_srRangeRejected = false;

    if (mode == Mode::Off) return prepare_failure("DLSS 4.5 disabled");
    if (mode != Mode::Dlaa && mode != Mode::SuperResolution)
        return prepare_failure("invalid DLSS 4.5 mode");
    if (!device || !renderWidth || !renderHeight || !outputWidth || !outputHeight)
        return prepare_failure("invalid D3D11 parameters or dimensions");
    if (renderWidth > D3D11_REQ_TEXTURE2D_U_OR_V_DIMENSION ||
        renderHeight > D3D11_REQ_TEXTURE2D_U_OR_V_DIMENSION ||
        outputWidth > D3D11_REQ_TEXTURE2D_U_OR_V_DIMENSION ||
        outputHeight > D3D11_REQ_TEXTURE2D_U_OR_V_DIMENSION)
        return prepare_failure("dimensions exceed the D3D11 limit");
    if (mode == Mode::Dlaa && (renderWidth != outputWidth || renderHeight != outputHeight))
        return prepare_failure("DLAA requires matching output and render resolutions");
    if (mode == Mode::SuperResolution &&
        (outputWidth <= renderWidth || outputHeight <= renderHeight))
        return prepare_failure("DLSS SR requires output larger than the render resolution");
    if (static_cast<std::uint64_t>(renderWidth) * outputHeight !=
        static_cast<std::uint64_t>(renderHeight) * outputWidth)
        return prepare_failure("render and output must have the same aspect ratio");

    const DXGI_FORMAT colorFormat = typed_color_format(backbufferFormat);
    if (colorFormat == DXGI_FORMAT_UNKNOWN)
        return prepare_failure("backbuffer format is not supported by DLSS 4.5");

    g_device = device;
    g_device->AddRef();
    if (FAILED(device->QueryInterface(IID_PPV_ARGS(&g_device5))) || !g_device5)
        return prepare_failure("ID3D11Device5 unavailable: shared fences are not supported");
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
    if (hostSource.empty()) return prepare_failure("invalid DLSS 4.5 host path");
    if (!stage_runtime(hostSource)) return false;

    if (!create_eye_resources(g_eyes[0]) || !create_eye_resources(g_eyes[1]))
        return prepare_failure("failed to create shared D3D11 textures for each eye");
    if (!create_job())
        return prepare_failure("could not create the host process guard");

    const DWORD gamePid = GetCurrentProcessId();
    if (!launch_host(g_eyes[0], gamePid) || !launch_host(g_eyes[1], gamePid))
        return prepare_failure("could not start both isolated x64 hosts");
    if (!connect_pipe(g_eyes[0], gamePid) || !connect_pipe(g_eyes[1], gamePid))
        return prepare_failure("timeout connecting the independent pipes for both eyes");
    if (!hello_host(g_eyes[0], gamePid) || !hello_host(g_eyes[1], gamePid))
        return prepare_failure("IPC v8 handshake rejected by one of the hosts");
    if (!build_host(g_eyes[0]) || !build_host(g_eyes[1]))
        return prepare_failure(g_srRangeRejected
            ? "This DLSS resolution/quality is outside the NVIDIA supported range"
            : "NGX could not create two independent temporal features");

    g_isReady = true;
    g_faulted = false;
    for (auto& timer : g_inputTimers) timer.prepare(g_device);
    set_status("%s active: %ux%u -> %ux%u, two eyes, same-frame",
               g_diagnosticTransport ? "BRIDGE TEST WITHOUT DLAA" :
               mode == Mode::Dlaa ? "DLAA 4.5" : "DLSS 4.5 SR",
               renderWidth, renderHeight, outputWidth, outputHeight);
    BVR_LOG("[dlss45] %s", g_status);
    return true;
}

} // namespace

bool prepare(ID3D11Device* device,
             UINT renderWidth, UINT renderHeight, DXGI_FORMAT backbufferFormat,
             UINT outputWidth, UINT outputHeight, Mode mode, bool depthInverted,
             const wchar_t* hostExePath, bool diagnosticTransport) noexcept {
    try {
        g_diagnosticTransport = diagnosticTransport;
#ifndef BVR_LATENCY_PROBE
        if (diagnosticTransport) return prepare_failure("bridge test not available in this version");
#endif
        if (diagnosticTransport && (mode != Mode::Dlaa || renderWidth != outputWidth || renderHeight != outputHeight))
            return prepare_failure("the bridge test requires matching native resolution");
        return prepare_impl(device, renderWidth, renderHeight, backbufferFormat,
                            outputWidth, outputHeight, mode, depthInverted,
                            hostExePath);
    } catch (...) {
        set_status("exception preparing the DLSS 4.5 bridge");
        BVR_LOG("[dlss45] %s", g_status);
        cleanup(false);
        return false;
    }
}

bool submit_eye(ID3D11DeviceContext* context, int eyeIndex,
                 ID3D11Texture2D* destination, ID3D11Texture2D* backbuffer,
                 ID3D11Texture2D* depth, ID3D11Texture2D* motion,
                 bool reset, float jitterX, float jitterY) noexcept {
    CP::Scope submitScope(eyeIndex == 0 ? CP::Stage::HostSubmitLeft : CP::Stage::HostSubmitRight,
                          eyeIndex >= 0 && eyeIndex < 2);
    try {
        if (!g_isReady || g_faulted) return false;
        if (eyeIndex < 0 || eyeIndex > 1) {
            runtime_failure("invalid eye index: %d", eyeIndex);
            return false;
        }
        if (g_eyes[eyeIndex].pendingDestination) {
            runtime_failure("eye %d input reused before completion", eyeIndex);
            return false;
        }
        if (!context_uses_device(context) ||
            !validate_frame_resources(destination, backbuffer, depth, motion)) {
            runtime_failure("incompatible resources in process_eye(%d)", eyeIndex);
            return false;
        }
        if (!std::isfinite(jitterX) || !std::isfinite(jitterY) ||
            std::abs(jitterX) > 1.0f || std::abs(jitterY) > 1.0f) {
            runtime_failure("invalid jitter in process_eye(%d)", eyeIndex);
            return false;
        }

        ID3D11DeviceContext4* context4 = nullptr;
        if (FAILED(context->QueryInterface(IID_PPV_ARGS(&context4))) || !context4) {
            runtime_failure("ID3D11DeviceContext4 unavailable");
            return false;
        }

        EyeState& eye = g_eyes[eyeIndex];
        const double started = bvr::diagnostic_clock_ms();
        {
            CP::Scope copiesScope(CP::Stage::HostCopies);
            g_inputTimers[eyeIndex].begin(context);
            context->CopyResource(eye.textures[FeedColor], backbuffer);
            context->CopyResource(eye.textures[FeedDepth], depth);
            context->CopyResource(eye.textures[FeedMotion], motion);
            g_inputTimers[eyeIndex].end(context);
        }

        const std::uint64_t value = ++eye.frame;
        eye.pendingDestination = destination;
        const HRESULT signalResult = context4->Signal(eye.fenceIn, value);
        if (FAILED(signalResult)) {
            release_one(context4);
            runtime_failure("input Signal failed for eye %d: 0x%08X", eyeIndex,
                            static_cast<unsigned>(signalResult));
            return false;
        }
        {
            CP::Scope flushScope(CP::Stage::HostFlush);
            context->Flush();
        }

        FeedFrame frame{};
        frame.fenceValue = value;
        frame.reset = (reset || value == 1) ? 1u : 0u;
        frame.jitterX = jitterX;
        frame.jitterY = jitterY;
        bool sent = false;
        {
            CP::Scope pipeScope(CP::Stage::HostPipe);
#ifdef BVR_DLSS_TAIL_OVERLAP
            // Byte-stream IPC v8 is unchanged. Avoid a second overlapped WriteFile
            // and event round trip for the tag; never rely on struct padding here.
            BYTE packet[1 + sizeof(frame)]{};
            packet[0] = 'F';
            std::memcpy(packet + 1, &frame, sizeof(frame));
            sent = pipe_write(eye, packet, sizeof(packet), kFramePipeTimeoutMs);
#else
            const BYTE tag = 'F';
            sent = pipe_write(eye, &tag, 1, kFramePipeTimeoutMs) &&
                   pipe_write(eye, &frame, sizeof(frame), kFramePipeTimeoutMs);
#endif
        }
        if (!sent) {
            release_one(context4);
            runtime_failure("host pipe lost for eye %d", eyeIndex);
            return false;
        }

        eye.pendingSubmitMs = bvr::diagnostic_clock_ms() - started;
        release_one(context4);
        return true;
    } catch (...) {
        runtime_failure("exception submitting an eye with DLSS 4.5");
        return false;
    }
}

bool resolve_eye(ID3D11DeviceContext* context, int eyeIndex,
                 ID3D11Texture2D* destination) noexcept {
    CP::Scope resolveScope(eyeIndex == 0 ? CP::Stage::HostResolveLeft : CP::Stage::HostResolveRight,
                           eyeIndex >= 0 && eyeIndex < 2);
    try {
        if (!g_isReady || g_faulted) return false;
        if (eyeIndex < 0 || eyeIndex > 1 || !destination || !context_uses_device(context)) {
            runtime_failure("invalid resources when completing eye %d", eyeIndex);
            return false;
        }
        EyeState& eye = g_eyes[eyeIndex];
        if (eye.pendingDestination != destination) {
            runtime_failure("pending output mismatch for eye %d", eyeIndex);
            return false;
        }
        ID3D11DeviceContext4* context4 = nullptr;
        if (FAILED(context->QueryInterface(IID_PPV_ARGS(&context4))) || !context4) {
            runtime_failure("ID3D11DeviceContext4 unavailable at completion");
            return false;
        }
        const auto value = eye.frame;
        // Wait on the CPU with a process/timeout escape before recording any GPU
        // dependency. A crashed helper can therefore never leave the game's queue
        // waiting forever. Once complete, retain the explicit D3D11 fence wait so
        // Output(n) -> dstXR is ordered by the GPU contract, not CPU timing luck.
        const double waitStarted = bvr::diagnostic_clock_ms();
        bool completed = false;
        {
            CP::Scope waitScope(eyeIndex == 0 ? CP::Stage::HostWaitLeft : CP::Stage::HostWaitRight);
            completed = wait_for_output(eye, value, true);
        }
        if (!completed) {
            release_one(context4);
            runtime_failure("timeout waiting for DLSS 4.5 on eye %d, frame %llu",
                            eyeIndex, static_cast<unsigned long long>(value));
            return false;
        }
        const double waitFinished = bvr::diagnostic_clock_ms();
        {
            CP::Scope copyScope(CP::Stage::HostCopy);
            const HRESULT waitResult = context4->Wait(eye.fenceOut, value);
            if (FAILED(waitResult)) {
                release_one(context4);
                runtime_failure("output Wait failed for eye %d: 0x%08X", eyeIndex,
                                static_cast<unsigned>(waitResult));
                return false;
            }
            if (!g_sharpnessPercent || g_mode != Mode::SuperResolution)
                context->CopyResource(destination, eye.textures[FeedOutput]);
            else if (!bvr::dlss_sharpen::render(context, eyeIndex, destination, g_sharpnessPercent)) {
                // Do not publish a pair with one sharpened eye and one unfiltered eye.
                release_one(context4);
                runtime_failure("could not apply sharpening to eye %d", eyeIndex);
                return false;
            }
        }
        const double finished = bvr::diagnostic_clock_ms();
        auto& costs = g_cpuCosts[eyeIndex];
        const double total = eye.pendingSubmitMs + finished - waitStarted;
        ++costs.count; costs.submit += eye.pendingSubmitMs;
        costs.wait += waitFinished - waitStarted; costs.total += total;
        if (total > costs.maxTotal) costs.maxTotal = total;
        eye.pendingDestination = nullptr;
        eye.pendingSubmitMs = 0;
        release_one(context4);
        return true;
    } catch (...) {
        runtime_failure("exception processing an eye with DLSS 4.5");
        return false;
    }
}


bool process_eye(ID3D11DeviceContext* context, int eye,
                 ID3D11Texture2D* destination, ID3D11Texture2D* backbuffer,
                 ID3D11Texture2D* depth, ID3D11Texture2D* motion,
                 bool reset, float jitterX, float jitterY,
                 BeforeResolveWork beforeResolve) noexcept {
    if (!submit_eye(context, eye, destination, backbuffer, depth, motion, reset, jitterX, jitterY))
        return false;
    try {
        if (beforeResolve.run) beforeResolve.run(beforeResolve.context);
    } catch (...) {
        runtime_failure("exception preparing submission while DLSS processes eye %d", eye);
        return false;
    }
    return resolve_eye(context, eye, destination);
}

ID3D11Texture2D* retain_submitted_color(int eyeIndex, ID3D11Texture2D* destination) noexcept {
    if (!ready() || eyeIndex < 0 || eyeIndex > 1 || !destination) return nullptr;
    const auto& eye = g_eyes[eyeIndex];
    if (eye.pendingDestination != destination || !eye.textures[FeedColor]) return nullptr;
    eye.textures[FeedColor]->AddRef();
    return eye.textures[FeedColor];
}

bool discard_pending() noexcept {
    bool ok = g_isReady && !g_faulted;
    for (EyeState& eye : g_eyes) {
        if (!eye.pendingDestination) continue;
        if (ok && !wait_for_output(eye, eye.frame)) {
            runtime_failure("timeout discarding pending eye %d", eye.index);
            ok = false;
        }
        eye.pendingDestination = nullptr;
        eye.pendingSubmitMs = 0;
    }
    return ok;
}

void release() noexcept {
    cleanup(true);
}

bool gpu_retired() noexcept {
    for (const EyeState& eye : g_eyes) {
        if (eye.pendingDestination) return false; // Still owns an unpublished XR destination.
        if (!eye.frame) continue;
        if (!eye.fenceOut) return false;
        const auto completed = eye.fenceOut->GetCompletedValue();
        if (completed == UINT64_MAX || completed < eye.frame) return false;
    }
    return true;
}

bool ready() noexcept {
    return g_isReady && !g_faulted;
}

bool bounded_wait_active() noexcept {
    return g_boundedWait.active(GetTickCount64());
}

bool set_sharpness(unsigned percent) noexcept {
    if (!ready() || percent > 100 || (percent && g_mode != Mode::SuperResolution)) return false;
    if (percent && !bvr::dlss_sharpen::prepare(g_device, g_eyes[0].textures[FeedOutput],
                                              g_eyes[1].textures[FeedOutput])) return false;
    g_sharpnessPercent = percent;
    return true;
}

void log_performance() noexcept {
    if (!ready()) return;
    for (int i=0; i<2; ++i) {
        const auto& c = g_cpuCosts[i];
        const auto& gpu = g_inputTimers[i];
        const double n = c.count ? double(c.count) : 1;
        BVR_LOG("[dlss45-perf] mode=%s eye=%d frames=%llu render=%ux%u output=%ux%u sharpness=%u "
                "cpuSubmitAvgMs=%.3f cpuWaitAvgMs=%.3f cpuTotalAvgMs=%.3f cpuTotalMaxMs=%.3f "
                "gpuInputSamples=%llu gpuInputAvgMs=%.3f gpuInputMaxMs=%.3f",
                g_diagnosticTransport ? "BRIDGE" : g_mode == Mode::SuperResolution ? "DLSS" : "DLAA", i,
                static_cast<unsigned long long>(c.count), g_renderWidth, g_renderHeight,
                g_outputWidth, g_outputHeight, g_sharpnessPercent,
                c.submit/n, c.wait/n, c.total/n, c.maxTotal,
                static_cast<unsigned long long>(gpu.samples()), gpu.average_ms(), gpu.max_ms());
        g_cpuCosts[i] = {}; g_inputTimers[i].clear_stats();
#ifdef BVR_CRITICAL_PATH_PROBE
        const auto& w = g_inputWaitCosts[i];
        // Separate counts keep unavailable/coalesced observations out of the
        // averages. These are CPU-observed wakes, not exclusive GPU/IPC cost.
        BVR_LOG("[input-wait-probe] mode=%s eye=%d frames=%llu render=%ux%u output=%ux%u "
                "inputReadyAtResolve=%llu inputPendingAtResolve=%llu inputRemovedAtResolve=%llu "
                "outputReadyAtResolve=%llu sampled=%llu sampleEvery=16 splitObserved=%llu "
                "unobserved=%llu coalesced=%llu inputRegistrationFailed=%llu unsuccessful=%llu "
                "staleInputWakes=%llu inputRemovedDuringWait=%llu "
                "sampledWaitAvgMs=%.3f unsampled=%llu unsampledWaitAvgMs=%.3f "
                "beforeInputWakeAvgMs=%.3f afterInputWakeAvgMs=%.3f "
                "cpuObservedNotGpuTimestamp=1",
                g_diagnosticTransport ? "BRIDGE" : g_mode == Mode::SuperResolution ? "DLSS" : "DLAA", i,
                static_cast<unsigned long long>(w.frames), g_renderWidth, g_renderHeight,
                g_outputWidth, g_outputHeight,
                static_cast<unsigned long long>(w.inputReady), static_cast<unsigned long long>(w.inputPending),
                static_cast<unsigned long long>(w.inputRemoved), static_cast<unsigned long long>(w.outputReady),
                static_cast<unsigned long long>(w.sampled), static_cast<unsigned long long>(w.split),
                static_cast<unsigned long long>(w.unobserved), static_cast<unsigned long long>(w.coalesced),
                static_cast<unsigned long long>(w.registrationFailed), static_cast<unsigned long long>(w.unsuccessful),
                static_cast<unsigned long long>(w.staleInputWakes), static_cast<unsigned long long>(w.removedDuringWait),
                w.sampled ? w.sampledTotalMs / double(w.sampled) : -1.0,
                static_cast<unsigned long long>(w.unsampled),
                w.unsampled ? w.unsampledTotalMs / double(w.unsampled) : -1.0,
                w.split ? w.beforeInputMs / double(w.split) : -1.0,
                w.split ? w.afterInputMs / double(w.split) : -1.0);
        g_inputWaitCosts[i] = {};
#endif
    }
}

const char* status() noexcept {
    return g_status;
}

} // namespace bvr::dlss45
