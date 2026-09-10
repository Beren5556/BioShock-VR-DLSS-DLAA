# Experimental DLSS 4.5 — historical design

> Inherited **BioShock 1** document. Its paths, host name and manual `nearPlaneUu` setting do not describe the BioShock 2-specific implementation. BS2's per-eye WORLD contract, actual near/far capture and limits are in [BS2-TEMPORAL.md](BS2-TEMPORAL.md).

This phase integrates official NVIDIA DLSS Super Resolution runtime 310.7.0.0 (DLSS 4.5) into BioShock Remastered VR:

- `dlaa`: equal render/output resolution; DLSS used as antialiasing.
- `sr`: smaller internal rendering and larger OpenXR output with matching aspect ratio.

No DLSS 5, Neural Rendering, RenoDX or ReShade addon. The mod's spatial upscaler is a separate alternative, never labelled DLSS or DLAA.

## Architecture

BioShock Remastered/mod are x86; NVIDIA NGX is x64. The mod starts two isolated x64 helpers, one per eye. Shared textures, fences and temporal histories are independent. Frames are synchronized so a left-eye image cannot reach the right eye.

Required beside the mod:

```text
host64\BioShockVR-DLSS45-Host64.exe
host64\nvngx_dlss.dll
host64\dlss-capabilities.ini
```

Manifest requirements: `phase=DLSS45`, `eyeHosts=2`, `runtime=310.7.0`, `protocol=8`. Initially the client also required an x64 `nvngx_dlss.dll` with FileVersion `310.7.0.0`; later versions allow other x64 runtimes with a warning.

## Configuration

The launcher transactionally writes `%LOCALAPPDATA%\BioshockVR\dlss.ini`, backing it up before replacement. The distributed example is disabled by default.

DLAA `outputWidth/outputHeight` must equal the game's render resolution. SR output must be larger in both dimensions with exactly matching aspect ratio. The launcher calculates/displays the resulting quality ratio.

DLSS and spatial upscaling cannot be enabled simultaneously. In menus, cutscenes or frames without valid temporal guides, SR may use the spatial filter only as a safety output retaining swapchain dimensions; this does not make that frame DLSS.

## First-version limitations

- Motion vectors are reconstructed from camera and depth. Hands/animated objects lack their own vectors and may trail.
- Game projection has no temporal jitter. The client sends zero jitter instead of inventing a nonexistent image offset.
- FOV, near plane and reversed depth must match the real BioShock projection. `nearPlaneUu` is a conservative advanced setting in this historical BS1 design.
- Final comfort/artifact assessment requires a headset. Automated tests only validate resources, synchronization and output.

Separate host logs:
`%LOCALAPPDATA%\BioshockVR\BioShockVR-DLSS45-eye0.log` and `eye1.log`.
