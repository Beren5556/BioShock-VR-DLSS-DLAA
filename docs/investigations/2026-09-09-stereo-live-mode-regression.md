# Stereo loss after mode change — September 9, 2026

Historical record: fixed in 0.2.11 source, then pending headset testing. Separately delivered 0.2.10 UI revision did not contain this fix.

## Retained evidence

artifacts/stereo-regression-20260909-225743/bioshockvr.log and dlss.ini.
Verified installed DLL SHA-256:
`FDCF823158C871E040C036A1ED2AF4D24B30A486227F0BFF5377411AE9B85952`.
Log identifies 0.2.10, build beren5556-v0.2.10-msi-steps.
User installation/settings were not changed during diagnosis.

## Observed sequence

- 22:56:54.488: NORMAL → DLAA, output 3428×3428; rebuilding starts.
- 22:56:55.972: both hosts ready; change confirmed; L/R builds continue.
- 22:56:57.418: reentry watchdog detects 300 ms without progress.
- 22:56:58.321: stereo disabled at its 1.2-second threshold.
- 22:56:58.895: bridge reports eye 1 timeout, frame 54.
- 22:57:01.895: helper shutdown reaches 3000 ms and terminates owned processes.
- From 22:57:03: progress returns to 72 presents/s, but 2nd=0/s and no stereo RE-ARMED. Further mode changes do not recover the second eye.

## Difference from the older failure

ReconfigureWindow protection remains in the current DLL and covers rebuilding. This timeout occurs **after APPLIED**, during DLAA evaluation; not evidence that the earlier fix was removed.

Hypothesis: bounded bridge wait mistaken for engine deadlock, followed by recovery overdependent on sampling g_activeDepth==0 for five ticks. Merely extending reconfiguration or blindly forcing stereo is insufficient: retain user intent, genuine-failure recovery and eye separation.

## Implemented 0.2.11 fix

- BoundedActivity describes already bounded IPC/output-fence/helper-exit waits. Both watchdogs consult it even after bridge readiness fails. Wait durations, protocol and eye delivery order unchanged. Nested scopes do not renew deadlines; beyond-limit stalls become visible again.
- Watchdog no longer rearms from its own thread by sampling g_activeDepth==0. BuildDetour recovers before tagging the left eye, in a new gameplay build after at least 500 ms progress.
- Only automatic disable recovers: requires stereo intent, unpoisoned engine, inline rendering, active hooks, gameplay and recent CalcView. Explicit OFF cancels intent even after watchdog disable. No camera/settings/image-option changes.
- Pure tests 40/40. D3D11/WARP 53/53, including actual 1.4-second wait previously exceeding the disable threshold; failed-helper output without an unsatisfied GPU wait or cross-eye mixing. Zero D3D11 debug warnings.

Limits: tests do not reproduce NVIDIA/headset. Host-timeout origin is unproven and disappearance of every timeout is not claimed. The fix prevents a recoverable failure becoming persistent 3D loss.
