#include "game/bioshock2r/exit_guard_contract.h"

#include <array>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <initializer_list>
#include <iterator>
#include <limits>
#include <vector>

namespace {
int failures = 0;
int cases = 0;
void check(bool okay, const char* label) {
    ++cases;
    if (!okay) {
        ++failures;
        std::printf("FAIL: %s\n", label);
    }
}

void request_exit_tests() {
    using bvr::b2r::exit_guard_contract::dispatch_request_exit;
    const int lo = std::numeric_limits<int>::min();
    const int hi = std::numeric_limits<int>::max();
    for (bool enabled : {false, true}) {
        for (int force : {lo, -2, -1, 0, 1, 2, hi}) {
            for (int status : {lo, -1, 0, 1, hi}) {
                std::atomic<bool> terminal{false};
                int originals = 0;
                int cleanups = 0;
                bool argsUnchanged = true;
                bool originalFirst = true;
                bool acceptedStatusUnchanged = true;
                auto original = [&](int forwardedForce, int forwardedStatus) {
                    ++originals;
                    argsUnchanged &= forwardedForce == force && forwardedStatus == status;
                };
                auto accepted = [&](int acceptedStatus) {
                    ++cleanups;
                    originalFirst &= originals == 1;
                    acceptedStatusUnchanged &= acceptedStatus == status;
                };
                const bool shouldClean = enabled && force == 0;
                dispatch_request_exit(enabled, force, status, terminal, original, accepted);
                check(originals == 1 && argsUnchanged, "every native call preserves force/status");
                check(cleanups == int(shouldClean), "only enabled non-forced return cleans up");
                check(originalFirst && acceptedStatusUnchanged,
                      "native acceptance precedes cleanup and retains exit status");
                check(terminal.load() == shouldClean, "forced/passive call does not set terminal latch");
                dispatch_request_exit(enabled, force, status, terminal, original, accepted);
                check(originals == 2 && argsUnchanged, "repeated native call still forwards exact args");
                check(cleanups == int(shouldClean), "repeated accepted call never repeats cleanup");
            }
        }
    }
    std::atomic<bool> terminal{false};
    bool exceptionPassed = false;
    int cleanups = 0;
    try {
        dispatch_request_exit(true, 0, 7, terminal,
            [](int, int) { throw 73; }, [&](int) { ++cleanups; });
    } catch (int error) {
        exceptionPassed = error == 73;
    }
    check(exceptionPassed, "an original exception propagates untouched");
    check(!terminal.load() && cleanups == 0, "original exception cannot become accepted exit");
    int originals = 0;
    int nestedStatus = 0;
    dispatch_request_exit(true, 0, 8, terminal,
        [&](int, int) { ++originals; }, [&](int) {
            ++cleanups;
            dispatch_request_exit(true, 0, 9, terminal,
                [&](int, int status) { ++originals; nestedStatus = status; },
                [&](int) { ++cleanups; });
        });
    check(originals == 2 && nestedStatus == 9, "reentrant native request still preserves its args");
    check(terminal.load() && cleanups == 1, "latch precedes cleanup and blocks reentrant cleanup");
}

template <size_t N, size_t M>
void signature_tests(uint32_t rva, const uint8_t (&signature)[N],
                     const bvr::b2r::patterns::ExitCodeOperand (&operands)[M]) {
    using bvr::b2r::exit_guard_contract::verified_code;
    // A relocated load address must pass too, not just the preferred image base.
    for (uint32_t base : {0x10000000u, 0x26000000u}) {
        std::array<uint8_t, N> bytes{};
        std::memcpy(bytes.data(), signature, N);
        for (const auto& operand : operands) {
            uint32_t value = base + operand.targetRva;
            if (operand.relative) value -= base + rva + operand.offset + 4;
            std::memcpy(bytes.data() + operand.offset, &value, sizeof value);
        }
        check(verified_code(bytes.data(), N, rva, base, signature, operands),
              "matching signature and relocated operands");
        check(!verified_code(bytes.data(), N - 1, rva, base, signature, operands),
              "truncated code refused");
        check(!verified_code(nullptr, N, rva, base, signature, operands),
              "null code refused");
        for (size_t i = 0; i < N; ++i) {
            bytes[i] ^= 1;
            check(!verified_code(bytes.data(), N, rva, base, signature, operands),
                  "every changed opcode, immediate, return and operand refused");
            bytes[i] ^= 1;
        }
    }
}

uint32_t read32(const std::vector<uint8_t>& bytes, size_t offset) {
    uint32_t result = 0;
    if (offset <= bytes.size() && sizeof result <= bytes.size() - offset)
        std::memcpy(&result, bytes.data() + offset, sizeof result);
    return result;
}

// Optional, read-only audit against the installed PE. Never executes it.
void audit_image(const char* path) {
    using namespace bvr::b2r;
    using namespace patterns;
    std::ifstream stream(path, std::ios::binary);
    std::vector<uint8_t> bytes((std::istreambuf_iterator<char>(stream)),
                               std::istreambuf_iterator<char>());
    if (bytes.size() < 0x100) { check(false, "audit image readable PE"); return; }
    const size_t pe = read32(bytes, 0x3C);
    if (pe > bytes.size() || bytes.size() - pe < 0x100 ||
        read32(bytes, pe) != 0x4550 || (read32(bytes, pe + 4) & 0xFFFF) != 0x14C ||
        (read32(bytes, pe + 24) & 0xFFFF) != 0x10B) {
        check(false, "audit image x86 PE32"); return;
    }
    const uint32_t imageBase = read32(bytes, pe + 24 + 28);
    const size_t sections = (read32(bytes, pe + 4) >> 16) & 0xFFFF;
    const size_t sectionTable = pe + 24 + (read32(bytes, pe + 20) & 0xFFFF);
    auto at = [&](uint32_t rva, size_t size) -> const uint8_t* {
        for (size_t section = 0; section < sections; ++section) {
            const size_t header = sectionTable + section * 40;
            if (header > bytes.size() || bytes.size() - header < 40) return nullptr;
            const uint32_t start = read32(bytes, header + 12);
            const uint32_t rawSize = read32(bytes, header + 16);
            const uint32_t rawStart = read32(bytes, header + 20);
            if (rva < start || rva - start > rawSize || size > rawSize - (rva - start))
                continue;
            const uint64_t offset = uint64_t(rawStart) + rva - start;
            if (offset <= bytes.size() && size <= bytes.size() - offset)
                return bytes.data() + static_cast<size_t>(offset);
        }
        return nullptr;
    };
    check(exit_guard_contract::verified_code(at(kExitTickRva, sizeof kExitTickHead),
              sizeof kExitTickHead, kExitTickRva, imageBase,
              kExitTickHead, kExitTickHeadOperands), "real PE Tick head");
    check(exit_guard_contract::verified_code(
              at(kExitNativeEmptyBranchRva, sizeof kExitNativeEmptyBranch),
              sizeof kExitNativeEmptyBranch, kExitNativeEmptyBranchRva, imageBase,
              kExitNativeEmptyBranch, kExitNativeEmptyBranchOperands),
          "real PE native zero-count RequestExit and ret4");
    check(exit_guard_contract::verified_code(at(kExitRequestRva, sizeof kExitRequestBody),
              sizeof kExitRequestBody, kExitRequestRva, imageBase,
              kExitRequestBody, kExitRequestBodyOperands), "real PE RequestExit body");
    check(exit_guard_contract::verified_jump(at(kExitTickThunkRva, 5), 5,
              kExitTickThunkRva, kExitTickRva), "real PE Tick thunk");
    check(exit_guard_contract::verified_jump(at(kExitRequestThunkRva, 5), 5,
              kExitRequestThunkRva, kExitRequestRva), "real PE RequestExit thunk");
    const uint8_t* slot = at(kGameEngineVtableRva + kExitTickVtblOffset, 4);
    uint32_t target = 0;
    if (slot) std::memcpy(&target, slot, sizeof target);
    check(slot && target == imageBase + kExitTickThunkRva, "real PE engine Tick vtable slot");
}
} // namespace

