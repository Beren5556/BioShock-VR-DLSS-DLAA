# Security

## Supported versions

Please reproduce reports with the latest public release. Historical releases
are preserved for reference; they are not maintained as separate security branches.

## Reporting a vulnerability

Do not post sensitive information, memory dumps or personal paths in a public
issue. Use the repository's private **Security > Advisories > Report a
vulnerability** option where available, or contact the owner privately.

Include, where possible:

- Exact version and installer SHA-256.
- Windows version, GPU, driver and VR runtime.
- Minimal reproduction steps.
- A trimmed log reviewed to remove personal information.

Do not attach BioshockHD.exe, Bioshock2HD.exe, game assets, tokens, credentials
or the NVIDIA SDK.

## Installer trust model

The dual-game MSI validates known compatible game executables, verifies
payload SHA-256 hashes, uses transactional installation and retains recovery
backups. It manages only the selected game's mod. Game files and executables
are not distributed in the package.

The installer is **not digitally signed**. Check its SHA-256 against the
checksum published with the matching release before running it.

If SmartScreen or an antivirus blocks the installer, do not disable protection
or add an exclusion. Leave a quarantined file quarantined and report the
security product, exact detection name, time and SHA-256 for investigation.

The package installs NVIDIA DLSS 310.7.0.0. Advanced users may manually replace
it with another x64 runtime, but only 310.7.0.0 is part of the verified artifact.
Any different DLL falls outside this release's integrity and compatibility
assurances. Repair restores the bundled runtime.
