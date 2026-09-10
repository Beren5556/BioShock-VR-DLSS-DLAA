# Distribution · BioShock 1–2 VR DLSS/DLAA · Spanish 0.2.16

The user confirmed BS2, then BS1 working and authorized a distribution containing both mods. Explicit GitHub publication approval followed on 2026-09-10. [Release 0.2.16](https://github.com/Beren5556/BioShock-VR-DLSS-DLAA/releases/tag/v0.2.16) uses the same frozen MSI, without regenerating installer/binaries. [Public notes](releases/v0.2.16-public.md) summarize installation/changes.

This is an English rendering of the Spanish release record. Its original downloadable MSI and checksums remain unchanged.

## Distribution artifact

- BioShock-1-2-VR-DLSS-DLAA-0.2.16.msi: one native MSI with individual dropdown.
- 23 files/game, 46 total; each run modifies only the chosen game.
- Shared core/launchers exactly as tested: internal version 0.2.13, identity `beren5556-bs12-v0.2.13-candidate2-sr-range-audit`.
- DLSS range and BS2 capability-profile fixes included.
- Updated installed guide/performance notes; licenses retained.
- Launchers in Build\Final, optional shortcuts, no automatic startup.

MSI SHA-256: `1C881F0A27416198FC25A068A79044BEED7FCC37160F38E7AFE2AE09CE9A2371`.
No Authenticode signature. Verify integrity with the supplied checksum; disabling SmartScreen/antivirus is not recommended.

Previous MSI retained. Do not repair corrected mods with 0.2.15: it would reintroduce earlier files/bugs. 0.2.16 has new product identities while retaining components and separate per-game upgrade families.

## Validation scope

Final MSI inspected without installation into everyday games: extract 46 files, check hashes, historical identities, selector, profiles, native instances, no nested installations or automatic launch.

Transactional tests use another MSI identity, same source/files. Only legitimate target-validation executables are copied, never launched; no full game assets copied. Real files/registrations compared before/after.

New upgrade test installs BS2 then BS1 with old payloads and upgrades corrected files one game at a time. Failures before/after copying verify predecessor registration/byte recovery. Preferences, first-original backup, shortcuts and other-game isolation checked.

Local reports: artifacts/single-game-isolated/ and artifacts/integration-0.2.16/msi/single-game/.
Verifiable summary: release/validation-v0.2.16.json.

Final results: 74 checks / 12 standard operations and 56 / 10 upgrading from 0.2.15, all pass; 40 migration units and historical-snapshot regression pass. All 12 core suites and 27 profile checks passed again at closure. Extraction also verifies 46 historical component identities.

Game logs confirm candidate2 and saved DLSS/DLAA changes in both titles. BS2 exits orderly. BS1 uses its inherited guard to terminate after a host fault during window destruction; this is not presented as native fault-free exit. That route was not changed after user acceptance.

### Silent-test permissions

First nonelevated lab run restored files but failed to reconstruct native registration after injected uninstall failure (1401/1406). That isolated installation was recovered/removed via MSI without changing permissions, ACLs or Windows policies. Real games unaffected.

Silent /qn tests require normal elevation under **the same account**, as earlier tests: this UI cannot request credentials mid-operation. Harness now rejects nonelevated runs before creating fixtures. Normal use opens MSI UI and accepts any standard Windows request. Package metadata permits this without changing per-user scope or enabling global elevation policies.
References: [Windows Installer/UAC](https://learn.microsoft.com/en-us/windows/win32/msi/using-windows-installer-with-uac), [elevation metadata](https://learn.microsoft.com/en-us/windows/win32/msi/word-count-summary).

## Historical build from frozen binaries

Requirements: PowerShell, .NET Framework, .NET for WiX 6.0.2, verifiable accepted 0.2.11 and original BS2 0.2.13 payloads, and tested candidate2 binaries. No game-folder payload or core/launcher rebuild during closure.

```powershell
.\installer\single-game\Prepare-Release.ps1 -Version 0.2.16 `
  -BasePayloadDirectory '<accepted 0.2.11 payload>' `
  -Bs2BasePayloadDirectory '<frozen BS2 0.2.13 payload>' `
  -Bs2BaseManifestPath '<BS2 manifest-0.2.13.json>' `
  -CandidateDirectory '<candidate2 with DLL and launchers/bs1, launchers/bs2>' `
  -ModBuildDirectory '<candidate2 core build>'

.\installer\single-game\Build-Msi.ps1 -Version 0.2.16 -Release `
  -Bs1PayloadDirectory '.\artifacts\distribution-0.2.16\payloads\bs1' `
  -Bs1ManifestPath '.\artifacts\distribution-0.2.16\payloads\manifest-bs1.json' `
  -Bs2PayloadDirectory '.\artifacts\distribution-0.2.16\payloads\bs2' `
  -Bs2ManifestPath '.\artifacts\distribution-0.2.16\payloads\manifest-bs2.json' `
  -BuildToolsDirectory '<verified WiX 6.0.2>'
```

release/validated-mods-v0.2.16.json pins accepted hashes. Preparation normalizes paths before profile selection, verifies complete bases/four optimizations and refuses staging overwrite. Final build rejects old manifests or payloads missing fixes. Extraction repeats those contracts against the MSI.

Once release/SHA256SUMS-v0.2.16.txt is frozen, distribution rebuild is blocked. **Always publish the same MSI, not another build under the same name.** -TestFamily packages use private identities and are never delivered.

## Delivery and limits

Verified installer copied to **Desktop / Lanzadores MOD VR** and retained under artifacts/distribution-0.2.16/. Notes, manifest and SHA-256 accompany local distribution.

User confirmation/technical tests are not universal guarantees. Future work: another computer and live BS2 graphics-effect changes (currently launcher save/restart). No equal-FPS promise or reopened depth-capture policy after binary acceptance.
