# BioShock 2: shutdown-order fix, historical 0.1.1-beta

Date: 2026-09-08, Europe/Madrid.

**Status at that stage: local shutdown validation; real headset and another computer pending.** Final sections distinguish verified lab window/menu exits, save checks and build/package hashes. Intermediate failures remain documented. These results do not certify a real headset runtime or count forced lab recovery as a pass.

Continues the preserved [exit investigation](BS2-EXIT-INVESTIGATION.md). Every test here used an isolated game copy with private profiles/saves, not the user's actual installation.
Evidence root:
`D:\BioShock2VR-DLSS-Lab\game-cc819bfd-7129-4494-97ff-2b9fd80f81a6\runs`.

## Located cause and fix

Full analysis of the function containing +0x4FF0FE corrected an incomplete earlier interpretation: it is not merely a display-settings function. It is `UGameEngine::Tick(float)`, with settings work near its beginning. The engine reads the first Client->Viewports element without first checking that the list is nonempty.

Tick **already has an empty-viewport exit branch**, but executes it after the invalid access. It calls native `RequestExit(0,0)` and returns. The fix brings that same decision forward after verifying engine/client identities. It invents no return value, writes no engine flags directly and does not catch a Tick exception to resume at another instruction.

Reproducible static derivation for x86 Bioshock2HD.exe SHA-256
`C2A31FB67B285C136203A4A6E739545177BE5753D972E6AEA0F44C65DDF22F8C`:

- Engine vtable +0x10BD7DC, slot +0xF4, thunk +0x174E, body +0x4FF0D0. Call +0x30DEF3 passes a float; callee ends with ret 4; caller consumes no result.
- Engine global +0x1A638F0; client at engine +0x4C; list data at client +0x44, count +0x48.
- Native branch +0x4FF33C…+0x4FF365: nonnull client, zero viewports, RequestExit(0,0), return. Failing access occurs earlier at +0x4FF0FE.
- RequestExit thunk +0x9345, body +0xB60C00, cdecl with two integers. First argument zero does not call ExitProcess: it posts WM_QUIT, activates main-loop exit and preserves the requested exit status.

The guard verifies prologue, accesses, original branch, epilogue, thunks, vtable slot and relocated operands. Mismatched executable/object identity disables the change. With viewports present, original Tick receives identical arguments. `BVR_SKIP=exit_guard` enables a controlled comparison without rebuilding.

## Why fixing Tick alone was insufficient

The first fix passed flat exit but **failed the next VR test**. This negative result is retained:

| Run | Intermediate variant | Observation |
| --- | --- | --- |
| off-06348445, PID 20448 | Tick guard, XR skipped | WM_CLOSE → WM_DESTROY → empty list → native exit → runtime cleanup → DLL_PROCESS_DETACH; code 0, no logged AV. |
| off-566074aa, PID 23544 | Same guard, NORMAL with OpenXR simulator | Another thread AV at +0xC3CFBD, read 0x80; unclean exit, actual code 0xC0000005. |

In the failed test WM_DESTROY arrived at 10:08:56.387; thread 15464 AV at .389; main thread 18588 reached the guard and began XR cleanup at .390. The AV therefore occurred **before** our runtime release. It attempted a method call through another object's null vtable. The log is a stack-address scan, not reliable symbolic unwinding; it neither identifies that thread as render/Flash nor proves one cause for every variant. Minidump writing failed, but exception log/code remained; the error was not converted to 0.

The second fix changes only verified BS2 window closure: on WM_CLOSE it calls RequestExit(0,0) **while the viewport is alive** and consumes the message. The native engine exit sequence then destroys the window, avoiding DestroyWindow invalidating objects while other threads still work. This is a causal shutdown-order improvement, not address-specific patches for every possible exception.

Menu paths are not intercepted through commands, keys or automatic dialog answers. Unverified identity forwards the message to the original procedure. Receipt of WM_CLOSE is logged separately from native exit acceptance.

## Menu gap and common exit point

Later run sr-a75cfe6c, PID 23752, found another gap: **the menu could accept exit and end the loop without another Tick or prior WM_CLOSE**. Empty-list/window guards alone did not request XR cleanup.

After real SR gameplay, the test separately established:

- Canceling Exit to Windows with Escape resumed gameplay without terminal cleanup. Telemetry recovered coherent stereo/history; cancellation was not accepted exit.
- Accepting exit/save created private slot `9_8_2026_10_31_35.bsb`, 967136 bytes. This proves a write, not clean subsequent exit or successful reload.
- CDB trace of verified +0xB60C00 recorded RequestExit(force=0,status=0), thread 0x5CB8 / 23736, the observed game thread. Immediate return +0x4F176F; stack places it in Tick via +0x50050E, wrapper and +0x30DEF9, not Present. Unsymbolized frames are address evidence, not guaranteed complete unwinding.
- WM_DESTROY followed at 10:31:38.218 with no XR cleanup log. Continuing from the breakpoint produced second-chance 0xC0000409, subcode 7 FAST_FAIL_FATAL_APP_EXIT, in lab runtime bvr_xrsim32+0x315DE. No preceding 0xC0000005 AV observed. CDB process code 1 **is not** the game exit code; the process observer produced no usable clean-exit result.

