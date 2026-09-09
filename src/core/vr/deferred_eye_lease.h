#pragma once
#include <cstdint>

namespace bvr {
// Owns the logical lease, not a COM reference. An OpenXR image remains acquired
// across the right-eye scene, then is either completed or restored BEFORE its
// one release. Clear first so error/teardown callbacks cannot release it twice.
template<class Chain, class Image> class DeferredEyeLease {
    Chain chain_{};
    Image image_{};
    uint64_t build_ = 0;
public:
    bool held() const { return build_ != 0; }
    bool sibling(uint64_t right) const { return held() && right != 0 && right == build_+1; }
    bool arm(Chain chain, Image image, uint64_t build) {
        if (held() || !chain || !image || !build || build == UINT64_MAX) return false;
        chain_=chain; image_=image; build_=build; return true;
    }
    template<class Complete, class Restore, class Release>
    bool close(bool publish, Complete complete, Restore restore, Release release) {
        if (!held()) return false;
        const auto chain=chain_; const auto image=image_;
        chain_={}; image_={}; build_=0;
        const bool completed=complete(publish,image);
        const bool contentsReady=publish&&completed ? true : restore(image);
        const bool released=release(chain);
        return publish && completed && contentsReady && released;
    }
};
}
