# BioShock 1–2 installer · historical 0.2.15

Requested by Carlos on 2026-09-10. None of the 46 mod files changed; cores/launchers were not rebuilt. Retains [per-game MSI architecture](SINGLE-GAME-MSI-0.2.14.md).

## Agreed UI changes

- Compact first page: “Select a game”, dropdown/buttons, without introductory or other-game-management paragraphs.
- “BioShock Remastered” without “(1)”.
- Confirmation omits the paragraph explaining that the other game is unchanged and no launcher/game starts automatically.

Only specified text removed; behavior unchanged. No automatic launcher/game startup after installation.

## Actual failure fixed

BS2's 0.2.14 attempt ended **1603**, not success, at 17:53:20. The user could close the wizard, but BS2 0.2.14 was not registered. Beta-reading rejection occurred before removing its files.
Local log: BioshockVR/bs2/WindowsInstaller/Logs/bs2-20260910-155240.log.

Actual beta recorded `BioShock 2 VR DLSS-DLAA Beta.lnk`; old reader accepted only `BioShock 2 VR DLSS-DLAA.lnk`. Previous fixtures used the latter and missed the real case reported by Carlos.

Now **only those two exact names** are accepted within the expected desktop. Beta path has separate `@beta-shortcut` backup identity, never confused with normal/new versioned shortcuts. Inventory/hash/directory/link/original checks remain; no authority expanded to unrelated files.

Beta validation also runs before InstallInitialize without file/component-registration writes, then repeats during backup to detect intervening changes.

New reader validated **21 actual records** and Beta shortcut read-only, without rewriting the user's manifest.

## Verification

- 40 migration unit checks: allowed names/location, foreign files, originals and distinct Beta-shortcut recovery.
- Isolated native MSI: normal install/recovery and beta migration with/without a pre-beta shortcut; failures after removal, after copying and during uninstall.
- Final 46-file extraction/SHA-256 and 46 historical component identities compared with source MSIs: PASS.
- Final MSI text/control audit: PASS.

Family: eed8abaf4d2145029f4bf067b5a9f1ab.
Reports: artifacts/single-game-isolated/<family>/single-game-0.2.15/.
Final: **204 integration checks, 30 MSI operations, all PASS**, plus 40 migration unit checks.
Reports: test-result.json (74/12), test-result-standalone-beta-new.json (65/9), test-result-standalone-beta-original.json (65/9).
Same-source isolated SHA-256:
`745BBE52CFD62F5AAABFAD4BF25902228CC25B95A5D02CB9CDEE3AEFCA88CB40`.

Tests use private paths/profiles/registrations; no full-game copies or game launches. Actual files/registrations checked before/after; delivery does not install into real games.

Preview capture could not inspect the intended window because another application was foreground; that app was not operated. Text, geometry/content verified without installation. User visual acceptance remained pending.

## Delivery

`BioShock-1-2-VR-DLSS-DLAA-0.2.15-CANDIDATO.msi`.
SHA-256: `4776D6F209512CB22213C58006677733F0A7D0DD8E699339EFFB55117DBB5BAC`.

**Desktop / Lanzadores MOD VR**. Launchers still install in their game's Build/Final, optional desktop shortcuts. 0.2.13/0.2.14 MSIs not overwritten. No GitHub publication at this stage. Another-PC installer and VR testing remained pending.
