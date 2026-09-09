#pragma once

#include <cmath>
#include <cstdint>
#include <limits>

// CPU-observed fence milestones, not GPU timestamps. No new GPU command, fence
// value or protocol message is introduced. Only one in sixteen current frames
// requests an extra CPU wake; all other frames take readiness snapshots only.
namespace bvr::input_wait_probe {

constexpr std::uint64_t kRemoved = (std::numeric_limits<std::uint64_t>::max)();
constexpr bool sampled_frame(std::uint64_t value) { return value && value % 16 == 0; }
enum class State { Pending, Ready, Removed };
enum class Wake { Output, Process, Input, Timeout, Failed };

inline State state(std::uint64_t completed, std::uint64_t target) {
    return completed == kRemoved ? State::Removed : completed >= target ? State::Ready : State::Pending;
}

inline std::uint32_t remaining_ms(double deadline, double now) {
    const double left = deadline - now;
    if (!(left > 0)) return 0;
    return static_cast<std::uint32_t>(std::ceil(left));
}

struct Observation {
    State inputAtEntry = State::Pending;
    bool sampled = false, outputAtEntry = false, success = false;
    bool splitObserved = false, coalesced = false, registrationFailed = false;
    bool inputRemovedDuringWait = false;
    std::uint32_t staleInputWakes = 0;
    double totalMs = 0, beforeInputMs = 0, afterInputMs = 0;
};

// Hooks supply current QPC milliseconds, completed fence values, event
// registration and a bounded wait. The output event always has priority over
// the input event. Diagnostic input failure never changes the output contract.
template<class Hooks>
bool wait(Hooks& hooks, std::uint64_t target, std::uint32_t timeoutMs,
          bool sample, Observation& result) {
    result = {};
    result.sampled = sample;
    const double start = hooks.now_ms();
    const double deadline = start + timeoutMs;
    result.inputAtEntry = state(hooks.input_value(), target);
    const auto output = state(hooks.output_value(), target);
    result.outputAtEntry = output == State::Ready;
    bool boundaryKnown = sample && result.inputAtEntry == State::Ready;
    double inputObservedAt = start;
    auto finish = [&](bool success) {
        const double end = hooks.now_ms();
        result.success = success;
        result.totalMs = end >= start ? end - start : 0;
        if (sample && success && boundaryKnown && inputObservedAt <= end) {
            result.splitObserved = true;
            result.beforeInputMs = inputObservedAt - start;
            result.afterInputMs = end - inputObservedAt;
        } else if (sample && success && result.inputAtEntry == State::Pending && !boundaryKnown) {
            // If output wins while both events are ready, the input timestamp
            // is unknowable. Never invent a zero or an exact input duration.
            result.coalesced = state(hooks.input_value(), target) == State::Ready;
        }
        return success;
    };
    if (output == State::Removed) return finish(false);
    if (output == State::Ready) return finish(true);
    if (!hooks.register_output(target)) return finish(false);

    bool watchInput = false;
    if (sample && result.inputAtEntry == State::Pending) {
        watchInput = hooks.register_input(target);
        result.registrationFailed = !watchInput;
    }
    for (;;) {
        // Every wake shares one deadline, including registration overhead.
        // A final zero-time poll still permits an already-complete output.
        const auto remaining = remaining_ms(deadline, hooks.now_ms());
        const Wake wake = hooks.wait(watchInput, remaining);
        if (wake == Wake::Output)
            return finish(state(hooks.output_value(), target) == State::Ready);
        if (wake != Wake::Input || !watchInput) return finish(false);
        const auto input = state(hooks.input_value(), target);
        if (input == State::Ready) {
            inputObservedAt = hooks.now_ms();
            boundaryKnown = true;
            watchInput = false;
        } else if (input == State::Removed) {
            result.inputRemovedDuringWait = true;
            watchInput = false;
        } else {
            // An older SetEventOnCompletion can fire after ResetEvent. Only
            // the current fence value identifies this frame's input boundary.
            ++result.staleInputWakes;
        }
        if (!remaining) return finish(false);
    }
}

struct Counters {
    std::uint64_t frames = 0, inputReady = 0, inputPending = 0, inputRemoved = 0;
    std::uint64_t outputReady = 0, sampled = 0, split = 0, coalesced = 0;
    std::uint64_t unobserved = 0, registrationFailed = 0, unsuccessful = 0;
    std::uint64_t staleInputWakes = 0, removedDuringWait = 0;
    std::uint64_t unsampled = 0;
    double sampledTotalMs = 0, unsampledTotalMs = 0, beforeInputMs = 0, afterInputMs = 0;
    void add(const Observation& value) {
        ++frames;
        if (value.inputAtEntry == State::Ready) ++inputReady;
        else if (value.inputAtEntry == State::Removed) ++inputRemoved;
        else ++inputPending;
        outputReady += value.outputAtEntry;
        staleInputWakes += value.staleInputWakes;
        removedDuringWait += value.inputRemovedDuringWait;
        unsuccessful += !value.success;
        if (!value.sampled) {
            ++unsampled;
            unsampledTotalMs += value.totalMs;
            return;
        }
        ++sampled;
        registrationFailed += value.registrationFailed;
        sampledTotalMs += value.totalMs;
        if (value.splitObserved) {
            ++split;
            beforeInputMs += value.beforeInputMs;
            afterInputMs += value.afterInputMs;
        } else {
            ++unobserved;
            coalesced += value.coalesced;
        }
    }
};
} // namespace bvr::input_wait_probe
