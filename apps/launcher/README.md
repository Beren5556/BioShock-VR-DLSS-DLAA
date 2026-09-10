# BioShock 1–2 VR DLSS/DLAA launchers

The 0.2.17 English edition builds the shared interface and Image page for both games with `Build-Launcher.ps1 -Game both`. `GameProfile` fixes each binary's paths and identity; BS2 retains Weapons and Shared.ini support. The MSI installs the launcher in the chosen game's `Build\Final` and can create a desktop shortcut. [English validation](../../docs/ENGLISH-0.2.17.md).

Legacy `Lanzador ...exe` filenames are retained for safe upgrades; displayed interfaces are English.

## Build

```powershell
.\apps\launcher\Build-Launcher.ps1 -Game both
```

Output: `artifacts\integration-0.2.17\launcher`. The .NET Framework compiler replaces final output only after success. Text is compiled as UTF-8.

## BioShock 2 configuration

- Steam AppID 409720; process Bioshock2HD; supported SHA-256: `C2A31FB67B285C136203A4A6E739545177BE5753D972E6AEA0F44C65DDF22F8C`.
- `%APPDATA%\BioshockHD\Bioshock2\Shared.ini [SharedOptions] ViewportX/ViewportY` governs rendering. The four PC keys in Bioshock2SP.ini are synchronized when present.
- The original BS2 selector offered flat 1920×1080 and squares from 1500 to 4050 in 150-pixel steps, retaining 2048, 2560, 3072 and 4096. The integrated Image page uses the shared published controls. Custom values remain supported; DLAA input equals output.
- Save and Save/launch prepare `StartupFullscreen=False` in SharedOptions and WinDrv.WindowsClient, including inherited True values when resolution was not edited. The pair is validated first; missing, duplicate or nonboolean keys block saving. Open/reload never writes.
- `%LOCALAPPDATA%\BioshockVR\bs2\vrpreset.ini`: camera, scale, independent hands, aim, turning, cinema and HUD.
- `%LOCALAPPDATA%\BioshockVR\bs2\weapons.ini`: eight weapon profiles, 16 fields each, checked against `src/game/bioshock2r/aim.cpp`.
- `%LOCALAPPDATA%\BioshockVR\bs2\dlss.ini`: Normal, native DLAA and DLSS SR; output resolution and quality derived from the render/output ratio.

Embedded defaults come from official BS2 v0.8.2 presets. Incompatible BS1 keys and the wrench gesture are not exposed.

The backend manifest must declare `[backend] game=bs2`, `adapter=bioshock2r`, `phase=DLSS45`, `eyeHosts=2` and `runtime=310.7.0`. Near-plane data stays internal; no public manual control is offered because the adapter uses captured projection.

## Writes and launch

Open/reload writes no files. Save detects external edits, verifies backups and atomically replaces files. Shared.ini and its SP mirror form one batch; vrpreset.ini and weapons.ini form another. A failed second replacement restores the first file's original bytes. Image, VR and DLSS batches save sequentially; a later failure is reported as partial saving. Unknown keys and unedited control values are preserved.

Save and launch verifies executable name/hash first. For BS2, it requests Steam AppID 409720 and waits up to 60 seconds. It closes only after a NEW exact-path process, started after the request, has a responsive window for three consecutive seconds. An old process, Steam itself or a different game path is not success. Early exit/timeout leaves a useful warning and the launcher open. Direct launch is attempted only if Windows rejects the Steam URI.

## Tests

```powershell
$exe = '.\artifacts\integration-0.2.17\launcher\Lanzador BioShock 2 VR DLSS-DLAA.exe'
$p = Start-Process -FilePath $exe -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
$p.ExitCode
# Visual QA requires a NEW, nonexistent directory:
$p = Start-Process -FilePath $exe -ArgumentList '--self-test-ui "<new test directory>"' -Wait -PassThru
$p.ExitCode
```

`--self-test` covers parsers, resolution policies, BS2 profiles, locked-second-file rollback, byte-for-byte backups, external-edit detection and launch states: old process, wrong path, no window, unresponsive window, early exit and timeout. A temporary Bioshock2HD.exe helper has a transparent responsive window. The real game is never launched.

`--self-test-ui` creates fake INIs with inherited fullscreen, briefly shows the launcher and verifies open-without-writes. It tests ordinary saving, windowed consistency, all presets, DLAA 1:1, custom resolution, SP mirroring, Normal/DLAA/DLSS, hands, weapons and unknown keys. It writes screenshots and PASS.txt or FAILURE.txt.

`--sandbox "<directory>"` supports manual inspection with isolated AppData and disables game launch completely.

Launch detection does not prove DLSS quality or VR stability. Spanish 0.2.16 was user-tested in both games. The English rebuild has automated coverage, not new headset or second-PC certification.

## Historical BS2 0.1.1 / 0.1.1.1 evidence

The standalone WinForms app targeted BioShock 2 Remastered Steam, mod 0.1.1-beta and corrected launcher 0.1.1.1. It could reside outside the game, discover Steam libraries or accept `--game "<path to Bioshock2HD.exe>"`.

The 0.1.1.1 launcher update did not regenerate the frozen 0.1.1-beta installer, which retained launcher 0.1.1.0.

Local validation on 2026-09-08 passed build, `--self-test`, `--self-test-ui` and `Verify-Repository.ps1`.
QA: `artifacts/bs2-tests/launcher-windowed-0.1.1.1-babfbb9057a34a418e2ec0071c8140b7`.
Launcher SHA-256: `06D04339030C193281C40F1672541D6F22E98A50140D1D98B6BAC363F2CEBE33`.

At that stage it was installed in Build/Final and copied to the desktop launchers folder; the shortcut targeted the updated EXE. Only that component's installed hash changed: 21/21 files verified, original backups intact. Previous EXE/manifest:
`artifacts/bs2-tests/launcher-hotfix-installed-9dba3bca9b7d42799b730c7ef3930a8f`.

All 34 protected files compared before/after (real configuration, saves, mod core, BS1 and installer) were unchanged. The game was not started. Headset quality/resolution testing remained pending: select the desired profile and save with the new launcher.