Private trace: sr-a75cfe6c/native-request-trace.log. Retained as **failed menu shutdown**, despite working cancellation/save writing. It demonstrates missing explicit cleanup on that route, not every internal detail behind simulator fail-fast.

The resulting hook targets the already fully fingerprinted RequestExit, the common point where the engine accepts exit after native dialogs/save flow:

1. Original always receives both untouched arguments and runs first. Its exceptions are neither caught nor transformed.
2. Only a return with force=0 confirms exit for cleanup. force!=0 retains forced behavior and is not called clean. Status is not replaced with zero.
3. A common latch set after the original and before release prevents repeat cleanup/reentry. Repeated native calls still receive their arguments.
4. Window/empty-list paths call this same point, without duplicate cleanup implementations. Tick/RequestExit hooks are published as a pair only when both enable. Failure attempts rollback of both; rollback failure is explicitly logged and any reachable detour remains passive forwarding.

No save answers are selected or cancellation dialogs bypassed. Menu save/cancel coverage was repeated with this hook; final results/observer revision below. Earlier window passes do not replace it.

## VR cleanup and error handling

Native exit acceptance requests terminal cleanup outside DllMain. A BS2 gate blocks new owned Present/Resize/initialization operations and all three temporal-guide callbacks. Observer/pacing threads must stop before releasing their resources; waits are bounded. Unsafe retirement retains resources and logs incomplete shutdown.

Release covers OpenXR session/instance, swapchains and DLSS helpers. Frame-finalization/resource-destruction errors persist and prevent “complete” cleanup status. Not all OpenXR calls offer timeouts; the confirmed-exit watchdog remains final protection, not proof of orderly release.

BS2 error policy:

- Unconfirmed requests alone do not arm the watchdog.
- Failures during confirmed exit retain report/error code.
- The 15-second watchdog uses WAIT_TIMEOUT, not zero.
- Other adapters' inherited protection is unchanged.

Code 0 alone remains insufficient: tests also require no close-interval AV, complete runtime cleanup and orderly DLL_PROCESS_DETACH.

## Intermediate corrected-window tests

These predate the final OpenXR-destruction error-reporting reinforcement; final-build repetitions are below.

| Run | Actual scope | Evidence |
| --- | --- | --- |
| off-e6e8e456, PID 18904 | NORMAL menu, OpenXR simulator | Native exit with one live viewport 10:14:13.040; cleanup .305; detach .455. Code 0, no logged AV. |
| dlaa-1a5027ad, PID 22516 | DLAA selected, menu, simulator | Native exit with one live viewport 10:16:00.867; cleanup 10:16:01.301; detach .464. Code 0, no logged AV. |

The second run has processed=0: menu fallback due to missing useful depth. **It does not validate active DLAA gameplay evaluation**, headset quality or performance. Simulation does not replace real Virtual Desktop/OpenXR.

Contract automation initially passed 364 synthetic cases: all fingerprint-byte mutations, two load bases, identity/viewport-count combinations. `--audit-image` adds six installed-PE checks (370 total), without running the game.

RequestExit adds 424 cases: argument preservation, original/cleanup order, forced mode, disabled function, repetition, reentry and original exception. Final build passed **794 with PE audit, zero failures**: 788 synthetic plus six executable checks. Also passed again: 23 exit-policy cases, 15 gate cases, WARP and 19 LAB-path-isolation cases.

## Remaining risks and acceptance criteria

1. SR menu save/cancel/resume and DLAA no-save were separately repeated with the common hook; final results below. This does not cover every dialog, engine state or caller on another thread.
2. Verify saves, not just process disappearance. Window exit adds no autosave and promises no save prompt. Prefer menu exit during gameplay. SR reloaded the earlier slot; subsequent DLAA reloaded the new SR slot with its hash preserved.
3. Final four window runs include NORMAL/DLAA/DLSS in lab gameplay. They do not replace separate menu/save or real-headset validation.
4. Repeat with real headset, focus changes, potentially blocked runtime and another computer. Reentrant/busy cleanup must report incomplete shutdown, not erase errors.
5. Historical +0xC312D2/+0xC37362 lack individual causal captures proving resolution in every circumstance.
6. Owned native-exit requests require verified fingerprints/identities. Observing original RequestExit requires the complete pair fingerprint, not a live client because it does not access it. Another executable build receives no fix and is logged accordingly.

