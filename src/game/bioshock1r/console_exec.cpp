#include "game/bioshock1r/console_exec.h"

#include "core/util/log.h"
#include "game/bioshock1r/input_drive.h"
#include "game/bioshock1r/patterns.h"
#include "game/bioshock1r/graphics_options.h"

#include <windows.h>

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <utility>

namespace bvr::b1r::console_exec {
namespace {

// UBOOL __thiscall Exec(const wchar_t* cmd, FOutputDevice& ar), ret 8.
// Dummy-EDX __fastcall stands in for thiscall (house pattern).
using ExecFn = int(__fastcall*)(void* self, void* edx, const wchar_t* cmd,
                                void* outputDevice);

// FOutputDevice stub. The engine's Logf helper (0x6E7AC0) probes vtbl+0x10
// as a 2-arg log filter FIRST and skips formatting + Serialize entirely when
// it returns 0 - so every slot here is a callee-pop-8 no-op RETURNING 0,
// which both suppresses output and keeps direct Serialize(text, event)
// (vtbl+0x4, 2 args) balanced.
//
// A slot invoked with a DIFFERENT arg count still unbalances the stack, and no
// stub can pop a count it does not know. That used to be written off as
// "accepted, SEH-guarded dev tool" - but SEH cannot catch a silent stack
// imbalance, and this stopped being a dev tool the moment the shipping build
// started re-asserting the reticle through it every 15 s forever (session 27).
// So instead of assuming, this now MEASURES:
//   - each slot is its own thunk that records the fact it was entered, so the
//     log says whether anything beyond the expected filter slot is ever
//     reached on a real build;
//   - esp is compared across the call and REPAIRED if it moved, and the seam
//     latches off for the session on any imbalance or fault.
uint32_t g_slotsHit = 0;
uint32_t g_slotsReported = 0;
bool g_seamDisabled = false;
const char* g_seamDisabledWhy = nullptr;

template <int Slot>
int __stdcall StubSlot(const void*, int) {
    g_slotsHit |= (1u << Slot);
    return 0;
}

constexpr int kStubSlots = 24;
void* g_stubVtbl[kStubSlots];
struct Stub {
    void** vptr = nullptr;
} g_stub;

template <int... I>
void fill_slots(std::integer_sequence<int, I...>) {
    ((g_stubVtbl[I] = reinterpret_cast<void*>(&StubSlot<I>)), ...);
}

void ensure_stub() {
    if (g_stub.vptr) return;
    fill_slots(std::make_integer_sequence<int, kStubSlots>{});
    g_stub.vptr = g_stubVtbl;
}

uintptr_t g_faultAddr = 0;
uintptr_t g_faultIp = 0;

int filter_capture(EXCEPTION_POINTERS* ep) {
    g_faultIp = ep->ContextRecord->Eip;
    g_faultAddr = ep->ExceptionRecord->NumberParameters >= 2
                      ? ep->ExceptionRecord->ExceptionInformation[1]
                      : 0;
    return EXCEPTION_EXECUTE_HANDLER;
}

// Kept separate from the SEH frame so the esp reads sit either side of a plain
// call with its own frame: any imbalance inside the callee shows up here as the
// caller's esp having moved, whatever the compiler did with the argument pushes.
// Repairing esp is what turns "the process is now quietly corrupt" into "one
// command did nothing and said so".
__declspec(noinline) int call_exec(ExecFn fn, void* obj, const wchar_t* wcmd, bool* balanced, void* output = &g_stub) {
    int r = 0;
#ifdef _M_IX86
    uintptr_t before = 0, after = 0;
    __asm mov before, esp
    r = fn(obj, nullptr, wcmd, output);
    __asm mov after, esp
    *balanced = (before == after);
    if (before != after) {
        __asm mov esp, before
    }
#else
    r = fn(obj, nullptr, wcmd, output);
    *balanced = true;
#endif
    return r;
}

int seh_exec(ExecFn fn, void* obj, const wchar_t* wcmd) {
    bool balanced = true;
    int r = 0;
    __try {
        r = call_exec(fn, obj, wcmd, &balanced);
    } __except (filter_capture(GetExceptionInformation())) {
        auto* base = reinterpret_cast<uint8_t*>(GetModuleHandleW(nullptr));
        BVR_LOG("[b1r] exec fault: eip=%p (exe+0x%X) fault-addr=%p",
                reinterpret_cast<void*>(g_faultIp),
                g_faultIp - reinterpret_cast<uintptr_t>(base),
                reinterpret_cast<void*>(g_faultAddr));
        g_seamDisabled = true;
        g_seamDisabledWhy = "a previous command faulted";
        return -1;
    }
    if (!balanced) {
        BVR_LOG("[b1r] exec STACK IMBALANCE on '%ls' - esp repaired and the engine-exec seam "
                "is disabled for this session (a vtable slot took an argument count the "
                "FOutputDevice stub cannot pop)",
                wcmd);
        g_seamDisabled = true;
        g_seamDisabledWhy = "a previous command unbalanced the stack";
        return -2;
    }
    // One line the first time a new set of stub slots is reached. Expected in
    // practice is the single filter slot at vtbl+0x10 (bit 4); anything else
    // means a code path we have not accounted for is calling into the stub.
    if (g_slotsHit != g_slotsReported) {
        g_slotsReported = g_slotsHit;
        BVR_LOG("[b1r] exec: FOutputDevice stub slots reached = 0x%06X%s", g_slotsHit,
                (g_slotsHit & ~(1u << 4)) ? " - UNEXPECTED slot(s) beyond the filter" : "");
    }
    return r;
}

// SEH-safe fetch of the UGameEngine object (vtable-identity checked).
void* resolve_engine() {
    auto* base = reinterpret_cast<uint8_t*>(GetModuleHandleW(nullptr));
    if (!base) return nullptr;
    __try {
        auto* engine = *reinterpret_cast<uint8_t**>(base + patterns::kGameEnginePtrRva);
        if (!engine) return nullptr;
        if (*reinterpret_cast<uint8_t**>(engine) != base + patterns::kEngineVtableRva)
            return nullptr;
        return engine;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return nullptr;
    }
}

enum class Entry { Viewport, Client, Engine };

// Resolution has its own failure latch: trying the new, corrected FExec
// subobject must not enable or disable unrelated shipping console commands.
bool g_resolutionDisabled = false;
uintptr_t g_resolutionFaultIp = 0;
uintptr_t g_resolutionFaultAddr = 0;

int capture_resolution_fault(EXCEPTION_POINTERS* ep) {
    g_resolutionFaultIp = ep->ContextRecord->Eip;
    g_resolutionFaultAddr = ep->ExceptionRecord->NumberParameters >= 2
                               ? ep->ExceptionRecord->ExceptionInformation[1]
                               : 0;
    return EXCEPTION_EXECUTE_HANDLER;
}

bool validate_resolution_target(uint8_t* base, uint8_t* viewport) {
    __try {
        auto* primary = *reinterpret_cast<uint8_t**>(viewport);
        auto* exec = viewport + patterns::kViewportExecThisOffset;
        auto* secondary = *reinterpret_cast<uint8_t**>(exec);
        if (primary != base + patterns::kViewportVtableRva ||
            secondary != base + patterns::kViewportExecVtableRva ||
            *reinterpret_cast<uint8_t**>(secondary) != base + patterns::kViewportExecRva)
            return false;
        auto* primaryCol = *reinterpret_cast<uint32_t**>(primary - sizeof(void*));
        auto* secondaryCol = *reinterpret_cast<uint32_t**>(secondary - sizeof(void*));
        return reinterpret_cast<uint8_t*>(secondaryCol) ==
                   base + patterns::kViewportExecLocatorRva &&
               primaryCol[0] == 0 && primaryCol[1] == 0 &&
               secondaryCol[0] == 0 &&
               secondaryCol[1] == patterns::kViewportExecThisOffset &&
               secondaryCol[3] == primaryCol[3]; // same RTTI TypeDescriptor
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}

int call_resolution_guarded(ExecFn fn, void* exec, const wchar_t* command) {
    bool balanced = true;
    int result = 0;
    __try {
        result = call_exec(fn, exec, command, &balanced);
    } __except (capture_resolution_fault(GetExceptionInformation())) {
        g_resolutionDisabled = true;
        auto* base = reinterpret_cast<uint8_t*>(GetModuleHandleW(nullptr));
        BVR_LOG("[b1r] live resolution: SETRES fault exe+0x%X address=%p; "
                "live-resolution lane disabled for this session",
                static_cast<unsigned>(g_resolutionFaultIp - reinterpret_cast<uintptr_t>(base)),
                reinterpret_cast<void*>(g_resolutionFaultAddr));
        return -1;
    }
    if (!balanced) {
        g_resolutionDisabled = true;
        BVR_LOG("[b1r] live resolution: SETRES stack imbalance repaired; "
                "live-resolution lane disabled for this session");
        return -1;
    }
    return result;
}

void run(const char* args, uint32_t rva, const char* label, Entry entry) {
    if (!args || !args[0]) {
        BVR_LOG("[b1r] exec: missing command text");
        return;
    }
    if (g_seamDisabled) {
        static bool once = false;
        if (!once) {
            once = true;
            BVR_LOG("[b1r] exec: seam DISABLED for this session (%s) - '%s' and later commands "
                    "are ignored; relaunch to re-enable",
                    g_seamDisabledWhy, args);
        }
        return;
    }
    ensure_stub();

    void* target = nullptr;
    if (entry == Entry::Engine) {
        target = resolve_engine();
        if (target) // Exec lives on the FExec subobject, not the UObject base
            target = static_cast<uint8_t*>(target) + patterns::kEngineExecThisOffset;
    } else {
        void* client = nullptr;
        void* viewport = nullptr;
        if (input_drive::resolve_engine_objects(&client, &viewport))
            target = entry == Entry::Viewport ? viewport : client;
    }
    if (!target) {
        BVR_LOG("[b1r] exec: %s object not resolvable (no world yet?)", label);
        return;
    }

    wchar_t wcmd[224];
    size_t n = 0;
    for (; n < 223 && args[n]; ++n)
        wcmd[n] = static_cast<wchar_t>(static_cast<unsigned char>(args[n]));
    while (n && (wcmd[n - 1] == L'\n' || wcmd[n - 1] == L'\r' || wcmd[n - 1] == L' '))
        --n;
    wcmd[n] = 0;
    if (n == 0) {
        BVR_LOG("[b1r] exec: empty command");
        return;
    }

    auto* base = reinterpret_cast<uint8_t*>(GetModuleHandleW(nullptr));
    auto fn = reinterpret_cast<ExecFn>(base + rva);
    int r = seh_exec(fn, target, wcmd);
    if (r == -1)
        BVR_LOG("[b1r] %s exec FAULTED on '%ls' (SEH caught - engine state "
                "unknown, prefer a relaunch before trusting further commands)",
                label, wcmd);
    else if (r == -2)
        BVR_LOG("[b1r] %s exec on '%ls' left the stack unbalanced - see the line above", label,
                wcmd);
    else
        BVR_LOG("[b1r] %s exec '%ls' -> %s", label, wcmd,
                r ? "HANDLED" : "unhandled (fell off this chain link)");
}

} // namespace

namespace {
bool g_graphicsDisabled = false;
bool g_graphicsUnexpectedSlot = false;
wchar_t g_graphicsOutput[512]{};
void* g_graphicsVtbl[kStubSlots]{};
struct GraphicsOutput { void** vptr = g_graphicsVtbl; } g_graphicsDevice;
template<int Slot> int __stdcall GraphicsSlot(const void* text, int) {
    if constexpr (Slot == 4) return 1; // Enable the known Logf filter.
    if constexpr (Slot == 1) {
        if (text) wcsncpy_s(g_graphicsOutput, static_cast<const wchar_t*>(text), _TRUNCATE);
    } else g_graphicsUnexpectedSlot = true;
    return 0;
}
template<int... I> void fill_graphics(std::integer_sequence<int, I...>) {
    ((g_graphicsVtbl[I] = reinterpret_cast<void*>(&GraphicsSlot<I>)), ...);
}
bool graphics_key(const char* key) {
    if (!key) return false;
    for (const auto& option : graphics_options::kOptions)
        if (strcmp(key, option.key) == 0) return true;
    return false;
}
bool graphics_exec(const wchar_t* command) {
    if (g_graphicsDisabled || !patterns::rva_trusted()) return false;
    void* engine = resolve_engine();
    if (!engine) return false;
    if (!g_graphicsVtbl[0]) fill_graphics(std::make_integer_sequence<int, kStubSlots>{});
    g_graphicsOutput[0] = 0;
    g_graphicsUnexpectedSlot = false;
    auto* base = reinterpret_cast<uint8_t*>(GetModuleHandleW(nullptr));
    bool balanced = true;
    int result = 0;
    __try {
        result = call_exec(reinterpret_cast<ExecFn>(base + patterns::kEngineExecRva),
            static_cast<uint8_t*>(engine) + patterns::kEngineExecThisOffset,
            command, &balanced, &g_graphicsDevice);
    } __except(EXCEPTION_EXECUTE_HANDLER) {
        g_graphicsDisabled = true;
    }
    if (!balanced || g_graphicsUnexpectedSlot) g_graphicsDisabled = true;
    if (g_graphicsDisabled) BVR_LOG("[graphics-controls] console lane unavailable; use launcher after exit");
    return result != 0 && !g_graphicsDisabled;
}
}

bool read_render_option(const char* key, std::wstring& value) {
    if (!graphics_key(key)) return false;
    wchar_t command[160];
    swprintf_s(command, L"get Engine.RenderConfig %hs", key);
    if (!graphics_exec(command) || !g_graphicsOutput[0]) return false;
    value = g_graphicsOutput;
    return true;
}
bool set_render_option(const char* key, const char* value) {
    if (!graphics_key(key) || !value) return false;
    const bool fluid = strcmp(key, "FluidSurfaceDetail") == 0;
    if (strcmp(value, fluid ? "High" : "True") != 0 && strcmp(value, fluid ? "Low" : "False") != 0) return false;
    wchar_t command[160];
    swprintf_s(command, L"set Engine.RenderConfig %hs %hs", key, value);
    return graphics_exec(command);
}

void run_viewport(const char* args) {
    run(args, patterns::kViewportExecRva, "viewport", Entry::Viewport);
}

void run_client(const char* args) {
    run(args, patterns::kClientExecRva, "client", Entry::Client);
}

void run_engine(const char* args) {
    run(args, patterns::kEngineExecRva, "engine", Entry::Engine);
}

ResolutionDispatch set_viewport_resolution(uint32_t width, uint32_t height) {
    if (g_resolutionDisabled) return ResolutionDispatch::Fault;
    if (width < 320 || height < 320 || width > 8192 || height > 8192 ||
        !patterns::rva_trusted())
        return ResolutionDispatch::Unavailable;

    void* client = nullptr;
    void* viewportObject = nullptr;
    if (!input_drive::resolve_engine_objects(&client, &viewportObject))
        return ResolutionDispatch::Unavailable;
    auto* base = reinterpret_cast<uint8_t*>(GetModuleHandleW(nullptr));
    auto* viewport = static_cast<uint8_t*>(viewportObject);
    if (!base || !validate_resolution_target(base, viewport)) {
        BVR_LOG("[b1r] live resolution: viewport/FExec identity check failed");
        return ResolutionDispatch::Unavailable;
    }

    wchar_t command[64];
    swprintf_s(command, L"setres %ux%ux32w", width, height);
    ensure_stub();
    auto fn = reinterpret_cast<ExecFn>(base + patterns::kViewportExecRva);
    const int result = call_resolution_guarded(
        fn, viewport + patterns::kViewportExecThisOffset, command);
    BVR_LOG("[b1r] live resolution: %ls -> %s (thread %lu; awaiting actual DXGI size)",
            command, result < 0 ? "FAULT" : result ? "HANDLED" : "unhandled",
            GetCurrentThreadId());
    return result < 0 ? ResolutionDispatch::Fault
                     : result ? ResolutionDispatch::Dispatched : ResolutionDispatch::Unavailable;
}

} // namespace bvr::b1r::console_exec
