# BioShock 2: DLAA/DLSS temporal contract

Technical-port validation record, not a declaration of final headset quality.
Target: **BioShock 2 Remastered**, `Bioshock2HD.exe`, Steam AppID `409720`. The original non-Remastered BioShock 2 executable is unsupported.

## Separation from BioShock 1

Provider: `src/game/bioshock2r/temporal_guides.*`. It retains the shared backend resource contract, but uses no bioshock1r cameras, hooks, matrices or addresses. Production BS2 data, including dlss.ini, lives under `%LOCALAPPDATA%\BioshockVR\bs2\`.

Game graphics configuration differs: Shared.ini is resolution authority, mirrored to Bioshock2SP.ini under `%APPDATA%\BioshockHD\Bioshock2\`. BS1's Bioshock.ini is untouched.

Per-eye flow:

```text
Tagged BS2 Draw -> final pose + same-build WORLD projection
                -> render-interval D24 depth
                -> R32F depth + R16G16F motion
                -> independent per-eye x64 NGX host
                -> result to that same eye's OpenXR swapchain
```

## Camera and WORLD projection: exact identity

Each sr_push_eye returns a monotonic identifier. Scenedraw retains it in thread-local scope, including the second Draw; nested Draws do not accidentally receive the outer eye's tag. Camera publishes only the final gameplay pose after eye offset. Lookup requires **exact eye/build**, maximum age 200 ms; it never substitutes “latest available camera”.

The matrix is not inferred from configured headset values. Observation targets the root FPlayerSceneNode created by UGameEngine::Draw; within that scope only its **primary WORLD** projection constructor is captured. Portal/reflection cameras and secondary foreground projection are ineligible, even if weapon-FOV adjustment gives them matching tangents.

Constructor pose must also match the CalcView publication: position tolerance 0.01 Unreal units and exact rotation modulo 65536. Duplicate camera/projection publication is rejected, not silently resolved by choosing the last.

### BS2-specific binary evidence

Local audit 2026-09-08. Executable SHA-256:
`C2A31FB67B285C136203A4A6E739545177BE5753D972E6AEA0F44C65DDF22F8C`.

| Point | BS2 RVA / meaning |
| --- | --- |
| Creation from root Draw | CALL 0x4EF44C, return 0x4EF451 |
| FPlayerSceneNode | thunk 0x5C95 → constructor 0x5BBD40; RTTI/vtable 0x10CF750 |
| Matrix update | CALL 0x5BBEB7 → thunk 0x59E3 → body 0x5C34A0 |
| Primary WORLD projection | return 0x5C4364, node destination +0x1D0 |
| Secondary foreground projection | return 0x5C43AF, node destination +0x380; excluded |
| Finite-perspective constructor | body 0x5BBC00, six floats, ret 0x18 |

WORLD explicitly incorporates the game's FOV option before aspect conversion; foreground uses its other lens parameter. This code chain identifies WORLD independently of numerical foreground-FOV coincidence. CALL/JMP targets and known prologue are verified before hooks; mismatch disables the temporal route without by itself disabling VR camera.

Reproducible audit recipe, ABI/tests:
[temporal_guides_bs2_test/README.md](../src/tools/temporal_guides_bs2_test/README.md).

## Actual per-eye depth and planes

The observer reads **actual** near/far arguments and validates the resulting same-build matrix. The audited function produces finite D3D perspective:

```text
m10 = far / (far - near)
m14 = -near * far / (far - near)
m11 = 1
tanHalfFovX = 1 / m0
tanHalfFovY = 1 / m5
```

Required: finite inputs, positive lens diagonal, expected zeros and `0 < near < far`. Unexpected reversed/asymmetric matrices are rejected. The engine's far selector has a 1024 branch and another reading a global initially 65536; near comes from a global initially 10. **No active branch is assumed**: captured per-eye values govern reconstruction. PrepareDesc near/far hints do not replace observation.

Validated convention: normal depth, near → 0 and far → 1. D24 converts to R32F retaining hardware depth, without inversion, for NGX. Near/far reconstruct position/motion; linear distance is not submitted as hardware depth.

DSV selection uses draw votes within the interval, restricted to correctly sized, single-sample, mip-0 D24. Binds/draws/clears are observed; D3D11 state is preserved/restored when copying still-bound depth. This remains a render-activity heuristic, not semantic identity of the engine's depth object.

Composition tangents and any independent WORLD-block read must agree with captured projection. Disagreement causes fallback, not invented FOV correction.

## Camera motion and zero jitter

Vectors are **camera-only**, reconstructed from depth and this eye's current/previous poses. Convention: current pixel → previous pixel, in render pixels, scales 1,1, R16G16_FLOAT.

Hands, dual weapons, animated enemies, particles, water and independently moving objects lack their own vectors. Trails/temporal errors may remain despite coherent camera/depth/IPC. Functional VR body/weapons do not imply their motion vectors are implemented.

The raster projection **has no temporal jitter**. Client sends jitterX=0/jitterY=0 for DLAA/SR; host forwards these to NGX. No undrawn subpixel sequence is claimed. Missing jitter limits temporal information, especially SR. Successful image processing does not prove quality equivalent to complete jitter/object-vector integration.

## History and safe rejection

Each eye owns textures/history. First valid frame requests reset, as do:

- Tangent/near/far changes.
- Same-eye sequence gaps (normally advances by two).
- Camera epoch changes: view, gameplay/cutscene, mode, recenter, stereo rearm or second-pass failure.
- History older than 200 ms or nonmonotonic clock.
- Large cuts: per-frame translation ≥256 UU or rotation ≥45 degrees on any axis.

New dimensions recreate resources and clear both histories. Missing/ambiguous camera/build/depth/projection, nonfinite data, out-of-order build or WORLD disagreement returns false for core safety output. Spatial fallback is not labelled DLSS/DLAA processing.

## Logs and validation

Production: `%LOCALAPPDATA%\BioshockVR\bs2\bioshockvr.log` plus separate eye-host logs in the same data directory.

| Signal | What it establishes |
| --- | --- |
| root WORLD finite projection observation ready | Signatures/branches verified, hooks installed; not yet gameplay proof |
| rootWorld=1 samePose=1 finite=1 | Valid observed root projection and matching pose |
| pubs=1 projPubs=1, matching build/cam | Unique publications for the exact eye build |
| nearFar=... epoch=... | Actually captured planes and camera epoch |
| coherent=1 hist=1 reject=none | Accepted inputs and continuous history for that eye |
| Increasing processed and error-free hosts | NGX execution, not fallback alone |

Initial capture log is one-shot per eye and may occur in a menu with samePose=0 because no gameplay camera is published there. Alone this is not failure. In gameplay, coherent=1 with projPubs=1 implies required root-node/matching-pose filters passed.

### Recorded DLAA evidence, 2026-09-08

Updated WARP test passed with code 0: deterministic camera resource/math checks, not in-game hooks. Production/LAB builds succeeded.

Game integration was repeated **with the root-constructor filter** in dlaa-f0d5823b, DLAA 1024×1024 → 1024×1024, two real NGX hosts and bvr-xrsim:

```text
D:\BioShock2VR-DLSS-Lab\game-cc819bfd-7129-4494-97ff-2b9fd80f81a6\runs\dlaa-f0d5823b
```

Audited run.json, data/bioshockvr.log, both data/DLSS45Host/eye0 and eye1 logs, evidence/dlaa-world, dlaa-world-motion, dlaa-pause, dlaa-resumed JSON and stereo gameplay capture. JSON 1032×1104 dimensions are simulator capture/composition, not different NGX resolution.

| Check | Observation |
| --- | --- |
| Stable WORLD 00:32:20 | 5650 NGX frames, 2825 per eye; coherent=1 hist=1 reject=none |
| Per-eye identity/planes | pubs=1 projPubs=1, build/camera match, nearFar=10/65536, epoch 8 |
| Real hosts | Successful NGX Init and DLAA model K feature; both evaluation counts increasing; no NGX errors in audited segment |
| Initial capture | FOCUSED, two projection views; gate 3415/3415/3415, discarded/outOfOrder 0; observed IPD 0.063 m, image in both eyes |
| Simulated turn | Yaw/pitch 15/5 degrees in dlaa-world-motion; changed camera poses, coherence/history retained |
| Pause | dlaa-pause: one quad, no temporal projection layer |
| Resume | dlaa-resumed: two-view projection and three quads return; epoch 8 → 10, per-eye resets 1 → 2 |
| Last diagnostic 00:33:50 | 12714 processed frames, 6357 per eye; coherent=1 hist=1 reject=none, planes 10/65536 |

tagMismatch stayed at 1 after load, then 2 after resume, without increases in observed stable segments; not zero throughout. Fallbacks increased in menu/pause and stopped after gameplay resumed. These are observed transitions, not proof for all transitions.

WM_CLOSE at 00:33:53 was followed by 0xC0000005 at Bioshock2HD.exe+0x4FF0FE, classified by the existing handler as known teardown failure and terminated without dump. This run is **not error-free shutdown**, despite successful gameplay NGX evaluation. Prior evidence: upstream commit 4071543 and [ENGINE_NOTES session 38](bioshock2/ENGINE_NOTES.md#the-faulting-site-0x4ff0fe---the-engines-pending-display-apply-virtual), including all-hooks-skipped bisect. The generic crash.cpp teardown guard calls TerminateProcess(...,0); code 0 does not prove absence of AV. New temporal observation neither changes that guard nor writes the faulting object's data; a dumpless run alone provides no causal stack.

### SR Quality integration, 2026-09-08

Also audited sibling sr-7590efc3: run.json, main/host logs, evidence/dlss-sr-world and dlss-sr-motion JSON, stereo turning capture. Root WORLD filter and bvr-xrsim are recorded.

| Check | Observation |
| --- | --- |
| Actual mode/resolution | Both hosts 1024×1024 → 1536×1536; SR Quality selected by NGX optimum 1024×1024 (2/3 per axis) |
| NGX evaluation | Feature creation and frames 1, 1800, 3600, 5400, 7200, 9000 in both logs; no audited-segment NGX errors |
| Last diagnostic 00:36:19 | 18936 processed, 9468 per eye; coherent=1 hist=1 reject=none |
| Temporal data | pubs=1 projPubs=1, matching build/camera, nearFar=10/65536, epoch 8, one initial reset/eye, zero gaps |
| Transitions/continuity | tagMismatch=1 after load and stable; mixed=0; fallback 1911 stable in final gameplay segment |
| WORLD/turning JSON | FOCUSED, two projection views; gates 4468/4468/4468 and 7141/7141/7141, discarded/outOfOrder 0; yaw/pitch 15/5 degrees, image in both eyes |

Simulator capture remains 1032×1104 per eye, not evidence of NGX processing size. SR dimensions are established by both feature-creation and eye0/eye1-ready logs.

Exit requested 00:36:22 again logged 0xC0000005, now Bioshock2HD.exe+0xC312D2, not DLAA's RVA. Upstream documents a prior teardown issue there, but generic classification/no dump cannot prove this run's precise cause. No clean-exit claim or use of forced code 0 as proof.

### High-resolution SR and NORMAL control, 2026-09-08

Runs sr-e171beb2 and off-57e9251e used unchanged code. Audited logs, evidence JSON, close-result.json, high-SR host logs and stereo gameplay captures.

| Check | High SR: sr-e171beb2 | NORMAL: off-57e9251e |
| --- | --- | --- |
| Established mode | Both NGX hosts: 2048×2048 → 3072×3072, Quality | dlss.ini/log mode=off; no host startup or DLSS45Host directory |
| Temporal result | 00:40:06: 15364 processed / 7682 per eye; coherent=1 hist=1 reject=none | No NGX frame claim; VR without DLSS/DLAA |
| Identity/history | pubs=1 projPubs=1, planes 10/65536, epoch 8, one reset/eye, gaps 0 | Root WORLD observer still installed; NORMAL is not vanilla or all-hooks-disabled |
| Continuity | mixed=0; tagMismatch=1, fallback 1141 stable at end | Stereo and simulated turning present |
| JSON | dlss-high-world/motion: FOCUSED, two views; gates 6343 and 9120 match waited/begun/ended | normal-world/motion: FOCUSED, two views; gates 5249 and 6369 match waited/begun/ended |
| Requested exit | WM_CLOSE, AV 0xC0000005 at +0xC37362, exit code 0 | WM_CLOSE, AV 0xC0000005 at +0xC312D2, exit code 0 |

All four JSON: discarded=0, outOfOrder=0, 0.063 m eye separation, 1032×1104 per-eye capture. Motion JSON: yaw/pitch 15/5 degrees. Images establish visible output in both eyes, not ghosting quality. High-SR hosts evaluate at least frame 7200 per eye without audited-segment NGX errors.

High resolution **does not establish sustained 90 Hz**. Although simulator configured 90, final observed beats were 71–73 Draws/s and equal second passes. Internal NGX timings are not whole-game performance or headset latency.

Both close-result.json record teardownFaultInLog=true. Code 0 is not interpreted as AV-free exit. NORMAL reproducing proves active NGX evaluation is unnecessary in these tests; it neither identifies the cause nor exonerates all injected code. High-SR +0xC37362 is kept distinct from the other sites.

These establish observed technical contracts for DLAA/two SR configurations, not final headset quality or object vectors. More resolutions/scenes/transitions and physical-headset comfort/quality require separate validation.

LAB `BVR_BS2_TEST_ISOLATION` requires absolute BVR_LAB_GAME_INI and valid sibling Shared.ini, without real-profile fallback. The lab proxy also isolates engine-read paths and is not production payload. Simulation does not prove physical-headset visuals, absence of ghosting or long-session comfort.
