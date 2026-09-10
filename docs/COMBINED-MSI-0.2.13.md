# Single BioShock 1–2 MSI · historical 0.2.13

> Carlos rejected this checkbox flow after testing. Superseded by [0.2.14 individual-dropdown MSI](SINGLE-GAME-MSI-0.2.14.md). The delivered MSI remains unmodified.

## Delivery requirement

Explicit September 10, 2026 correction: **one native MSI based on BioShock 1's MSI**, containing both mods. Not an EXE opening two MSIs, nor an MSI running nested installations.

The old Desktop / Lanzadores MOD VR EXE was rejected as delivery; it was not deleted, moved or overwritten and must not be presented as an MSI. New MSI is copied there only after verification. Launchers always install in each game's Build/Final, with optional desktop shortcuts.

## Historical implementation

- One distribution ProductCode/UpgradeCode and Windows Apps entry.
- Independent Game_bs1/Game_bs2 features, each with own destination, mod record, profile, originals and shortcut.
- Native Segoe UI wizard based on BS1: game selection, folders, shortcuts, per-game repair, confirmation, progress/result. No automatic game/launcher launch.
- Reopening retains installed mods selected. Add the other; unchecking installed mod requests removal. Unchecking the last uses REMOVE=ALL.
- BS1: 23 accepted 0.2.11 files without rebuilding. Original MSI remains reference, not nested.
- BS2: 23 already-built candidate files verified against manifest. No core/launcher rebuild for this correction.
- Recoverable BS1 actions compiled once per game with separate property/action names; BS2 beta migration reused.
- Same-key-path predecessor component GUIDs retained. First combined install adopts registered standalone MSIs, explained in wizard and preventing deselection during migration, avoiding two products owning the same files when adding the second game later.

## Local candidate delivery

Initial blockage resolved. Verified/copied on 2026-09-10 to **Desktop / Lanzadores MOD VR**:

`BioShock-1-2-VR-DLSS-DLAA-0.2.13-CANDIDATO.msi`
SHA-256: `0F344B2160043A8D13695876A81B76A7BA19174BCDD68A4809AD673364AF3C1D`.

Frozen: do not rebuild/replace different bytes with the same ProductCode/version.

- Full suite: **69 checks / 11 operations**, pass.
- Isolated simulated BS1 predecessor + BS2 beta migration: **43 / 3**, pass.
- Beta migration validation: **29 checks / 0 failures**.
- Final MSI extraction: **46 files identical to verified payloads**, four native features, no nested MSI.
- Visual navigation to both-game confirmation, without Apply.
- Two leftover test registrations recovered/uninstalled through Windows Installer; logs/backups retained.

MSI remains per-user but permits Windows Installer elevation requests, enabling native registration recovery after injected uninstall failure. No policies/ACLs changed.
Evidence/identities/limits: [final MSI validation](COMBINED-MSI-VALIDATION-2026-09-10.md).

No actual games changed, cores/launchers rebuilt or GitHub publication. Real installation, headset and other-PC tests were then pending. Fixture migration does not mean Carlos's games were upgraded.

## Historical build and test

```powershell
.\installer\combined\Build-Msi.ps1 `
  -Bs1PayloadDirectory '<accepted stable-0.2.11/msi-build-0.2.11/payload>' `
  -Bs2PayloadDirectory '.\artifacts\integration-0.2.13\msi\bs2\msi-build-0.2.13\payload' `
  -Bs2ManifestPath '.\artifacts\integration-0.2.13\msi\bs2\manifest-0.2.13.json' `
  -BuildToolsDirectory '<verified WiX 6.0.2>' `
  -TestFamily '<lowercase GUID, 32 digits>'

.\installer\combined\Test-Msi.ps1 `
  -ManifestPath '<isolated manifest.json>' `
  -Bs1Exe '<legitimate BioshockHD.exe>' `
  -Bs2Exe '<legitimate Bioshock2HD.exe>'
```

Only two legitimate executables are copied with fake configuration: **no full-game copies or game execution**. Test identities/targets are separate from real products. -TestPredecessor bs1 creates a simulated predecessor identity, not rebuilt/distributed 0.2.12 code.

Technical basis: [MSI features](https://learn.microsoft.com/en-us/windows/win32/msi/feature-table), [REINSTALL](https://learn.microsoft.com/en-us/windows/win32/msi/reinstall), [RemoveExistingProducts](https://learn.microsoft.com/en-us/windows/win32/msi/removeexistingproducts-action).
Predecessor removal occurs during first installation, not maintenance; hence joint adoption in this historical architecture.
