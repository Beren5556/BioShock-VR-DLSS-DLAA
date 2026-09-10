# BS2: DLAA/DLSS blocked after installing 0.2.15

Historical status: installed profile corrected; confirmation in a new game run pending. At Carlos's request, the mod was prioritized without regenerating/replacing the MSI. This fix was subsequently included in 0.2.16.

## Confirmed cause

The September 10, 18:09:49 `%LOCALAPPDATA%\BioshockVR\bs2\bioshockvr.log` read `mode=dlaa`, 2950×2950 output and valid runtime 310.7.0.0, then reported that dlss-capabilities.ini did not match the game/DLSS45/two-host/runtime 310.7.0/IPC v8 contract and used native direct copy.

Rejection occurred before launching DLSS hosts. Later requests were rejected too, returning the overlay to the previous mode. September 8 host logs did not belong to this run.

The installed file was BS1's accepted generic profile without identity. BS2 requires `game=bs2` and `adapter=bioshock2r`, already present in `installer/profiles/bs2/dlss-capabilities.ini`. Payload selection missed them because it compared a backslash path with the base manifest's forward slashes.

## Applied change

With game, hosts and launcher closed, only the two identity entries were added to:

```text
BioShock 2 Remastered\Build\Final\host64\dlss-capabilities.ini
```

The result matches the repository BS2 profile byte for byte. Recoverable previous copy:

```text
artifacts/hotfix-bs2-capabilities-20260910/dlss-capabilities.original.ini
```

Previous SHA-256: `7C52BD6F6F186C40CDA847F0E143BDCFF94F0CB9BAC355977C27C2E27B857D77`.
Corrected SHA-256: `FCB20488F19FB39851FF1683E0C4DA4B4AA71D25B9BE12634AD95BC5AAD5508C`.

No binaries, graphics settings, resolution, host caches or BS1 files changed. Normal core preparation copies the correct profile into private per-eye directories. Core validation was not weakened.

In source, `installer/msi/Build-Msi.ps1` normalizes separators before selection. `Test-PayloadProfiles.ps1` tests the actual selection loop without copying files or running an installer, and checks profile contracts through Windows INI APIs.

## Verification and remaining work at that point

- 27 checks passed: BS1/BS2 selection with either separator, BS1 profile integrity, cross-profile rejection and installed BS2 profile acceptance.
- BS2 core/host/runtime, BS1 core/profile and desktop MSI hashes unchanged.
- Configuration remained DLAA, 2950×2950. The game was not automatically started.
- New-host startup and DLAA remaining active needed in-game confirmation before testing DLSS.
- Frozen 0.2.15 MSI/payloads remained untouched and contained the old profile; repair/reinstall could undo this hotfix. The next release needed a rebuilt BS2 payload with the correct profile and this test, not reuse of the defective payload or overwrite of a delivered version.

Repeat the profile test:

```powershell
./installer/msi/Test-PayloadProfiles.ps1 -BasePayloadDirectory '<accepted BS1 0.2.11 payload>' -InstalledBs2Directory '<BS2 Build\Final>'
```

This resolved the identified immediate fallback to NORMAL, but was not yet headset image, performance or stability validation.
