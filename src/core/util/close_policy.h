#pragma once

#include <cstdint>

// No Win32, clocks, threads or process operations here. Keep the decision
// boundary independently testable; crash.cpp supplies observed signals/time.
namespace bvr::crash::close_policy {

enum class Host { Legacy, Bioshock2 };
enum class Signal { CloseRequest, RequestCancelled, TeardownConfirmed };

constexpr std::uint32_t kGraceMs = 15000;
constexpr std::uint32_t kTimeoutExitCode = 258; // WAIT_TIMEOUT
constexpr std::uint32_t kMissingExceptionExitCode = 574; // ERROR_UNHANDLED_EXCEPTION

struct State {
    bool confirmed = false;
    std::uint64_t confirmedAtMs = 0;
};

constexpr bool confirms_teardown(Host host, Signal signal) {
    return signal == Signal::TeardownConfirmed ||
           (host == Host::Legacy && signal == Signal::CloseRequest);
}

// Request/cancellation have no persistent side effects in BS2. Only a real
// teardown signal may stop engine work or start the watchdog. Once confirmed,
// repeated messages/cancellation cannot postpone or reverse that transition.
constexpr State observe(State state, Host host, Signal signal,
                        std::uint64_t nowMs) {
    if (!state.confirmed && confirms_teardown(host, signal))
        return {true, nowMs};
    return state;
}

constexpr std::uint32_t remaining_grace_ms(State state, std::uint64_t nowMs) {
    if (!state.confirmed || nowMs < state.confirmedAtMs) return kGraceMs;
    const std::uint64_t elapsed = nowMs - state.confirmedAtMs;
    return elapsed >= kGraceMs ? 0 : kGraceMs - static_cast<std::uint32_t>(elapsed);
}

constexpr bool watchdog_due(State state, std::uint64_t nowMs) {
    return state.confirmed && remaining_grace_ms(state, nowMs) == 0;
}

constexpr std::uint32_t watchdog_exit_code(Host host) {
    return host == Host::Bioshock2 ? kTimeoutExitCode : 0;
}

struct FaultDecision {
    bool writeReport = true;
    bool terminate = false;
    std::uint32_t exitCode = 0;
};

constexpr FaultDecision on_fault(Host host, bool teardownConfirmed,
                                 std::uint32_t exceptionCode) {
    if (!teardownConfirmed) return {};
    // This repository still builds the other adapters. Do not silently change
    // their released shutdown behavior while fixing the independent BS2 mod.
    if (host == Host::Legacy) return {false, true, 0};
    // The window state does not establish the cause of a fault. Keep its first
    // report/dump and the actual exception code, even during confirmed close.
    return {true, true, exceptionCode ? exceptionCode : kMissingExceptionExitCode};
}

} // namespace bvr::crash::close_policy
