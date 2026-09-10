# Historical combined native MSI validation · 2026-09-10

## Local delivery

`Desktop / Lanzadores MOD VR / BioShock-1-2-VR-DLSS-DLAA-0.2.13-CANDIDATO.msi`

- Single native MSI, no EXE wrapper or nested MSI installations.
- Size: 39,313,408 bytes.
- SHA-256: `0F344B2160043A8D13695876A81B76A7BA19174BCDD68A4809AD673364AF3C1D`.
- ProductCode: `{40AB1EBF-2130-97AE-25AA-FC9E11E3C2F9}`.
- UpgradeCode: `{18AFA4D3-F5EC-1895-5F34-7F882D40A18C}`.
- Delivered version frozen: new corrections need a new version, not different bytes with this ProductCode/version.
- Rejected earlier EXE retained but is NOT the file to use.
- Nothing published/sent to GitHub at this stage.

## Test evidence

| Test | Evidence | Result |
|---|---|---|
| Install, add other mod, repair, shortcuts, individual/full removal, injected failures | artifacts/combined-isolated/b55d20efc1ed42b38af47fbfba398367/combined-0.2.13/test-result.json | 69 checks, 11 operations, pass |
| Simulated earlier BS1 MSI + simulated BS2 Format=3 beta adoption | artifacts/combined-isolated/65c07e21093347ec9bdcd681c701c1e9/combined-0.2.13/test-result.json | 43 checks, 3 operations, pass |
| Strict beta manifest/path/backup validation and foreign-data rejection | artifacts/integration-0.2.13/migration-tests/results.txt | 29 checks, 0 failures |
| Actual final MSI extraction/SHA-256, without installation | artifacts/integration-0.2.13/msi/combined/package-verification.json | 46 files, 4 features, no nested MSI |
| MSI-requested elevation from nonelevated process | Family 2ca3242f11d645d4b3175108344d25fe, 01-install-bs1.log | MSI_LUA request/consent and MsiRunningElevated=1 |

Fixtures copy only two legitimate executables and generate fake settings, not full games; they never run them. Real payloads, executables, preferences and registrations are checked before/after: unchanged.

BS1 migration uses a **simulated isolated predecessor** to verify MajorUpgrade, shared GUIDs, original preservation and predecessor removal. This is not an upgrade of Carlos's real installation or another-PC certification. BS2 beta also uses a Format=3 inventory fixture, not its real installation.

## Rollback blockage resolved

Earlier per-user MSIs declared no elevation required. During injected full-removal failure, files recovered but Windows denied internal registry restoration (1406). New MSI retains per-user scope and lets Windows Installer request elevation: SummaryInformation's “no elevation required” bit is cleared **before hashing**. ACLs, AlwaysInstallElevated and Windows policies are unchanged.

Tests explicitly verify product/both features remain registered after rollback and subsequent normal uninstall succeeds. First validated with basic UI/consent; Carlos accepted intentional-failure dialogs. Final repetition ran silently from an elevated process to avoid more prompts.

References: [MSI privilege metadata](https://learn.microsoft.com/en-us/windows/win32/msi/word-count-summary), [Windows Installer/UAC](https://learn.microsoft.com/en-us/windows/win32/msi/using-windows-installer-with-uac).

Failed families a55300c6cb994b4d894cd10d949fdc89 and bbff2aeb646c45f089712701b5a61338 were recovered/uninstalled through Windows Installer; MsiQueryProductState=-1 verified. Logs/backups retained; no manual MSI registry deletion. First recovery JSON failed to serialize a PowerShell 7 list, fixed with ToArray(). Operation logs/independent absence checks were retained.

## Interface and limits

Visual navigation on isolated MSI: both-game selection, BS1 folder, BS2 folder and confirmation with both actions/paths. Stopped before Apply. Native planning tests also check maintenance plans. No claim that every UI combination or full-UI Apply was tested.

Each launcher installs inside its own Build/Final with independent optional desktop shortcuts. No game/launcher is started. Accepted BS1 0.2.11/candidate BS2 0.2.13 payloads were already built; this correction did not rebuild cores/launchers.

Then pending: voluntary real BS2 installation/headset test, live graphics parity and another computer. A local candidate, not definitive VR stability certification.
