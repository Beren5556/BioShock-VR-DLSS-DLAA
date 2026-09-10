# Testing

## Current English edition

Tests must not alter installed games or be presented as a replacement for
physical-headset validation. Use the explicit isolated fixtures below.

### Source and launchers

    node scripts/localization/english.mjs verify
    .\scripts\Verify-Repository.ps1 -BuildLauncher

The first check compares runtime/installer source to Spanish `v0.2.16`,
allowing only the reviewed literal translations, launcher version and MSI
language. The second checks version metadata, manifests, checksums, launcher
policies, forbidden files, builds both launchers and runs their self-tests.

The build retains the original game identifiers, settings, resolution
rounding, four performance options, per-eye separation and exit guards.

### Core tests

    cmake --preset integration-win32
    cmake --build --preset integration --parallel 4
    .\scripts\Test-Integration.ps1
    .\artifacts\integration-0.2.17\build\src\Release\image_hud_layout_test.exe C:\work\hud-preview.bmp

The 12 integration programs cover image policy/controller, stereo recovery,
graphics options, resolution mailbox, close policy, exit gate, BS2 exit guard,
temporal guides for both games, DLSS overlap and BS2 configuration.

The HUD test uses the production offscreen text renderer. All nine graphics
rows and the longest restart message must fit. It opens no game or XR session.

### Actual NVIDIA runtime

With games closed, provide the verified 0.2.16 payload root:

    .\scripts\localization\Test-English-Runtime.ps1 -BasePayloadRoot C:\work\verified-0.2.16\payloads

The test validates/copies only the known host, runtime and per-game
capabilities into private fixtures. It exercises the actual x86 client
against NVIDIA NGX, switches NORMAL/DLAA/DLSS, checks the rejected 1474-pixel
input and valid 1476-to-2950 SR case, reads back both eyes, then releases
resources. No game or OpenXR session is opened. This tests reconstruction
plumbing, not the appearance or performance of a real game scene.

### Launcher layout

Each game launcher supports:

    <launcher.exe> --self-test
    <launcher.exe> --preview-image C:\work\preview.png 0

The preview index is 0–6. It uses fixture configuration and renders the
actual form offscreen. Inspect all pages, labels, instructions and buttons.
Check long descriptions and scrolling, not just the first page.

`--sandbox <directory>` redirects launcher configuration and disables
game startup. Do not run an ordinary launcher against real profiles merely
to inspect translated text.

### Native MSI package checks

    .\installer\single-game\Verify-Package.ps1 -ManifestPath <manifest.json> -BuildToolsDirectory <verified-tools>

Checks include all 46 embedded file hashes, both native instance transforms,
English language, single-game selection, no nested MSI action, per-user
scope, no automatic game launch and component identities.

### Isolated MSI transactions and Spanish upgrades

Build an English test MSI with `-TestFamily <32 hex characters>`.
Build the Spanish 0.2.16 predecessor from its **unchanged** source and exact
published payload using the same isolated family. A checkout's line endings
can alter the JSON pin-file hash; `Prepare-SpanishPredecessor.ps1` creates
test input manifests bound to that checkout after verifying all 46 payload
hashes and release pins. It does not modify the Spanish source or payload.

Run from a normally elevated shell under the **same user account**:

    .\installer\single-game\Test-Msi.ps1 -ManifestPath <English-isolated-manifest> -Bs1Exe <legitimate-BS1-exe> -Bs2Exe <legitimate-BS2-exe> -FixtureBase C:\work\EnglishMsiStandard
    .\installer\single-game\Test-Msi.ps1 -ManifestPath <English-isolated-manifest> -PreviousSingleGameManifest <Spanish-isolated-manifest> -Bs1Exe <legitimate-BS1-exe> -Bs2Exe <legitimate-BS2-exe> -FixtureBase C:\work\EnglishMsiUpgrade

Only the two legitimate game executables are copied into fixtures for
compatibility checks; they are **never run or distributed**. No entire game
directory is copied. Fixtures redirect configuration, backups, shortcuts
and product identities and verify that real installations are unchanged.

The tests cover clean install, repair, preferences, independent game
management, uninstall, injected failures before/after file installation,
rollback, upgrade from a genuinely different predecessor and original-file
recovery. A successful MSI exit alone is insufficient.

Do not disable rollback, modify Config.Msi ACLs, change Windows security
policies or bypass UAC. A required elevation prompt must be handled by the
user. Keep failed fixtures for diagnosis and use MSI recovery, not ad-hoc
registry deletion. Test MSI packages never go in the public delivery folder.

### Legacy migration and payload selection

    .\installer\msi\Test-LegacyMigration.ps1 -BuildToolsDirectory <verified-tools>
    .\installer\msi\Test-PayloadProfiles.ps1 -BasePayloadDirectory <verified-0.2.11-payload>

These cover the strict legacy BS2 inventory, valid original backups,
shortcut ownership and per-game payload selection without installing into
a real game.

## Historical EXE test evidence

The earlier `installer/Test-Installer.ps1` tested the EXE installer, not
the current MSI. It used private `%TEMP%\BvrInstallerTests` directories,
retained failures, supported protected-path fingerprints, and exercised
clean installation, update, restoration, missing-path rejection and
20 embedded resource hashes.

Recorded installer SHA-256 values:

- 0.2.1-beta:
  `7B79BF92BDFEFFF1F857E783F8D9EDA11A62010BBA9C915C1F167BFBA07A6A69`.
  Clean install/restore, byte-for-byte update/restore, incompatible-path
  rejection and 13 protected real files unchanged.
- 0.2.2-beta:
  `1C35B82A417C1A8AE5F9A71C688E27221E7C6E8E9859513C6F806E58C96977A4`.
  Included real migration from 0.2.1 and restoration of 20 pre-existing files.
- 0.2.3-beta:
  `2722C00F1C354781428213CA7CE5C28FDE56D86EA13CB8A8FAF59D36FE616EE0`.
  Included migration from 0.2.2, exact-PID UI tests and removal of empty
  host64/package directories, with source game and installer files unchanged.

These are historical results, not evidence that a later build was tested.

## Manual validation and limits

Before claiming new headset validation, test startup, both eyes,
NORMAL/DLAA/DLSS changes, resolution/quality controls, launcher persistence,
loading a save, demanding water/reflection scenes and exit. A new build must
not inherit an unqualified acceptance claim from a prior binary.

The user accepted Spanish 0.2.16 in both games. Second-PC validation remains
pending, and no equality of FPS between games is claimed. See
[English evidence and remaining limits](ENGLISH-0.2.17.md).
