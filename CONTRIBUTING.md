# Contributing

Use a focused branch, explain the observable effect of the change and include
the validation performed.

This is a fork of [BioShock VR](https://github.com/VR-Stereo-Hub/bioshock-trilogy-vr),
created by Mohamad Balouza. Every contribution must preserve that attribution
and clearly distinguish upstream work from changes specific to this fork.

## Project rules

- Preserve compatibility with the BioShock VR v0.8.2 base identified in PROVENANCE.md.
- Do not present experimental fork changes as original-project features.
- Do not commit game files, dumps, RenderDoc captures, credentials, the NGX SDK
  or standalone NVIDIA binaries.
- Keep NORMAL, DLAA and DLSS 4.5 as the public modes. DLSS 5 is outside this release.
- Do not expose FXAA, the spatial upscaler or the full Bioshock.ini editor
  without an explicit product decision.
- Add tests or verifiable justification for every functional change.
- For the English edition, keep runtime changes text-only and preserve
  configuration keys, values, per-game identities and upgrade component paths.
- Keep public documentation and UI text in English on the English branch.

Before proposing a change:

    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Verify-Repository.ps1 -BuildLauncher
    node scripts/localization/english.mjs verify

For installer changes, also follow docs/TESTING.md and use isolated fixtures.
Never use a real installation as a writable test fixture. Preserve historical
release artifacts rather than silently replacing them.
