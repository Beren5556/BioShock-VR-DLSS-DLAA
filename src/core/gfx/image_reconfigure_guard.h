#pragma once
#include <atomic>
#include <cstdint>

namespace bvr::image_controls {
// One bounded window for the actual GPU retirement / engine / host rebuild.
// Both watchdogs read the same deadline. Polling or an attempted second begin
// cannot extend it; a genuine hang still becomes eligible for normal recovery.
class ReconfigureWindow {
public:
    static constexpr uint64_t MaxMs = 15000;
    bool begin(uint64_t now) noexcept {
        const uint64_t deadline = now + MaxMs;
        if (deadline <= now) return false;
        uint64_t empty = 0;
        return deadline_.compare_exchange_strong(empty, deadline);
    }
    void end() noexcept { deadline_.store(0); }
    bool active(uint64_t now) const noexcept {
        const uint64_t deadline = deadline_.load();
        return deadline && now < deadline;
    }
private:
    std::atomic<uint64_t> deadline_{0};
};
} // namespace bvr::image_controls
