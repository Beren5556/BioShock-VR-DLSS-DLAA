# BioShock 1–2 integration · historical 0.2.13

> **Historical document.** The accepted Spanish baseline is [0.2.16](RELEASE-0.2.16.md), with one MSI and both corrected/user-accepted mods. Current English delivery: [0.2.17](ENGLISH-0.2.17.md). Pending states and EXE architecture below describe earlier phases.

> **Requirement corrected 2026-09-10:** Carlos requires one native MSI. EXE + two MSIs below is history, not approved delivery. **Later correction:** multiple-selection 0.2.13 MSI was also rejected. Current architecture is `installer/single-game`: [individual dropdown 0.2.14](SINGLE-GAME-MSI-0.2.14.md).

Historical status: local candidate in development, unpublished and not installed in real games; work authorized September 10, 2026. Headset testing is separate; automation does not certify FPS/image quality. Final hashes/tests: [candidate validation](VALIDATION-0.2.13-CANDIDATE.md).

## Distribution and limits

### Delivery folder agreed with Carlos

Since September 10, 2026, **every delivered installer must also be copied to Desktop / Lanzadores MOD VR**, retaining the build artifact. Resolve the actual user desktop, verify the copy's SHA-256 and link that copy when identifying what to open. Never overwrite different bytes under the same name or remove previous versions without confirmation. Copying does not mean installing/running. Installed launchers remain in Build/Final; desktop receives shortcuts.

The historical self-contained EXE selected one game per run; rerun for the other. Both can coexist.

| Item | BioShock 1 | BioShock 2 |
| --- | --- | --- |
| Internal package | Accepted 0.2.11 MSI, not regenerated | Candidate 0.2.13 MSI |
| Steam / executable | 409710 / BioshockHD.exe | 409720 / Bioshock2HD.exe |
| Local profile | BioshockVR | BioshockVR\bs2 |
| Game resolution | Bioshock.ini, WinDrv | Shared.ini, SharedOptions; SP mirrored |
| Installed launcher | Accepted 0.2.11 | Shared 0.2.13 UI with BS2 weapons |
| In-game graphics | Existing native route | INI reading; change in launcher/restart |

ProductCode, UpgradeCode, components, shortcuts and registry identities are independent. The common EXE does not own/register both products; each MSI is maintained separately through Windows Apps.

Both launchers can build for shared-UI checks. **Newly built BS1 launcher was not distributed in this candidate**: the EXE retained full 0.2.11 MSI SHA-256
`2A5365E332DC311C0E010AD0BECEEAB34E905F66C6F40AB5CF44824EDAD9F660`.
Version 0.2.12 remained discarded.

## Implemented at that stage

- Explicit per-game camera/depth/image adapters, not copied BS1 engine addresses.
- Four BS1 optimizations enabled in BS2: eye overlap, write-invalidated depth reuse, frame-tail overlap and early headset delivery.
- Exact projection, per-eye temporal isolation, weapons and BS2 exit guards preserved. Automatic first-cutscene debug capture removed from distribution builds.
- Shared Image page: NORMAL/DLSS/DLAA, per-eye output, quality, calculated internal resolution and nine graphics options; BS2 Weapons retained.
- Headset mode/resolution requests on the correct thread; save only after confirmation. BS2 native resize still required headset testing.
- Coordinated core saving of Shared.ini/Bioshock2SP.ini/dlss.ini: prepare/verify backups before replacement, rollback ordinary failures, retain recovery bytes on incompatible external edits. No multi-file power-loss atomicity promise.
- BS2 saves windowed mode, never writes on open/reload. Launcher closes after a new exact-path process responds for three seconds, with a 60-second maximum wait. Failure leaves explanation/window open.
- BS2 Format=3 0.1.0-beta/0.1.1-beta migration verifies 21 originals. Uninstall originals predate beta, not its installed DLLs. Repair preserves the first backup. Failed migration restores beta; completed migration never later reactivates the old manifest. Beta backups remain.
- Common selector verifies two embedded MSIs, never automatically opens launcher/game or reboots Windows.

## Historical reproducible build

From this integration root, with x86 MSVC and VS 2022 CMake:

~~~powershell
cmake --preset integration-win32
cmake --build --preset integration --parallel 4 --target bioshockvr xinput_proxy
.\apps\launcher\Build-Launcher.ps1 -Game both
~~~

