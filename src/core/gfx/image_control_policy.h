#pragma once
// Image geometry contract. HUD uses 100 px and a five-percentage-point grid.
#include <cstdint>
#include <initializer_list>

namespace bvr::image_controls {
enum class RenderMode { Normal, Dlss, Dlaa };
enum class Panel { Hidden, Resolution, Quality, Mode, Sharpness, Graphics };
enum class Probe { Off, Normal, Captures, Dlaa, Bridge };
#if defined(BVR_LATENCY_PROBE)
inline constexpr Probe kMiddleProbe = Probe::Bridge;
#else
inline constexpr Probe kMiddleProbe = Probe::Captures;
#endif
#if defined(BVR_PERFORMANCE_PROBE)
inline constexpr bool kProbeAvailable = true;
#else
inline constexpr bool kProbeAvailable = false;
#endif
struct Fraction { uint32_t numerator = 2, denominator = 3; };
inline constexpr Fraction kQualitySteps[] = {
    {7, 20}, {2, 5}, {9, 20}, {1, 2}, {11, 20}, {3, 5},
    {13, 20}, {7, 10}, {3, 4}, {4, 5}, {17, 20}, {9, 10}
};
// Recover exact old preferences when loading rounded legacy render sizes;
// opening the new menu must not silently change a user's saved ratio.
inline constexpr Fraction kLegacyQualitySteps[] = {
    {1, 3}, {2, 5}, {1, 2}, {29, 50}, {3, 5}, {2, 3}, {7, 10}, {4, 5}, {9, 10}
};
inline constexpr uint32_t kMinimum = 1024, kMaximum = 8192, kResolutionStep = 100;
struct Settings {
    RenderMode mode = RenderMode::Normal;
    uint32_t outputWidth = 0, outputHeight = 0;
    Fraction srScale{}; // A preference, not DLAA's active scale.
    uint32_t renderWidth = 0, renderHeight = 0;
    uint32_t sharpnessPercent = 0; // Optional post-DLSS nitidez; 0 preserves prior output.
    Probe probe = Probe::Off; // Session-only; never serialized into the user's INI.
};
inline uint32_t gcd(uint32_t a, uint32_t b) noexcept {
    while (b) { const uint32_t r = a % b; a = b; b = r; }
    return a;
}
inline bool valid_scale(Fraction scale) noexcept {
    return scale.numerator && scale.denominator &&
           scale.numerator < scale.denominator && scale.denominator <= 8192;
}
inline bool geometry(Settings& s) noexcept {
    if (s.outputWidth < kMinimum || s.outputHeight < kMinimum ||
        s.outputWidth > kMaximum || s.outputHeight > kMaximum ||
        (s.outputWidth & 1u) || (s.outputHeight & 1u)) return false;
    if (s.mode != RenderMode::Dlss) {
        s.renderWidth = s.outputWidth; s.renderHeight = s.outputHeight;
        return true;
    }
    if (!valid_scale(s.srScale)) return false;
    const uint32_t g = gcd(s.outputWidth, s.outputHeight);
    const uint32_t rw = s.outputWidth / g, rh = s.outputHeight / g;
    const uint64_t scaled = uint64_t(g) * s.srScale.numerator;
    const uint32_t lower = uint32_t(scaled / s.srScale.denominator) & ~1u;
    uint64_t bestError = UINT64_MAX;
    uint32_t best = 0;
    for (uint32_t m : {lower, lower + 2}) {
        const uint32_t w = rw * m, h = rh * m;
        if (w < kMinimum || h < kMinimum || w >= s.outputWidth || h >= s.outputHeight)
            continue;
        const uint64_t value = uint64_t(m) * s.srScale.denominator;
        const uint64_t error = value > scaled ? value - scaled : scaled - value;
        if (error < bestError) { best = m; bestError = error; }
    }
    if (!best) return false;
    s.renderWidth = rw * best; s.renderHeight = rh * best;
    return true;
}
inline bool same_render_path(const Settings& a, const Settings& b) noexcept {
    return a.mode == b.mode && a.probe == b.probe && a.outputWidth == b.outputWidth &&
           a.outputHeight == b.outputHeight && a.renderWidth == b.renderWidth &&
           a.renderHeight == b.renderHeight &&
           uint64_t(a.srScale.numerator) * b.srScale.denominator ==
               uint64_t(b.srScale.numerator) * a.srScale.denominator;
}
inline bool same(const Settings& a, const Settings& b) noexcept {
    return same_render_path(a, b) && a.sharpnessPercent == b.sharpnessPercent;
}
inline bool step_mode(Settings& s, int direction) noexcept {
    if (!direction) return false;
    Settings candidate = s;
    const int mode = (int(s.mode) + (direction > 0 ? 1 : 2)) % 3;
    candidate.mode = static_cast<RenderMode>(mode);
    if (!geometry(candidate)) return false; // Never change quality to force a mode.
    s = candidate;
    return true;
}
inline bool step_sharpness(Settings& s, int direction) noexcept {
    if (s.mode != RenderMode::Dlss || !direction || s.sharpnessPercent > 100) return false;
    const int next = int(s.sharpnessPercent) + (direction > 0 ? 5 : -5);
    const uint32_t clamped = uint32_t(next < 0 ? 0 : next > 100 ? 100 : next);
    if (clamped == s.sharpnessPercent) return false;
    s.sharpnessPercent = clamped;
    return true;
}
inline bool step_resolution(Settings& s, int direction) noexcept {
    if (!direction) return false;
    const uint32_t current = s.outputWidth > s.outputHeight ? s.outputWidth : s.outputHeight;
    int next = int(current) + (direction > 0 ? int(kResolutionStep) : -int(kResolutionStep));
    while (next >= int(kMinimum) && next <= int(kMaximum)) {
        Settings candidate = s;
        candidate.outputWidth = candidate.outputHeight = uint32_t(next);
        if (geometry(candidate)) { s = candidate; return true; }
        next += direction > 0 ? kResolutionStep : -int(kResolutionStep);
    }
    return false;
}
inline bool step_quality(Settings& s, int direction) noexcept {
    if (s.mode != RenderMode::Dlss || !valid_scale(s.srScale) || !direction) return false;
    const int count = int(sizeof(kQualitySteps) / sizeof(kQualitySteps[0]));
    for (int n = 0; n < count; ++n) {
        const Fraction f = kQualitySteps[direction > 0 ? n : count - 1 - n];
        const int64_t delta = int64_t(f.numerator) * s.srScale.denominator -
                              int64_t(s.srScale.numerator) * f.denominator;
        if ((direction > 0 && delta <= 0) || (direction < 0 && delta >= 0)) continue;
        Settings candidate = s; candidate.srScale = f;
        if (geometry(candidate)) { s = candidate; return true; }
    }
    return false;
}
inline Panel next_panel(Panel current, RenderMode mode) noexcept {
    if (current == Panel::Hidden) return Panel::Mode;
    if (current == Panel::Mode) return Panel::Resolution;
    if (current == Panel::Resolution)
        return mode == RenderMode::Dlss ? Panel::Quality : Panel::Graphics;
    if (current == Panel::Quality && mode == RenderMode::Dlss) return Panel::Sharpness;
    if (current == Panel::Sharpness) return Panel::Graphics;
    return Panel::Hidden;
}
inline const char* probe_name(Probe p) noexcept {
    return p == Probe::Normal ? "A NORMAL" : p == Probe::Bridge ? "B PUENTE SIN DLAA" :
           p == Probe::Captures ? "B NORMAL + CAPTURAS" :
           p == Probe::Dlaa ? "C DLAA" : "OFF";
}
inline bool next_probe(const Settings& current, const Settings& original, Settings& next) noexcept {
    if (!kProbeAvailable) return false;
    next = original;
    next.probe = current.probe == Probe::Off ? Probe::Normal :
                 current.probe == Probe::Normal ? kMiddleProbe :
                 current.probe == kMiddleProbe ? Probe::Dlaa : Probe::Off;
    if (next.probe == Probe::Off) return true; // Restore the exact session snapshot.
    next.mode = (next.probe == Probe::Dlaa || next.probe == Probe::Bridge)
                    ? RenderMode::Dlaa : RenderMode::Normal;
    // Fix ALL variants to the already-existing engine backbuffer. No SETRES,
    // no new ladder and no game INI write, even when entering from DLSS SR.
    next.outputWidth = original.renderWidth;
    next.outputHeight = original.renderHeight;
    next.sharpnessPercent = 0;
    return geometry(next);
}
} // namespace bvr::image_controls
