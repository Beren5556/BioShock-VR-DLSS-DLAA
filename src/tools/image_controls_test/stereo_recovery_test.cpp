#include "core/util/bounded_activity.h"
#include "core/gfx/image_reconfigure_guard.h"
#include "game/bioshock1r/stereo_recovery.h"
#include <cstdio>

namespace {
unsigned checks = 0, failures = 0;
void expect(bool ok, const char* message) {
    ++checks; if (!ok) ++failures;
    std::printf("%s: %s\n", ok ? "PASS" : "FAIL", message);
}
}
int main() {
    bvr::BoundedActivity activity;
    expect(!activity.active(100), "watchdog unchanged outside bridge waits");
    {
        bvr::BoundedActivity::Scope scope(activity, 100, 5250);
        expect(activity.active(1400), "bounded fence wait exceeds old 1.2s stereo threshold without auto-off");
        {
            bvr::BoundedActivity::Scope nested(activity, 200, 20000);
            expect(!activity.active(5350), "nested operation cannot extend the original deadline");
        }
        expect(activity.active(5349), "nested scope cannot clear its parent's protection");
        expect(!activity.begin(6000, 10000), "expired unfinished operation cannot renew protection");
    }
    expect(!activity.active(101), "scope clears protection immediately on completion");
    try {
        bvr::BoundedActivity::Scope scope(activity, 10000, 4250);
        throw 1;
    } catch (...) {}
    expect(!activity.active(11000), "exception unwinding also clears protection");
    expect(!activity.begin(UINT64_MAX - 1, 5) && !activity.begin(10, 0), "invalid budget cannot mask a hang");

    // Replay the observed ordering: rebuild ended, then frame 54 waited,
    // then helper shutdown, then normal scene progress resumed.
    bvr::image_controls::ReconfigureWindow rebuild;
    rebuild.begin(54488); rebuild.end();
    expect(!rebuild.active(57418), "old rebuild guard alone cannot cover the recorded frame-54 wait");
    activity.begin(57100, 5250);
    expect(activity.active(57418) && activity.active(58321), "recorded watchdog detection and auto-off now fall inside bounded host wait");
    activity.end(); activity.begin(58895, 4250);
    expect(activity.active(61895), "recorded 3-second helper cleanup keeps stereo intent intact");
    activity.end();
    expect(!activity.active(62040), "no blanket grace period remains after helper cleanup");

    using bvr::b1r::scenedraw::StereoRecoveryGate;
    StereoRecoveryGate recovery;
    expect(!recovery.observe(1000, 1, true), "recovery starts observing at a safe gameplay boundary");
    bool tooEarly = false;
    for (uint64_t i = 1; i < 36; ++i) tooEarly |= recovery.observe(1000 + i * 14, 1 + i, true);
    expect(!tooEarly, "many fast frames cannot bypass the 500ms recovery interval");
    expect(recovery.observe(1504, 37, true), "72fps recovers at boundary even if every watchdog sample saw depth greater than zero");
    expect(!recovery.observe(1518, 38, false), "cleared auto-off prevents repeated rearming");

    recovery.observe(2000, 1, true);
    for (uint64_t i = 1; i <= 8; ++i)
        expect(!recovery.observe(2000 + i * 100, 1, true), "frozen presents never qualify as recovery");
    recovery.observe(3000, 1, true);
    for (uint64_t i = 1; i <= 4; ++i) recovery.observe(3000 + i * 100, 1 + i, true);
    expect(!recovery.observe(3900, 6, true), "long gap restarts the healthy interval");
    expect(!recovery.observe(4000, 7, true), "old healthy time is not reused after a stall");

    const char* blockers[] = {"explicit stereo off", "unhook", "poisoned engine",
        "threaded renderer", "loading or cinematic", "nested or right-eye build", "another live rebuild"};
    for (const char* blocker : blockers) {
        recovery.reset(); recovery.observe(5000, 1, true);
        for (uint64_t i = 1; i <= 4; ++i) recovery.observe(5000 + i * 100, i + 1, true);
        expect(!recovery.observe(5500, 6, false), blocker);
        expect(!recovery.observe(5600, 7, true), "eligibility returning requires a fresh healthy interval");
    }
    std::printf("Stereo recovery: %u/%u passed\n", checks - failures, checks);
    return failures ? 1 : 0;
}
