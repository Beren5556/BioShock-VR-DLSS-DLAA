# English 0.2.17 release verification

Date: 11 September 2026. Source branch: `codex/english-v0.2.17`.
Spanish reference: `v0.2.16`, commit
`efbafc38e4f5cff28b7b960e1bf28d00c62ee758`.

This release changes visible language and version metadata, not rendering
behavior. Both Spanish mods were accepted by the user. That acceptance does
not constitute a headset test of the newly compiled English binaries.

## Frozen deliverable

- Asset: `BioShock-1-2-VR-DLSS-DLAA-0.2.17-EN.msi`
- Size: **38,461,440 bytes**
- SHA-256: `EA8D5A363F43A6EFF44A5187A2BCBC26ABDE6F55C6A989C6BD7A84A5B829F943`
- [Complete payload manifest](../release/manifest-v0.2.17.json)
- [Binary pins](../release/validated-mods-v0.2.17.json)
- [Machine-readable validation summary](../release/validation-v0.2.17.json)
- [Checksum file](../release/SHA256SUMS-v0.2.17.txt)

The byte-identical MSI was delivered to the user's requested desktop installer
folder. It was not installed into either real game. The Spanish MSI remains
unchanged at SHA-256
`1C881F0A27416198FC25A068A79044BEED7FCC37160F38E7AFE2AE09CE9A2371`.

The final English core is
`1A4568FEDE7C9FA58D24A3F7B424E9A3FA22234C8F5960A3A28DA299BFA53942`.
The x64 host, NVIDIA runtime 310.7.0.0, game proxies and per-game capabilities
are byte-identical to the Spanish base. Launcher hashes and all 46 files are
recorded in the manifest. Game EXEs/assets are not distributed.

## Scope and equivalence

`node scripts/localization/english.mjs verify` passes for **241** C#/C++ and
MSI files, with **1,364** mapped literal occurrences. It compares the working
source to the Spanish tag and permits only reviewed English literals, launcher
version strings and MSI language metadata. Configuration identifiers, parsing,
paths, resolution rounding, stereo histories, fences, waits and control flow
remain unchanged. The map checks formatting placeholders and line breaks.

English text covers installer actions/dialogs, both launchers, in-headset menu
labels/errors, user documentation and repository-facing prose. Historical
technical notebooks are identified as history; consolidated English records
link to the immutable originals. Old release manifests and compatibility
filenames retain their historical data.

The four stable optimizations remain enabled: left-eye overlap, depth-copy
reuse, tail overlap and early XR delivery. Performance, latency, critical-path
and BS2-isolation diagnostics remain disabled.

## Automated and presentation results

| Area | Final result |
|---|---|
| Core Release build | Passed |
| Core tests | 12/12 executables passed |
| Both frozen launchers | Self-tests passed, exit 0 |
| Real NVIDIA runtime / mod client | 16/16 checks for each game profile |
| Launcher previews | Seven tabs per game, 14 images reviewed |
| In-headset graphics layout | Nine rows plus restart notice fit: 416 of 444 pixels |
| MSI payload extraction | 46/46 file hashes verified |
| MSI presentation | 54 controls in bounds; English language 1033 |
| Upgrade identities | 48 payload/registration components unchanged; two new versioned shortcut identities |
| Legacy migration | 40 checks, no failure; historical snapshot regression passed |
| Payload profile selection | 25 checks passed |
| Standard isolated MSI operations | 74 checks across 12 operations |
| Spanish-to-English isolated upgrades | 56 checks across 10 operations |

The GPU transition test uses the actual `dlss45_client.cpp`, frozen x64 host,
NVIDIA runtime and each game's capabilities. It exercises DLAA → NORMAL,
deliberate unsupported 1474-pixel rejection, valid 1476/2950 DLSS, and repeated
DLAA/DLSS transitions with distinct eye images. It is a real GPU/host test,
**not** a game or OpenXR session.

Launcher previews were read-only and did not save profiles or start games.
Compact English cinema labels avoid clipping while retaining full explanatory
help. MSI selector and longest folder dialog were also inspected through the
native Windows Installer read-only preview, including high-DPI rendering.
Control bounds alone are not claimed to prove all fonts/DPI configurations.

## Installation, recovery and upgrade coverage

The final isolated package contains the same final payload bytes, but uses a
separate test product/registry family. Its SHA-256 is
`1FA7ED509BDE79AB22941738131E8B683C7F8AF50D9C066329A1EDB84223C349`.

Its predecessor was built from unchanged Spanish 0.2.16 source with Spanish
MSI language metadata, using the same isolated family. Predecessor SHA-256:
`C95F31E90D33359FB935B9A19C2FEAEE97ECF70BEDD22BB982C271B767CBDB8A`.

Both suites passed under the same user's normally elevated Windows session.
The 22 MSI operations cover installation of both isolated mods, intentional
failure rollback, repair, uninstall rollback, reinstall, shortcut toggling,
cross-language upgrades with failures before/after retirement, successful
upgrades and final removal. Checks include the other game's payload,
registration, configuration and original-file backup preservation.
All test products were removed. Real game files/profiles/registrations were
not changed.

Final local evidence (generated, ignored by Git):

- `artifacts/integration-0.2.17/tests/20260911-002111/results.json`
- `artifacts/integration-0.2.17/runtime-tests/20260911-002502/results.json`
- `artifacts/integration-0.2.17/english-ui/final/`
- `artifacts/integration-0.2.17/msi/single-game/package-verification.json`
- `artifacts/integration-0.2.17/msi/single-game/english-presentation.json`
- `artifacts/single-game-isolated/16941903fe84472597bd873bd626f278/single-game-0.2.17/`

Local evidence contains machine paths, so the public validation file is a
sanitized summary, not a claim that private raw logs are bundled in Git.

## Negative results and limitations

An initial fixture nested deeply below the source checkout hit Windows path
length limits before payload writes. The failed evidence was retained; the
test-only fixture root was shortened and the full final suites passed.
This is **not** a fix or guarantee of arbitrary long-path installation support.

An initial test still expected a Spanish error substring and was updated to
the translated literal. An initial identity assertion also incorrectly
required version-specific shortcut GUIDs to remain unchanged; it was corrected
to distinguish stable file/registration components from new versioned shortcut
registrations. Neither change modified production runtime/installer behavior.

Earlier unpublished label-polish build rounds were retained separately.
Only the final hash above identifies this release and the desktop delivery.

No new physical-headset acceptance, second-computer installation, exhaustive
DPI sweep or universal performance guarantee is claimed. Those remain manual
validation opportunities, not results of synthetic tests.
