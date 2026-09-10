# BioShock VR: synthetic x86 → x64 stereo test

`bvr-stereo-client32.cpp` tests the transport required by the mod without touching BioShock:

- 32-bit D3D11 client and IPC protocol v8.
- Two x64 hosts with completely independent pipes, resources, fences and histories.
- Left/right submission order over 300 frames per eye.
- Same-frame synchronization (`FEED_BUILD_ASYNC_HOME` disabled).
- DLAA 4.5 at 640×360 or DLSS 4.5 SR at 960×540 → 1920×1080.
- Readback of every output: red eye 0, blue eye 1 to detect cross-eye contamination.
- Final checks of separate counters, fences and logs.

Preparation copies the clean package (`BioShockVR-DLSS45-Host64.exe`, official `nvngx_dlss.dll`, `dlss-capabilities.ini`) to `tests/bvr-stereo-runtime/<mode>/eye0` and `eye1`. The client rejects ReShade proxies, `nvngx_dlssnr.dll` and Neural Rendering addons. This test covers only DLSS 4.5 SR/DLAA.

From PowerShell:

```powershell
.\tests\Run-BvrStereoBridgeTest.ps1 -Mode Both -Frames 300 `
  -PackageDir .\dist\BioShockVR-DLSS45
```

The default package is `dist\BioShockVR-DLSS45`, so `-PackageDir` may be omitted. The exact packaged host/runtime/manifest is staged into isolated eye directories before each mode. `-Mode DLAA` or `-Mode SR` runs one mode. Each eye directory retains its `BioShockVR-DLSS45-eyeN.log` for audit.

## Resolution-boundary regression

The client accepts `--square-output 2950` and uses the mod's real geometry policy: Performance is 1476×1476, not 1474×1474; DLAA is 2950×2950. Pass this to the executable with an already prepared test runtime. Neither the game nor OpenXR is opened.

The CMake `dlss_runtime_test32` target uses the real `dlss45_client.cpp` implementation, not just the IPC contract. It is opt-in and requires an NVIDIA GPU and explicitly prepared test directories:

```text
dlss_runtime_test32.exe bs2 <verified host64/BioShockVR-DLSS45-Host64.exe> <test data directory>
```

Use `bs1` with the published BS1 host profile or `bs2` with its identified profile. Never use the game's personal data directory as the test directory.

It verifies DLAA/NORMAL/DLSS transitions, rejection of old size 1474 and subsequent recovery, GPU readback from both eyes and bounded host shutdown. It is not part of GPU-free automation and does not certify the game engine or headset.
