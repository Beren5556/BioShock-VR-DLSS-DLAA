# Provenance and traceability

This document separates the upstream base, fork adaptations and components
that retain their own terms.

## English 0.2.17 base

The English branch `codex/english-v0.2.17` starts from Spanish tag
`v0.2.16`, commit `efbafc38e4f5cff28b7b960e1bf28d00c62ee758`.
The Spanish release was accepted by the user in both games and remains intact.

The English edition changes display text, documentation and release/language
metadata. It retains the original rendering behavior, per-game profiles,
NVIDIA runtime, host and proxy binaries. Its rebuilt core and launchers have
their own hashes and do not inherit an unqualified physical-headset acceptance
claim from the Spanish binaries.

Spanish 0.2.16 MSI:
`BioShock-1-2-VR-DLSS-DLAA-0.2.16.msi`,
SHA-256 `1C881F0A27416198FC25A068A79044BEED7FCC37160F38E7AFE2AE09CE9A2371`.
See [the published manifest](release/manifest-v0.2.16.json) and
[English verification](docs/ENGLISH-0.2.17.md).

## Historical 0.2.13 integration

The accepted BS1 reference was tag v0.2.11, commit
894cae888dbe74611875eb82921a6ac80f423297; HEAD 065a43e preserved its functional
code. Earlier BS2 work was preserved in local branch codex/bs2-pre-0.2.13,
commit 7b4514090d5d1f319b463f158d076f4155a5347e. Integration used a new
working copy without changing either reference. Version 0.2.12 was excluded.

That historical EXE embedded the accepted BS1 MSI without rebuilding it and
a candidate BS2 MSI. It was later superseded by the native dual-game MSI.
See [integration history and limits](docs/INTEGRATION-0.2.13.md).

## Primary acknowledgement

**Mohamad Balouza** created BioShock VR and the fundamental virtual-reality
implementation: stereoscopic rendering, 6DOF tracking, motion controllers and
BioShock integration. This fork adds DLSS/DLAA and distribution tools; it does
not claim authorship of the original mod. Our sincere thanks go to Mohamad
Balouza and VR-Stereo-Hub for maintaining and publishing that foundation.

## BioShock VR upstream

- Project: https://github.com/VR-Stereo-Hub/bioshock-trilogy-vr
- Author identified by the project: Mohamad Balouza, https://github.com/mohamad-balouza
- Base version: v0.8.2
- Base commit: 5bc599923bf73bf154cc35f7265ff2c568e82016
- License: MIT, preserved in LICENSE.

The upstream Git history is retained so every fork change can be compared
with its exact base.

## DLSS helper host

- Source project: https://github.com/jlrouzies-fr/DLSS5-Feeder
- Author: Jean-Laurent ROUZIES
- Reference commit: 927d76d30e888bce497f5c5f8d496fcb696da335
- License: MIT, preserved in components/dlss-host/LICENSE.

Only the subset adapted to BioShock VR is kept in components/dlss-host.
Historical names containing dlss5 preserve source provenance. The distributed
host is built with BVR_DLSS45_ONLY=1 and implements only DLSS 4.5 SR/DLAA.

