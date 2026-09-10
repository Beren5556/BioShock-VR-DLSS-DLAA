#include "core/util/close_policy.h"

#include <cstdint>
#include <cstdio>
#include <limits>

namespace {
int failures = 0;
int checks = 0;

void check(bool ok, const char* description) {
    ++checks;
    if (!ok) {
        ++failures;
        std::printf("FAIL: %s\n", description);
    }
}
} // namespace

int main() {
    using namespace bvr::crash::close_policy;
    constexpr auto bs2 = Host::Bioshock2;
    constexpr std::uint32_t av = 0xC0000005u;

    State state{};
    check(!state.confirmed && !watchdog_due(state, 1000000), "running has no watchdog");
    state = observe(state, bs2, Signal::CloseRequest, 10);
    check(!state.confirmed && !watchdog_due(state, 1000000), "BS2 request cannot arm watchdog");
    state = observe(state, bs2, Signal::CloseRequest, 20);
    check(!state.confirmed, "repeated request cannot confirm teardown");
    state = observe(state, bs2, Signal::RequestCancelled, 30);
    check(!state.confirmed && !watchdog_due(state, 1000000), "cancelled request remains live");

    const auto requestedFault = on_fault(bs2, state.confirmed, av);
    check(requestedFault.writeReport && !requestedFault.terminate,
          "unconfirmed fault has normal reporting, not forced successful close");

    state = observe(state, bs2, Signal::TeardownConfirmed, 1000);
    check(state.confirmed && state.confirmedAtMs == 1000, "confirmation records its own time");
    state = observe(state, bs2, Signal::TeardownConfirmed, 8000);
    check(state.confirmedAtMs == 1000, "repeated confirmation cannot extend grace period");
    state = observe(state, bs2, Signal::CloseRequest, 9000);
    state = observe(state, bs2, Signal::RequestCancelled, 10000);
    check(state.confirmed && state.confirmedAtMs == 1000,
          "confirmed teardown cannot be reversed by request/cancellation");
    check(!watchdog_due(state, 15999) && remaining_grace_ms(state, 15999) == 1,
          "watchdog waits for the entire grace period");
    check(watchdog_due(state, 16000) && remaining_grace_ms(state, 16000) == 0,
          "watchdog fires at exact grace boundary");
    check(watchdog_due(state, 16001), "watchdog remains due beyond boundary");
    check(!watchdog_due(state, 999) && remaining_grace_ms(state, 999) == kGraceMs,
          "earlier clock sample cannot underflow to an immediate timeout");

    const auto direct = observe({}, bs2, Signal::TeardownConfirmed, 0);
    check(direct.confirmed && watchdog_due(direct, kGraceMs),
          "direct confirmation and time zero are valid");
    const auto nearLimit = observe({}, bs2, Signal::TeardownConfirmed,
                                  std::numeric_limits<std::uint64_t>::max() - 10);
    check(!watchdog_due(nearLimit, std::numeric_limits<std::uint64_t>::max()) &&
              remaining_grace_ms(nearLimit, std::numeric_limits<std::uint64_t>::max()) == kGraceMs - 10,
          "timeout arithmetic does not overflow by adding a deadline");

    const auto confirmedFault = on_fault(bs2, true, av);
    check(confirmedFault.writeReport && confirmedFault.terminate && confirmedFault.exitCode == av,
          "confirmed-close AV retains report and original nonzero exit code");
    const auto otherFault = on_fault(bs2, true, 0xC0000409u);
    check(otherFault.writeReport && otherFault.terminate && otherFault.exitCode == 0xC0000409u,
          "confirmed-close fatal code is not replaced with the known AV code");
    const auto missingFault = on_fault(bs2, true, 0);
    check(missingFault.writeReport && missingFault.terminate &&
              missingFault.exitCode == kMissingExceptionExitCode,
          "missing exception record cannot become success");
    check(watchdog_exit_code(bs2) == 258, "BS2 watchdog reports WAIT_TIMEOUT, not success");

    State legacy = observe({}, Host::Legacy, Signal::CloseRequest, 42);
    check(legacy.confirmed && legacy.confirmedAtMs == 42, "legacy WM_CLOSE behavior is preserved");
    legacy = observe(legacy, Host::Legacy, Signal::RequestCancelled, 80);
    check(legacy.confirmed && legacy.confirmedAtMs == 42, "legacy teardown remains irreversible");
    const auto legacyFault = on_fault(Host::Legacy, true, av);
    check(!legacyFault.writeReport && legacyFault.terminate && legacyFault.exitCode == 0,
          "legacy close-fault treatment is unchanged");
    check(watchdog_exit_code(Host::Legacy) == 0, "legacy watchdog exit code is unchanged");
    const auto legacyRunning = on_fault(Host::Legacy, false, av);
    check(legacyRunning.writeReport && !legacyRunning.terminate,
          "legacy ordinary faults still use the normal report path");

    std::printf("%s: close policy (%d checks, %d failures)\n",
                failures ? "FAIL" : "PASS", checks, failures);
    return failures ? 1 : 0;
}
