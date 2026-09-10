# BioShock 1–2 installer · historical 0.2.14

> Superseded by [0.2.15](SINGLE-GAME-MSI-0.2.15.md), fixing BS2's Beta shortcut and simplifying requested text. Delivered 0.2.14 remains unmodified.

Carlos confirmed on 2026-09-10: first a dropdown choosing **one game**, then the normal wizard only for that mod, installed or not. The other game is untouched. Both can be installed by opening the same MSI twice.

## Delivery and contents

One `BioShock-1-2-VR-DLSS-DLAA-0.2.14-CANDIDATO.msi`, no EXE wrapper. **Installer** version shared by both games.

- BS1: 23 accepted 0.2.11 files; core/launcher not rebuilt.
- BS2: 23 candidate 0.2.13 files; core/launcher not rebuilt.
- Launchers in each game's Build\Final.
- Independent optional desktop shortcuts.
- No automatic launcher/game startup at completion.
- Upgrade/repair preserves preferences; uninstall preserves game, saves, preferences/recoverable backups and restores previous files.

Candidate SHA-256:
`4F08745EC9054C6CB0EEBB76435566AE3311D151D6299E7318AEB602E97CA277`.

Agreed destination: **Desktop / Lanzadores MOD VR**. No replacement/deletion of 0.2.13 MSI or old EXE without authorization. Not published to GitHub.

## MSI operation

`installer/single-game` embeds two native Windows Installer instance transforms, bs1/bs2. Each has its own product/upgrade/path/registry/backup identity. Windows shows an entry for each installed mod, both version 0.2.14.

The base product only shows the dropdown; it installs/registers nothing. Next runs a UI-only action opening the same MSI for the chosen instance and closes the selector before any transaction. No nested MSI action or both-game default install.

The chosen wizard confirms directory/shortcut. If this version is installed, continue to repair/reinstall or uninstall only that mod. Other-game features are disabled; an additional check prevents planning their files even with external ADDLOCAL=ALL.

Upgrade codes and 46 predecessor component identifiers are retained. Standalone MSI upgrade removes only that product. From combined 0.2.13, only the selected game's features are removed; the other remains registered until selected. Migrating the last removes the old product.

Old BS2 beta is recognized only when choosing BS2, preserving original-file traceability. Choosing BS1 does not adopt/reinstall BS2 beta.

References: [native instance transforms](https://learn.microsoft.com/en-us/windows/win32/msi/installing-multiple-instances-with-instance-transforms), [selective Upgrade removal](https://learn.microsoft.com/en-us/windows/win32/msi/upgrade-table).

## Message observed by Carlos

MsiInstaller at 2026-09-10 16:05:39 reported an unrecognized installer-backup file (1603). Historical BS1 Original.xml uses / in three host64 paths; 0.2.13 compared against backslash paths.

MsiStorage.cs now normalizes separators **in memory only**, retaining the exact allowlist and rejecting duplicates, absolute paths, traversal and foreign files. User originals are not rewritten. MSI identities still derive from original canonical paths.

## Validation and limits

Final extraction checks 46 SHA-256 values, two embedded instances, 46 historical component identities, per-user scope and normal Windows elevation capability. No permissions/security policies or manual Windows Installer registry changes.

Private tests use BioShockVRInstallerTests identities and BvrMsiTest-<family> directories. Only legitimate executables needed for path validation are copied: **no full-game copies or game launches**. Real files/registrations checked before/after.

Coverage: individual install/repair/shortcuts/uninstall, injected rollback, historical slash-path backups, standalone MSI + beta migration and combined-MSI separation. UI reviewed to confirmation then canceled before Apply.

Final suite 2026-09-10: **161 checks, 24 operations, all PASS**.

| Scenario | Checks | Operations |
| --- | ---: | ---: |
| Separate install/maintenance/removal | 73 | 12 |
| Split combined 0.2.13 installation | 35 | 6 |
| Previous BS1 MSI + BS2 beta, no cross-adoption | 53 | 6 |

Reports under
`artifacts/single-game-isolated/ea32089c9d36404b994d146f03bcd183/single-game-0.2.14/`:
test-result.json, test-result-combined-upgrade.json, test-result-standalone-beta.json.
Same-source isolated MSI SHA-256:
`DD644FD0CFA340DE6D88E413D4D7ABB68C8A7C6B5478EBEF0E9AAE6BD544FCC5`.
Only test identities/paths differ. Production candidate content/historical identities audited by
`artifacts/integration-0.2.14/msi/single-game/package-verification.json`.

Both-instance maintenance plans also tested. Installer.OpenProduct exposes the untransformed base; test applies its embedded transform to a private copy before running only immediate planning actions. All 24 integration operations actually use native msiexec/instances/registration.

No FPS, live-graphics parity or BS2 headset-quality certification. User installer test/another-PC validation remained pending. Delivery does not modify actual games.

## Build and verification

Build-Msi.ps1 requires -Bs1PayloadDirectory, -Bs2PayloadDirectory, -Bs2ManifestPath and -BuildToolsDirectory (WiX 6.0.2). Accepted BS1 manifest: release/manifest-v0.2.11.json. -TestFamily <32 hex> creates disjoint test identities; never distribute it.

Verify-Package.ps1 -ManifestPath ... -BuildToolsDirectory ... checks contents. Add -Bs1SourceMsi/-Bs2SourceMsi to compare production candidate component GUIDs against originals.

Test-Msi.ps1 requires isolated manifest and -Bs1Exe/-Bs2Exe. -PredecessorManifest enables upgrade suite; -FixtureBase selects a new scenario directory. Native registration recovery is tested from normally elevated, consented processes. Silent tests do not expose intentional-failure dialogs to Carlos.

After delivery, release/SHA256SUMS-vX.Y.Z.txt blocks rebuilding that version. Later changes require another version.