Reference payload is only the 23 accepted 0.2.11 files checked against release/manifest-v0.2.11.json. Never use installer/Payload, 0.2.12 or a game folder. NVIDIA 310.7.0.0/licenses retained. Verified WiX 6.0.2/source/license required.

~~~powershell
$acceptedPayload = '<verified 0.2.11 payload>'
$wixTools = '<verified WiX 6.0.2 tools>'
$acceptedMsi = '<accepted BioShock-VR-DLSS-DLAA-0.2.11.msi>'
.\installer\msi\Build-Msi.ps1 -GameId bs2 -BasePayloadDirectory $acceptedPayload -BuildToolsDirectory $wixTools
.\installer\unified\Build-Bundle.ps1 -BioShock1Msi $acceptedMsi -BioShock2Manifest '.\artifacts\integration-0.2.13\msi\bs2\manifest-0.2.13.json'
~~~

Output: artifacts/integration-0.2.13/bundle/BioShock-1-2-VR-DLSS-DLAA-0.2.13-CANDIDATO.exe, manifest.json and embedded-resource verification. Rebuilding changes candidate hashes; never reuse an installed candidate's ProductCode for a normal update. Delivered versions freeze; subsequent changes need a new version.

## Tests and evidence

- scripts/Test-Integration.ps1: all 12 executables pass. Image policy 107; controller 46; BS1 stereo recovery 40; graphics options 21; resolution mailbox 13; exit policy 23; BS2 guards 788; exit gate; BS1/BS2 WARP with 13 depth-reuse cases each; transport/overlap 53; BS2 saving 20.
- Both launcher self-tests pass; Image previews reviewed.
- installer/msi/Test-LegacyMigration.ps1: 29 unit checks.
- installer/msi/Test-Msi.ps1 -TestLegacyMigration: install, standard repair, rollback before/after removal, uninstall and exact beta recovery. Real protected hashes checked; no test product left registered.
- Selector --verify checks both resources; --window-test briefly opens then exits; --preview generates an image without installation.
- installer/unified/Test-Bundle.ps1: 24 checks for versions/payloads, correct launcher shortcuts, exact-name retirement and disjoint component GUID/ProductCode/UpgradeCode/registry identities. Read-only MSI databases, no install.

MSI suites use isolated Windows identities and fake configuration. Only the legitimate executable needed to validate the target is copied (27.7 MB BS2), never full games/assets; it is not launched. Evidence/backups remain; test products are uninstalled.

~~~powershell
.\scripts\Verify-Repository.ps1 -BuildLauncher
.\scripts\Test-Integration.ps1
.\installer\msi\Test-LegacyMigration.ps1 -BuildToolsDirectory $wixTools
# First build an isolated family:
$testFamily = [Guid]::NewGuid().ToString('N')
.\installer\msi\Build-Msi.ps1 -GameId bs2 -TestFamily $testFamily -BasePayloadDirectory $acceptedPayload -BuildToolsDirectory $wixTools
.\installer\msi\Test-Msi.ps1 -GameExeSource '<legitimate Bioshock2HD.exe>' -ManifestPath ".\artifacts\msi-isolated\$testFamily\0.2.13\manifest-0.2.13.json" -FixtureBase '<test directory>' -TestLegacyMigration -TestShortcutChoice
~~~

Test-Msi requires an isolated family and rejects distribution MSI. Test packages cannot target outside BvrMsiTest-<family> or enter the common EXE. Do not use Prepare-BS2-TestCopy for these tests: it is a historical LAB tool for another scenario and can copy game assets.

## Next steps and delivery criteria at that stage

1. Verify final package/identities against validation report.
2. Agree BS2 installation with Carlos; brief NORMAL → DLSS → DLAA → NORMAL, resolution, loading, water viewed from outside, hands/HUD and headset exit test.
3. Real coexistence/install in both orders. Sentinel/hash/identity checks are technical evidence, not a replacement for user testing.
4. Derive native BS2 graphics route if live F4 required. Explicitly pending; launcher save/restart supported.
5. Validate another computer; publish only after authorization.

No claim yet of complete parity, headset stability or improved FPS. No “fixed water shader” or demonstrated NVIDIA fault; accepted optimizations ported without reopening DLSS 5 investigation.
