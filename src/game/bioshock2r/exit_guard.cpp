#include "game/bioshock2r/exit_guard.h"
#include "game/bioshock2r/exit_guard_contract.h"

#include "core/util/crash.h"
#include "core/util/diag.h"
#include "core/util/game_exit_gate.h"
#include "core/util/log.h"
#include "core/vr/openxr_runtime.h"
#include "game/bioshock2r/patterns.h"

#include <windows.h>
#include <MinHook.h>

#include <atomic>
#include <cstring>

namespace bvr::b2r::exit_guard {
namespace {

static_assert(sizeof(void*) == 4, "The verified BS2 exit contract is x86 only");
using TickFn = void(__fastcall*)(void* engine, void* edx, float deltaSeconds);
using RequestExitFn = void(__cdecl*)(int force, int status);
TickFn g_originalTick = nullptr;
RequestExitFn g_originalRequestExit = nullptr;
// Patched engine entry point, distinct from the trampoline. Both our native
// routes must enter the RequestExit hook too, just like an accepted menu quit.
RequestExitFn g_requestExit = nullptr;
pattern_scan::ProcessImage g_image{};
std::atomic<bool> g_terminalStarted{false};
std::atomic<bool> g_installed{false};

bool image_range(const pattern_scan::ProcessImage& image, uint32_t rva, size_t bytes) {
    return image.base && rva <= image.size && bytes <= image.size - rva &&
           pattern_scan::is_memory_valid(image.base + rva, bytes);
}

template <size_t N, size_t M>
bool verify_code(const pattern_scan::ProcessImage& image, uint32_t rva,
                 const uint8_t (&bytes)[N],
                 const patterns::ExitCodeOperand (&operands)[M]) {
    if (!image_range(image, rva, N)) return false;
    for (const auto& operand : operands)
        if (!image_range(image, operand.targetRva, 1)) return false;
    return exit_guard_contract::verified_code(
        image.base + rva, N, rva,
        static_cast<uint32_t>(reinterpret_cast<uintptr_t>(image.base)), bytes, operands);
}

bool verify_stub(const pattern_scan::ProcessImage& image, uint32_t stubRva,
                 uint32_t targetRva) {
    if (!image_range(image, stubRva, 5) || !image_range(image, targetRva, 1))
        return false;
    return exit_guard_contract::verified_jump(image.base + stubRva, 5,
                                               stubRva, targetRva);
}

bool verify_image(const pattern_scan::ProcessImage& image) {
    using namespace patterns;
    const uint32_t slotRva = kGameEngineVtableRva + kExitTickVtblOffset;
    if (!image_range(image, slotRva, sizeof(void*))) return false;
    const uint8_t* tickStub = nullptr;
    std::memcpy(&tickStub, image.base + slotRva, sizeof tickStub);
    return tickStub == image.base + kExitTickThunkRva &&
           verify_stub(image, kExitTickThunkRva, kExitTickRva) &&
           verify_stub(image, kExitRequestThunkRva, kExitRequestRva) &&
           verify_code(image, kExitTickRva, kExitTickHead,
                       kExitTickHeadOperands) &&
           verify_code(image, kExitNativeEmptyBranchRva, kExitNativeEmptyBranch,
                       kExitNativeEmptyBranchOperands) &&
           verify_code(image, kExitRequestRva, kExitRequestBody,
                       kExitRequestBodyOperands);
}

// This SEH boundary protects ONLY our read-only identity probe. A failed
// probe returns to the untouched original Tick; it never swallows a fault
// from Tick or from the engine's RequestExit function.
bool verified_viewport_count(void* expectedEngine, bool requireExpected,
                             int32_t* viewportCount) {
    using namespace patterns;
    __try {
        if (!g_image.base || !viewportCount) return false;
        const uint8_t* object = *reinterpret_cast<const uint8_t* const*>(
            g_image.base + kGameEnginePtrRva);
        if (!object || (requireExpected && object != expectedEngine) ||
            *reinterpret_cast<const uint8_t* const*>(object) !=
                g_image.base + kGameEngineVtableRva)
            return false;
        const uint8_t* client = *reinterpret_cast<const uint8_t* const*>(
            object + kEngineClientOffset);
        if (!client || *reinterpret_cast<const uint8_t* const*>(client) !=
                           g_image.base + kWindowsClientVtableRva)
            return false;
        *viewportCount = *reinterpret_cast<const int32_t*>(
            client + kClientViewportsCountOffset);
        return true;
    } __except (GetExceptionCode() == EXCEPTION_ACCESS_VIOLATION
                    ? EXCEPTION_EXECUTE_HANDLER : EXCEPTION_CONTINUE_SEARCH) {
        return false;
    }
}

void __fastcall TickDetour(void* engine, void* edx, float deltaSeconds) {
    // The pair is published only after both hooks are enabled. During a failed
    // installation/rollback even a reachable detour is an inert forwarder.
    if (!g_installed.load(std::memory_order_acquire)) {
        g_originalTick(engine, edx, deltaSeconds);
        return;
    }
    int32_t viewportCount = -1;
    const bool verified = verified_viewport_count(engine, true, &viewportCount);
    if (exit_guard_contract::native_empty_exit(verified, verified, verified, viewportCount)) {
        if (g_terminalStarted.load(std::memory_order_acquire)) return;
        BVR_LOG("[b2r] exit guard: verified engine has zero viewports; "
                "advancing its native RequestExit(0,0) branch before Tick");
        // Exactly the call and return the original empty-array branch makes.
        // Neither an exception resume nor a direct write to engine flags.
        g_requestExit(0, 0);
        return;
    }
    g_originalTick(engine, edx, deltaSeconds);
}

void __cdecl RequestExitDetour(int force, int status) {
    exit_guard_contract::dispatch_request_exit(
        g_installed.load(std::memory_order_acquire), force, status,
        g_terminalStarted, g_originalRequestExit, [](int acceptedStatus) {
            BVR_LOG("[b2r] exit guard: native RequestExit accepted (force 0, status %d, "
                    "thread %lu); starting shared terminal cleanup",
                    acceptedStatus, GetCurrentThreadId());
            crash::note_teardown("engine accepted native RequestExit");
            if (!vr::shutdown_on_game_exit())
                BVR_LOG("[b2r] exit guard: native RequestExit VR shutdown INCOMPLETE; "
                        "native engine exit continues, not a verified clean shutdown");
        });
}

} // namespace

bool request_window_close() {
    if (!g_installed.load(std::memory_order_acquire) || !g_requestExit ||
        !game_exit::host_is_bioshock2()) return false;
    int32_t viewportCount = -1;
    if (!verified_viewport_count(nullptr, false, &viewportCount) || viewportCount < 0) {
        BVR_LOG("[b2r] exit guard: WM_CLOSE native route REFUSED - live engine/client "
                "identity not verified; forwarding original message");
        return false;
    }
    if (g_terminalStarted.load(std::memory_order_acquire)) return true;
    BVR_LOG("[b2r] exit guard: routing WM_CLOSE through native RequestExit(0,0) "
            "with %d viewport(s) still alive (thread %lu); window destruction deferred "
            "to engine shutdown", viewportCount, GetCurrentThreadId());
    g_requestExit(0, 0);
    return true;
}

bool install(const pattern_scan::ProcessImage& image) {
    if (g_installed.load(std::memory_order_acquire)) return true;
    if (diag::skip("exit_guard")) {
        BVR_LOG("[b2r] exit guard: skipped (BVR_SKIP=exit_guard)");
        return false;
    }
    if (!verify_image(image)) {
        BVR_LOG("[b2r] exit guard: REFUSED - Tick/empty-viewports/RequestExit "
                "signature or vtable chain differs; engine left untouched");
        return false;
    }
    g_image = image;
    g_requestExit = reinterpret_cast<RequestExitFn>(
        const_cast<uint8_t*>(image.base + patterns::kExitRequestRva));
    void* tickTarget = const_cast<uint8_t*>(image.base + patterns::kExitTickRva);
    void* requestTarget = reinterpret_cast<void*>(g_requestExit);
    MH_STATUS status = MH_CreateHook(tickTarget, reinterpret_cast<void*>(&TickDetour),
                                     reinterpret_cast<void**>(&g_originalTick));
    if (status != MH_OK) {
        BVR_LOG("[b2r] exit guard: MH_CreateHook(Tick) failed: %s", MH_StatusToString(status));
        return false;
    }
    status = MH_CreateHook(requestTarget, reinterpret_cast<void*>(&RequestExitDetour),
                           reinterpret_cast<void**>(&g_originalRequestExit));
    if (status != MH_OK) {
        BVR_LOG("[b2r] exit guard: MH_CreateHook(RequestExit) failed: %s",
                MH_StatusToString(status));
        const MH_STATUS rollback = MH_RemoveHook(tickTarget);
        if (rollback != MH_OK)
            BVR_LOG("[b2r] exit guard: Tick rollback INCOMPLETE: %s",
                    MH_StatusToString(rollback));
        return false;
    }
    // Enable the shared exit path first. Never use MH_ALL_HOOKS or a global
    // queued apply here: other modules own their hook activation independently.
    status = MH_EnableHook(requestTarget);
    if (status == MH_OK) status = MH_EnableHook(tickTarget);
    if (status != MH_OK) {
        BVR_LOG("[b2r] exit guard: hook-pair activation failed: %s; rolling back both",
                MH_StatusToString(status));
        const MH_STATUS tickRollback = MH_RemoveHook(tickTarget);
        const MH_STATUS requestRollback = MH_RemoveHook(requestTarget);
        // Keep trampoline pointers intact if MinHook cannot undo a patch:
        // unpublished detours remain passive, never calling a null original.
        if (tickRollback != MH_OK || requestRollback != MH_OK)
            BVR_LOG("[b2r] exit guard: hook-pair rollback INCOMPLETE (Tick %s, "
                    "RequestExit %s); terminal handling remains unpublished",
                    MH_StatusToString(tickRollback), MH_StatusToString(requestRollback));
        return false;
    }
    g_installed.store(true, std::memory_order_release);
    BVR_LOG("[b2r] exit guard: VERIFIED hook pair installed; native empty-viewport "
            "exit runs before unsafe Tick access; all accepted non-forced "
            "RequestExit calls share terminal cleanup");
    return true;
}

} // namespace bvr::b2r::exit_guard
