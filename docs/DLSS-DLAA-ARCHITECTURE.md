# DLSS/DLAA architecture

> This document describes the original **BioShock 1** fork architecture.
> Executable names, data paths and the bioshock1r provider here are not the
> BioShock 2 contract. See [BS2-TEMPORAL.md](BS2-TEMPORAL.md) for that adapter.

Since v0.2.0-beta, the mod runs inside BioShock Remastered's x86 process
while NVIDIA NGX runs in separate x64 helpers. This avoids attempting to
load a 64-bit library into a 32-bit game.

## Per-eye flow

    BioshockHD.exe x86
      -> xinput1_3.dll loads bioshockvr.dll
      -> BioShock VR renders each eye in D3D11
      -> color, depth and motion vectors are produced
      -> dlss45_client.cpp publishes resources and IPC synchronization
      -> left-eye Host64 / right-eye Host64
      -> NVIDIA NGX DLAA or DLSS 4.5 Super Resolution
      -> bioshockvr.dll copies each result to its eye's OpenXR swapchain
      -> headset compositor

Each eye has independent processes, resources, history and synchronization.
Temporal history is never reused from the opposite eye.

## Public modes

| Mode | Input | Output | NGX host |
|---|---|---|---|
| NORMAL | Render resolution | Same resolution | No |
| DLAA | Render resolution | Same resolution, 1:1 | Yes |
| DLSS | Lower resolution | Larger output, same aspect ratio | Yes |

The public configuration is in `%LOCALAPPDATA%\BioshockVR\dlss.ini`.
The launcher validates even dimensions, limits, ratio and runtime contract
before saving. It also forces UseFxaa=0 and disables any legacy upscaler.ini.

## Components

- `src/core/gfx/dlss45_client.*`: IPC client, validation and both host lifecycles.
- `src/game/bioshock1r/temporal_guides.*`: depth and motion for temporal processing.
- `src/core/vr/openxr_runtime.*`: render/output resolution, per-eye submission and OpenXR composition.
- `components/dlss-host`: adapted x64 host built with BVR_DLSS45_ONLY.
- `apps/launcher`: safe configuration and WinForms launcher.
- `installer`: self-contained packaging, transactional installation and restoration.

## Failures and recovery

Invalid dimensions or capabilities prevent DLSS/DLAA activation. Host loss
invalidates the temporal frame rather than presenting mixed eyes or history.
Logs distinguish the game client from each eye's helper.

The installer stores a local manifest outside the game and backs up every
replaced file. Restore uses this state to return the previous contents, not
an approximation of an original installation.

## Out of scope

- DLSS 5 Neural Rendering.
- Frame Generation or Multi Frame Generation.
- Ray Reconstruction.
- FXAA as a public option.
- Spatial upscaling as a public option.

Historical host names containing dlss5 preserve upstream provenance only;
they do not describe this release's capabilities.
