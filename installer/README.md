# BioShock 1–2 common installer · 0.2.17 English

Current entry points:
`single-game/Prepare-EnglishRelease.ps1` and
`single-game/Build-Msi.ps1`.

One native MSI contains both complete mods. Its first-screen dropdown
selects one game per run. Products, preferences and backups remain separate.
Neither game nor launcher starts automatically. This is not an EXE wrapper.

See [building](../docs/BUILDING.md),
[testing](../docs/TESTING.md) and
[English edition verification](../docs/ENGLISH-0.2.17.md).

For the Spanish 0.2.16 pipeline, see
[its release closure](../docs/RELEASE-0.2.16.md).
The content below and `unified/` describe historical formats.

## Historical BioShock 1 MSI 0.2.11

That release used `msi/Build-Msi.ps1`; see
[Windows Installer notes](msi/README.md). The earlier EXE installer is
retained as tooling and historical reference, not the current delivery.

## Earlier standalone EXE installer

The WinForms installer included everything required to add the mod to a
compatible BioShock Remastered installation. It did not require the original
VR mod to be installed first.

The public UI asked only for the `Build\Final` directory. Before writing,
it validated the compatible game executable, checked all embedded resource
SHA-256 hashes and retained a recoverable manifest and backup of every
replaced file. Installation did not modify the INIs.

Restore returned the files preceding the first installation byte for byte
and removed files that had not existed.

## Historical local payload

Distribution binaries are not committed individually. The corresponding
release carries the tested installer; `payload-manifest.json` pins the
historical binary hashes.

    .\installer\Import-Local-Payload.ps1 -PayloadDirectory C:\work\verified-payload
    .\installer\Build-Installer.ps1

The directory must match [payload-manifest.json](payload-manifest.json).
The script rejects any mismatching hash. Those commands reproduce the old
EXE format, not 0.2.17.

Its full historical test required a legitimate compatible executable:

    .\installer\Test-Installer.ps1 -GameExecutable C:\work\Build\Final\BioshockHD.exe

The test used isolated copies, opened and closed the two interfaces by exact
PID, and exercised clean installation, upgrades, restoration and rejection
of incompatible paths. See the current testing guide for the native dual MSI.
