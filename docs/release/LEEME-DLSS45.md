# DLSS 4.5 / DLAA beta 0.2.6 — historical control-wheel guide

Historical English guide. Current instructions are in the repository README; later releases changed step sizes/key direction.

Reorganized keys without changing then-working resolution/quality ranges; smaller/lower panel. Retained 0.2.5 fixes: Close ends installer, successful launcher opening exits it; failed opening stays with explanation. Stereo watchdog respects OpenXR's bounded rebuild rather than treating it as a 1.2 s hang; normal safeguards remain outside the operation/after 15 s. Candidate needed headset confirmation and did not yet replace public 0.2.3.

## Historical controls

- F1 DLSS: Mode → Resolution → DLSS Quality → DLSS Sharpness → hide.
- F1 NORMAL/DLAA: Mode → Resolution → hide.
- F2 increases, F3 decreases in this candidate; Mode cycles NORMAL/DLSS/DLAA; F6 no longer changes mode. Later releases use F2 decrease/F3 increase.
- F1 after last page hides without changing settings; next opens Mode. Hidden F2/F3 do nothing.
- Square per-eye output 1024–8192, step 256, skipping internal render below 1024.
- DLSS linear ratios: 1/3, 40%, 50%, 58%, 60%, 2/3, 70%, 80%, 90%; even rounding slightly changes effective ratio.
- NORMAL/DLAA 100%; DLSS preference retained.
- Headset panel independent of reconstructed imagery.
- Sharpness 0–100%, step 5, DLSS only; default 0 bypasses filter exactly. Post-DLSS filter, not NVIDIA's unsupported old sharpness parameter. No resolution/quality/history/NGX/OpenXR rebuild. Preference retained but inactive in NORMAL/DLAA.

Changes show applied only after actual engine/OpenXR-pair confirmation, then save for launcher reuse. Native resolution command uses windowed mode; launcher startup forcing was not yet implemented. Failure attempts previous-setting recovery; failed recovery explicitly requires restart.

## Retained corrected baseline

Two confirmed 0.1 issues corrected: normal finite D3D depth (near=0, far=1) had been incorrectly declared reversed/infinite; overlapping NGX ranges could select the wrong tier, now best matching optimal resolution is selected then range-validated (50% really Performance).

Monotonic Build/eye/camera/depth identity rejects incoherent L/R pairs and resets histories on loading, pause, capture failure or discontinuity.

## Resolution meaning

Render width/height is what BioShock draws per eye; VR output is OpenXR's target. DLAA requires equality. SR keeps output target and calculates smaller render from quality; changing quality changes render, not output. Choose output then Quality/Balanced/Performance/Ultra Performance. Fine render adjustment becomes custom.

## Architecture and files

Game/mod x86; NVIDIA NGX x64. Two independent per-eye helpers/textures/fences/histories.

```text
host64\BioShockVR-DLSS45-Host64.exe
host64\nvngx_dlss.dll
host64\dlss-capabilities.ini
```

Manifest: phase=DLSS45, eyeHosts=2, runtime=310.7.0, protocol=8. Includes tested official NVIDIA 310.7.0.0 (DLSS 4.5), no DLSS 5/Neural Rendering/RenoDX/ReShade.

Advanced alternative x64 runtime substitution is allowed with warning, no functionality/stability/quality guarantee, at user's risk. Host K/M/L profiles remain; reinstall restores 310.7.0.0.

Launcher transactionally backs up/writes %LOCALAPPDATA%\BioshockVR\dlss.ini. DLSS/DLAA and spatial filter are distinct/exclusive. Save and launch waits 30 seconds for BioshockHD.exe; closes after confirmed process or successful direct start, otherwise stays open.

## First install and restore

Run original game through Steam to main menu, close it to create %APPDATA%\BioshockHD\Bioshock\Bioshock.ini. Installer 0.2.6 blocks install until it exists.

Failed post-install automatic launcher opening does not invalidate installed files. Exact manual path/desktop shortcut provided.
Restore previous state reinstates prior files/removes empty package directories; personal LocalAppData settings/recovery backup retained.

## Known historical limits

CPU/D3D11 tests do not replace control/resolution/headset image testing. Water-related loss was not definitively attributed. Sampled GPU/per-eye wait measurements add no new waits/capture changes and make no FPS-improvement claim. NORMAL retains direct path.

- Engine dynamically chooses far 1024 or 65536 UU. Historical WORLD route provisionally uses 65536; telemetry needed to determine scene-specific 1024 needs.
- No raster jitter; NGX receives (0,0) rather than nonexistent offset. Without jitter, DLAA may not exceed NORMAL sharpness.
- Camera/depth-derived motion only; animated objects/hands lack vectors and may trail.
- If right eye fails after left NGX completes, that pair cannot be undone; both histories invalidate and the next complete pair uses spatial fallback.
- Menus/loading/cutscenes/incoherent guides use safe direct/spatial output, not stale temporal history.

Logs:

```text
%LOCALAPPDATA%\BioshockVR\bioshockvr.log
%LOCALAPPDATA%\BioshockVR\BioShockVR-DLSS45-eye0.log
%LOCALAPPDATA%\BioshockVR\BioShockVR-DLSS45-eye1.log
```