## Lab instance blockage: not a passed test

After sr-a75cfe6c/PID 23752 failed, CIM showed a residual process with thread 23736 and 1163 handles while .NET already treated it as exited. A new launch showed “BioShock 2 already running”. This contradictory state alone does not identify an internal mechanism.

Run off-1533ecd5/PID 11556 stayed at `init complete; waiting for first Present`, never reaching gameplay and not testing corrected exit. Closing its dialog produced unhandled 0xC0000005 at Bioshock2HD.exe+0x30CB4C, 10:45:19.434. Dump: `off-1533ecd5/data/crash/bvr_20260908_104519.dmp`. **Not counted as PASS**, even though it eventually disappeared.

Recovery used CIM Win32_Process.Terminate exclusively on the residual PID whose path was verified as LAB. Identified test helpers were also closed. Two later checks found no residual BS2 process/thread. No thread contexts, registry or real-game files changed; Windows reboot was unnecessary. Forced lab recovery is not clean game exit; it enabled independent subsequent tests.

## Final results and delivery

### Build identification

The following runs' run.json/exit-test.json identify this build pair. Executed code is the **instrumented LAB core**; LAB/production binaries are not claimed identical.

| Artifact | SHA-256 |
| --- | --- |
| Executed LAB bioshockvr.dll | B4474C2681C46609A32E98AE848C2216B3EC18C2E94C8383FFE9C844908A751D |
| Associated production core | E6FB724A2D8024A85E8879972F8E1612A3724B7CD135EA1FE2B60AC03D0DB5B3 |
| Tested 0.1.1 installer | A685E0493294AD0CF0CC44286E6D5201D06CEC9462C5B1DE07C579DDE532C304 |

Installer suite: **16 checks, passed=true**, 08:45:27 UTC:
`artifacts/bs2-tests/installer-0.1.1-real-upgrade-f9043a4c174c40a5a4e6ea9b6630f490/summary.json`.

Covers install/repair/restore, actual 0.1.0 upgrade/downgrade rejection, injected rollback, conflict/original preservation and install-vs-launcher-open failures. No game launch; headset/other-PC explicitly pending.
Earlier `installer-0.1.1-real-upgrade-0719596bdd8745b1aaf6bc885a815629/summary.json` retains passed=false and 14 completed checks; not counted as a pass or removed.

### Final common-fix window exits

All four close-result.json reports: exited=true, exitCode=0, cleanExit=true, runtimeShutdownComplete=true, orderlyDetach=true; no first-chance/teardown faults **within the exit interval**. Logs show RequestExit(0,0) acceptance → complete terminal cleanup → DLL_PROCESS_DETACH. All use BVR_VEH=1.

| Run / PID | Scope | Last processing evidence | Local exit timing |
| --- | --- | --- | --- |
| off-ac33d37f / 18024 | Flat menu; skipXR=true | No XR session/DLSS evaluation | PASS: request 10:46:37.008; cleanup .158; detach .324 |
| off-d61eb964 / 18716 | NORMAL private gameplay, simulator | GAMEPLAY 10:46:50.963; no DLSS | PASS: request 10:46:57.733; cleanup 10:46:58.210; detach .621 |
| dlaa-0766826d / 15944 | DLAA private gameplay, simulator | 10:47:30.569: processed=1184, gen L/R=592/592; both coherent=1, hist=1, epoch=8; mixed=0 | PASS: request 10:47:30.778; cleanup 10:47:31.480; detach 10:47:32.060 |
| sr-22a15855 / 25356 | DLSS SR private gameplay, simulator | 10:48:04.208: processed=1050, gen L/R=525/525; both coherent=1, hist=1, epoch=8; mixed=0 | PASS: request 10:48:04.480; cleanup .996; detach 10:48:05.592 |

Counters are last written samples, not inferred final totals. “gen” means per-eye evaluation count, **not Frame Generation**. DLAA/SR logs also retain startup/menu fallback counts 758/762 and accumulated tagMismatch=1; describing the last coherent pair does not erase them.

**“No AV” limitation:** all three gameplay runs logged two pre-exit first-chance AVs at bioshockvr.dll+0x2BF68: NORMAL 10:46:57.193/.194, DLAA 10:47:19.307, SR 10:47:53.810. Processes continued to orderly exit. Offline analysis identified `v = sp[i]` in watchdog_all_threads(), `src/core/vr/openxr_runtime.cpp:1406`: an __try/__except-protected stack probe stopping at unreadable memory. Mapping came from build COFF openxr_runtime.obj and DLL B447…, not an available PDB: function RVA 0x2BCF0 + offset 0x278 = 0x2BF68.

