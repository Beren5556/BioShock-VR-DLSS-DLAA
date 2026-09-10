#pragma once

#include "game/bioshock2r/patterns.h"

#include <atomic>
#include <cstddef>
#include <cstdint>
#include <cstring>

namespace bvr::b2r::exit_guard_contract {

// No dependence on WM_CLOSE or an exception address. This is exactly the
// engine's native empty-viewport condition, narrowed to verified identities.
inline bool native_empty_exit(bool currentEngine, bool engineIdentity,
                              bool clientIdentity, int32_t viewportCount) {
    return currentEngine && engineIdentity && clientIdentity && viewportCount == 0;
}

// The native function is the acceptance boundary, not the caller's menu or
// window request. Preserve BOTH arguments on every call, including repeated
// calls and forced exits. Only a returned, non-forced call starts our terminal
// work, once; an exception/non-return from the original never becomes success.
template <class NativeCall, class AcceptedExit>
void dispatch_request_exit(bool enabled, int force, int status,
                           std::atomic<bool>& terminalStarted,
                           NativeCall nativeCall, AcceptedExit acceptedExit) {
    nativeCall(force, status);
    if (enabled && force == 0 &&
        !terminalStarted.exchange(true, std::memory_order_acq_rel))
        acceptedExit(status);
}

// Relocation-transparent x86 code contract, used by runtime and mutation
// tests. Every byte is checked, including the four bytes of each operand.
template <size_t N, size_t M>
bool verified_code(const uint8_t* code, size_t available, uint32_t codeRva,
                   uint32_t imageBase, const uint8_t (&bytes)[N],
                   const patterns::ExitCodeOperand (&operands)[M]) {
    if (!code || available < N) return false;
    for (size_t i = 0; i < N; ++i) {
        bool operandByte = false;
        for (const auto& operand : operands) {
            if (operand.offset > N || sizeof(uint32_t) > N - operand.offset)
                return false;
            if (i < operand.offset || i >= operand.offset + sizeof(uint32_t))
                continue;
            operandByte = true;
            if (i == operand.offset) {
                uint32_t actual = 0;
                std::memcpy(&actual, code + i, sizeof actual);
                uint32_t expected = imageBase + operand.targetRva;
                if (operand.relative)
                    expected -= imageBase + codeRva + static_cast<uint32_t>(i) +
                                sizeof(uint32_t);
                if (actual != expected) return false;
            }
        }
        if (!operandByte && code[i] != bytes[i]) return false;
    }
    return true;
}

inline bool verified_jump(const uint8_t* stub, size_t available, uint32_t stubRva,
                          uint32_t targetRva) {
    if (!stub || available < 5 || stub[0] != 0xE9) return false;
    uint32_t relative = 0;
    std::memcpy(&relative, stub + 1, sizeof relative);
    return stubRva + 5 + relative == targetRva;
}

} // namespace bvr::b2r::exit_guard_contract
