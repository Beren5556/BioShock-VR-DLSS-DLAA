#include "core/vr/critical_path_probe.h"
#include <atomic>
#include <cstdarg>
#include <cstdio>
#include <new>
#include <string>
#include <thread>
#include <vector>

namespace P = bvr::critical_path_probe;
namespace {
unsigned checks = 0, failures = 0;
std::atomic<std::int64_t> ticks{0};
std::atomic<unsigned> clockReads{0};
std::vector<std::string> logs;
std::int64_t logCost = 0;
std::int64_t clock_now() noexcept { ++clockReads; return ticks.load(); }
void at(std::int64_t value) { ticks = value; }
void expect(bool condition, const char* label) {
    ++checks;
    if (!condition) ++failures;
    std::printf("%s: %s\n", condition ? "PASS" : "FAIL", label);
}
std::uint64_t stage(const P::testing::Snapshot& s, P::Stage value) {
    return s.stageTicks[static_cast<unsigned>(value)];
}
std::uint64_t partition(const P::testing::Snapshot& s) {
    std::uint64_t result = 0;
    for (auto value : s.stageTicks) result += value;
    return result;
}
constexpr P::Info info{2, 2, 3072, 3072, 3072, 3072};
void fresh(std::int64_t frequency = 1000) {
    at(0); clockReads = 0; logs.clear(); logCost = 0;
    P::testing::use_clock(&clock_now, frequency);
    P::configure(info, true);
}
}
namespace bvr::log {
void write(const char* format, ...) {
    char text[4096]{};
    va_list arguments;
    va_start(arguments, format);
    std::vsnprintf(text, sizeof(text), format, arguments);
    va_end(arguments);
    logs.emplace_back(text);
    ticks += logCost;
}
}

