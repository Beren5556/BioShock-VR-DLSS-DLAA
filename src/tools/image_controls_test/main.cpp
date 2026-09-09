// CPU-only contract tests. No game, GPU, files or user configuration required.
#include "core/gfx/image_control_policy.h"
#include "core/gfx/image_reconfigure_guard.h"
#include <cstdio>

namespace {
using namespace bvr::image_controls;
unsigned checks = 0, failures = 0;

void expect(bool result, const char* description) {
    ++checks;
    if (!result) {
        ++failures;
        std::printf("FAIL: %s\n", description);
    }
}

Settings settings(RenderMode mode, uint32_t output, Fraction scale = {2, 3}) {
    Settings s;
    s.mode = mode;
    s.outputWidth = s.outputHeight = output;
    s.srScale = scale;
    return s;
}

bool fraction_is(Fraction actual, uint32_t numerator, uint32_t denominator) {
    return uint64_t(actual.numerator) * denominator ==
           uint64_t(numerator) * actual.denominator;
}

void geometry_and_launcher_parity() {
    // Exact expected outputs also exercised by the launcher's C# self-test.
    const uint32_t expected[] = {1434, 1638, 1844, 2048, 2252, 2458, 2662, 2868, 3072, 3276, 3482, 3686};
    const unsigned count = unsigned(sizeof(kQualitySteps) / sizeof(kQualitySteps[0]));
    expect(count == 12, "twelve HUD DLSS quality steps, 35% through 90%");
    for (unsigned i = 0; i < count; ++i) {
        Settings s = settings(RenderMode::Dlss, 4096, kQualitySteps[i]);
        expect(geometry(s) && s.renderWidth == expected[i] && s.renderHeight == expected[i],
               "4096 output rounds to the same even render extent as C#");
    }

    Settings tied = settings(RenderMode::Dlss, 4096, {2049, 4096});
    expect(geometry(tied) && tied.renderWidth == 2048, "exact half-way ties round down");
    Settings custom = settings(RenderMode::Dlss, 3840, {2800, 3840});
    expect(geometry(custom) && custom.renderWidth == 2800, "custom SR ratio survives unchanged");
    Settings rectangular = settings(RenderMode::Dlss, 4096, {1, 2});
    rectangular.outputHeight = 2304;
    expect(geometry(rectangular) && rectangular.renderWidth == 2048 &&
           rectangular.renderHeight == 1152, "valid custom rectangular aspect is preserved");

    for (RenderMode mode : {RenderMode::Normal, RenderMode::Dlaa}) {
        Settings native = settings(mode, 3584, {7, 10});
        expect(geometry(native) && native.renderWidth == 3584 && native.renderHeight == 3584 &&
               fraction_is(native.srScale, 7, 10), "native modes are 100% without erasing SR preference");
        native.mode = RenderMode::Dlss;
        expect(geometry(native) && native.renderWidth == 2508 &&
               fraction_is(native.srScale, 7, 10), "returning to DLSS restores its remembered ratio");
    }

    Settings a = settings(RenderMode::Dlss, 4096, {2, 3});
    Settings b = settings(RenderMode::Dlss, 4096, {4, 6});
    expect(geometry(a) && geometry(b) && same(a, b), "equivalent rational preferences compare equal");
    b.mode = RenderMode::Normal;
    expect(!same(a, b), "Normal remains a distinct render path even at matching dimensions");
}

void bounds_and_invalid_pairs() {
    expect(valid_scale({1, 8192}), "launcher-compatible maximum denominator accepted");
    expect(!valid_scale({1, 8193}) && !valid_scale({1, 1000000}),
           "ratios the launcher cannot reload are rejected");
    expect(!valid_scale({0, 3}) && !valid_scale({3, 3}) && !valid_scale({4, 3}) &&
           !valid_scale({1, 0}), "invalid SR fractions rejected");
    for (RenderMode mode : {RenderMode::Normal, RenderMode::Dlss, RenderMode::Dlaa}) {
        Settings small = settings(mode, 1022);
        Settings large = settings(mode, 8194);
        Settings odd = settings(mode, 3073);
        expect(!geometry(small) && !geometry(large) && !geometry(odd),
               "output bounds and even dimensions match launcher in every mode");
    }
    Settings nativeMinimum = settings(RenderMode::Normal, 1024);
    Settings nativeMaximum = settings(RenderMode::Dlaa, 8192);
    expect(geometry(nativeMinimum) && geometry(nativeMaximum), "native range endpoints remain usable");
    Settings impossible = settings(RenderMode::Dlss, 1024);
    expect(!geometry(impossible), "DLSS never silently renders below the 1024 minimum");
    Settings shortAspect = settings(RenderMode::Dlss, 1920);
    shortAspect.outputHeight = 1080;
    expect(!geometry(shortAspect), "minimum is checked on both rectangular axes");
}

void resolution_steps() {
    for (RenderMode mode : {RenderMode::Normal, RenderMode::Dlss, RenderMode::Dlaa}) {
        Settings s = settings(mode, 4096, {7, 10});
        expect(geometry(s) && step_resolution(s, -1) && s.outputWidth == 3996 &&
               s.outputHeight == 3996 && fraction_is(s.srScale, 7, 10),
               "resolution down subtracts 100 pixels and keeps SR preference");
        expect(step_resolution(s, 1) && s.outputWidth == 4096,
               "resolution up returns to the prior shared step");
    }
    Settings custom = settings(RenderMode::Normal, 4504);
    expect(step_resolution(custom, 1) && custom.outputWidth == 4604,
           "custom output increases exactly 100 pixels without rounding");
    custom = settings(RenderMode::Normal, 4504);
    expect(step_resolution(custom, -1) && custom.outputWidth == 4404,
           "custom output decreases exactly 100 pixels without rounding");
    Settings lower = settings(RenderMode::Normal, 1024);
    geometry(lower);
    Settings before = lower;
    expect(!step_resolution(lower, -1) && same(lower, before), "lower limit does not mutate settings");
    Settings upper = settings(RenderMode::Dlaa, 8192);
    geometry(upper);
    before = upper;
    expect(!step_resolution(upper, 1) && same(upper, before), "upper limit does not mutate settings");
    Settings srMinimum = settings(RenderMode::Dlss, 3072, {1, 3});
    geometry(srMinimum);
    before = srMinimum;
    expect(!step_resolution(srMinimum, -1) && same(srMinimum, before),
           "invalid smaller output is rejected without changing active DLSS");
}

void quality_steps_and_panels() {
    Settings s = settings(RenderMode::Dlss, 4096, kQualitySteps[0]);
    geometry(s);
    const unsigned count = unsigned(sizeof(kQualitySteps) / sizeof(kQualitySteps[0]));
    for (unsigned i = 1; i < count; ++i)
        expect(step_quality(s, 1) && fraction_is(s.srScale, kQualitySteps[i].numerator,
               kQualitySteps[i].denominator), "quality up visits each shared step in order");
    Settings before = s;
    expect(!step_quality(s, 1) && same(s, before), "quality stops at 90% instead of becoming DLAA");
    for (unsigned i = count - 1; i > 0; --i)
        expect(step_quality(s, -1) && fraction_is(s.srScale, kQualitySteps[i - 1].numerator,
               kQualitySteps[i - 1].denominator), "quality down visits each shared step in order");
    s = settings(RenderMode::Dlss, 4056, {2360, 4056});
    expect(step_quality(s, 1) && fraction_is(s.srScale, 3, 5), "custom quality steps up to 60%");
    s = settings(RenderMode::Dlss, 4056, {2360, 4056});
    expect(step_quality(s, -1) && fraction_is(s.srScale, 11, 20), "custom quality aligns down to 55%");
    for (unsigned i = 1; i < count; ++i) {
        const Fraction a = kQualitySteps[i - 1], b = kQualitySteps[i];
        expect((int64_t(b.numerator) * a.denominator - int64_t(a.numerator) * b.denominator) * 20 ==
               int64_t(a.denominator) * b.denominator, "every adjacent HUD quality differs by exactly five percentage points");
    }
    Settings legacy = settings(RenderMode::Dlss, 4096, {2,3}); geometry(legacy);
    expect(step_quality(legacy, -1) && fraction_is(legacy.srScale, 13,20), "legacy 66.7% aligns down to 65%");
    legacy = settings(RenderMode::Dlss, 4096, {2,3}); geometry(legacy);
    expect(step_quality(legacy, 1) && fraction_is(legacy.srScale, 7,10), "legacy 66.7% aligns up to 70%");

    for (RenderMode mode : {RenderMode::Normal, RenderMode::Dlaa}) {
        Settings native = settings(mode, 4096, {7, 10});
        geometry(native);
        before = native;
        expect(!step_quality(native, 1) && !step_quality(native, -1) && same(native, before),
               "F2/F3 quality changes do not affect native modes or stored SR preference");
        expect(next_panel(Panel::Hidden, mode) == Panel::Mode &&
               next_panel(Panel::Mode, mode) == Panel::Resolution &&
               next_panel(Panel::Resolution, mode) == Panel::Graphics &&
               next_panel(Panel::Graphics, mode) == Panel::Hidden,
               "F1 mode-resolution-graphics-hidden omits quality and sharpness in native modes");
        expect(!step_sharpness(native, 1) && same(native, before), "native modes cannot change sharpness");
    }
    expect(next_panel(Panel::Hidden, RenderMode::Dlss) == Panel::Mode &&
           next_panel(Panel::Mode, RenderMode::Dlss) == Panel::Resolution &&
           next_panel(Panel::Resolution, RenderMode::Dlss) == Panel::Quality &&
           next_panel(Panel::Quality, RenderMode::Dlss) == Panel::Sharpness &&
           next_panel(Panel::Sharpness, RenderMode::Dlss) == Panel::Graphics &&
           next_panel(Panel::Graphics, RenderMode::Dlss) == Panel::Hidden,
           "F1 cycles mode-resolution-quality-sharpness-graphics-hidden for DLSS");
    s = settings(RenderMode::Dlss, 4096, {7,10}); geometry(s); before = s;
    expect(step_sharpness(s, 1) && s.sharpnessPercent == 5 && same_render_path(s, before),
           "nitidez changes no render path, dimensions, ratio or history");
    expect(step_mode(s, 1) && s.mode == RenderMode::Dlaa && s.sharpnessPercent == 5 &&
           fraction_is(s.srScale, 7, 10), "next mode DLAA retains SR and sharpness preferences");
    expect(step_mode(s, 1) && s.mode == RenderMode::Normal, "mode wraps DLAA to NORMAL");
    expect(step_mode(s, -1) && s.mode == RenderMode::Dlaa, "mode reverse wraps NORMAL to DLAA");
    expect(step_mode(s, -1) && s.mode == RenderMode::Dlss && s.sharpnessPercent == 5 &&
           s.renderWidth == before.renderWidth, "return DLSS restores exact previous geometry and nitidez");
    s.sharpnessPercent = 100;
    expect(!step_sharpness(s, 1) && s.sharpnessPercent == 100, "nitidez upper bound does not wrap");
    s.sharpnessPercent = 0;
    expect(!step_sharpness(s, -1) && s.sharpnessPercent == 0, "nitidez zero is stable");
    s = settings(RenderMode::Normal, 1024); geometry(s); before = s;
    expect(!step_mode(s, 1) && same(s, before), "impossible DLSS mode does not silently change values");
}
} // namespace

