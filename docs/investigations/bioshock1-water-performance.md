# BioShock 1 water performance investigation

Historical investigation, 8–9 September 2026. Paused at the user's request on
9 September. This English consolidation preserves the findings and limits;
the [complete original dated notebook](https://github.com/Beren5556/BioShock-VR-DLSS-DLAA/blob/efbafc38e4f5cff28b7b960e1bf28d00c62ee758/docs/investigations/bioshock1-water-performance.md)
remains immutable in the Spanish source history.

This is not an outstanding request to install diagnostics or run more headset
tests. For the accepted defaults and final recommendations, see
[Performance](../PERFORMANCE.md), [architecture](../DLSS-DLAA-ARCHITECTURE.md)
and the [0.2.17 English report](../ENGLISH-0.2.17.md).

## Question and measurement limits

The user observed large drops near water in BioShock 1 and at the underwater
start of BioShock 2. NORMAL could remain smooth while DLAA or DLSS slowed down.
Lower internal resolution alone therefore did not explain total frame cost.

The first BioShock 1 log ended at 21:16:35 on 8 September:
254,538 bytes, SHA-256
`A66638C7E425E23B6DCEB542058E1A85BF664B6F916CF109E4F870E23B999E78`.
Analysis was stored under
`artifacts/candidate-0.2.6/evidence/`.
The analyzer excluded zero samples, the first three seconds after changes,
and intervals with fewer than three samples. Its rate is a roughly
once-per-second internal counter, not compositor FPS.

There were 20 applied changes and one rejected change, with no automatic stereo
shutdown. The rejected 1434 → 3584 request restored 1792 → 3584 safely.

| Time | Mode / internal → output | Samples | Min–max | Mean |
|---|---|---:|---:|---:|
| 21:12:00.984 | DLAA 3072 → 3072 | 36 | 71–72 | 71.94 |
| 21:12:39.561 | NORMAL 3072 | 11 | 71–72 | 71.91 |
| 21:13:46.383 | DLSS 2048 → 3072 | 22 | 71–72 | 71.91 |
| 21:14:24.996 | DLSS 2390 → 3584 | 40 | 58–90 | 76.72 |
| 21:15:19.128 | DLSS 1792 → 3584 | 36 | 60–90 | 75.75 |
| 21:16:00.147 | DLAA 3584 → 3584 | 3 | 52–52 | 52.00 |
| 21:16:05.978 | NORMAL 3584 | 23 | 89–90 | 89.96 |

These are observations, not a controlled benchmark: the cap changed from
72 to 90 Hz and neither scene nor view was held constant.

## Initial depth and synchronization audit

The selected D24 depth candidate could be copied repeatedly while depth votes
were collected. In 32 stable windows, the measured ratio was 7.075–10.231
copies per eye, averaging 8.412. A logical 3584-square D24 texture is 49 MiB;
8–10 full copies represent 392–490 MiB per eye of logical copy volume,
**not measured physical memory traffic**.

A 24-iteration GPU microbenchmark on an RTX 4090, driver 32.0.16.1656,
measured:

| Size | Copies | Unchanged source, ms | With clears, ms |
|---|---:|---:|---:|
| 2048 | 1 | 0.0194 | 0.0228 |
| 2048 | 8 | 0.1530 | 0.2423 |
| 3072 | 1 | 0.0445 | 0.0505 |
| 3072 | 8 | 0.3538 | 0.5300 |
| 3584 | 1 | 0.0640 | 0.0718 |
| 3584 | 4 | 0.2555 | 0.3703 |
| 3584 | 8 | 0.5105 | 0.7215 |
| 3584 | 12 | 0.7653 | 1.0045 |

This isolated test excluded the game, NGX and OpenXR. It neither bounded their
cost nor explained the whole observed slowdown.

The bounded CPU wait before depending on host GPU output is a safety mechanism:
a dead host must not leave the game waiting forever on an unsignaled GPU fence.
Removing that guard was not an acceptable optimization. A D3D11 Flush submits
work asynchronously; it does not prove GPU completion.

Motion vectors were camera-derived, not object/water-specific. The evidence did
not identify a particular water shader, fog effect, resource or NVIDIA defect.
Default sharpening was zero. The optional post-filter was tested separately
(21 values, both eyes, 48 alpha checks), not confused with deprecated NGX
sharpening.

## Diagnostic sequence

All diagnostic builds were temporary experiments, not release defaults.

### Diagnostic 1: separate guide capture from host processing

At 2560, compare A = NORMAL, B = native guide capture without the host,
C = DLAA. Two cycles measured A/B at approximately 72 and C at means
64.56 / 65.32, with a range of 59–72. CPU host waits were about
2.52–2.54 ms per eye. Guide capture was valid; a 72 Hz cap could still hide
additional work in B. The user's description of cloud-like content was not
proof of a particular fog or smoke pass.

### Diagnostic 2: sampled scene timings

The probe sampled scene, pre-capture, guides and output CPU/GPU intervals at
1/16. GPU queries were read without DONOTFLUSH violations or forced waits:
unready measurements were skipped, never game frames. The timer used four
slots; its regular sampling policy was 1/64. CPU and GPU intervals had different
sample counts and could overlap, so their averages must not be summed.

The log ended at 00:08:48, 195,688 bytes, SHA-256
`FEE88F78298EB7B2642F01407F184C28C39A87FB0C7F860575CBA6B555FD7297`.
In a slow view, per-eye scene CPU rose from about 1.9/1.7 to 4.9/4.6 ms.
Output CPU was 2.6–2.9 ms and GPU elapsed output 1.6–1.7 ms, while the
internal rate fell from 72 to 59–61. A/B could sustain 72 even with a
4.7–4.8 ms scene. Across 96 valid windows there was no fallback or dropped
frame; two startup tag mismatches were separate from steady state.

### Performance 1: overlap the left-eye host work

The sequence became:

1. Submit the left eye.
2. Render the right-eye scene.
3. Submit and resolve the right eye.
4. Resolve the left eye and deliver the pair.

The left XR image remains owned until resolution or safe fallback. Bounded
waits, fences, independent eye histories and same-frame pairing remain intact;
no old-eye frame is silently substituted. Initial opt-in tests passed
38 transport, 38 timer, 94 policy, 54 controller and 48 sharpening checks.
WARP tests were not NVIDIA/OpenXR acceptance tests.

Candidate DLL:
`FE9C2DF6CA8BC51F1A0845CEF445330E2550AE6F700A7757CEA48A9A9E4F31B5`.

The user reported a major improvement. From 00:40:57 to 00:47:20, 22,088
pairs completed with no aborts. Left CPU wait reached approximately zero;
right waits were about 3.055 ms at DLAA 2560 and 6.230 ms at 3328, in different
views, so this was not a fixed-scene scaling benchmark.

### Performance 2: reuse an unchanged winning depth copy

Reuse requires the same winning depth source and an unchanged write counter.
Voting and selection are never postponed. Draw depth writes, clears, transfers
and command lists invalidate reuse; complete hook coverage is required.
Context slots 36, 39, 40 and 110 were audited. R24 depth and stencil were not
treated as interchangeable.

Thirteen WARP tests passed; ten synthetic read-only subpasses required one
copy. That synthetic saving was not claimed as a game result.

Candidate DLL:
`0A1B76490E99A7A7C9A7AF980B1D40FE98921F4BB688A93982D18E5787AAB3BC`.

The user did not perceive improvement at the 3072 latency threshold.
The 08:56:29–09:00:10 log showed all 19 hooks active and 20,062 avoided
copies out of 50,144 (40%). Approximate right-eye CPU waits were 2.87, 3.15
and 4.22 ms at DLAA 2560, 2816 and 3072. SR 1792 → 2560 and 2150 → 3072
measured 1.95 and 2.64 ms. These are elapsed waits, not exclusive NGX cost.

### Latency 1: full bridge without NGX

A = NORMAL, B = complete host bridge with a copy replacing NGX, C = DLAA.
This keeps the same internal geometry, without SETRES or INI changes.
Transport version 2 requires explicit acknowledgement; an older half-image
behavior cannot satisfy the test. The separate helper/profile did not replace
the installed host, NVIDIA runtime or user INI.

Predicted display time measured using QPC is not a deadline or compositor
latency. The diagnostic did not alter GPU wait policy or poses. Validation
included 204 real-NVIDIA checks, 97 policy, 54 controller, 38 overlap,
43 timer, 13 depth and 48 sharpening checks.

At 3072, 11:45–11:47, A averaged 72, B 71.93 and C 67.05 internally.
Right wait rose from 2.137 to 4.533 ms. B's approximately 0.05 ms helper
copy excluded the wait for input and therefore was not total bridge cost.
Scene CPU was around 4–4.4 ms. At a 13.889 ms XR period, elapsed time after
xrWaitFrame was 4.188 / 6.732 / 8.899 ms for A/B/C; xrEndFrame was
0.08–0.11 ms. There were 4,642 complete pairs, no aborted pair and 518 clean
scene samples. Startup and rebuild events were distinguished from steady state.

Log SHA-256:
`133E017A2CBDF4447A93B23993A659AAF2DFF4610BAF2B07762E09D4EAB6812C`.

### Performance 3: overlap independent tail work

After right submission, prepare independent HUD/aim work before waiting.
Move desktop work after capture, reuse the left shared-color resource
(36 MiB at 3072 RGBA8), and send the unchanged IPC-8 request in one pipe write.
The requested HUD placement became (-0.10, +0.38, -1.20).

Validation: 279 real-NVIDIA checks including 3072 input / 4608 SR output,
47 transport tests, 97 policy, 54 controller, 43 timer and 48 sharpening.

Candidate DLL:
`53A6F78940D77643727032A39A3FD2775415165FE794F48FAA394697B0FCD3A9`.

At 13:06–13:08 all three modes stayed around 72 internally. However the
diagnostic preserved the preceding SR internal size of 2150, not the earlier
3072: only 48.98% of those pixels were rendered. This run cannot establish
the optimization's benefit at 3072.

The user reported VD latency 44–45 ms in A/B and about 14 ms more in C.
Predicted intervals of 46.136 / 46.178 / 59.916 ms were consistent with a
roughly one-refresh difference (13.889 ms), but did not prove a specific queue
or runtime fault. Right waits were 1.312 → 1.987 ms; HUD overlap was only
about 0.005 ms. There were 5,291 avoided-copy pairs, no abort and 498 clean
scene samples.

Log SHA-256:
`2BC97AA384454EDB87E7B99ABC0F84C8340E42EA8A7EE6E802B55EDAEE419EDE`.

### Performance 4: earlier XR delivery and runtime observation

Move desktop presentation after xrEndFrame only for the compatible focused
BioShock 1 path. NORMAL, menus, fallback and other games remain unchanged;
no flush delay was introduced. Observe optional VD
`ovr_SetFloat(AppGpuTime)` calls without changing arguments/results, adding
I/O or delaying the call. Public VDXR 1.0.9/1.1.0 source was context, not
proof of the installed 1.0.10 implementation.

Validation: 36 observer/delivery, 97 policy, 54 controller, 43 timer,
47 transport and 48 sharpening checks.

Candidate DLL:
`3F4D5E120151E038DAED5A307C7AB10CDD62311DA1443EDFAE0D2ECB49B643A2`.

At 3072, 13:43–13:47, the user reported A at 44 ms / 72 FPS / GAME 7 ms,
B at 58 ms / 72 FPS, C at 58 ms / about 62 FPS / GAME 14 ms.
B already incurred the extra latency without NVIDIA processing.

Elapsed time outside xrWaitFrame averaged 10.460 / 12.592 / 14.511 ms.
C intervals reached 14.861–14.894 ms, exceeding the 13.889 ms period.
These elapsed wall times are neither exclusive CPU busy time nor VD GAME.
C's internal rate was 67–68 rather than the user's VD reading of about 62.
Observed AppGpuTime around 7.7–7.9 ms did not equal GAME 14 ms.

Log SHA-256:
`107FEFDECC762E6231F135F4E7D089D271569548A1B0EA484DBD7E38C029F5A7`.

### Performance 5: account for input readiness and the whole frame

Partition exclusive elapsed CPU intervals for engine, desktop Present, XR,
capture, guide generation, submission, waits and output. The helper's earlier
measurement started after the queue Wait, so it omitted input readiness.

A sampled input-ready event (1/16) used the same absolute five-second output
deadline, without adding GPU commands or changing the fence protocol.
Output/process/input ordering was preserved. Coalesced observations were
marked unavailable (-1) with separate denominators, not invented timestamps.
Validation passed 35 accounting, 22 input-event, 56 transport, 36 delivery,
97 policy and 54 controller checks.

Candidate DLL:
`79EA8CFB4058F6ECB592B6072C918EE5B800FA9D01730AB93B117CBA204DB8A2`.

At 14:41–14:44, the user reported A at 42 ms / GAME 4 / 72 FPS,
B at 58 ms / GAME about 10 / 72 FPS, and C at 58 ms / GAME 15–16 with drops.
In 88 consistent accounting reports, B's approximately 2.10 ms wait split
into 1.69 ms until input ready and 0.41 ms after it. It was neither a pure-copy
cost nor a measured 16 ms bridge operation.

At the same DLAA 3072 resolution, yaw around 32700 yielded 15.18–15.48 ms
cycles; yaw around 17800 yielded 13.88–13.89 ms. Engine left/right time changed
from about 9.7–9.85 to 6.1–6.2 ms while the wait increased from 3.9 to 5.1 ms.
Guide CPU time was around 0.125 ms; right-scene GPU elapsed time fell from
about 5 to 3.5 ms, including capture and queue effects.

Log SHA-256:
`D391117AC5F0317C4C38D11439A9DF84FE725072B76A5687EB9383C64A14434C`.

## Conclusions retained in the release

- Stereo host work and CPU/GPU scheduling matter alongside pixel count.
  DLSS can reduce scene pixels without guaranteeing a shorter complete frame.
- The evidence justified safe overlap, depth reuse, tail overlap and earlier
  delivery. It did not justify removing bounded waits or mixing eye histories.
- View-dependent scene work can cross a headset refresh boundary. Internal
  frame counters, VD FPS, GAME, predicted intervals and exclusive GPU times
  are different measurements and must not be substituted for each other.
- Later Extra 1/2/3 effect comparisons identified dynamic reflections as the
  largest graphics cost and ripples as a smaller one. The historical Extra 3
  state was not the final default: subsequent releases defaulted both off.
  See [the accepted performance notes](../PERFORMANCE.md).
- No specific water/fog shader defect, driver defect or universal NVIDIA
  regression was established. No quantitative performance claim is inferred
  from the English text-only rebuild.

The four accepted optimizations are enabled in the stable integration preset;
performance, latency and critical-path diagnostics are disabled. The English
edition preserves those settings and behavior.
