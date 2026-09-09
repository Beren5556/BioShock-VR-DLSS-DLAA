#pragma once
#include <cstdint>

namespace bvr::delivery_probe {
// This candidate changes only healthy BS1 paired temporal projection output.
// NORMAL, untagged/menu/fallback/other-game paths retain their previous order.
inline bool defer_desktop(bool bs1, bool temporal, bool pairedRight,
                          bool projection, bool focused) noexcept {
    return bs1 && temporal && pairedRight && projection && focused;
}
class DesktopTail {
    bool deferred_, done_ = false;
public:
    explicit DesktopTail(bool deferred) : deferred_(deferred) {}
    template<class Work> void run(bool afterXr, Work&& work) {
        if (done_ || afterXr != deferred_) return;
        done_ = true;
        work();
    }
};

struct FrameInfo {
    unsigned phase = 0, mode = 0;
    unsigned renderWidth = 0, renderHeight = 0, outputWidth = 0, outputHeight = 0;
    bool deferredDesktop = false;
    bool operator==(const FrameInfo&) const = default;
};
struct GpuSample {
    unsigned calls = 0, positive = 0, zero = 0, invalid = 0, accepted = 0;
    double sumMs = 0, maxMs = 0;
};
// install() only inspects an ALREADY loaded named VD module. It never loads a
// runtime, changes registry/configuration, or redirects another process.
void install(const char* runtimeName) noexcept;
void begin_end_frame() noexcept;
GpuSample finish_end_frame() noexcept;
void observe(const FrameInfo&, const GpuSample&, double endCpuMs, double desktopCpuMs) noexcept;
void end_phase() noexcept;
void before_wait() noexcept;
void after_end(std::int64_t qpc) noexcept;
void desktop_present(double cpuMs, unsigned syncInterval) noexcept;

#ifdef BVR_DELIVERY_PROBE_TEST
using SetFloatFn = char(__cdecl*)(void*, const char*, float);
bool install_test(SetFloatFn target) noexcept;
void remove_test() noexcept;
#endif
}
