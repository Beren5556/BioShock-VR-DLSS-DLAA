# BioShock 2 temporal-guide validation

The production adapter is `game/bioshock2r/temporal_guides.*`; this directory is
test/audit code, not a payload for the game installation. No game executable or
proprietary shader data is included.

## Test target

`temporal_guides_bs2_test32` links the real guide implementation to a deterministic
camera-publication stub and a D3D11 WARP device. It checks D24 conversion, dominant
DSV votes, bound-DSV clear/re-arm, OM and compute-state restoration, independent
eye outputs/history, pixel-space reprojection directions, missing/duplicate tags
and inputs, FOV and history resets, both finite far branches (1024 and 65536),
actual captured near/far overriding wrong prepare hints, unsupported surfaces,
and root-node/camera pose identity. It is not a headset-quality or animated-object
motion test.

Build using the repository's normal CMake Win32 configuration and target name.
Run `temporal_guides_bs2_test32.exe`; success prints `PASS` and exits with 0.

## Read-only binary derivation, 2026-09-08

Audited Steam `Bioshock2HD.exe`, image base `0x10900000`, SHA256:

`C2A31FB67B285C136203A4A6E739545177BE5753D972E6AEA0F44C65DDF22F8C`

All addresses below are **BS2-derived RVAs**, not copied from BS1.

| Observation | RVA / result |
| --- | --- |
| Root `UGameEngine::Draw` constructs the player scene | CALL `0x4EF44C`, return `0x4EF451` |
| Constructor thunk and body | `0x5C95` -> `0x5BBD40`, `ret 0x2C` |
| Constructor type identified from MSVC RTTI | `FPlayerSceneNode`, vtable `0x10CF750` |
| Constructor updates view/projection matrices | CALL `0x5BBEB7` -> thunk `0x59E3` -> body `0x5C34A0` |
| Primary, world projection | builder CALL `0x5C435F`, return `0x5C4364`, copied to node `+0x1D0` |
| Secondary, foreground lens | builder CALL `0x5C43AA`, return `0x5C43AF`, copied to node `+0x380` |
| Finite perspective builder | thunk `0x855D` -> body `0x5BBC00`, `ret 0x18` |
| Near input | global `0x14977E4`, disk value `10` |
| Far selector | thunk `0x71E4` -> body `0x5BE510`; `1024` special case or global `0x1499FE8`, disk value `65536` |

The first lens explicitly loads the world's option at updater `0x5C41F9`, applies
its scale and node lensA through `0x5C4214`, and performs the aspect conversion.
The second lens uses node lensB directly. This establishes world/foreground
identity independently of whether the live foreground-FOV match makes their
tangents numerically equal.

The root constructor takes three pointer arguments, camera location by value,
FRotator by value, and two lens floats: 44 bytes of x86 stack arguments. It receives
the final CalcView pose from Draw, constructs the scene, and returns the node that
Draw submits to the renderer. The production observer checks the CALL/JMP targets
and finite-builder prologue before installing either hook. Capture is eligible
only inside this root constructor and the exact eye/build scope. Other constructor
callers (`0x4F9A6B`, `0x68330A`) and other scene updater callers are excluded.
The captured constructor pose must match the corresponding final CalcView
publication (location tolerance `0.01` Unreal units; exact modulo-65536 rotation).

The builder output is validated against the **observed call arguments**:
`m10=f/(f-n)`, `m14=-nf/(f-n)`, `m11=1`, positive diagonal lens terms, all other
perspective entries zero. Thus normal D3D hardware depth is confirmed, and the
adapter never guesses whether the far selector chose 1024 or 65536. D24 depth is
copied without reversal into the R32F input sent to NGX; reconstruction alone
uses the finite near/far pair.

`audit_projection.py` only reads a locally supplied PE32 executable and prints
findings. Example operations (replace `GAME_EXE` with the local path):

```text
python audit_projection.py GAME_EXE rtti FPlayerSceneNode
python audit_projection.py GAME_EXE calls 5C95
python audit_projection.py GAME_EXE dis 4EF3F5 --span C0
python audit_projection.py GAME_EXE dis 5BBD40 --span 19A
python audit_projection.py GAME_EXE dis 5C41ED --span 1CB
python audit_projection.py GAME_EXE dis 5BBC00 --span 140
python audit_projection.py GAME_EXE dis 5BE510 --span 100
```

`calls` is a byte-pattern candidate scan, not a full disassembler; its reported
sites must be verified by disassembly. The `dis` operation uses local Visual
Studio BuildTools `dumpbin`. No binary data is written or downloaded.

## Scope and failure behavior

Guesses are not passed to temporal reconstruction: absent, stale, duplicate,
out-of-order, non-finite or mismatched camera/projection data returns false for
the caller's spatial fallback. Projection/FOV changes, eye sequence gaps, camera
mode/view/cinematic/recenter epochs, large cuts and stale history request reset.
Output textures and histories belong to each eye separately.

Motion vectors are camera-only, current pixel to previous pixel, in pixels,
with scales `1,1` and no jitter. Independent animation, weapons, particles and
disocclusion need separate treatment; neither this test nor a successful NGX
evaluation establishes full motion-vector parity or headset visual quality.
Source DSV selection is draw-vote-based and restricted to compatible D24 surfaces;
it is not an engine-object identity hook.

Production profiles are untouched by this test. For isolated integration only,
`BVR_BS2_TEST_ISOLATION` makes `game_ini.cpp` require an absolute
`BVR_LAB_GAME_INI` named `Bioshock2SP.ini` and valid sibling `Shared.ini`; absence
fails closed instead of searching the user's real profile. Isolation of the
engine's own config/save filesystem is a separate lab-proxy responsibility.
