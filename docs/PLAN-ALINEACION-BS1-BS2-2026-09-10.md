# Historical plan: align BioShock 2 with BioShock 1 and unify installation

September 10, 2026, Europe/Madrid. Development authorized; 0.2.13 candidate built/tested in isolation. [Executed integration and limits](INTEGRATION-0.2.13.md). This English consolidated plan preserves the decisions; the [original dated record](https://github.com/Beren5556/BioShock-VR-DLSS-DLAA/blob/v0.2.16/docs/PLAN-ALINEACION-BS1-BS2-2026-09-10.md) remains in the immutable Spanish tag.

Later decisions supersede the original EXE proposal: distribution now uses a **native MSI**, individual game dropdown and separate instance maintenance. See [0.2.16 closure](RELEASE-0.2.16.md).

## Objective and user decisions

Use Carlos's accepted BS1 DLSS/DLAA 0.2.11 as functional/UX reference. Bring its improvements into existing BS2 without losing BS2-specific adaptation. One project/repository, **one downloadable installer with one version containing both mods**. Owners of both games run it once per game; install/upgrade/repair/remove must not affect the other.

Shared BS1 launcher appearance/controls; adapt only identity, paths and supported BS2 functions. Launchers belong in each Build\Final; desktop receives shortcuts, not independently functioning EXE copies. Installer deliveries belong in Desktop / Lanzadores MOD VR, hash-verified without overwriting previous versions.

Initial session was planning only; later implementation/build/isolated tests authorized. Real-game deployment/publication required agreement. One-off shutdown authorization belonged only to the earlier session and must not recur. Separate BIOSHOCK/DLSS 5 research is out of scope.

Integration worktree BioShock12VR-DLSS-DLAA, branch codex/bioshock-1-2-v0.2.13.
BS2 recoverable snapshot 7b4514090d5d1f319b463f158d076f4155a5347e, local codex/bs2-pre-0.2.13: 85 files copied/verified; no reset or wholesale overwrite of dirty originals.

## Confirmed baseline

BS1 reference BioShockVR-DLSS-DLAA-GitHub:

- Accepted v0.2.11 commit 894cae888dbe74611875eb82921a6ac80f423297.
- Local codex/v0.2.11-stable HEAD 065a43e: same functional code, only CI UTF-8/documented decision additions.
- **0.2.12 discarded**: do not import its changes, translations, binaries, caches or remote branch.
- Accepted artifacts/stable-0.2.11/BioShock-VR-DLSS-DLAA-0.2.11.msi SHA-256:
  2A5365E332DC311C0E010AD0BECEEAB34E905F66C6F40AB5CF44824EDAD9F660.
- artifacts/stable-0.2.11/msi-build-0.2.11/payload: 23/23 match release/manifest-v0.2.11.json.
- No installer/Payload, generic build output or game directory as distribution source. Never regenerate an accepted MSI or recycle discarded version identities.

The BS1 task confirmed closure; its old test counts were historical, not tests rerun that night.

BS2 reference BioShock2VR-DLSS-DLAA:

- 0.1.1-beta and later launcher 0.1.1.1. Frozen installer lacks some later launcher changes; inventory source/binaries separately.
- Steam 409720, x86 Bioshock2HD.exe SHA-256 C2A31FB67B285C136203A4A6E739545177BE5753D972E6AEA0F44C65DDF22F8C.
- Preserve WORLD camera/projection/scene selection, exact temporal identity, per-eye history, native exit guards/OpenXR cleanup.
- Shared.ini [SharedOptions] resolution authority, Bioshock2SP.ini mirror, windowed launcher startup.
- Separate %LOCALAPPDATA%\BioshockVR\bs2, protected saves/configuration, specific weapons/calibrations.
- New exact-path responsive process confirmation before launcher closes; verified configuration backup/recovery.
- GameId=bs2 old-installer original recovery/later-edit preservation must migrate intact.

## Architecture

| Layer | Share from BS1 | Retain/adapt for BS2 |
| --- | --- | --- |
| Temporal processing | Four optimizations, transport, checks, image policy | Camera/depth/motion production, auxiliary scenes, projection, exit |
| UI | Compact Image, headset menu, graphics catalogue/messages | Identity, paths, INIs, weapons, supported functions |
| Installation | Proven MSI visual experience/transactions | Independent target, identity, manifests/backups |

Common entry does not require one DLL. Initially retain separate adapters/packages; port locally before refactoring if generalization would destabilize the accepted core.

Historical initial recommendation was an EXE embedding exact BS1 MSI plus new BS2 MSI, with independent registration and one distribution version. A two-feature MSI was considered more invasive. Carlos later explicitly required a native MSI and then rejected checkbox multi-selection; these superseding decisions govern current work.

## Phases and acceptance gates

### 0. Preserve and inventory

Snapshot BS2 dirty/new files; record accepted BS1 source/manifests/binaries/configuration without changing branch/MSI/game. Inventory installed versions before deployment. Classify functions as shared, BS1-specific, BS2-specific or pending. Do not proceed with uncertain source/binary provenance.

### 1. Performance and stability

Port incrementally:

1. Left-host work overlapping right scene.
2. Depth reuse only without invalidating writes.
3. Independent frame-tail overlap and redundant-color-copy removal with correct ownership.
4. Compatible stereo delivery before desktop-only mirror.

Retain generation/frame checks, one pending job per eye, bounded waits, dead-host detection and coherent fallback. Place reconfiguration recovery at safe BS2 engine points; never undo user OFF/shutdown guards. Do not overwrite openxr_runtime.cpp wholesale: explicitly adapt BS1 calls. Preserve exact camera rejection; no “latest camera” substitution. NORMAL must avoid unnecessary temporal overhead.

### 2. Headset controls and graphics

- NORMAL/DLSS/DLAA and confirmed effective resolution.
- F1 pages, F2/F3 selection/value, F4 only graphics toggle.
- 100-pixel per-eye steps replace old BS2 150 proposal without rounding saved values.
- Headset DLSS 35–90%, 5-point steps, skipping invalid geometry; preserve old ratios/preferences.
- Post-DLSS sharpness 0–100%, step 5, default 0; DLSS only, no full rebuild; headset control, not compact Image.
- Nine graphics options: shaders, shadows, reflections, post-processing, ripples, particles, distortion, high-quality post-processing, fluid detail.
- Verify BS2 safe-thread actual read/write. Distinguish applied from saved/restart-required; no false live-application claim.
- Preserve compact accepted presentation.

Fresh defaults: reflections/ripples off, other six booleans on, fluids High. Keep editable and mark shadows/reflections/ripples for cost. Never overwrite existing preferences during upgrade.

### 3. BS2 launcher

Reuse compact output/mode/DLSS quality/read-only internal resolution. Launcher quality uses **100 internal pixels** with calculated percentage, intentionally different from headset 5-point steps.

Retain camera, hands, movement, cinematics, HUD and BS2 weapons. No BS1 wrench gesture, personal offsets/calibrations or revived FXAA/spatial/general-INI editor.

Retain windowed launch, coordinated Shared/SP validation/recovery and actual new-game confirmation. Port useful messages/direct-start alternatives without weakening it. Open/reload writes nothing; save detects external edits. Missing original INI means run game once, not fabricate a full profile. Keep tested NVIDIA 310.7.0.0, nonblocking alternate-x64 warning and explicit repair.

### 4. Common installer and migration

Separate executable/hash, Steam ID, discovery, INI/capabilities, registry/MSI/component/shortcut/backup identities and ownership. Explicit path outranks detection; ask on multiple valid copies. Never reuse BS1 family for BS2.

Initially retain BS1 MSI byte-for-byte. Migrate BS2 0.1.0/0.1.1 and later launcher while retaining pre-first-install originals and edits. Explain repair/restore/preservation; retain recoverable conflicts and reject wrong-game/incompatible manifests.

Reuse verified snapshots, rollback, final hashes/progress/errors. No disabled rollback or system ACL changes. Install success distinct from later launcher issues; no automatic launcher/game launch. Include required licenses/credits/redistributables/examples, not games, private SDK or LAB tools.

### 5. Automated and isolated validation

Minimum matrix: BS1 only, BS2 only, both, both install orders, different Steam drives, clean install, old-BS2 upgrade, repair, individual uninstall, cancellation, injected rollback, edited settings, ambiguous copy, wrong path/executable.

Verify other-game hashes/preferences before/after. Any wrapper removal must not silently remove the other game. Repeat geometry/menu/INI/startup/stereo/eye-isolation/host-failure/exit checks. Separate synthetic/WARP/LAB, NVIDIA and real-headset evidence. Earlier tests do not certify a new build.

### 6. Brief user test and delivery

Prepared BS2 candidate: NORMAL → DLSS → DLAA → NORMAL; resolution/menu/stereo/hands/HUD/save loading/clean exit. Compare water viewed from outside and dry scene at matching output/settings. Keep proportional; do not restart lengthy water/latency research.

On regression retain evidence/restore previous candidate; do not vary multiple settings or claim unmeasured causes. Check BS1 installation with exact payload and brief functional regression where relevant. Another computer remains explicitly pending until available.

After acceptance: new versions where required, hashes/manifests, clear notes and **one versioned installer with both mods/licenses**. Internal versions support independent maintenance without regenerating accepted artifacts. GitHub publication only after authorization.

## Success criteria and limits

Functional parity/stability, not identical FPS across engines/scenes. Major confirmed BS1 improvement was left-eye overlap. No fixed water shader or demonstrated NVIDIA bug: reflection/ripple costs identified, investigation paused, VD latency jump unresolved. Camera-derived vectors are not complete object/water/transparency motion.

Exclude 0.2.12, DLSS 5, Frame Generation, performance probes/LAB hosts, weakened camera guards or imposed personal settings. Keep good BS1 version available unchanged. Finish when applicable BS2 catalogue and agreed tests pass, with independent installer maintenance.

## Sources

BS1 STATUS, PERFORMANCE, v0.2.11 public/validation notes, MSI README, Image/menu/graphics code and manifests. Closure confirmation from task **MOD BIOSHOCK DLSS**, ID 01a0781f-7569-72f3-9938-77c9c0b61648.
BS2 DEVELOPMENT, EXIT-FIX, INSTALLER, launcher README and existing source state. Interpret historical documents by version; later user decisions/build evidence take precedence.