All three data/pacetrace.log place these exceptions in a diagnostic thread dump triggered by a four-second Present pause before shutdown. Enumeration ends NORMAL .197, DLAA .310, SR .812; each seen=64, reported=64, openFail=0, suspFail=0, ctxFail=0. This is handled diagnostic SEH, not an unhandled teardown AV. Logs remain. Precise claim: orderly shutdown with no AV during its interval, not no first-chance exception anywhere in the session.

### SR menu: reload, cancellation and save-before-exit

sr-af337369, PID 9992, LAB B447…: **reviewed menu-exit PASS**, subject to the original observer caveat below. This is not inferred from WM_CLOSE: native menu acceptance has no preceding WM_CLOSE.

| Check | Evidence |
| --- | --- |
| Previous-slot reload | menu-test.json identifies 9_8_2026_10_31_35.bsb, SHA-256 D58A9F68BABFDD07238A6FE28532EFAA78FD8F017C9C2959B7FEAAB34C76AA3A from sr-a75cfe6c. Main agent visually verified gameplay after Continue; GAMEPLAY logged 10:49:08.210. |
| Cancel and resume | menu-cancel-result.json, 10:51:19: cancelAccepted=true, gameResumed=true, nativeExitAlreadyCalled=false, shutdownAlreadyStarted=false. 10:51:20.810: gen L/R=2026/2026, coherent=1, hist=1, epoch=10. |
| Save before exit | New private 9_8_2026_10_52_33.bsb, 969738 bytes, SHA-256 CB844DE5E9F8B76A194D3EED9A03D46A81E9B0C6B5AB44204A6E11B7A8DFD139; inventoried in menu-close-result.json. Earlier slot hash unchanged. |
| Native exit/cleanup | RequestExit(force=0,status=0) accepted 10:52:36.900, thread 24436; XR cleanup 10:52:37.157; detach .622; observed process code 0. |

Last accumulated counters: processed=7122, gen L/R=3561/3561. Dialogs fall back due to missing depth; the last menu sample is not a new gameplay pair. The cited coherent resume sample belongs to the time after cancellation.

**Original negative report is not rewritten.** menu-close-result.json retains cleanExit=false/faultInLog=true despite code 0, native acceptance, complete cleanup and detach. The first observer searched **the entire log** for first-chance AVs: two +0x2BF68 probe lines occurred at 10:49:13.853, over three minutes before accepted exit.

Separate menu-close-reviewed.json links the original and records cleanExit=true, faultDuringExit=false, unhandledFaultInRun=false, firstChanceLogLinesBeforeExit=2, originalReportPreserved=true. This is temporal-scope review, not exception deletion or an exit-code change.

Subsequent `scripts/Watch-BS2-MenuExit.ps1` starts the interval at native RequestExit acceptance. It still requires code 0, complete cleanup/detach, rejects interval first-chance/cleanup faults and unhandled-failure evidence anywhere in the run. Earlier first-chance lines are explicitly counted. This applies the WM_CLOSE exit criterion without hiding prior diagnostics.

### DLAA menu: new SR save reload and exit without saving

dlaa-fb6f44e2, PID 26140, LAB B447…: **no-save menu-exit PASS**, a new run separate from SR. menu-test.json identifies newly created 9_8_2026_10_52_33.bsb with SHA-256 CB844DE5E9F8B76A194D3EED9A03D46A81E9B0C6B5AB44204A6E11B7A8DFD139. Main agent loaded/checked it; GAMEPLAY at 10:56:03.413. Save writing is thus complemented by subsequent reload of that exact file.

| Check | Evidence |
| --- | --- |
| DLAA after new SR slot load | processed=4006, gen L/R=2003/2003. Returning to dialogs shows missing-depth fallback, not new gameplay evaluation there. |
| No-save exit choice | Main agent confirmed Yes to exit and No save in the second dialog (Escape), checked in the UI, then native exit occurred. |
| Saves preserved | Same three private slots; names/sizes/hashes unchanged, none created/modified on no-save exit. New SR slot remains 969738 bytes and hash CB844…. |
| Exit/cleanup | Native acceptance 10:57:37.785, thread 6632; cleanup .989; detach 10:57:38.434. menu-close-result.json: code 0, cleanExit=true, acceptance/cleanup/detach true, faultDuringExit=false, unhandledFaultInRun=false. |

This uses the temporally scoped observer and retains firstChanceLogLinesBeforeExit=2: SEH +0x2BF68 probe at 10:56:09.766, before acceptance, not during cleanup. No report rewrite is needed and those lines are not treated as nonexistent.

Documented local validation totals **four window exits and two menu exits**, with each row's limits and transparent SR observer review. No universal guarantee for another executable, RequestExit from a worker holding engine locks, Present/Resize reentry or blocked external runtime. Real headset/another computer remained pending. Final protected-original checks and delivery status belong to the main agent.
