# Building

## Current English edition: 0.2.17

Use the single-game native MSI pipeline below. Historical EXE bundles and
per-game MSI scripts remain in the repository for provenance and regression
tests; they are not the current distribution pipeline.

Requirements:

- 64-bit Windows 10 or 11.
- Git with submodule support.
- Visual Studio 2022 Build Tools, desktop C++, MSVC x86/x64 and CMake.
- Windows .NET Framework 4.x compiler for the WinForms launchers and MSI actions.
- WiX Toolset 6.0.2 and its matching UI/DTF packages for MSI packaging.
- A locally supplied, appropriately licensed NVIDIA NGX SDK only if rebuilding
  the x64 host. The English release reuses the verified 0.2.16 host/runtime.

## Get the source

    git clone --recursive --branch codex/english-v0.2.17 https://github.com/Beren5556/BioShock-VR-DLSS-DLAA.git
    cd BioShock-VR-DLSS-DLAA

For an existing checkout:

    git submodule update --init --recursive

## Mod and launchers

    cmake --preset integration-win32
    cmake --build --preset integration --parallel 4
    powershell -NoProfile -ExecutionPolicy Bypass -File .\apps\launcher\Build-Launcher.ps1 -Game both

Outputs are under `artifacts/integration-0.2.17/`.
The game and injected DLLs are **x86**; do not build bioshockvr.dll or its
proxy as x64. The NGX host is a separate **x64** process.

The integration preset requires all four tested optimizations and excludes
performance, latency, critical-path and BS2-isolation diagnostics.
Do not use experimental presets for a release.

## Text-only verification

    node scripts/localization/english.mjs verify
    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify-Repository.ps1 -BuildLauncher
    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Integration.ps1

The localization verifier compares 241 C#/C++ and MSI source files to
`v0.2.16`. Only the exact reviewed translations, launcher version strings
and MSI language metadata are allowed. Configuration identifiers, numeric
rules, render synchronization and control flow must remain unchanged.
The translation inventory is in `localization/en-US.json`.

## English MSI: historical release-preparation recipe

The commands below record how the frozen release was prepared. Published
pins and checksums intentionally prevent replacing the delivered 0.2.17
production MSI. They are not a clean-checkout one-command packaging recipe.
For ordinary source builds, use the mod/launcher section above. A future
distribution needs its own version, reviewed pins and isolated validation;
do not remove the immutability guard to overwrite this release.

The payload source must be the verified **0.2.16** payloads and per-game
manifests, not a copy of a complete game directory.

    .\installer\single-game\Prepare-EnglishRelease.ps1 -BasePayloadRoot C:\work\verified-0.2.16\payloads

This verifies every base file against the published 0.2.16 manifest, replaces
only the English core, launchers and documentation, and checks that the
example INI keys/values are unchanged. It writes release pins and two
23-file staged payloads under `artifacts/distribution-0.2.17/payloads`.
Existing frozen staging/pins are not silently overwritten.

    .\installer\single-game\Build-Msi.ps1 -Version 0.2.17 -Bs1PayloadDirectory .\artifacts\distribution-0.2.17\payloads\bs1 -Bs1ManifestPath .\artifacts\distribution-0.2.17\payloads\manifest-bs1.json -Bs2PayloadDirectory .\artifacts\distribution-0.2.17\payloads\bs2 -Bs2ManifestPath .\artifacts\distribution-0.2.17\payloads\manifest-bs2.json -BuildToolsDirectory C:\work\msi-tools -Release

Output:
`artifacts/integration-0.2.17/msi/single-game/BioShock-1-2-VR-DLSS-DLAA-0.2.17-EN.msi`.

The MSI uses language 1033/en-US, unchanged per-game upgrade families and
unchanged payload/registration component key paths. The two versioned desktop
shortcut registrations have new identities for the new version.
Historical launcher/document filenames stay
unchanged for safe upgrades. A new ProductCode/version identifies the new
release. One installer contains both mods, selected one at a time.

    .\installer\single-game\Verify-Package.ps1 -ManifestPath .\artifacts\integration-0.2.17\msi\single-game\manifest.json -BuildToolsDirectory C:\work\msi-tools

Do not distribute an unverified rebuild. Read [testing](TESTING.md) and the
[English verification report](ENGLISH-0.2.17.md). Release checksum files make
published production versions immutable.

## Optional x64 host rebuild

Supply the licensed development files locally:

    components\dlss-host\external\ngx\
      nvsdk_ngx.h
      nvsdk_ngx_helpers.h
      nvsdk_ngx_defs_dlssd.h
      libs\nvsdk_ngx_d.lib

From a Visual Studio x64 tools environment:

    components\dlss-host\host\build-bvr-dlss45-host.bat

The script defines `BVR_DLSS45_ONLY=1`. Never commit the SDK, import
libraries, NVIDIA runtime or generated binaries. Rebuilding the host is
outside the English text-only change and requires separate validation.

## Historical pipelines

The old `installer/Build-Installer.ps1`, `installer/msi/Build-Msi.ps1`
and `installer/unified/Build-Bundle.ps1` reproduce earlier development
formats. Their old payload manifests and notes are historical evidence,
not instructions to publish an EXE instead of the current dual-game MSI.
