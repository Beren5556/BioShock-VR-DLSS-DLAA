# BioShock 2: historical exit-crash investigation

Date: 2026-09-08, Europe/Madrid. Status at that point: **reproduced and narrowed down; not fixed**.
Authorized scope was diagnosis. Production code was not changed and the beta was not rebuilt, installed or published.

## Conclusion

`Bioshock2HD.exe+0x4FF0FE` reads a null pointer in the game executable after requesting window closure. It reproduces without DLSS, without creating OpenXR, with all four mod subsystems skipped and **without loading bioshockvr.dll**.

NGX/DLSS, OpenXR and this beta's new temporal hooks are therefore not necessary to trigger this variant. It matches a failure documented in the base mod before this adaptation. Evidence points to the engine's shutdown path but does not establish who nulls the object or exclude all environmental influence: the final control retains the isolation XInput proxy and external game-loaded components. This is not “pure vanilla” validation.

It also does not prove that the other early-morning sites (`+0xC312D2`, `+0xC37362`) share exactly the same cause.

## New tests

All ran from the menu, windowed, private 1024×1024 resolution, separate profiles/save copies. WM_CLOSE was requested; confirmed/saved “Exit to Windows” menu flow was not tested.

Evidence root:
`D:\BioShock2VR-DLSS-Lab\game-cc819bfd-7129-4494-97ff-2b9fd80f81a6\runs`.

| Run / PID | Actual configuration | First close-time AV | Test ending |
| --- | --- | --- | --- |
| off-5e58b01c / 3684 | NORMAL; BVR_SKIP=xr; BVR_VEH=1; adapter/D3D active | 0xC0000005, +0x4FF0FE, READ address 0 | CDB reached second chance; closure exceeded 10 s. Only the lab process was terminated. Not natural-exit timing. |
| off-83222d24 / 20668 | NORMAL; BVR_SKIP=input,adapter,d3d11,xr; BVR_VEH=1 | Same instruction/null read | CDB captured/detached; process exited 0xC0000005. |
| off-76963be7 / 20120 | bioshockvr.dll absent throughout; LAB proxy present | Same instruction/null read | CDB captured/detached; process exited 0xC0000005. |

Second-control initialization logs confirm no input, adapter or D3D11 hooks and no OpenXR creation. The third CDB module inventory lacks bioshockvr.dll; `lm m bioshockvr` returns no module and no bioshockvr.log is generated.

Each run retains exit-debugger-trace.log; the last two also retain close-result.json. Preparation writes run.json: its hashes describe prepared files, not proof of loading. In the third control the DLL was renamed **after** preparation; debugger inventory and this note describe the effective configuration.

## Technical capture

Already-installed x86 CDB 10.0.26100.3916 attached to each PID while the window responded. No game-memory patch was used for diagnosis.

- Exception 0xC0000005, read 0x00000000.
- Address Bioshock2HD.exe+0x4FF0FE; EAX=0.
- Access follows engine global chain +0x1A638F0 → +0x4C → +0x44. The final member yields the null pointer read by the next instruction.
- Estimated stack matches all controls: +0x30DEF9, +0x30CE43, +0x2E4C49, +0x30F8CE, +0xCDBC5E.
- CDB warns of missing unwind information; these returns are not a reliable symbolic stack. Captures do not show USER32 in this chain or prove an exception inside WndProc.
- First capture's .ecxr failed, but exception record, current registers and instruction agree. Later captures obtained current data without relying on .ecxr.

Debugging changes exception handling and shutdown timing. These tests locate the first AV, not normal-exit timing or natural exception-filter behavior.

## Existing base-mod behavior

`src/core/util/crash.cpp`, `src/core/ui/overlay.cpp` and `src/core/framework/dllmain.cpp` were unchanged from HEAD `1de552a`. This adaptation did not introduce their exit behavior.

WndProc marks shutdown on WM_CLOSE, WM_DESTROY or WM_ENDSESSION. Thereafter the filter generically classifies exceptions as known host failures, omits dumps and calls `TerminateProcess(..., 0)`. A 15-second watchdog also exists.

**Exit code 0 and no dump do not equal clean shutdown.** “terminating cleanly” described forced termination hiding the failure result, not orderly resource release. Generic classification does not establish the cause of future exceptions.

In four early tests the AV arrived 8–16 ms after WM_CLOSE, before normal OpenXR cleanup or DLL_PROCESS_DETACH logs. It was not the 15 s watchdog or DLSS host's 3 s exit wait.

Relevant historical references (line numbers refer to the original document revisions):

- `docs/bioshock2/ENGINE_NOTES.md:1236–1341`: earlier +0x4FF0FE analysis/all-hooks-skipped control. The old no-proxy control had no exception observer and alone did not prove vanilla failure.
- `docs/bioshock2/ENGINE_NOTES.md:1414–1433`: alternative menu/save exit. Disabling forced flush then caused a hang and was reverted; do not propose it again without a demonstrated safe protocol.
- `docs/STATUS.md:4423`: same address during inactive gameplay. Matching addresses cannot classify a failure as exit-only without run state.
- `docs/BS2-TEST-RESULTS.md:94`: beta test issue.

## Limits and proposed subsequent work

1. Distinguish orderly shutdown, shutdown crash and protective termination; preserve the real error and do not call TerminateProcess clean.
2. Identify what destroys/nulls the object and why it is still read. No fixed-address jump or merely swallowing the AV; the site also appears outside shutdown.
3. Capture +0xC312D2/+0xC37362 separately, compare menu/window exit and verify saving, cancellation and slowness before changing the watchdog.
4. Validate any patch with NORMAL/DLAA/DLSS, real headset and later another computer. These tests do not replace that validation.

These were proposed next steps, **not implemented changes**.

## Separate lab issue

First attempt off-a4248059 ended during simulator startup, before requesting exit: Application Error 1000, bvr_xrsim32.dll, 0xC0000409, offset 0x315DE. This is not the investigated AV or a demonstrated real-OpenXR-runtime failure. It was excluded; later controls skipped XR. The simulator is a lab tool, not the beta's shipped runtime.

## Final state of this investigation

- All 30 protected originals, including real configuration/saves/binaries, matched the prior inventory byte for byte.
- Temporarily renamed LAB DLL restored with the same hash.
- No test game, debugger or helper remained running.
- Production core, launcher and installer were not rebuilt/changed.
- No fix was applied and the issue was not marked resolved.

| File | Final verified SHA-256 |
| --- | --- |
| Desktop 0.1.0-beta installer | A61BCCE385C0095645C67FE27A937E0D2B661E3C8CE2805F401C273EA73D9D6D |
| Production core | 8DFD11347E8B80623CC3678BD030BD9B77B76FACA9BEF3766C1E66E224D0554A |
| Beta launcher | 613772D164550745AB3A58924CF8A156EBCB95DE312869D01166361CA4A0EF8D |
| Restored LAB core | A32EABCE3EE62A2EC502875AF45A21D19857E215A5630DB675A2B7D8A503E4F6 |
