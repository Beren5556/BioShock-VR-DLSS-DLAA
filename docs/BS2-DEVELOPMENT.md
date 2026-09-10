# BioShock 2 DLSS/DLAA — historical 0.1.1-beta adaptation

## Base and scope

Independent working copy created from BS1 commit `1de552a` and its local 0.2.2 candidate on September 7–8, 2026. At that stage, the BioShock 1 repository/installation remained separate and this adaptation was unpublished.

The user confirmed the initial official VR mod 0.8.2 test: VirtualDesktopXR 1.0.10, Quest 3 and working stereo. This validated the VR base, not yet the new DLSS/DLAA path.

## BioShock 2 contract

| Item | Value |
|---|---|
| Steam | AppID 409720 |
| Executable | Bioshock2HD.exe, x86 |
| SHA-256 | C2A31FB67B285C136203A4A6E739545177BE5753D972E6AEA0F44C65DDF22F8C |
| Game settings | Bioshock2SP.ini and Shared.ini |
| Effective resolution | Shared.ini, SharedOptions |
| Mod/host settings | %LOCALAPPDATA%\BioshockVR\bs2 |
| Package capabilities | game=bs2, adapter=bioshock2r, IPC v8 |

The core generates BioShock 2 adapter temporal data. Camera, draw tag and projection must refer to the same eye/draw; missing or inconsistent data falls back without DLSS for that pair. Each eye retains an independent NGX host/history.

The x64 host and NVIDIA DLSS 310.7.0.0 are reused from BS1: their D3D11-resource/IPC contract is game-executable-independent. The injected DLL, launcher, settings and installer are adapted.

## Requested improvements included in scope

- Distinguish completed installation from a subsequent launcher-open failure.
- Confirm a new game process and its path before closing the launcher.
- Explain restoration/preservation, remove empty owned directories and retain later user changes.
- Prepare repeatable validation on another computer.

## Validation at that stage

[BS2-TEST-RESULTS.md](BS2-TEST-RESULTS.md) preserves first-candidate 0.1.0-beta results: all three modes worked in the isolated game, including SR 2048² → 3072²; installer/launcher suites passed.

Version 0.1.1 has its own identity for later exit changes and was retested locally: four window closes (flat/NORMAL/DLAA/SR), SR menu cancel/resume/save and DLAA menu exit without saving, reloading the new SR save. The final installer passed 16 local checks. Exact hashes, results, intermediate failures and observer revision are in [BS2-EXIT-FIX.md](BS2-EXIT-FIX.md). Game tests use a copy and instrumented LAB core, not the user's actual game.

The original AV diagnosis, also observed in NORMAL, remains in [BS2-EXIT-INVESTIGATION.md](BS2-EXIT-INVESTIGATION.md). The old guard's exit code 0 did not mean clean release. New passes require native acceptance, full XR cleanup, detach and actual code 0; handled first-chance exceptions from the diagnostic probe are not hidden.

The simulated headset checks execution, resources and composition, not user-perceived visual quality or every exit caller/engine state. A real-headset test of this historical version remained pending. Another-PC testing requires access to that computer and is pending until performed.
