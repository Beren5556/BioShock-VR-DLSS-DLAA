#pragma once

#include <cstddef>
#include <cstdint>

// Pure selection logic shared by the BioShock VR host and its deterministic
// test.  NGX deliberately gives several presets overlapping dynamic-resolution
// ranges.  The intended PerfQuality value is therefore the preset whose
// advertised optimal render extent is closest to the requested render ratio;
// range membership is a separate validity check performed afterwards.
namespace bvr_sr_quality {

struct Candidate {
    int quality = 0;
    const char* name = nullptr;
    std::uint32_t optimalWidth = 0;
    std::uint32_t optimalHeight = 0;
    std::uint32_t minimumWidth = 0;
    std::uint32_t minimumHeight = 0;
    std::uint32_t maximumWidth = 0;
    std::uint32_t maximumHeight = 0;
    bool offered = false;
};

struct Selection {
    std::size_t index = 0;
    long double squaredNormalizedError = 0.0L;
};

inline bool closest_optimal(const Candidate* candidates, std::size_t count,
                            std::uint32_t renderWidth, std::uint32_t renderHeight,
                            std::uint32_t outputWidth, std::uint32_t outputHeight,
                            Selection* result) noexcept {
    if (candidates == nullptr || count == 0 || result == nullptr ||
        renderWidth == 0 || renderHeight == 0 || outputWidth == 0 || outputHeight == 0) {
        return false;
    }

    bool found = false;
    Selection best{};
    for (std::size_t i = 0; i < count; ++i) {
        const Candidate& candidate = candidates[i];
        if (!candidate.offered || candidate.optimalWidth == 0 ||
            candidate.optimalHeight == 0) {
            continue;
        }

        const long double dx =
            (static_cast<long double>(renderWidth) - candidate.optimalWidth) /
            static_cast<long double>(outputWidth);
        const long double dy =
            (static_cast<long double>(renderHeight) - candidate.optimalHeight) /
            static_cast<long double>(outputHeight);
        const long double error = dx * dx + dy * dy;
        if (!found || error < best.squaredNormalizedError) {
            best.index = i;
            best.squaredNormalizedError = error;
            found = true;
        }
    }

    if (found) *result = best;
    return found;
}

inline bool input_in_range(const Candidate& candidate, std::uint32_t renderWidth,
                           std::uint32_t renderHeight) noexcept {
    if (!candidate.offered || candidate.minimumWidth == 0 ||
        candidate.minimumHeight == 0 || candidate.maximumWidth < candidate.minimumWidth ||
        candidate.maximumHeight < candidate.minimumHeight) {
        return false;
    }
    return renderWidth >= candidate.minimumWidth &&
           renderWidth <= candidate.maximumWidth &&
           renderHeight >= candidate.minimumHeight &&
           renderHeight <= candidate.maximumHeight;
}

} // namespace bvr_sr_quality
