# Changelog

This project uses MAJOR.MINOR.PATCH versions and prerelease suffixes where appropriate.

## 0.2.17 English edition · 2026-09-11

- English installer, launchers, in-game text and repository documentation on a separate branch; Spanish 0.2.16 remains available.
- Rebuilt core and launchers with English text and version metadata. Rendering, temporal optimizations, settings, ownership and recovery logic are unchanged.
- Identical verified NVIDIA runtime, host and game-specific profiles to 0.2.16.
- Cross-language MSI upgrades retain the per-game installation identities.
- Automated source-equivalence, runtime, launcher, installer, rollback and upgrade validation.

This is a language-only release, not a new rendering implementation. See the [English edition report](docs/ENGLISH-0.2.17.md).

## 0.2.16 · BioShock 1–2 · 2026-09-10

- One distributable MSI, individual game selection and two complete mods.
- Exactly the cores and launchers confirmed working by the user in both games; packaging does not rebuild them.
- Fixed 50% DLSS rounding that could fall below NVIDIA's minimum. Rejection preserves the previous setting and explains it in the overlay.
- Restored the BS2-specific DLSS profile omitted by earlier packaging.
- Retained four temporal optimizations, separate profiles, launcher improvements and Beta shortcut migration fixed in 0.2.15.
- SHA-256-pinned inventories and upgrades from 0.2.15 with genuinely different files; the other game is untouched.
- Updated installation, recovery, credits and limitations documentation.

Installer version 0.2.16 covers both games; accepted cores and launchers retain internal version 0.2.13. Publication uses the same approved MSI. Another computer and live F4 graphics changes for BS2 remain pending.
Details: [0.2.16 closure](docs/RELEASE-0.2.16.md).

## 0.2.13 · local integration · 2026-09-10

- EXE selector with two internal MSIs; BS1 0.2.11 preserved byte for byte.
- BS2 integrates temporal optimizations and the shared Image page, retaining its camera, projection, weapons, paths and exit guards.
- Coordinated Shared/SP/DLSS saving and original-backup migration from the BS2 beta.
- Isolated core, launcher, installation, rollback and repair tests.
- Pending at that stage: headset/performance, real coexistence, BS2 F4 graphics and another computer.

Not published. Details: [0.2.13 integration](docs/INTEGRATION-0.2.13.md).

## 0.2.11 · 2026-09-09

Release of the tested installer and improvements developed since 0.2.3.

- Full MSI with folder detection, upgrade/repair, optional versioned shortcut and final usage instructions.
- Fixed RBF security warnings and installed-package conflict.
- Compact launcher, simplified Image page and verified graphics options.
- Reflections and water ripples disabled by default; upgrades preserve preferences. Four performance optimizations retained.
- Headset F1 menu, F2 previous and F3 next; F4 only for graphics options. Resolution in 100-pixel steps and DLSS quality in 5-percentage-point steps.
- Second-eye recovery after automatic watchdog disable.
- NVIDIA 310.7.0.0 included; other x64 versions allowed with a warning.
- Releases 0.2.0-beta, 0.2.1-beta and 0.2.3-beta moved to drafts.

Details and limits: [public notes](docs/releases/v0.2.11-public.md).

## 0.2.3-beta · 2026-09-08

Installation, launch and restoration reliability update.

### Changed

- Installation success is distinguished from a later failure to open the launcher automatically. Manual launch instructions are shown without reporting installation failure.
- **Save and launch** keeps the launcher open until it detects `BioshockHD.exe`. If Steam does not open the game within 30 seconds, direct launch is offered; failure leaves the launcher open.
- **Restore previous state** removes empty package directories and explains that personal settings and the recovery backup remain.
- Automated tests cover directory cleanup and actual migration from 0.2.2.

### Validation pending

- Full second-PC run covering SmartScreen/antivirus, Windows dependencies, shortcut and real headset launch before stable promotion.

## 0.2.2-beta · 2026-09-07

First-run and advanced runtime compatibility update based on initial external feedback.

### Changed

- The installer requires one prior BioShock Remastered launch and checks for `Bioshock.ini`.
- The launcher closes after successfully handing launch to Steam or the direct executable.
- NVIDIA DLSS 310.7.0.0 remains included, tested and recommended.
- Other x64 `nvngx_dlss.dll` versions are no longer blocked: the launcher gives a nonblocking warning and the mod logs use at the user's own risk.

### Unchanged

- NORMAL, DLAA and DLSS 4.5, automatic K/M/L selection and per-eye stereo host.
- No DLSS 5 Neural Rendering integration.

## 0.2.1-beta · 2026-09-07

First public fork beta, using the same validated functional core as 0.2.0-beta.

### Changed

- Prominent credits and explicit thanks to Mohamad Balouza for BioShock VR and its foundational work.
- Visible original-project and v0.8.2 links on GitHub, in the installer, launcher and installed documentation.
- Documentation, security policy and metadata prepared for public release.
- Launcher and installer updated to 0.2.1 without functional mod/host payload changes.

### Validation

- Published-tree and fork-history audit.
- License, provenance, hash, build and reversible-installer checks.

## 0.2.0-beta · 2026-09-07

First tester distribution.

### Added

- Experimental DLSS 4.5 Super Resolution and DLAA for BioShock Remastered VR.
- x86/x64 transport and independent per-eye processing.
- Native Windows launcher with NORMAL, DLAA and DLSS.
- Self-contained, verified, reversible installer requiring no earlier mod installation.
- Strict compatible-executable detection and SHA-256 checking of 20 embedded resources.
- Byte-for-byte backup/restoration of replaced files.
- Source, dependency and distribution-binary traceability.

### Changed

- Installer reduced to a game path and browser.
- Installer and launcher use native Windows controls.
- Full Bioshock.ini tab removed from the interface.

### Removed from the published edition

- FXAA and experimental spatial-upscaling controls.
- Any DLSS 5 Neural Rendering option or compatibility claim.

### Validation

- 20-resource self-test.
- Clean installation, upgrade, restoration and incompatible-path rejection in isolated copies.
- Protected files in the real installation remained unchanged.
