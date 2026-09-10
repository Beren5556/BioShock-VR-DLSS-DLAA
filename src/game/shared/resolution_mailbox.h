#pragma once
#include <cstdint>
#include <mutex>

namespace bvr::game {
enum class ResolutionStatus { Pending, Dispatched, Unavailable, Fault };
// One request, two threads. Taking keeps it Pending until complete; cancel
// cannot pretend to retract work already dispatched to the game thread.
class ResolutionMailbox {
public:
    bool offer(uint32_t width, uint32_t height) {
        if (width < 640 || height < 480 || width > 8192 || height > 8192 ||
            (width & 1) || (height & 1)) return false;
        std::lock_guard<std::mutex> lock(mutex_);
        if (status_ == ResolutionStatus::Pending) return false;
        request_ = (uint64_t(width) << 32) | height;
        status_ = ResolutionStatus::Pending;
        return true;
    }
    uint64_t take() {
        std::lock_guard<std::mutex> lock(mutex_);
        const auto value = request_; request_ = 0; return value;
    }
    bool cancel() {
        std::lock_guard<std::mutex> lock(mutex_);
        if (!request_) return false;
        request_ = 0; status_ = ResolutionStatus::Unavailable; return true;
    }
    void complete(bool dispatched) {
        std::lock_guard<std::mutex> lock(mutex_);
        if (status_ == ResolutionStatus::Pending && request_ == 0)
            status_ = dispatched ? ResolutionStatus::Dispatched : ResolutionStatus::Unavailable;
    }
    ResolutionStatus status() {
        std::lock_guard<std::mutex> lock(mutex_); return status_;
    }
private:
    std::mutex mutex_;
    uint64_t request_ = 0;
    ResolutionStatus status_ = ResolutionStatus::Unavailable;
};
}
