#pragma once

// BioShock 2's confirmed engine-exit path may run while the init thread is
// loading OpenXR, or while another thread is in Present. This terminal gate
// serializes those users before shutdown. It never participates in DllMain.
#include <windows.h>
#include <atomic>
#include <cwchar>

namespace bvr::game_exit {

class Gate {
public:
    Gate() noexcept { InitializeCriticalSection(&section_); }
    // Process-lifetime synchronization: do not enter/delete a possibly owned
    // critical section during DLL/static teardown. Test gates deliberately
    // use the same process-lifetime rule.
    ~Gate() = default;
    Gate(const Gate&) = delete;
    Gate& operator=(const Gate&) = delete;

    bool enter(bool wait) noexcept {
        if (terminal_.load(std::memory_order_acquire)) return false;
        if (wait) EnterCriticalSection(&section_);
        else if (!TryEnterCriticalSection(&section_)) return false;
        if (terminal_.load(std::memory_order_acquire)) {
            LeaveCriticalSection(&section_);
            return false;
        }
        acquired();
        return true;
    }

    bool seal_and_enter(DWORD timeoutMs) noexcept {
        terminal_.store(true, std::memory_order_release);
        // Shutdown inside one of our own callbacks would destroy the frame
        // whose stack is still active. Reentrant normal callbacks are allowed;
        // reentrant shutdown is explicitly not.
        if (owner_.load(std::memory_order_acquire) == GetCurrentThreadId())
            return false;
        const ULONGLONG start = GetTickCount64();
        do {
            if (TryEnterCriticalSection(&section_)) {
                acquired();
                return true;
            }
            if (GetTickCount64() - start >= timeoutMs) return false;
            Sleep(1);
        } while (true);
    }

    void leave() noexcept {
        if (--depth_ == 0) owner_.store(0, std::memory_order_release);
        LeaveCriticalSection(&section_);
    }

    bool terminal() const noexcept { return terminal_.load(std::memory_order_acquire); }

private:
    void acquired() noexcept {
        if (depth_++ == 0) owner_.store(GetCurrentThreadId(), std::memory_order_release);
    }
    CRITICAL_SECTION section_{};
    std::atomic<bool> terminal_{false};
    std::atomic<DWORD> owner_{0};
    unsigned depth_ = 0; // accessed only by the owner of section_
};

inline Gate& gate() noexcept {
    static Gate value;
    return value;
}

inline bool host_is_bioshock2() noexcept {
    static const bool value = [] {
        wchar_t path[MAX_PATH]{};
        const DWORD count = GetModuleFileNameW(nullptr, path, MAX_PATH);
        if (!count || count >= MAX_PATH) return false;
        const wchar_t* leaf = std::wcsrchr(path, L'\\');
        return _wcsicmp(leaf ? leaf + 1 : path, L"Bioshock2HD.exe") == 0;
    }();
    return value;
}

class Scope {
public:
    // Render callbacks never wait for runtime initialization or a different
    // render thread: a busy gate forwards the original D3D call unchanged.
    // Only the framework init thread may wait for a current callback to leave.
    explicit Scope(bool enabled, bool wait = false) noexcept
        : gate_(enabled ? &gate() : nullptr) {
        entered_ = !enabled || gate_->enter(wait);
        owns_ = enabled && entered_;
    }
    Scope(Gate& value, bool enabled, bool wait = false) noexcept : gate_(&value) {
        entered_ = !enabled || value.enter(wait);
        owns_ = enabled && entered_;
    }
    ~Scope() { if (owns_) gate_->leave(); }
    Scope(const Scope&) = delete;
    Scope& operator=(const Scope&) = delete;
    explicit operator bool() const noexcept { return entered_; }
private:
    Gate* gate_;
    bool entered_ = false;
    bool owns_ = false;
};

class ShutdownScope {
public:
    explicit ShutdownScope(DWORD timeoutMs) noexcept : ShutdownScope(gate(), timeoutMs) {}
    ShutdownScope(Gate& value, DWORD timeoutMs) noexcept
        : gate_(&value), owns_(value.seal_and_enter(timeoutMs)) {}
    ~ShutdownScope() { if (owns_) gate_->leave(); }
    ShutdownScope(const ShutdownScope&) = delete;
    ShutdownScope& operator=(const ShutdownScope&) = delete;
    explicit operator bool() const noexcept { return owns_; }
private:
    Gate* gate_;
    bool owns_;
};

} // namespace bvr::game_exit
