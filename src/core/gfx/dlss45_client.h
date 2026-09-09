#pragma once

#include <d3d11.h>

// 32-bit D3D11 client for the out-of-process, 64-bit DLSS 4.5 host.
//
// This module contains no NGX code and has no dependency on DLSS5-Feeder. It
// speaks the small, fixed IPC v8 wire contract directly. Each eye owns a
// different host, shared texture set, fence pair and temporal counter, so left
// and right histories can never alternate through one DLSS feature.

namespace bvr::dlss45 {

enum class Mode {
    Off = 0,
    Dlaa,
    SuperResolution,
};

// Independent work after the input is copied/fenced, before waiting for output.
// Must not rebuild the bridge, reuse its inputs, or overwrite the raw backbuffer
// needed by the caller's fallback. Empty keeps the synchronous ordering.
struct BeforeResolveWork {
    void (*run)(void*) = nullptr;
    void* context = nullptr;
};

// Starts two hidden x64 helpers and builds one feature per eye.
//
// hostExePath names the packaged BioShockVR-DLSS45-Host64.exe. Its directory
// must also contain the official nvngx_dlss.dll. The two files are copied into
// isolated eye0/eye1 runtime directories under %LOCALAPPDATA%\BioshockVR;
// no ReShade proxy, nvngx_dlssnr.dll or Neural Rendering add-on is loaded.
//
// Dlaa requires output == render. SuperResolution requires a larger output
// with the same aspect ratio. depthInverted describes the hardware-depth
// texture for feature creation and must match the guide producer. Calling
// prepare again first releases the old session. A false result leaves the game
// safe to use its normal copy path.
bool prepare(ID3D11Device* device,
             UINT renderWidth, UINT renderHeight, DXGI_FORMAT backbufferFormat,
             UINT outputWidth, UINT outputHeight, Mode mode, bool depthInverted,
             const wchar_t* hostExePath, bool diagnosticTransport = false) noexcept;

// Processes one eye synchronously. eye is 0 for left and 1 for right.
//
// backbuffer, depthR32F and mvRG16F are copied into this eye's shared inputs;
// the input fence is signalled; the matching host evaluates; then a same-frame
// output-fence wait and an exact GPU CopyResource place Output(n) in dstXR.
// reset and jitter are forwarded to that eye's independent temporal history.
// The current caller sends zero jitter in both modes until a matching raster
// projection offset exists; the transport itself is deliberately mode-neutral.
//
// False means the bridge was not ready or faulted. No unsatisfied GPU wait is
// queued on failure, allowing the caller to fall back without hanging D3D11.
bool process_eye(ID3D11DeviceContext* context, int eye,
                 ID3D11Texture2D* dstXR, ID3D11Texture2D* backbuffer,
                 ID3D11Texture2D* depthR32F, ID3D11Texture2D* mvRG16F,
                 bool reset, float jitterX, float jitterY,
                 BeforeResolveWork beforeResolve = {}) noexcept;

// Split form used only by BioShock 1's paired-frame overlap. submit_eye queues
// per-eye inputs, the existing input fence and the host request. It does NOT
// wait for NGX or write dstXR. Keep that image acquired until resolve_eye or
// fallback has completed. One pending frame per eye, with no input reuse.
bool submit_eye(ID3D11DeviceContext* context, int eye,
                ID3D11Texture2D* dstXR, ID3D11Texture2D* backbuffer,
                ID3D11Texture2D* depthR32F, ID3D11Texture2D* mvRG16F,
                bool reset, float jitterX, float jitterY) noexcept;
// Retains the bounded CPU completion check and explicit GPU fence ordering.
bool resolve_eye(ID3D11DeviceContext* context, int eye, ID3D11Texture2D* dstXR) noexcept;
// AddRef'd current submitted input color, not the mutable DXGI backbuffer.
// Valid only for this pending destination; caller releases it after resolving
// or restoring the same frame, BEFORE submitting that eye's next frame.
ID3D11Texture2D* retain_submitted_color(int eye, ID3D11Texture2D* dstXR) noexcept;
// Retire submitted inputs without publishing output, on interrupted pairs.
bool discard_pending() noexcept;

// Bounded shutdown. Pipes close first so healthy hosts drain and release their
// fences; helpers that do not exit are terminated because this module created
// them. The job-object guard also prevents orphan helpers after a game crash.
void release() noexcept;

// No helper may still own GPU work when a live reconfiguration retires the
// shared textures. A successful process_eye already waited for these fences.
bool gpu_retired() noexcept;

bool ready() noexcept;
// Only true while inside a bounded IPC/fence/helper-shutdown wait. Independent
// of ready(): recovery after a failed helper must not disable engine stereo.
bool bounded_wait_active() noexcept;
const char* status() noexcept;
// No feature/eye-history rebuild. 0 keeps the exact existing CopyResource path.
bool set_sharpness(unsigned percent) noexcept;
void log_performance() noexcept;

} // namespace bvr::dlss45
