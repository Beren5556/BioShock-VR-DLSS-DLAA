#include "core/vr/critical_path_probe.h"

#ifdef BVR_CRITICAL_PATH_PROBE
#include "core/util/log.h"
#include <windows.h>
#include <atomic>
#include <cstdio>

namespace bvr::critical_path_probe {
namespace {
constexpr unsigned stageCount = static_cast<unsigned>(Stage::Count);
constexpr const char* names[stageCount] = {
    "other", "engineLeft", "engineRight", "presentSetup", "xrPrepare",
    "xrWait", "overlay", "captureLeft", "captureRight", "captureOther",
    "guides", "hostSubmitLeft", "hostSubmitRight", "hostCopies", "hostFlush",
    "hostPipe", "hostResolveLeft", "hostResolveRight", "hostWaitLeft",
    "hostWaitRight", "hostCopy", "xrEnd", "desktopComposite", "hudRoll",
    "desktopPresent"
};
struct Totals {
    std::uint64_t stages[stageCount]{};
    std::uint64_t cycles = 0, discarded = 0, invalidations = 0;
    std::uint64_t scopes = 0, ticks = 0, maxTicks = 0;
};
struct State {
    Info info{};
    bool active = false, cycleOpen = false;
    std::uint64_t generation = 1, topToken = 0, nextToken = 0;
    Stage stage = Stage::Other;
    std::int64_t frequency = 0, phaseStart = 0, reportStart = 0;
    std::int64_t last = 0, cycleStart = 0;
    std::uint64_t pending[stageCount]{};
    std::uint64_t pendingScopes = 0;
    Totals totals{};
};
State state;
// Only the owner reads or changes State. The atomic owner ID is the sole
// shared access made by rejected foreign callers; no lock or wait is needed.
std::atomic<DWORD> owner{0};
static_assert(std::atomic<DWORD>::is_always_lock_free);

#ifdef BVR_CRITICAL_PATH_PROBE_TEST
testing::Clock testClock = nullptr;
std::int64_t testFrequency = 0;
#endif
std::int64_t now() noexcept {
#ifdef BVR_CRITICAL_PATH_PROBE_TEST
    if (testClock) return testClock();
#endif
    LARGE_INTEGER value{};
    QueryPerformanceCounter(&value);
    return value.QuadPart;
}
std::int64_t frequency() noexcept {
#ifdef BVR_CRITICAL_PATH_PROBE_TEST
    if (testClock) return testFrequency;
#endif
    static const std::int64_t value = [] {
        LARGE_INTEGER result{};
        return QueryPerformanceFrequency(&result) ? result.QuadPart : 0;
    }();
    return value;
}
bool own_thread() noexcept {
    return owner.load(std::memory_order_acquire) == GetCurrentThreadId();
}
bool active() noexcept { return own_thread() && state.active; }
void clear_pending() noexcept {
    for (auto& value : state.pending) value = 0;
    state.pendingScopes = 0;
}
void invalidate_cycle(std::int64_t timestamp) noexcept {
    ++state.totals.discarded;
    ++state.totals.invalidations;
    ++state.generation;
    state.topToken = 0;
    state.stage = Stage::Other;
    state.cycleOpen = false;
    state.last = timestamp;
    clear_pending();
}
bool charge(std::int64_t timestamp) noexcept {
    if (timestamp < state.last) {
        invalidate_cycle(timestamp);
        return false;
    }
    if (state.cycleOpen)
        state.pending[static_cast<unsigned>(state.stage)] +=
            static_cast<std::uint64_t>(timestamp - state.last);
    state.last = timestamp;
    return true;
}
void report(std::int64_t timestamp) noexcept {
    if (!state.totals.cycles || timestamp - state.reportStart < state.frequency)
        return;
    const auto totals = state.totals;
    state.totals = {};
    state.reportStart = timestamp;
    const double perCycleMs = 1000.0 / double(state.frequency) / double(totals.cycles);
    char stageText[1536]{};
    std::size_t used = 0;
    std::uint64_t partition = 0;
    for (unsigned i = 0; i < stageCount; ++i) {
        partition += totals.stages[i];
        const int written = std::snprintf(stageText + used, sizeof(stageText) - used,
            "%s%sMs=%.3f", i ? " " : "", names[i], totals.stages[i] * perCycleMs);
        if (written < 0 || static_cast<std::size_t>(written) >= sizeof(stageText) - used)
            break;
        used += static_cast<std::size_t>(written);
    }
    // Start the next cycle at the actual end boundary, not after log I/O.
    // Formatting and logging belong to Other even if end_cycle was called
    // from a live XrEnd scope. They therefore cannot silently disappear.
    const Stage previous = state.stage;
    state.stage = Stage::Other;
    BVR_LOG("[critical-path-probe] phase=%u mode=%u render=%ux%u output=%ux%u "
        "ageMs=%llu cycles=%llu discarded=%llu invalidations=%llu scopeCalls=%llu "
        "cycleMs=%.3f cycleMaxMs=%.3f partitionErrorTicks=%lld %s "
        "exclusivePartition=1 includesWaits=1 cpuBusy=0 gpuExclusive=0 notTotalVdLatency=1",
        state.info.phase, state.info.mode, state.info.renderWidth, state.info.renderHeight,
        state.info.outputWidth, state.info.outputHeight,
        static_cast<unsigned long long>(double(timestamp - state.phaseStart) * 1000.0 / double(state.frequency)),
        static_cast<unsigned long long>(totals.cycles), static_cast<unsigned long long>(totals.discarded),
        static_cast<unsigned long long>(totals.invalidations), static_cast<unsigned long long>(totals.scopes),
        totals.ticks * perCycleMs, double(totals.maxTicks) * 1000.0 / double(state.frequency),
        static_cast<long long>(partition) - static_cast<long long>(totals.ticks), stageText);
    if (charge(now())) state.stage = previous;
}
}

void configure(const Info& info, bool enabled) noexcept {
    const DWORD caller = GetCurrentThreadId();
    DWORD bound = owner.load(std::memory_order_acquire);
    if (!bound) {
        if (!enabled) return;
        if (!owner.compare_exchange_strong(bound, caller, std::memory_order_acq_rel)
            && bound != caller) return;
    } else if (bound != caller) return;
    if (!enabled) { reset(); return; }
    if (state.active && state.info == info) return;
    const auto generation = state.generation + 1;
    state = {};
    state.generation = generation;
    state.frequency = frequency();
    if (state.frequency <= 0) return;
    state.info = info;
    state.active = true;
    state.last = state.phaseStart = state.reportStart = now();
}
void reset() noexcept {
    if (!own_thread()) return;
    const auto generation = state.generation + 1;
    state = {};
    state.generation = generation;
}
Scope::Scope(Stage selected, bool enabled) noexcept {
    if (!enabled || !active()) return;
    if (static_cast<unsigned>(selected) >= stageCount) return;
    if (!charge(now())) return;
    generation_ = state.generation;
    token_ = ++state.nextToken;
    parentToken_ = state.topToken;
    parentStage_ = state.stage;
    state.topToken = token_;
    state.stage = selected;
    if (state.cycleOpen) ++state.pendingScopes;
}
Scope::~Scope() noexcept {
    if (!token_ || !active() || generation_ != state.generation) return;
    const auto timestamp = now();
    if (state.topToken != token_) {
        // Malformed non-LIFO lifetime cannot produce a plausible but false
        // partition. Drop the partial sample and invalidate all live scopes.
        invalidate_cycle(timestamp);
        return;
    }
    if (!charge(timestamp)) return;
    state.stage = parentStage_;
    state.topToken = parentToken_;
}
void end_cycle() noexcept {
    if (!active()) return;
    const auto timestamp = now();
    if (!charge(timestamp)) return;
    if (!state.cycleOpen) {
        ++state.totals.discarded;
        state.cycleOpen = true;
    } else {
        auto& totals = state.totals;
        const auto elapsed = static_cast<std::uint64_t>(timestamp - state.cycleStart);
        ++totals.cycles;
        totals.scopes += state.pendingScopes;
        totals.ticks += elapsed;
        if (elapsed > totals.maxTicks) totals.maxTicks = elapsed;
        for (unsigned i = 0; i < stageCount; ++i) totals.stages[i] += state.pending[i];
    }
    state.cycleStart = timestamp;
    clear_pending();
    report(timestamp);
}

#ifdef BVR_CRITICAL_PATH_PROBE_TEST
namespace testing {
void clear() noexcept {
    state = {};
    owner.store(0, std::memory_order_release);
}
void use_clock(Clock clock, std::int64_t ticksPerSecond) noexcept {
    clear();
    testClock = clock;
    testFrequency = ticksPerSecond;
}
Snapshot snapshot() noexcept {
    Snapshot result{};
    if (!own_thread()) return result;
    result.info = state.info;
    result.active = state.active;
    result.cycleOpen = state.cycleOpen;
    result.stage = state.stage;
    result.generation = state.generation;
    result.cycles = state.totals.cycles;
    result.discarded = state.totals.discarded;
    result.invalidations = state.totals.invalidations;
    result.scopeCalls = state.totals.scopes;
    result.cycleTicks = state.totals.ticks;
    result.maxCycleTicks = state.totals.maxTicks;
    for (unsigned i = 0; i < stageCount; ++i) {
        result.pendingTicks[i] = state.pending[i];
        result.stageTicks[i] = state.totals.stages[i];
    }
    return result;
}
}
#endif
}
#endif
