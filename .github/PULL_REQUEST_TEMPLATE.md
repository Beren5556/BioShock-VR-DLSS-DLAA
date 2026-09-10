## Purpose

Describe the observable change and why it belongs in this fork release.

## Validation

- [ ] I ran `scripts/Verify-Repository.ps1 -BuildLauncher`.
- [ ] I did not add game files, credentials, local payloads or standalone NVIDIA components.
- [ ] I documented any functionality or compatibility changes.
- [ ] For installer changes, I completed the isolated tests described in `docs/TESTING.md`.
- [ ] For English localization, I ran `node scripts/localization/english.mjs verify` and checked text layout.

## Risks and recovery

Describe known risks, any manual VR tests performed, and how to reverse the change.
