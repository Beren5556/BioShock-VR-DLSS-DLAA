# Performance — BioShock 1 VR DLSS/DLAA

## Investigation status

Investigation paused by Carlos on September 9, 2026. Tested optimizations and controls remain. No sufficiently specific additional fix was identified to continue investigating compatibility with the most expensive effects.

Closure version 0.2.7 consolidated the mod, simplified only Image and added Windows Installer (MSI), without opening the launcher at completion. It retained values, F1/F2/F3 navigation and tested optimizations. Version 0.2.9 retained them, added a final F1 graphics page and changed defaults following Carlos's later decision.

## Findings

Effect tests used the same `0.2.6-perf5` DLL and changed graphics settings only. These are user observations in the tested scene, not improvement percentages generalizable to every computer.

| Option | Observation |
| --- | --- |
| Real-time reflections | Largest identified penalty; substantially reduces DLAA headroom. |
| Water ripples | Moderate penalty, also with reflections off. |
| Shadows | Smaller additional worsening over the previous combination; moderate observed impact. |
| High-quality soft particles | Little perceived impact; the last test did not isolate this setting from the reflection change. |
| Post-processing, high-quality post-processing, distortion, high-detail shaders | Reenabling these in sequence did not reproduce the major drop. |
| Fluid detail | No independent test; High was present in all three extra tests. |

- No demonstrated fixed 3072 DLAA ceiling: with reflections/ripples off, resolution could be raised above this with good results while retaining other original effects.
- Steam remained visible during good performance; its presence alone does not explain the problem.
- Reflection/ripple costs accumulate. A greater-than-additive interaction was not quantified and no defective shader was identified.
- Tests identify expensive settings, but do not fully separate game cost from possible amplification by the mod's temporal integration. They do not establish an NVIDIA bug.
- The Virtual Desktop total-latency jump on mode changes remains unexplained. It is neither assumed to be an indicator error nor confused with one effect's cost.

## Compared configurations

| Configuration | Reflections | Ripples | High-quality particles | Other tested effects |
| --- | --- | --- | --- | --- |
| Extra 1 | Off | Off | Off | On; fluids High |
| Extra 2 | Off | On | Off | On; fluids High |
| Extra 3 | On | On | On | On; fluids High |

Extra 1 provided the greatest headroom. Extra 2 retains ripples with a smaller penalty than reflections.

**Defaults since 0.2.9, following the user's later decision:** reflections/ripples off; other seven options on, fluids High. High-quality particles remain on. No unperformed measurement is attributed to this exact combination; it follows the extra tests' findings. Versions 0.2.7/0.2.8 used Extra 3 defaults.

Reflections, ripples and shadows have a red asterisk and one shared “High performance impact” footer, as requested for interface simplification. That warning does not erase the differences above or block any option. Upgrades retain preferences; **Defaults** adopts the new selection. **All on** refers only to the nine tested options, not FXAA or the old spatial upscaler.

## Retained optimizations

- Left-eye temporal work overlaps the right-eye scene.
- Reuse unchanged depth captures, avoiding redundant copies while retaining copies after writes.
- Reuse already submitted color and overlap independent frame-tail work.
- Submit to the headset before desktop-mirror-only work on the compatible temporal path.

The first major improvement was confirmed in-game. No individually demonstrated gain is attributed to every later change. Diagnostic counters/A/B/C modes are not additional optimizations or user options. Required eye/game/DLSS-host synchronization remains.

## Traceability

Technical history: [performance investigation](investigations/bioshock1-water-performance.md).
Per-test INIs/logs/results: local `artifacts/effects-isolation-2026-09-09`, excluded from public distribution.
Common test DLL SHA-256:

`79EA8CFB4058F6ECB592B6072C918EE5B800FA9D01730AB93B117CBA204DB8A2`.

This conclusion is specific to BioShock 1. It did not modify BioShock 2, the DLSS 5 project or tested NVIDIA 310.7.0.0.