int main(int argc, char** argv) {
    using namespace bvr::b2r;
    using namespace patterns;
    request_exit_tests();
    for (bool current : {false, true})
        for (bool engine : {false, true})
            for (bool client : {false, true})
                for (int32_t count : {std::numeric_limits<int32_t>::min(), -1, 0,
                                      1, 2, std::numeric_limits<int32_t>::max()})
                    check(exit_guard_contract::native_empty_exit(current, engine,
                              client, count) == (current && engine && client && count == 0),
                          "only verified native zero-count branch exits; all others forward");
    signature_tests(kExitTickRva, kExitTickHead, kExitTickHeadOperands);
    signature_tests(kExitNativeEmptyBranchRva, kExitNativeEmptyBranch,
                    kExitNativeEmptyBranchOperands);
    signature_tests(kExitRequestRva, kExitRequestBody, kExitRequestBodyOperands);
    uint8_t stub[] = {0xE9, 0, 0, 0, 0};
    uint32_t displacement = kExitTickRva - kExitTickThunkRva - 5;
    std::memcpy(stub + 1, &displacement, sizeof displacement);
    check(exit_guard_contract::verified_jump(stub, sizeof stub,
              kExitTickThunkRva, kExitTickRva), "verified vtable link thunk");
    check(!exit_guard_contract::verified_jump(stub, sizeof stub,
              kExitTickThunkRva, kExitTickRva + 1), "wrong thunk target refused");
    check(!exit_guard_contract::verified_jump(stub, sizeof stub - 1,
              kExitTickThunkRva, kExitTickRva), "truncated thunk refused");
    for (auto& byte : stub) {
        byte ^= 1;
        check(!exit_guard_contract::verified_jump(stub, sizeof stub,
                  kExitTickThunkRva, kExitTickRva), "modified thunk refused");
        byte ^= 1;
    }
    if (argc == 3 && std::strcmp(argv[1], "--audit-image") == 0)
        audit_image(argv[2]);
    else if (argc != 1)
        check(false, "usage: bvr_bs2_exit_guard_test [--audit-image game.exe]");
    std::printf("BS2 exit guard contract: %d cases, %d failures\n", cases, failures);
    return failures ? 1 : 0;
}
