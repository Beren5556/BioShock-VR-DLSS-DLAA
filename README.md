# BioShock 1–2 VR · DLSS/DLAA

English edition **0.2.17**, based on the user-validated Spanish **0.2.16** release.
One native Windows MSI contains the complete mods for **BioShock Remastered**
and **BioShock 2 Remastered**. Select one game from the first-screen dropdown;
each run installs, updates, repairs or uninstalls only that game's mod.
Run the same MSI again to manage the other game.

This edition translates the installer, launchers, in-headset menus and
documentation. It does **not** change rendering algorithms, performance
options, game profiles or DLSS/DLAA behavior. The Spanish branch and
[0.2.16 release](https://github.com/Beren5556/BioShock-VR-DLSS-DLAA/releases/tag/v0.2.16)
remain available.

> **Special thanks to [Mohamad Balouza](https://github.com/mohamad-balouza)**,
> creator of [BioShock VR](https://github.com/VR-Stereo-Hub/bioshock-trilogy-vr).
> He created the fundamental VR implementation: stereoscopic rendering, 6DOF
> tracking, motion controllers and game integration. This community fork adds
> DLSS/DLAA and distribution tools on top of that work; it does not claim
> authorship of the original mod. Please visit and support the original project.

## Download and install

Download **[BioShock-1-2-VR-DLSS-DLAA-0.2.17-EN.msi](https://github.com/Beren5556/BioShock-VR-DLSS-DLAA/releases/download/v0.2.17-en/BioShock-1-2-VR-DLSS-DLAA-0.2.17-EN.msi)**
from the [English release](https://github.com/Beren5556/BioShock-VR-DLSS-DLAA/releases/tag/v0.2.17-en).
The complete mod is included: you do not need to install the original mod first.

1. Start your chosen game from Steam once, reach the main menu, then close it.
2. With the game and launcher closed, open the MSI and select the game.
3. Confirm its `Build\Final` directory. It must contain `BioshockHD.exe`
   for BioShock or `Bioshock2HD.exe` for BioShock 2. Use **Browse** if needed.
4. Install the mod. The launcher goes in the game's directory; a desktop
   shortcut is optional. Neither the launcher nor game opens automatically.
5. Open the launcher, select NORMAL, DLAA or DLSS, then **Save and launch**.
   The launcher closes after confirming that the correct game has started
   and has a responsive window. If it cannot confirm startup, it stays open
   and explains the problem.

To install both mods, run the same MSI once per game. Game executables and
game assets are not included or replaced. Saves are not modified.

The launcher executable retains its historical filename,
`Lanzador BioShock VR DLSS-DLAA.exe` or
`Lanzador BioShock 2 VR DLSS-DLAA.exe`, to preserve existing MSI component
identities, shortcuts and upgrade compatibility. Its interface is English.
Existing backup directories and installed document filenames are likewise
retained; this is not a partial language installation. Numeric separators
follow your Windows locale.

## Image modes and controls

- **NORMAL:** native rendering without DLSS or DLAA.
- **DLAA:** temporal antialiasing at the selected output resolution, 1:1.
- **DLSS 4.5:** image reconstruction from a lower internal resolution.

**F1** opens the in-headset menu and cycles its pages. **F2** decreases/goes
back; **F3** increases/goes forward. Resolution changes in 100-pixel steps
in this menu; DLSS quality changes in 5-percentage-point steps.

On BioShock 1's **Graphics options** page, F2/F3 select a row and **F4**
changes its value. F4 does not change other pages. For BioShock 2, change
graphics effects in the launcher, save and restart the game.

Use the launcher or mod image controls for resolution. The game's own
settings menu can reset it to a non-square size. The BioShock 2 launcher
prepares windowed mode and synchronizes Shared.ini with its SP mirror.

The internal DLSS resolution is rounded to a supported even size. For
example, 2950 output at 50% uses 1476 input, avoiding NVIDIA's 1475 minimum
rather than rounding down to 1474. Unsupported requests retain the previous
setting and display the reason.

## Requirements and limits

- 64-bit Windows and compatible Steam editions of the two Remastered games.
- A headset and PCVR runtime compatible with the original BioShock VR v0.8.2.
- NVIDIA RTX GPU and compatible driver for DLSS/DLAA. NORMAL does not use NGX.
- Tested game executable SHA-256:
  - BioShock: `AEC21A0072CFDB15E4B525E2320C87256F14F16894F714272069270AD099A05B`
  - BioShock 2: `C2A31FB67B285C136203A4A6E739545177BE5753D972E6AEA0F44C65DDF22F8C`

The installer includes NVIDIA `nvngx_dlss.dll` **310.7.0.0 x64**. Other x64
versions can be used manually with a warning, at the user's own risk;
compatibility, stability and image quality are not guaranteed. Repair
restores the included version. NVIDIA's own license applies.

No DLSS 5 Neural Rendering, Frame Generation or Ray Reconstruction is
included. FXAA, the old spatial upscaler and the full game-INI editor remain
hidden in the public launcher. This fork supports BioShock 1 and 2; the
inherited Infinite development code is not a supported DLSS/DLAA package.

## Performance

All four tested optimizations are retained: overlapping DLSS eye work,
reusing unchanged depth, overlapping frame-tail work, and submitting to the
headset before desktop-mirror work. Defaults disable reflections and water
ripples; updates preserve existing preferences.

DLAA can cost more than NORMAL. DLSS reduces internal resolution but adds
processing, copies and synchronization, so an FPS gain is not guaranteed
in every scene. The games have different engine costs; equal FPS is not
promised. See [performance notes](docs/releases/v0.2.17-performance.md).

## Upgrade, repair and uninstall

Reopen the MSI, select the game and follow its normal setup process.
Updates and repairs preserve personal preferences and the first backup of
original files. Repair reinstalls the bundled mod components. Uninstall
removes owned files and restores known originals from valid backups.

Profiles are separate: `%LOCALAPPDATA%\BioshockVR` for BioShock and
`%LOCALAPPDATA%\BioshockVR\bs2` for BioShock 2. Preferences and recovery
backups are retained on uninstall. Legacy beta migration verifies its
inventory; if safe recovery cannot be established, it stops with an error.
Do not delete the old mod's backups before updating.

## Build and verification

Clone with submodules:

    git clone --recursive --branch codex/english-v0.2.17 https://github.com/Beren5556/BioShock-VR-DLSS-DLAA.git
    cd BioShock-VR-DLSS-DLAA
    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify-Repository.ps1 -BuildLauncher
    node scripts/localization/english.mjs verify
    cmake --preset integration-win32
    cmake --build --preset integration

Building the x86 mod requires Visual Studio 2022/MSVC and CMake. Use these
presets to retain all four optimizations and exclude diagnostic probes.
The separate x64 host requires a locally supplied NVIDIA NGX SDK; the
English package reuses the exact verified 0.2.16 host/runtime.

The repository contains original history, mod source, per-game adapters,
the x64 bridge, launcher, installer and tests. Game files, NVIDIA binaries,
local payloads and build artifacts are not committed. Distribution binaries
are supplied as release assets, with SHA-256 checksums.

See [building](docs/BUILDING.md), [testing](docs/TESTING.md),
[English release notes](docs/releases/v0.2.17.md) and
[localization verification](docs/ENGLISH-0.2.17.md).

## Credits and licenses

- **Mohamad Balouza / VR-Stereo-Hub:** original
  [BioShock VR v0.8.2](https://github.com/VR-Stereo-Hub/bioshock-trilogy-vr/releases/tag/v0.8.2), MIT.
- **Beren5556:** this community DLSS/DLAA fork, per-eye transport,
  launchers, installer and related documentation.
- **Jean-Laurent ROUZIES:** [DLSS5-Feeder](https://github.com/jlrouzies-fr/DLSS5-Feeder),
  the adapted x64 host's source; **NIGos:**
  [dlss5-bridge](https://github.com/NIGos/dlss5-bridge), inherited bridge code.
  Both are MIT. Historical names containing dlss5 preserve provenance;
  this host is built in DLSS-4.5-only mode.
- **NVIDIA:** DLSS/NGX under its [included license](docs/licenses/NVIDIA-DLSS-LICENSE.txt).
  This software contains source code provided by NVIDIA Corporation.

Full details: [acknowledgements](ACKNOWLEDGEMENTS.md),
[third-party notices](THIRD_PARTY_NOTICES.md) and [provenance](PROVENANCE.md).
This repository uses [MIT](LICENSE) except where components specify their
own terms; NVIDIA DLSS/NGX is not relicensed under MIT.

Unofficial project, not affiliated with or endorsed by 2K Games, Take-Two
Interactive, NVIDIA or the original mod's authors. A legitimate game copy
is required. All trademarks remain their owners' property.

## Validation scope

The Spanish 0.2.16 base was accepted by the user in both games. The English
edition is rebuilt for translated text and checked separately; that earlier
headset acceptance is not presented as a new English-build VR test.
Automated and isolated installer tests do not guarantee identical behavior
on every computer, headset, resolution or scene. Second-PC validation remains
pending. The MSI is unsigned; verify its published SHA-256 before running it.