int main() {
    std::puts("Deterministic CPU wall-time accounting tests. No game, GPU, VR runtime, INI or installed DLL access.");
    fresh();
    expect(P::testing::snapshot().active, "first enabled configure binds the caller thread");
    {
        P::Scope before(P::Stage::EngineLeft);
        at(10);
    }
    P::end_cycle();
    auto s = P::testing::snapshot();
    expect(s.cycles == 0 && s.discarded == 1 && s.cycleOpen,
        "first boundary discards the incomplete pre-observation cycle");
    at(12);
    {
        P::Scope engine(P::Stage::EngineLeft);
        at(15);
        {
            P::Scope present(P::Stage::PresentSetup);
            at(17);
            { P::Scope wait(P::Stage::HostWaitLeft); at(22); }
            at(23);
        }
        at(27);
    }
    at(30); P::end_cycle();
    s = P::testing::snapshot();
    expect(s.cycles == 1 && s.cycleTicks == 20 && s.maxCycleTicks == 20, "boundary-to-boundary cycle is exact");
    expect(stage(s, P::Stage::Other) == 5, "gaps before and after scopes belong to Other");
    expect(stage(s, P::Stage::EngineLeft) == 7, "parent resumes after nested scope without inclusive double counting");
    expect(stage(s, P::Stage::PresentSetup) == 3 && stage(s, P::Stage::HostWaitLeft) == 5,
        "nested API wait time is isolated from its parent");
    expect(partition(s) == s.cycleTicks && s.scopeCalls == 3, "exclusive stages exactly partition the cycle");
    at(32); P::end_cycle();
    s = P::testing::snapshot();
    expect(s.cycles == 2 && s.cycleTicks == 22 && s.maxCycleTicks == 20 && stage(s, P::Stage::Other) == 7,
        "multiple cycles aggregate totals and preserve the worst complete cycle");
    expect(logs.empty(), "short windows do not emit per-frame logs");

    fresh(); P::end_cycle();
    const unsigned initialReads = clockReads;
    { P::Scope disabled(P::Stage::HostFlush, false); at(5); }
    { P::Scope invalid(P::Stage::Count); at(7); }
    P::configure(info, true);
    expect(clockReads == initialReads, "disabled/invalid scopes and unchanged configure do not read QPC");
    P::end_cycle();
    s = P::testing::snapshot();
    expect(s.cycleTicks == 7 && stage(s, P::Stage::Other) == 7 && s.scopeCalls == 0,
        "ignored scopes cannot alter stage attribution");

    fresh(); P::end_cycle();
    {
        P::Scope parent(P::Stage::EngineRight);
        at(2);
        { P::Scope same(P::Stage::EngineRight); at(5); }
        at(7);
    }
    P::end_cycle();
    s = P::testing::snapshot();
    expect(stage(s, P::Stage::EngineRight) == 7 && partition(s) == 7,
        "nested scopes with the same stage remain exclusive");

    fresh(); P::end_cycle();
    {
        P::Scope xr(P::Stage::XrEnd);
        at(3); P::end_cycle();
        expect(P::testing::snapshot().stage == P::Stage::XrEnd, "cycle close preserves a still-live scope");
        at(5);
    }
    at(9); P::end_cycle();
    s = P::testing::snapshot();
    expect(s.cycles == 2 && stage(s, P::Stage::XrEnd) == 5 && stage(s, P::Stage::Other) == 4,
        "scope crossing a boundary splits cleanly between adjacent cycles");

    fresh(); P::end_cycle();
    const auto generation = P::testing::snapshot().generation;
    {
        P::Scope oldParent(P::Stage::EngineLeft);
        at(3);
        P::reset();
        expect(!P::testing::snapshot().active, "reset disables observation while old scope remains live");
        P::configure(info, true);
        at(4); P::end_cycle();
        { P::Scope newScope(P::Stage::Overlay); at(8); }
        at(9);
    }
    expect(P::testing::snapshot().stage == P::Stage::Other && P::testing::snapshot().generation > generation,
        "old destructor after reset cannot restore a stale parent");
    at(10); P::end_cycle();
    s = P::testing::snapshot();
    expect(s.cycleTicks == 6 && stage(s, P::Stage::Overlay) == 4 && stage(s, P::Stage::EngineLeft) == 0,
        "reset excludes old geometry and only counts complete new cycles");

    fresh(); P::end_cycle();
    auto replacement = info; replacement.phase = 3; replacement.renderWidth = 2150;
    {
        P::Scope stale(P::Stage::CaptureLeft);
        at(3); P::configure(replacement, true);
        at(5); P::end_cycle();
        at(6);
    }
    at(10); P::end_cycle();
    s = P::testing::snapshot();
    expect(s.info == replacement && s.cycles == 1 && s.cycleTicks == 5 && stage(s, P::Stage::Other) == 5,
        "phase or geometry reconfigure invalidates live scopes and old partial cycles");
    P::configure(replacement, false);
    const auto disabledReads = clockReads.load();
    { P::Scope ignored(P::Stage::Overlay); at(20); P::end_cycle(); }
    expect(!P::testing::snapshot().active && clockReads == disabledReads, "disabled observer adds no timing samples");

    fresh(); P::end_cycle();
    {
        P::Scope ownerScope(P::Stage::EngineLeft);
        const auto reads = clockReads.load();
        std::thread foreign([&] {
            P::configure(replacement, true);
            P::Scope ignored(P::Stage::HostPipe);
            P::end_cycle(); P::reset();
        });
        foreign.join();
        expect(clockReads == reads && P::testing::snapshot().info == info &&
            P::testing::snapshot().stage == P::Stage::EngineLeft,
            "foreign thread cannot sample, reset, reconfigure or overwrite the owner stage");
        at(6);
    }
    P::end_cycle();
    expect(stage(P::testing::snapshot(), P::Stage::EngineLeft) == 6, "owner scope resumes unchanged after foreign calls");

    fresh(); P::end_cycle();
    alignas(P::Scope) unsigned char outerMemory[sizeof(P::Scope)];
    alignas(P::Scope) unsigned char innerMemory[sizeof(P::Scope)];
    auto* outer = new (outerMemory) P::Scope(P::Stage::CaptureLeft);
    at(2);
    auto* inner = new (innerMemory) P::Scope(P::Stage::HostCopies);
    at(5); outer->~Scope();
    expect(P::testing::snapshot().invalidations == 1 && !P::testing::snapshot().cycleOpen,
        "out-of-order destruction discards a malformed cycle instead of reporting false timings");
    inner->~Scope();
    at(6); P::end_cycle();
    at(9); P::end_cycle();
    s = P::testing::snapshot();
    expect(s.cycles == 1 && s.cycleTicks == 3 && stage(s, P::Stage::Other) == 3,
        "malformed-scope recovery does not resurrect the stale nested stack");

    fresh(); at(10); P::end_cycle();
    { P::Scope beforeRollback(P::Stage::EngineLeft); at(8); }
    expect(P::testing::snapshot().invalidations == 1, "backward clock invalidates sample without unsigned underflow");
    at(12); P::end_cycle(); at(15); P::end_cycle();
    expect(P::testing::snapshot().cycleTicks == 3, "accounting recovers after a clock anomaly");

    fresh(); P::end_cycle();
    {
        P::Scope engine(P::Stage::EngineLeft);
        at(250);
        { P::Scope wait(P::Stage::XrWait); at(500); }
        at(750);
    }
    at(1000); logCost = 7; P::end_cycle();
    expect(logs.size() == 1, "one report is emitted after one second, not once per scope or frame");
    const auto& report = logs.front();
    expect(report.find("phase=2 mode=2 render=3072x3072 output=3072x3072 ageMs=1000 cycles=1") != std::string::npos,
        "report identifies phase, geometry, age and complete-cycle sample count");
    expect(report.find("cycleMs=1000.000 cycleMaxMs=1000.000 partitionErrorTicks=0") != std::string::npos &&
        report.find("engineLeftMs=500.000") != std::string::npos && report.find("xrWaitMs=250.000") != std::string::npos,
        "report converts clock ticks and emits exact exclusive stage means");
    expect(report.find("includesWaits=1 cpuBusy=0 gpuExclusive=0 notTotalVdLatency=1") != std::string::npos,
        "report explicitly distinguishes elapsed waits from CPU busy, GPU and VD total latency");
    at(1010); P::end_cycle();
    s = P::testing::snapshot();
    expect(s.cycles == 1 && s.cycleTicks == 10 && stage(s, P::Stage::Other) == 10,
        "reporting overhead remains inside Other of the next cycle rather than disappearing");
    expect(logs.size() == 1, "report window is reset without producing duplicate logs");

    fresh(); P::end_cycle();
    {
        P::Scope xr(P::Stage::XrEnd);
        at(1000); logCost = 7; P::end_cycle();
        expect(P::testing::snapshot().stage == P::Stage::XrEnd, "report restores the live stage after accounting its own cost");
        at(1010);
    }
    at(1012); P::end_cycle();
    s = P::testing::snapshot();
    expect(stage(s, P::Stage::Other) == 9 && stage(s, P::Stage::XrEnd) == 3 && partition(s) == 12,
        "logging inside a live XrEnd scope is charged to Other, not falsely to runtime submission");

    fresh(10000000); P::end_cycle();
    { P::Scope wait(P::Stage::HostWaitRight); at(100000); }
    at(150000); P::end_cycle();
    s = P::testing::snapshot();
    expect(s.cycleTicks == 150000 && stage(s, P::Stage::HostWaitRight) == 100000 && partition(s) == 150000,
        "QPC-scale tick frequencies retain exact integer partitioning");
    P::testing::use_clock(&clock_now, 0); P::configure(info, true);
    expect(!P::testing::snapshot().active, "unavailable clock frequency disables measurement without changing execution");
    P::testing::use_clock(nullptr, 0);
    std::printf("Critical-path checks: %u/%u passed\n", checks - failures, checks);
    return failures ? 1 : 0;
}
