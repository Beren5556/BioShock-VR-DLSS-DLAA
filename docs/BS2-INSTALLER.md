# BioShock 2: historical installer contract and acceptance

## Scope

Standalone 0.1.1-beta installer: Steam AppID 409720, x86 `Bioshock2HD.exe` and compatible hash. Initial configuration: `%APPDATA%\BioshockHD\Bioshock2\Bioshock2SP.ini` and `Shared.ini`. Installation/restoration does not modify INIs.

Format 3 manifest, `GameId=bs2`, under `%LOCALAPPDATA%\BioshockVR\bs2\Installer-DLSS-DLAA`. Rejects BS1 manifests, out-of-inventory/duplicate paths and paths traversing links/junctions. Embedded hashes derive from the validated payload manifest at build time.

Version 0.1.1 accepts only BS2 0.1.0-beta/0.1.1-beta Format 3 manifests with the exact inventory. Upgrade retains the pre-first-install backup and adopts new hashes/version. Restoring an older installation does not artificially change its version when conflicts remain. Unknown/future/other-game versions are rejected.

`scripts/Verify-Repository.ps1` inspects EXE resources through read-only .NET Framework reflection without executing it. From PowerShell 7 it relaunches in Windows PowerShell 5.1, preserving optional `-BuildLauncher`. Verify a frozen candidate without that argument: rebuilding regenerates its launcher binary.

## Improvements inherited from BS1's next version

1. Installation and opening are separate results. Windows may fail to open the UI without invalidating verified files/backups. The warning retains installation success and provides the manual path.
2. Real game-process confirmation belongs to the BS2 launcher.
3. Restore reports replaced/removed file counts, removed empty owned directories, recovery paths and conflicts. Saves, INIs, VR/DLSS settings and backups remain. Later file/shortcut edits stay in place; the manifest remains active to finish restoration after conflict resolution. This is not wholesale cleanup.
4. Another-PC testing remained a pending publication requirement.

## Recovery

Original backups are validated before Restore. Each operation captures its previous state for rollback. Restoration with conflicts is not marked complete. Repair can replace changed files but first preserves identified `Conflicts-Before-Repair-*` copies; it never replaces the original initial backup. Unknown files never enter the inventory.

Injected-error tests verify exception rollback midway through install/restore. Forced system interruption may require rerunning Restore using persistent manifests/backups. Whole-filesystem power-loss atomicity is not claimed.

## Local 0.1.1 candidate result

Installer SHA-256:
`A685E0493294AD0CF0CC44286E6D5201D06CEC9462C5B1DE07C579DDE532C304`.

Passed 16 local checks, including upgrade from the actual 0.1.0 installer, restoration/rollback, conflict preservation and separation of install success from launcher-open failure.
Report: `artifacts/bs2-tests/installer-0.1.1-real-upgrade-f9043a4c174c40a5a4e6ea9b6630f490/summary.json`.

The suite never launches the game. It does not replace LAB-core exit tests or headset image validation.

[BS2-EXIT-FIX.md](BS2-EXIT-FIX.md) links production/LAB hashes and separately documents six local window/menu exits, save/reload, intermediate failed tests and observer limitations. Forced lab termination and diagnostic first-chance AVs are not presented as complete exception-free runs.

## Another PC: pending, not performed

Record Windows, Steam, .NET Framework, GPU/driver, headset and OpenXR runtime. Use this exact installer hash:

- Download/copy the final EXE and record SmartScreen/antivirus warnings.
- Open the UI; inspect characters, scaling and buttons.
- Compatible clean game: first Steam launch, installation and shortcut.
- Confirm launcher-open failure is not reported as installation failure.
- Test NORMAL then DLAA/DLSS in the headset, including loading and menus.
- Save and launch: verify it waits for the actual process and warns if Steam accepts the request but the game never appears.
- Repair, clean restoration and restoration with a subsequently modified file.
- Check untouched INIs, saves, unrelated files and any BS1 installation.

Do not publish or mark this historical checklist complete solely because local tests pass. Retain date, version/hash, results and observed problems on that PC.