Portions derive from [NIGos/dlss5-bridge](https://github.com/NIGos/dlss5-bridge),
copyright 2026 NIGos, MIT. Its required license copy is preserved in
components/dlss-host/external/bridge-1.0.19/LICENSE.

The unchanged x64 host SHA-256 is
`480D4A931C0CA5669B11061EFB28239BE6A041D1452BA50EACC30E0B26291453`.

## NVIDIA DLSS/NGX

- Bundled runtime: nvngx_dlss.dll 310.7.0.0.
- SHA-256: BE6E434A94CA32499515EB62CA0E6C274526055D568D0426E4C652DCDFB6EE6E.
- Applicable license: docs/licenses/NVIDIA-DLSS-LICENSE.txt.
- Required notice: This software contains source code provided by NVIDIA Corporation.

The development SDK, headers and import libraries are not committed.
The runtime is not distributed as a standalone product: it is embedded in
the application installer with its license terms.

## Fork-specific code

- Rendering adaptation, transport and temporal guides for DLSS/DLAA.
- WinForms launcher: apps/launcher.
- Current native MSI: installer/single-game, with shared actions and tests
  in installer/msi. The previous WinForms installer remains historical source
  in installer/src.
- Release-specific scripts, documentation, manifests and English localization.

## Other third parties

Inherited dependencies, revisions and licenses are listed in
THIRD_PARTY_NOTICES.md and their submodules. Neither game executables nor
BioShock game assets are included in the repository or releases.

## Public v0.2.11 artifact

- File: BioShock-VR-DLSS-DLAA-0.2.11.msi
- SHA-256: 2A5365E332DC311C0E010AD0BECEEAB34E905F66C6F40AB5CF44824EDAD9F660
- Launcher: D6340CD90684DE6733BB10B52019AC81E755C3F60A5BC442829F91A5B550BDDA
- x86 mod: DADA33F07F38B3E617B63E0A1119D290F1D69C05E4C30A9E14970169EBA92BFB
- x64 host: 480D4A931C0CA5669B11061EFB28239BE6A041D1452BA50EACC30E0B26291453
- NVIDIA runtime: 310.7.0.0, unchanged from the preceding distribution.
- Full [manifest](release/manifest-v0.2.11.json).

The earlier artifacts below are retained for traceability; their releases
are archived as drafts. Historical filenames are not translated.

## v0.2.1-beta artifact

- File: Instalador BioShock VR DLSS-DLAA Beta 0.2.1.exe
- SHA-256: 7B79BF92BDFEFFF1F857E783F8D9EDA11A62010BBA9C915C1F167BFBA07A6A69
- Embedded launcher: 298E4E7E744DBD5EC11FF7A32083B1CA5EB787C7BD8A7F23C504E3062B57D0D4
- Embedded x64 host: 480D4A931C0CA5669B11061EFB28239BE6A041D1452BA50EACC30E0B26291453

## v0.2.2-beta artifact

- File: Instalador BioShock VR DLSS-DLAA Beta 0.2.2.exe
- SHA-256: 1C35B82A417C1A8AE5F9A71C688E27221E7C6E8E9859513C6F806E58C96977A4
- Embedded launcher: A325009A20BC13680D2215C0805D0705D7D5DE811911C178364225916DC0499A
- Embedded x86 fork: 3EB2347E57669C1036A94CFC3C3D9ECEF713CD41F81776A2B50A1BD0DCA37677
- Embedded x64 host: 480D4A931C0CA5669B11061EFB28239BE6A041D1452BA50EACC30E0B26291453
- NVIDIA runtime: nvngx_dlss.dll 310.7.0.0, SHA-256 BE6E434A94CA32499515EB62CA0E6C274526055D568D0426E4C652DCDFB6EE6E

## v0.2.3-beta artifact

- Published asset: Instalador-BioShock-VR-DLSS-DLAA-Beta-0.2.3.exe
- SHA-256: 2722C00F1C354781428213CA7CE5C28FDE56D86EA13CB8A8FAF59D36FE616EE0
- Embedded launcher: 403B43DA8980622C4B85FAFDC4574F0D369C8B707489955F0C1C414E8123740A
- Embedded x86 fork: 7107B2CEBE567913888CD2FE6F58F304F435438466C81FF5E002627F0B192C78
- Embedded x64 host: 480D4A931C0CA5669B11061EFB28239BE6A041D1452BA50EACC30E0B26291453
- NVIDIA runtime: nvngx_dlss.dll 310.7.0.0, SHA-256 BE6E434A94CA32499515EB62CA0E6C274526055D568D0426E4C652DCDFB6EE6E
