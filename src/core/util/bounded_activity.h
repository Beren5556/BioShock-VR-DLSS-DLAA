#pragma once
#include <atomic>
#include <cstdint>

namespace bvr {
// Describes an existing timeout, never adds a wait or extends an outer one.
// Render-thread scopes write; watchdog threads only read the deadline.
class BoundedActivity {
public:
    bool begin(uint64_t now, uint64_t budgetMs) noexcept {
        if (!budgetMs || now + budgetMs <= now) return false;
        uint64_t empty = 0;
        return deadline_.compare_exchange_strong(empty, now + budgetMs);
    }
    void end() noexcept { deadline_.store(0); }
    bool active(uint64_t now) const noexcept {
        const auto deadline = deadline_.load();
        return deadline && now < deadline;
    }
    class Scope {
    public:
        Scope(BoundedActivity& activity, uint64_t now, uint64_t budgetMs) noexcept
            : activity_(activity), owner_(activity.begin(now, budgetMs)) {}
        ~Scope() { if (owner_) activity_.end(); }
        Scope(const Scope&) = delete;
        Scope& operator=(const Scope&) = delete;
    private:
        BoundedActivity& activity_;
        bool owner_;
    };
private:
    std::atomic<uint64_t> deadline_{0};
};
}
