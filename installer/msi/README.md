# Per-game Windows Installer — historical 0.2.13 integration

> Historical reference and shared custom-action code. Current distribution uses `../single-game/Build-Msi.ps1`: [English 0.2.17](../../docs/ENGLISH-0.2.17.md), [Spanish 0.2.16 closure](../../docs/RELEASE-0.2.16.md). Do not build the old EXE wrapper for new deliveries.

Historically, `Build-Msi.ps1 -GameId bs2` required BasePayloadDirectory (23 accepted 0.2.11 files) and verified WiX tools. Build-Bundle.ps1 then embedded that candidate and the exact BS1 MSI. Test-Msi requires an isolated-family ManifestPath; Test-LegacyMigration covers BS2 Format=3 betas 0.1.0/0.1.1. BS2 backups live under BioshockVR\bs2\WindowsInstaller; registry/product identities are separate from BS1. [Historical integration](../../docs/INTEGRATION-0.2.13.md).

## Historical BS1 0.2.11 MSI

Per-user package for BioShock 1 Remastered. Installs the complete mod in `Build\Final`, registers maintenance in Windows Apps and creates a desktop shortcut if the default-enabled checkbox is selected. Repair preserves that choice. Neither launcher nor game is automatically started. Final instructions differ according to shortcut creation. Runtime text only states that NVIDIA DLSS 310.7.0.0 is included.

Version 0.2.11 included stereo recovery, then pending headset confirmation. A new ProductCode/version upgraded 0.2.10 without error 1638. The same MSI supports repair/reinstallation.

Path priority: explicit selection, registered MSI installation, previous-installer manifest, Steam libraries. Multiple compatible copies without a prior selection require the user's choice. A game root is normalized to `Build\Final`.

## Historical build

Requirements: MSVC 2022 x86, CMake, .NET Framework 4.7.2+, .NET 6 runtime for WiX 6.0.2; no dotnet SDK required. Existing project tools import the local base payload, not a full game folder. The payload is not committed.

From the repository root:

```powershell
cmake --preset stable-win32
cmake --build --preset stable --parallel 4 --target bioshockvr
& .\installer\msi\Build-Msi.ps1
```

The historical script checked all four optimizations, disabled three diagnostic modes, built the launcher and produced `artifacts/stable-0.2.11/*.msi` plus SHA-256 manifest. It included the distribution host and NVIDIA 310.7.0.0. First use downloaded WiX pinned to 6.0.2 from NuGet and the official tag's source/license, retaining MS-RL notices. Tool terms are in `OSMFEULA.txt`.

Delivered versions are immutable: an existing `release/SHA256SUMS-v<version>.txt` blocks regeneration of that public MSI. Even UI changes require a new version. Changing only PackageCode while retaining ProductCode/version needs small-update mechanisms unsuitable for normal double-click installation; this caused 1638 in the 0.2.10 UI revision. We use an [MSI major upgrade](https://learn.microsoft.com/en-us/windows/win32/msi/major-upgrades) with a new ProductCode/shared UpgradeCode. Users need not uninstall first; registry data is not manipulated to evade Windows checks.

## Isolated validation

```powershell
& .\installer\msi\Test-Msi.ps1 -GameExeSource 'E:\path\Build\Final\BioshockHD.exe'
```

The executable is COPIED to a unique temporary directory, never included in the MSI or launched. Configuration, backups and desktop are redirected to fixtures. Tests refuse to replace a registered real MSI installation. Coverage includes clean install, defaults, repair, preference preservation, uninstall, controlled rollback and previous-mod recovery. Real installation/INI hashes are also checked.

Version 0.2.10 retained the compact launcher, versioned shortcut, progress text separate from its bar and four optimizations. Headset resolution changes by exactly 100 pixels; DLSS quality uses 5-percentage-point steps. F4 remains graphics-page-only. Notes must exist before packaging. Publication and final headset testing are separate steps.

With a real product registered, build tests with `Build-Msi.ps1 -TestFamily <32 hexadecimal characters>`. This changes ProductCode, UpgradeCode, all component GUIDs and registry keys; none are shared with distribution. Actions reject targets outside private `BvrMsiTest-<family>` directories. These packages are never distributed or placed beside the public installer.

`Test-Msi.ps1 -ManifestPath <isolated manifest> -FixtureBase X:\SteamLibrary` tests the affected drive without entering the real installation.
`-UpgradeManifestPath <new manifest in the same family>` checks upgrade against NEW payload hashes, not old ones. Real-family MSIs are rejected when an installation is registered. Real registry/files must remain unchanged.
`-ReproducePackageCollision`, only for isolated families, copies the old MSI and changes its PackageCode to reproduce 1638 without touching the original. The upgrade suite then tests normal installation of the new version and same-file reinstallation, including shortcuts.
`-TestShortcutChoice` adds no-shortcut install, choice-preserving repair, enabling/disabling and recovery from either failed change. Silent `BVR_DESKTOPSHORTCUT=0` disables the shortcut; `=1` enables it. The UI's unchecked box leaves the property empty; an initialization marker prevents execution from reenabling it by default.
Private directories/logs remain as evidence; test products are uninstalled at completion.

`Test-MsiPresentation.ps1` reads the MSI without installation: active dialog, overlap checks, shortcut version/target, old-name removal and previous backup path compatibility. It is safe with a real product installed.
`Preview-Progress.cs` is a development-only MSI preview API helper that exits after 60 seconds. It is not shipped. Visual tests use an MSI copy with sample multiline status text.

## Error 1926 / error 5 fix

Reproduced with the 0.2.9 algorithm in an isolated E: folder under the same nonelevated user. Windows Installer tried protecting `.rbf` files created while removing existing files in `E:\Config.Msi`; the user could modify the game folder but not administer this system folder.

Version 0.2.10 first saves a recoverable snapshot, verifies all hashes and unchanged destinations, then removes only listed files before standard RemoveFiles/InstallFiles. The action includes the exact previously registered shortcut, including desktops on another drive. RollbackFiles is already scheduled before removal. Windows Installer still manages/recover its new files, shortcuts, registry and previous-product upgrade. Rollback is not disabled, ALLUSERS is not changed, privileges are not elevated by this fix, and Config.Msi ACLs are untouched.

Tests cover failure immediately after removal and after copying. MSI code 0 alone is insufficient: Error 1926 in the corrected package's log fails the test.
Standard mechanism: [Microsoft Rollback Installation](https://learn.microsoft.com/en-us/windows/win32/msi/rollback-installation).

## Recovery

A recoverable snapshot precedes replacement. On failure the MSI restores the prior state byte for byte. Uninstall restores files from before the first MSI, leaves personal INIs and retains backups in `%LOCALAPPDATA%\BioshockVR\WindowsInstaller` (BS2 uses its separate bs2 subtree).

A fresh install applies defaults to the nine tested `Engine.RenderConfig` options when the game INI exists: `RealTimeReflection` and `UseRippleSystem=False`, `FluidSurfaceDetail=High`, others True. Upgrade/repair preserves preferences. Repair reinstalls NVIDIA 310.7.0.0; an alternative x64 DLL does not trigger automatic repair or block launch merely because of its version.
