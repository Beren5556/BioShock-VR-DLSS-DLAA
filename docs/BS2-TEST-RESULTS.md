# BioShock 2 DLSS/DLAA 0.1.0-beta — historical local results

Date: early September 8, 2026 (Europe/Madrid).
Status at that point: local headset-test candidate, neither published nor installed over the original game. Not validated on another computer.

## Frozen artifacts

| Component | SHA-256 |
|---|---|
| 0.1.0-beta installer | A61BCCE385C0095645C67FE27A937E0D2B661E3C8CE2805F401C273EA73D9D6D |
| BS2 launcher | 613772D164550745AB3A58924CF8A156EBCB95DE312869D01166361CA4A0EF8D |
| Production bioshockvr.dll | 8DFD11347E8B80623CC3678BD030BD9B77B76FACA9BEF3766C1E66E224D0554A |

The installer contains 21 resources: eight payload entries and thirteen usage/license documents. Original v0.8.2 proxy, x64 host and NVIDIA 310.7.0.0 are identified in `installer/payload-manifest.json`. The game executable is not packaged.

## Application and packaging tests

- Production/laboratory x86 compilation: PASS.
- Updated BS2 temporal-contract WARP test: PASS, including pose identity, finite planes, required capture, histories, resets and invalid cases.
- Launcher `--self-test`: PASS for parsers, resolutions, BS2 values, external-edit detection, backups/rollback and launch state machine.
- Real helper named `Bioshock2HD.exe`: exact path, new process and three consecutive responsive-window seconds required. The actual game was not run in this suite. Timeout, early exit and old process rejected.
- `--self-test-ui`: PASS with fake INIs, authoritative Shared.ini 800×600, 2048×2048 save/mirroring, eight weapons, hands and unknown-key preservation.
- Native visual launcher/installer inspection: readable text/controls; both closed afterward without UI installation.
- Installer: 13 groups PASS, 21 resources verified and 21 distinct original files restored byte for byte. Includes repair, preserved conflicts, corrupt backups, BS1 manifest, isolated shortcut, failure after six writes and install/launcher-open separation.
- Repository verifier: PASS, inspecting actual embedded resources and rejecting the real LAB DLL. PowerShell 7 delegates this .NET Framework metadata read to Windows PowerShell 5.1.
- LAB path guards: 19 PASS cases, including `..` escape, ADS, relative paths and real junctions at leaf/ancestor levels.
- Legacy `tools/install`, `uninstall` and `package` entries disabled and checked to fail before writing, avoiding BS1 paths/inventory.

Evidence: `artifacts/tests/installer/summary.json`,
`artifacts/launcher-tests/final-ui-f21f47d92e4d442497dc018bcdebfc5f`,
`scripts/Test-BS2-LabPathGuard.ps1` and `src/tools/temporal_guides_bs2_test`.

## Saved-game integration with simulated OpenXR

Compatible x86 Bioshock2HD.exe SHA-256:
`C2A31FB67B285C136203A4A6E739545177BE5753D972E6AEA0F44C65DDF22F8C`.

Local RTX 4090; test runtime `bvr-xrsim`, previously validated with xr_hello32. Registered OpenXR runtime unchanged.

A physical game/save copy, without links to originals, used private per-run profiles, INIs, logs and hosts. The test core shares functional rendering code with a path-isolation variant; **it is not the distributed binary**. SHA-256:
`A32EABCE3EE62A2EC502875AF45A21D19857E215A5630DB675A2B7D8A503E4F6`.
The lab proxy is not packaged either.

| Mode | Per-eye render → output | Run | Gameplay result |
|---|---|---|---|
| NORMAL | 1024² → 1024² | off-57e9251e | Stereo and turning; no DLSS hosts |
| DLAA | 1024² → 1024² | dlaa-f0d5823b | 6357 images per eye; turning, pause/resume |
| DLSS SR | 1024² → 1536² | sr-7590efc3 | 9468 images per eye and turning |
| High DLSS SR | 2048² → 3072² | sr-e171beb2 | At least 7682 images per eye and turning |

Stable DLAA/SR gameplay: `coherent=1`, `hist=1`, `reject=none`, `pubs=1`, `projPubs=1`, `nearFar=10/65536`, `mixed=0`, `gap=0`.
WORLD projection requires Draw's root FPlayerSceneNode and the same published eye/draw pose. Foreground/auxiliary nodes are rejected.

Menus fall back without DLSS when temporal data is missing; this is not NGX initialization failure. Incomplete transition tags are rejected: one loading and one DLAA resume case, without recurring increases in stable segments. Pause switches to one quad; resume restores two projection views, history and a new epoch (8 → 10).

Left/right/SBS captures and scene/turning JSON were retained per mode. Inspection confirms game imagery in both eyes, not sharpness, ghosting or comfort in a real headset.

The high-resolution pass does not establish sustained 90 Hz: final logged segments are approximately 71–73 pairs/s. User resolution was unchanged; this is not a universally optimal setting.

`artifacts/bs2-tests/game-copy.json` and `latest-run.json` locate private logs under `D:\BioShock2VR-DLSS-Lab\game-<GUID>\runs`. These are retained for reproduction; the copy occupies approximately 21 GB.

## Exit issue observed: pending at that stage

All processes responded to WM_CLOSE and ended without leftover games/hosts. However, logs show teardown AV `0xC0000005`, also in NORMAL without NGX. Sites: `+0x4FF0FE` (DLAA), `+0xC312D2` (NORMAL/SR), `+0xC37362` (high SR).

Upstream documented exit crashes before this adaptation. Its generic teardown handler terminates with code 0 and no dump. Therefore **exit code 0 does not prove error-free shutdown**, and that generic message does not identify the cause. At this point the issue was neither fixed nor hidden. Evidence/limits: [BS2-TEMPORAL.md](BS2-TEMPORAL.md).

## Preservation and pending work

- Thirty original protected files (BS1/BS2 configuration, BS2 saves, real executable/DLLs) identical before/after.
- Real game retained official VR mod 0.8.2 and the user's successfully tested configuration; candidate not deployed over it.
- No BS1 package modification/regeneration or publication.
- Pending: install candidate and compare NORMAL/DLAA/DLSS in the headset, including animation, HUD, loading, cutscenes and menu exit.
- Pending: full installer/launcher run on another computer.
- Known limits: camera-only vectors, zero jitter, no independent animated-object/hand vectors, no DLSS 5/Frame Generation.

Conclusion at that stage: engine, launcher and installer technically prepared/tested on this computer. A headset-acceptance beta, not a universally validated final release.
