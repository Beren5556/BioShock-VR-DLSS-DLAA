#include "game/bioshock1r/performance_probe.h"
#ifdef BVR_PERFORMANCE_PROBE
#include "core/gfx/eye_timeline.h"
#include "core/util/log.h"
#include "game/bioshock1r/camera.h"
#include <algorithm>

namespace bvr::b1r::performance_probe {
namespace {
using Probe = image_controls::Probe;
EyeTimeline g_timeline;
image_controls::Settings g_settings{};
ID3D11Device* g_device = nullptr; // borrowed; shutdown precedes VR device release
uint64_t g_lastLog = 0;
struct Window {
    uint64_t samples = 0, fallback = 0, deferred = 0, firstMs = 0, lastMs = 0;
    double cpu[4]{}, gpu[4]{}, sceneMax = 0, bridgeMax = 0;
    EyeTimeline::Result first{}, last{};
} g_windows[2];
void collect() {
    g_timeline.poll([](const EyeTimeline::Result& r) {
        if (r.eye < 0 || r.eye > 1) return;
        auto& w = g_windows[r.eye];
        // CaptureResult: Failed=0, Direct=1, Dlss=2, GuidesOnly=3, PendingLeft=4.
        const int wanted = (g_settings.probe == Probe::Dlaa || g_settings.probe == Probe::Bridge) ? 2 :
                           g_settings.probe == Probe::Captures ? 3 : 1;
        const bool pendingLeft = wanted == 2 && r.eye == 0 && r.outcome == 4;
        if (r.outcome != wanted && !pendingLeft) { ++w.fallback; return; }
        if (pendingLeft) ++w.deferred;
        if (!w.samples || r.startMs < w.firstMs) { w.first = r; w.firstMs = r.startMs; }
        if (!w.samples || r.endMs >= w.lastMs) { w.last = r; w.lastMs = r.endMs; }
        ++w.samples;
        for (unsigned i=0; i<4; ++i) { w.cpu[i]+=r.cpuMs[i]; w.gpu[i]+=r.gpuMs[i]; }
        w.sceneMax = std::max(w.sceneMax, r.gpuMs[0]);
        w.bridgeMax = std::max(w.bridgeMax, r.gpuMs[3]);
    });
}
void report(bool force = false) {
    const auto now = GetTickCount64();
    if (!force && now-g_lastLog < 2000) return;
    g_lastLog = now;
    for (int eye=0; eye<2; ++eye) {
        auto& w = g_windows[eye];
        if (!w.samples && !w.fallback) continue;
        const double n = w.samples ? double(w.samples) : 1.0;
        const auto& a = w.first; const auto& z = w.last;
        BVR_LOG("[scene-probe] phase=%u eye=%d render=%ux%u output=%ux%u samples=%llu fallback=%llu "
                "fromTick=%llu toTick=%llu cpuScene=%.3f gpuScene=%.3f gpuSceneMax=%.3f "
                "cpuPreCapture=%.3f gpuPreCapture=%.3f cpuGuides=%.3f gpuGuides=%.3f "
                "cpuOutput=%.3f gpuOutput=%.3f gpuOutputMax=%.3f dropped=%llu deferred=%llu "
                "camera=%d/%d rotFrom=%d,%d,%d rotTo=%d,%d,%d posTo=%.2f,%.2f,%.2f",
                unsigned(g_settings.probe), eye, g_settings.renderWidth, g_settings.renderHeight,
                g_settings.outputWidth, g_settings.outputHeight,
                static_cast<unsigned long long>(w.samples), static_cast<unsigned long long>(w.fallback),
                static_cast<unsigned long long>(w.firstMs), static_cast<unsigned long long>(w.lastMs),
                w.cpu[0]/n, w.gpu[0]/n, w.sceneMax, w.cpu[1]/n, w.gpu[1]/n,
                w.cpu[2]/n, w.gpu[2]/n, w.cpu[3]/n, w.gpu[3]/n, w.bridgeMax,
                static_cast<unsigned long long>(g_timeline.dropped()),
                static_cast<unsigned long long>(w.deferred), a.cameraValid?1:0, z.cameraValid?1:0,
                a.rotation[0],a.rotation[1],a.rotation[2],z.rotation[0],z.rotation[1],z.rotation[2],
                z.position[0],z.position[1],z.position[2]);
        w = {};
    }
}
}
void shutdown() {
    g_timeline.cancel(); collect(); report(true); g_timeline.release();
    diagnostic_timeline_owns_queries = false;
    g_device = nullptr; g_settings = {}; g_windows[0] = {}; g_windows[1] = {};
}
void configure(ID3D11Device* device, const image_controls::Settings& settings) {
    if (g_device == device && image_controls::same_render_path(g_settings, settings)) return;
    shutdown(); g_settings = settings; g_device = device;
    if (!device || settings.probe == Probe::Off) return;
    const bool ready = g_timeline.prepare(device, 16);
    diagnostic_timeline_owns_queries = ready;
    g_lastLog = GetTickCount64();
    BVR_LOG("[scene-probe] ready=%d phase=%u stridePerEye=16 windowMs=2000 "
            "gpu=queue-elapsed-not-exclusive cpu=wall-intervals preCapture=XR-and-overlay "
            "output=interop-plus-NGX-plus-copy not-pure-NGX "
            "deferred-left=submit-only left-completion-in-right; legacyGpuSamplers=%s",
            ready?1:0, unsigned(settings.probe), ready?"suspended":"unchanged");
}
void scene_begin(int eye, uint64_t build) {
    if (!g_timeline.ready()) return;
    collect(); report(); g_timeline.begin(eye, build);
}
void scene_end() { g_timeline.mark(EyeTimeline::SceneEnd); }
void build_end(int eye, uint64_t build) {
    if (g_timeline.matches(eye, build)) g_timeline.cancel();
}
void capture_begin(int eye, uint64_t build) {
    if (!g_timeline.matches(eye, build)) { g_timeline.cancel(); return; }
    g_timeline.mark(EyeTimeline::CaptureStart);
    camera::DrivenEyeCamera pose{};
    if (camera::driven_eye_cam_for_build(eye, build, &pose))
        g_timeline.camera(pose.location, pose.rotation);
}
void guides_end() { g_timeline.mark(EyeTimeline::GuidesEnd); }
void capture_end(int outcome) { g_timeline.finish(outcome); }
}
#endif
