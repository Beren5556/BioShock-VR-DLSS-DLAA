#pragma once
#include <cstdint>

namespace bvr::critical_path_probe {

enum class Stage : unsigned {
    Other, EngineLeft, EngineRight, PresentSetup, XrPrepare, XrWait, Overlay,
    CaptureLeft, CaptureRight, CaptureOther, Guides, HostSubmitLeft,
    HostSubmitRight, HostCopies, HostFlush, HostPipe, HostResolveLeft,
    HostResolveRight, HostWaitLeft, HostWaitRight, HostCopy, XrEnd,
    DesktopComposite, HudRoll, DesktopPresent, Count
};

struct Info {
    unsigned phase = 0, mode = 0;
    unsigned renderWidth = 0, renderHeight = 0, outputWidth = 0, outputHeight = 0;
    bool operator==(const Info&) const = default;
};

#ifdef BVR_CRITICAL_PATH_PROBE
// The first configure binds this observer to its caller's render thread.
// Foreign threads cannot configure, reset, close cycles or change scopes.
// Identical repeated configuration is a no-op; geometry/phase changes safely
// invalidate any live scopes. reset disables observation until configure.
void configure(const Info&, bool enabled) noexcept;
void reset() noexcept;

// Nested scopes partition elapsed WALL time exclusively. A child suspends its
// parent, which resumes on destruction; unscoped time belongs to Other.
// These values include API/IPC waits and are NOT CPU busy time, exclusive GPU
// time, or Virtual Desktop's GAME/total-latency metric. No GPU queries or waits
// are added. A reset/reconfigure never resurrects an older scope's parent.
class Scope {
    std::uint64_t generation_ = 0, token_ = 0, parentToken_ = 0;
    Stage parentStage_ = Stage::Other;
public:
    explicit Scope(Stage, bool enabled = true) noexcept;
    ~Scope() noexcept;
    Scope(const Scope&) = delete;
    Scope& operator=(const Scope&) = delete;
    Scope(Scope&&) = delete;
    Scope& operator=(Scope&&) = delete;
};

// Call at healthy xrEndFrame RETURN, not at Present entry. The first boundary
// is discarded; later samples cover the entire interval between boundaries,
// including desktop work, the next engine pair, xrWaitFrame and submission.
void end_cycle() noexcept;
#else
inline void configure(const Info&, bool) noexcept {}
inline void reset() noexcept {}
class Scope {
public:
    explicit Scope(Stage, bool = true) noexcept {}
    Scope(const Scope&) = delete;
    Scope& operator=(const Scope&) = delete;
    Scope(Scope&&) = delete;
    Scope& operator=(Scope&&) = delete;
};
inline void end_cycle() noexcept {}
#endif

#if defined(BVR_CRITICAL_PATH_PROBE) && defined(BVR_CRITICAL_PATH_PROBE_TEST)
namespace testing {
using Clock = std::int64_t (*)() noexcept;
// Test process only: install before use, with no live scopes/foreign callers.
void use_clock(Clock, std::int64_t ticksPerSecond) noexcept;
void clear() noexcept;
struct Snapshot {
    Info info{};
    bool active = false, cycleOpen = false;
    Stage stage = Stage::Other;
    std::uint64_t generation = 0, cycles = 0, discarded = 0, invalidations = 0;
    std::uint64_t scopeCalls = 0, cycleTicks = 0, maxCycleTicks = 0;
    std::uint64_t pendingTicks[static_cast<unsigned>(Stage::Count)]{};
    std::uint64_t stageTicks[static_cast<unsigned>(Stage::Count)]{};
};
Snapshot snapshot() noexcept;
}
#endif
}
