# Sandbox runtime optimization for #184

Issue #184 remains open: these changes reduce measured action time and allocations, but the complete 10 ms frame target is not established. The measurements below include both SDK and Neowyn changes.

## Method

Unity 6000.5.4f1 Editor on Linux, Mesa llvmpipe (LLVM 20.1.2 software renderer). Compare current SDK main `3ffcfee28980dd4351b94ef271ced2e04715dca7` and game main `5df9003d3ecf258a04447b86698c2a681a754c9e` against the paired candidate. Each version ran twice, serially, with no concurrent Unity tests. Profiling experiments and screenshot capture were disabled for these runs. This environment differs from the original issue’s Mac; do not compare its absolute times with those historical measurements.

The accumulated-action timing harness is unchanged. It starts a disposable Menu save, plants 0/12/36 strawberries, edits soil, opens the chest twice per population, places a distant two-cell bed and walks with a stationary cursor and watering can. It verifies plant counts, real trait scores, target-cell movement and zero unrelated bed-triggered plant evaluations. The candidate adds active-trait equivalence against HasTrait outside the measured windows. The original-scene fixture has since been retired, so these runs compare current main with the candidate.

Results aggregate the two runs at 30–36 plants: 12 soil edits, 12 placements, four chest openings and two beds per version. Walking uses every observed frame at 36 plants. `actionMs` times the synchronous call; `maxFrameMs` retains the existing maximum elapsed coroutine gap over the following three observations (one for walking). `allocatedBytes` includes complete observed frames, not just action allocations. Per-call profiler recorders and CSV output remain identical between versions; editor scheduling, rendering and fixture overhead are included in frame gaps. No timing assertion or smoothness claim follows from these means.

## Action time and allocations

| Action | Main mean action ms | Candidate mean action ms | Main observed bytes | Candidate observed bytes |
| --- | ---: | ---: | ---: | ---: |
| soil.edit | 0.790 | 0.758 | 37,352 | 40,626 |
| plant.place | 18.358 | 7.823 | 2,125,444 | 807,686 |
| chest.open | 70.960 | 4.451 | 8,702,964 | 413,550 |
| bed.place | 11.435 | 7.921 | 2,409,560 | 1,291,461 |
| water.walk | 0.001 | 0.001 | 30,849 | 25,836 |

## Observed frame gaps

| Action | Main mean maximum gap ms | Candidate mean maximum gap ms | Main peak ms | Candidate peak ms |
| --- | ---: | ---: | ---: | ---: |
| soil.edit | 5.656 | 5.414 | 7.278 | 6.223 |
| plant.place | 28.726 | 14.335 | 31.237 | 17.509 |
| chest.open | 153.768 | 19.640 | 159.522 | 25.792 |
| bed.place | 21.012 | 16.281 | 21.455 | 18.044 |
| water.walk | 5.916 | 6.304 | 38.067 | 38.189 |

Planting and chest actions improve substantially. Walking has no demonstrated improvement; candidate walking means are slightly higher, and both versions contain outliers. Planting, chest and bed frame gaps still exceed 10 ms. Keep #184 open for the remaining frame and allocation work and validation on the original hardware.

## Changes and semantic coverage

- Complete local constructor graphs retain their already-absent literal defaults and fresh variant stamp without replay. Sparse graphs, removed stored defaults, changed dependencies and storage-dependent replay retain their existing reconstruction path. Tests deliberately make constructors throw to distinguish safe reuse from required replay.
- Lookup-binding inference indexes authored, writable and virtual bindings while preserving ambiguity. Publication, replacement, deletion, virtual retirement and reset update the indexes; candidate/replay contexts retain the original scan. Tests cover stored replacement and fresh constructor publication.
- Virtual UUIDv5 construction shares a SHA1 instance per expansion and parsed namespace bytes. Golden Unicode, escaped paths, system prefixes and empty-source identities verify compatibility. Empty constructor arguments avoid reference inference.
- Neowyn prepares and reuses storage rows/buttons, releases subscriptions on close, restores exact enabled controls, and excludes hidden UI from navigation and raycasts. Tests exercise locked slots, previously disabled controls, reopening another chest, current-chest transfer, tooltip rebinding and persisted quantity.
- Placement guides retain equivalent tooltips and avoid quantity-only recreation. Mature variant sprite changes refresh existing obstacle-fade bounds without duplicate components. Plant, furniture, watering, inventory and menu tests preserve behavior.

## Validation

Full SDK suite: 2,685 passed, three skipped, one existing explicit run-versus-call timing benchmark failed its ratio; that benchmark passed when rerun alone. Final focused constructor/variant/runtime tests: 392 passed. Game focused UI/runtime suite: 15 passed and one stale test expectation failed; corrected inventory suite then passed both tests, including the expanded lifecycle and save round trip. Both accumulated performance runs passed all semantic assertions. Rig verification and repository C# formatting checks passed.

Blind replay skips and persistent transparent-menu rendering were tested and removed: they either risk constructor semantics or make steady walking slower. Temporary profiler markers, defines, screenshot code, local package references and Unity-generated asset churn are excluded. No public SDK API changes.

Raw reports: [sandbox-runtime-184-main.csv](sandbox-runtime-184-main.csv), [sandbox-runtime-184-main-repeat.csv](sandbox-runtime-184-main-repeat.csv), [sandbox-runtime-184-candidate.csv](sandbox-runtime-184-candidate.csv), [sandbox-runtime-184-candidate-repeat.csv](sandbox-runtime-184-candidate-repeat.csv).
