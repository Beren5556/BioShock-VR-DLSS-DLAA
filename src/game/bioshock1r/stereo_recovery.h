#pragma once
#include <cstdint>

namespace bvr::b1r::scenedraw {
// Called only at top-level gameplay build boundaries, before tagging LEFT.
// Reaching the next boundary proves the previous build actually returned;
// no watchdog-thread sampling of the tiny interval outside a detour is needed.
class StereoRecoveryGate {
public:
    bool observe(uint64_t now, uint64_t presents, bool eligible) noexcept {
        if (!eligible) { reset(); return false; }
        if (!tracking_ || now < lastMs_ || now - lastMs_ > 250 || presents <= lastPresents_) {
            tracking_ = true;
            startedMs_ = lastMs_ = now;
            lastPresents_ = presents;
            progress_ = 0;
            return false;
        }
        lastMs_ = now;
        lastPresents_ = presents;
        ++progress_;
        if (progress_ >= 5 && now - startedMs_ >= 500) {
            reset();
            return true;
        }
        return false;
    }
    void reset() noexcept { tracking_ = false; progress_ = 0; }
private:
    bool tracking_ = false;
    unsigned progress_ = 0;
    uint64_t startedMs_ = 0, lastMs_ = 0, lastPresents_ = 0;
};
}
