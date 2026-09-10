# Historical local validation · 0.2.13 candidate · 2026-09-10

## Result

Candidate built and isolated checks passed. **Not installed in real games, published or yet headset-validated at this stage.**

Integration commit 27c175ca7d5e95bf5788265dc382b9b331dde7c7, branch codex/bioshock-1-2-v0.2.13. Local merge completed; subsequent documentation does not change tested binaries.

## Identified artifacts

- Single BioShock-1-2-VR-DLSS-DLAA-0.2.13-CANDIDATO.exe (70.9 MiB):
  `63701688677BFF132917677893B60AE6460391719CA4793422ED94ECD4ED6E1A`.
- Embedded accepted BS1 0.2.11 MSI, not regenerated:
  `2A5365E332DC311C0E010AD0BECEEAB34E905F66C6F40AB5CF44824EDAD9F660`.
- Embedded candidate BS2 0.2.13 MSI:
  `EE45605FEC0BAD54D11EB48C29B13CED5041F1A884ED5A6F8B987AF080947033`.
- Nondistributable isolated-family MSI:
  `CF181C9DBEC5C47C1F97C73FCF3D24FC25F97D55A6D7E8E0D9F231010C1122C0`.
  Its 23 payload files match the BS2 candidate.

Under artifacts/integration-0.2.13/bundle and msi/bs2; local Git-ignored artifacts. Hashes identify this build, not a stable release.

## Checks

| Suite | Result |
| --- | --- |
| Core | 12/12 executables pass |
| Temporal WARP | BS1/BS2 pass, 13 reuse cases each |
| BS2 saving | 20/20 including rollback/external conflict |
| Launchers | Both build/self-test; Image visually reviewed |
| Beta migration | 29/29 unit checks |
| Final BS2 MSI | 114 checks, 21 MSI runs, 0 rollback-security warnings |
| Common EXE | 24/24 resources, separate identities, UI/exit |
| PowerShell syntax | 50 scripts, no parse errors |
| Repository | Policy, versions, licenses/manifests and git diff --check pass |

Isolated MSI product uninstalled; reports/recovery copies retained. MSI fixtures use only the legitimate executable needed for target checks and fake data; no complete game copied or launched.

Evidence:

- artifacts/integration-0.2.13/tests/20260910-105023/results.json
- artifacts/integration-0.2.13/migration-tests/results.txt
- artifacts/msi-isolated/0bd55642b6e640158728629532379e54/0.2.13/msi-test-result.json
- artifacts/integration-0.2.13/bundle/verification.json
- artifacts/integration-0.2.13/bundle/manifest.json
- artifacts/integration-0.2.13/bundle/selector-final.png

Final MSI fixture: BvrMsiBattery-18ac224b6a5b471db1b2cd71aeba8eef.
All 85 modified/new original-BS2-reference files still match snapshot 7b4514090d5d1f319b463f158d076f4155a5347e.
BS1 reference remains 065a43e, accepted payload 23/23 hashes.

Read-only actual installed BS2 manifest: Format=3, GameId=bs2, 0.1.1-beta, 21 records. Four previously existing originals retain expected-hash backups. This inspection did not migrate them.

## Remaining work at that point

- Agree BS2 candidate installation; headset NORMAL/DLSS/DLAA, resolution changes, water viewed from outside, hands/HUD, loading and exit.
- Real coexistence/install in both orders. Disjoint identities do not replace functional testing.
- Derive native BS2 F4 if live parity required; currently launcher save/restart.
- Complete launch parity: BS2 direct start on failed Steam request exists; BS1's additional direct-start offer after timeout was not yet ported.
- Another computer; publish only after acceptance.

No identical-FPS, complete-parity or VR-stability claim. No PC shutdown: earlier shutdown authorization was exceptional to a different session.