int main() {
    Settings original = settings(RenderMode::Dlss, 3840, {2800, 3840});
    geometry(original); original.sharpnessPercent = 35;
    Settings probe = original, next;
    if (kProbeAvailable) {
        for (Probe stage : {Probe::Normal, kMiddleProbe, Probe::Dlaa}) {
            expect(next_probe(probe, original, next) && next.probe == stage,
                   "F4 advances through the three diagnostic variants");
            expect(next.renderWidth == 2800 && next.renderHeight == 2800 &&
                   next.outputWidth == 2800 && next.outputHeight == 2800 && next.sharpnessPercent == 0,
                   "probe keeps the actual engine size and no sharpness across all variants");
            expect((next.mode == RenderMode::Dlaa) ==
                       (stage == Probe::Dlaa || stage == Probe::Bridge),
                   "bridge-only uses the same native temporal path as DLAA");
            expect(!same_render_path(probe, next), "each probe variant requires its own resource path");
            probe = next;
        }
        expect(next_probe(probe, original, next) && same(next, original),
               "F4 exit restores exact custom SR ratio, geometry and sharpness");
    } else {
        expect(!next_probe(probe, original, next), "normal builds contain no usable F4 diagnostic");
    }
    ReconfigureWindow window;
    expect(!window.active(1000), "watchdog is unchanged outside a rebuild");
    expect(window.begin(1000), "explicit rebuild arms bounded watchdog window");
    expect(window.active(1300) && window.active(2200) && window.active(2631),
           "observed 1.63-second change does not trigger stereo auto-off");
    expect(window.active(3000), "window also covers the two-second GPU retirement bound");
    expect(!window.begin(3000), "polling cannot renew an existing transaction deadline");
    expect(window.active(15999) && !window.active(16000), "genuine hang is exposed at 15 seconds");
    expect(!window.begin(16001) && !window.active(16001), "expired live transaction cannot renew itself");
    window.end();
    expect(!window.active(1200), "completion or rejection clears the window immediately");
    expect(window.begin(20000) && window.active(21631), "next explicit change receives its own window");
    window.end();
    expect(!window.active(21632), "watchdog resumes immediately after the next change");
    expect(!window.begin(UINT64_MAX - 5), "overflow never creates an unbounded window");
    geometry_and_launcher_parity();
    bounds_and_invalid_pairs();
    resolution_steps();
    quality_steps_and_panels();
    std::printf("Image control policy: %u/%u checks passed\n", checks - failures, checks);
    return failures ? 1 : 0;
}
