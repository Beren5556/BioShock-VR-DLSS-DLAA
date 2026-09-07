#include "../host/bvr-sr-quality.h"

#include <cstdio>
#include <cstring>

namespace {

using bvr_sr_quality::Candidate;

constexpr std::uint32_t kOutputWidth = 1920;
constexpr std::uint32_t kOutputHeight = 1080;

// Deliberately overlapping ranges reproduce NGX's important property: range
// containment cannot identify the intended PerfQuality value.
Candidate make_candidate(int quality, const char* name, std::uint32_t optimalWidth,
                         std::uint32_t optimalHeight) {
    Candidate value{};
    value.quality = quality;
    value.name = name;
    value.optimalWidth = optimalWidth;
    value.optimalHeight = optimalHeight;
    value.minimumWidth = 640;
    value.minimumHeight = 360;
    value.maximumWidth = kOutputWidth;
    value.maximumHeight = kOutputHeight;
    value.offered = true;
    return value;
}

bool expect(const Candidate* candidates, std::size_t count, std::uint32_t width,
            std::uint32_t height, const char* expected, const char* ratioLabel) {
    bvr_sr_quality::Selection selection{};
    if (!bvr_sr_quality::closest_optimal(candidates, count, width, height,
                                         kOutputWidth, kOutputHeight, &selection)) {
        std::printf("[FAIL] ratio %s: no preset selected\n", ratioLabel);
        return false;
    }
    const Candidate& chosen = candidates[selection.index];
    const bool inRange = bvr_sr_quality::input_in_range(chosen, width, height);
    const bool correct = std::strcmp(chosen.name, expected) == 0;
    std::printf("[%s] ratio %s (%ux%u -> %ux%u) -> %s; optimal %ux%u; range=%s\n",
                correct && inRange ? "PASS" : "FAIL", ratioLabel, width, height,
                kOutputWidth, kOutputHeight, chosen.name, chosen.optimalWidth,
                chosen.optimalHeight, inRange ? "accepted" : "rejected");
    return correct && inRange;
}

} // namespace

int main() {
    Candidate candidates[] = {
        make_candidate(4, "Ultra Quality", 1478, 831),
        make_candidate(2, "Quality", 1280, 720),
        make_candidate(1, "Balanced", 1114, 626),
        make_candidate(0, "Performance", 960, 540),
        make_candidate(3, "Ultra Performance", 640, 360),
    };
    constexpr std::size_t candidateCount = sizeof(candidates) / sizeof(candidates[0]);

    bool passed = true;
    passed &= expect(candidates, candidateCount, 960, 540, "Performance", "0.5000");
    passed &= expect(candidates, candidateCount, 1280, 720, "Quality", "0.6667");
    passed &= expect(candidates, candidateCount, 1114, 626, "Balanced", "~0.5800");
    passed &= expect(candidates, candidateCount, 640, 360, "Ultra Performance", "0.3333");

    // Selection happens first. If NGX says the selected preset's dynamic range
    // does not contain the exact input, it must be rejected instead of silently
    // falling through to a different overlapping preset.
    candidates[3].minimumWidth = 1000;
    candidates[3].minimumHeight = 600;
    bvr_sr_quality::Selection selection{};
    const bool selected = bvr_sr_quality::closest_optimal(
        candidates, candidateCount, 960, 540, kOutputWidth, kOutputHeight,
        &selection);
    const bool rejected = selected &&
        std::strcmp(candidates[selection.index].name, "Performance") == 0 &&
        !bvr_sr_quality::input_in_range(candidates[selection.index], 960, 540);
    std::printf("[%s] nearest preset is validated after selection and rejected when "
                "outside its own NGX range\n", rejected ? "PASS" : "FAIL");
    passed &= rejected;

    std::printf("SR quality selection deterministic test: %s\n",
                passed ? "PASS" : "FAIL");
    return passed ? 0 : 1;
}
