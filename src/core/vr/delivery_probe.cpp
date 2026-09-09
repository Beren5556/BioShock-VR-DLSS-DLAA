#include "core/vr/delivery_probe.h"
#include "core/util/log.h"
#include <windows.h>
#include <MinHook.h>
#include <cmath>
#include <cstring>

namespace bvr::delivery_probe {
namespace {
// OVR_PUBLIC_FUNCTION(ovrBool) ovr_SetFloat uses the C ABI and a char return.
using SetFloat = char(__cdecl*)(void*, const char*, float);
SetFloat original = nullptr;
void* hookTarget = nullptr;
HMODULE retainedModule = nullptr;
bool attempted = false;
thread_local bool insideEnd = false;
thread_local GpuSample pending{};

// No log I/O, GPU query, allocation, wait or parameter modification in the
// detour. The original result and exact arguments are preserved, even for NaN.
char __cdecl observe_set_float(void* session, const char* property, float seconds) {
    const char result = original(session, property, seconds);
    if (insideEnd && property && std::strcmp(property, "AppGpuTime") == 0) {
        ++pending.calls;
        if (result) ++pending.accepted;
        if (!std::isfinite(seconds) || seconds < 0) ++pending.invalid;
        else if (seconds == 0) ++pending.zero;
        else {
            ++pending.positive;
            const double ms = double(seconds) * 1000.0;
            pending.sumMs += ms;
            if (ms > pending.maxMs) pending.maxMs = ms;
        }
    }
    return result;
}
bool attach(void* target) noexcept {
    if (!target || hookTarget) return false;
    if (MH_CreateHook(target, reinterpret_cast<void*>(&observe_set_float),
                      reinterpret_cast<void**>(&original)) != MH_OK) return false;
    if (MH_EnableHook(target) != MH_OK) {
        MH_RemoveHook(target); original = nullptr; return false;
    }
    hookTarget = target;
    return true;
}
struct Window {
    std::uint64_t frames = 0, gpuCalls = 0, gpuSamples = 0, zeros = 0, invalid = 0, accepted = 0;
    double gpuSum = 0, gpuMax = 0, endSum = 0, desktopSum = 0, desktopMax = 0;
    std::uint64_t gapSamples = 0, presentSamples = 0, nonzeroSync = 0;
    double gapSum = 0, gapMax = 0, presentSum = 0, presentMax = 0;
};
Window window;
FrameInfo current;
bool configured = false;
std::uint64_t phaseStart = 0, lastReport = 0;
std::int64_t previousEnd = 0;
double clock_scale() {
    static const double scale = [] { LARGE_INTEGER f{}; QueryPerformanceFrequency(&f); return 1000.0 / double(f.QuadPart); }();
    return scale;
}
void report(std::uint64_t now, bool force = false) {
    if (!window.frames || (!force && now-lastReport < 1000)) return;
    const auto& w = window;
    BVR_LOG("[delivery-probe] phase=%u mode=%u render=%ux%u output=%ux%u ageMs=%llu frames=%llu "
        "desktopAfterXr=%d desktopCpuMs=%.3f desktopMaxMs=%.3f xrEndCpuMs=%.3f "
        "vdGpuCalls=%llu vdGpuSamples=%llu vdGpuMs=%.3f vdGpuMaxMs=%.3f vdGpuZero=%llu vdGpuInvalid=%llu vdAccepted=%llu "
        "betweenEndAndWaitSamples=%llu betweenEndAndWaitCpuMs=%.3f betweenEndAndWaitMaxMs=%.3f "
        "desktopPresentSamples=%llu desktopPresentCpuMs=%.3f desktopPresentMaxMs=%.3f nonzeroSync=%llu "
        "observer=%d gpuValueDelayedByRuntime=1 gpuNotExclusive=1 notTotalVdLatency=1",
        current.phase, current.mode, current.renderWidth, current.renderHeight, current.outputWidth, current.outputHeight,
        (unsigned long long)(now-phaseStart), (unsigned long long)w.frames, current.deferredDesktop ? 1 : 0,
        w.desktopSum / w.frames, w.desktopMax, w.endSum / w.frames,
        (unsigned long long)w.gpuCalls, (unsigned long long)w.gpuSamples, w.gpuSamples ? w.gpuSum/w.gpuSamples : 0,
        w.gpuMax, (unsigned long long)w.zeros, (unsigned long long)w.invalid, (unsigned long long)w.accepted,
        (unsigned long long)w.gapSamples, w.gapSamples ? w.gapSum/w.gapSamples : 0, w.gapMax,
        (unsigned long long)w.presentSamples, w.presentSamples ? w.presentSum/w.presentSamples : 0, w.presentMax,
        (unsigned long long)w.nonzeroSync, hookTarget ? 1 : 0);
    window = {}; lastReport = now;
}
}

void install(const char* runtimeName) noexcept {
    if (attempted || !runtimeName || std::strcmp(runtimeName, "VirtualDesktopXR") != 0) return;
    HMODULE mod = GetModuleHandleW(L"VirtualDesktop.LibOVRRT32_1.dll");
    if (!mod) return; // Session creation may not have loaded it yet.
    attempted = true;
    const auto target = GetProcAddress(mod, "ovr_SetFloat");
    if (target && GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
            reinterpret_cast<LPCWSTR>(target), &retainedModule) && attach(reinterpret_cast<void*>(target))) {
        // The hook and its module reference live with the injected mod/process,
        // like the existing D3D11 detours. No per-session duplicate references.
        BVR_LOG("[delivery-probe] observing VD AppGpuTime in this process; exact values/results passed through; no timing override");
    } else {
        if (retainedModule) { FreeLibrary(retainedModule); retainedModule = nullptr; }
        BVR_LOG("[delivery-probe] VD GPU observer unavailable; rendering unchanged, missing samples are NOT zero GPU time");
    }
}
void begin_end_frame() noexcept { pending = {}; insideEnd = true; }
GpuSample finish_end_frame() noexcept { insideEnd = false; const auto result = pending; pending = {}; return result; }
void observe(const FrameInfo& info, const GpuSample& gpu, double endCpuMs, double desktopCpuMs) noexcept {
    const auto now = GetTickCount64();
    if (!configured || !(info == current)) {
        report(now, true); window = {}; current = info; configured = true;
        phaseStart = lastReport = now; previousEnd = 0;
    }
    auto& w = window;
    ++w.frames; w.gpuCalls += gpu.calls; w.gpuSamples += gpu.positive;
    w.zeros += gpu.zero; w.invalid += gpu.invalid; w.accepted += gpu.accepted;
    w.gpuSum += gpu.sumMs; if (gpu.maxMs > w.gpuMax) w.gpuMax = gpu.maxMs;
    w.endSum += endCpuMs; w.desktopSum += desktopCpuMs;
    if (desktopCpuMs > w.desktopMax) w.desktopMax = desktopCpuMs;
    report(now);
}
void before_wait() noexcept {
    if (!configured || !previousEnd) return;
    LARGE_INTEGER now{}; QueryPerformanceCounter(&now);
    const double ms = double(now.QuadPart-previousEnd)*clock_scale(); previousEnd = 0;
    if (!std::isfinite(ms) || ms < 0) return;
    ++window.gapSamples; window.gapSum += ms;
    if (ms > window.gapMax) window.gapMax = ms;
}
void end_phase() noexcept {
    report(GetTickCount64(), true);
    configured = false; window = {}; previousEnd = 0;
}
void after_end(std::int64_t qpc) noexcept { previousEnd = qpc; }
void desktop_present(double cpuMs, unsigned syncInterval) noexcept {
    if (!configured || !std::isfinite(cpuMs) || cpuMs < 0) return;
    ++window.presentSamples; window.presentSum += cpuMs;
    if (cpuMs > window.presentMax) window.presentMax = cpuMs;
    if (syncInterval) ++window.nonzeroSync;
}
#ifdef BVR_DELIVERY_PROBE_TEST
bool install_test(SetFloatFn target) noexcept { return attach(reinterpret_cast<void*>(target)); }
void remove_test() noexcept {
    if (hookTarget) { MH_DisableHook(hookTarget); MH_RemoveHook(hookTarget); hookTarget = nullptr; original = nullptr; }
    insideEnd = false; pending = {};
}
#endif
}
