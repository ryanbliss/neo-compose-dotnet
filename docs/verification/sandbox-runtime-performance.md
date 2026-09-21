# Sandbox locality and runtime performance

Measured September 21, 2026 in Unity 6000.5.4f1 on the Neowyn development Mac. The candidate uses this SDK, the paired Compose #995 export/generator, and local Neowyn keyed listeners and authored `HasTrait` changes. These are combined integration results, not isolated SDK-only improvements or release-player guarantees.

## Workload and semantic checks

`NeoSandboxPerformanceTests.AccumulatedSandboxActions` creates a disposable save and progressively plants 0, 12 and 36 strawberries in a six-by-six patch. It edits soil, plants through the real placement validator, opens the real chest UI twice at each stage, places a two-cell bed outside the plants' neighborhoods, and walks with a stationary mouse and equipped watering can. The real scene, renderer, controller, UI and NeoScript remain active.

The fixture checks persisted placement counts, nonempty active trait profiles, equality with authored `HasTrait`, actual movement and target-cell changes, and zero plant-score refreshes for a distant bed insertion. The scene parity test separately exercises planting, watering, harvesting, reactions, furniture and their original UI.

The baseline uses the SDK branch before this performance change and the previous app invalidation. The candidate retains all trait and preference calculations but shares the plant's score model with hover/flyout UI and uses the legacy game's keyed neighborhood subscriptions. Duplicate callbacks coalesce per frame; disposal cancels pending callbacks.

Do not benchmark production's pre-#995 computed collection bindings: raw dictionaries filtered with `OfType<IReadOnlyPlantTrait>` silently empty the active traits. A run caught in that state was rejected. The valid candidate was regenerated with the actual local generator and passed the semantic assertions.

## Results

At 30–36 plants, the mean maximum frame gap in each operation's following-frame observation window was:

| Operation | Baseline frame | Candidate frame | Baseline action | Candidate action |
| --- | ---: | ---: | ---: | ---: |
| Soil edit | 2552.05 ms | 38.67 ms | 6.22 ms | 1.97 ms |
| Plant seed | 3100.91 ms | 145.02 ms | 325.42 ms | 97.44 ms |
| Chest open | 239.67 ms | 108.90 ms | 172.25 ms | 50.39 ms |
| Bed placement | 3226.93 ms | 81.82 ms | 304.80 ms | 74.12 ms |

Soil/plant averages contain six operations, chest two, bed one at 36 plants. Raw samples and all 0/12/36 phases are in the accompanying CSVs. Allocation observations include following frames: dense soil averaged 82.3 MB before / 11.2 MB after; planting 137.6 MB / 49.5 MB; bed 129.9 MB / 29.4 MB; chest 18.1 MB / 11.3 MB. Other editor work can contribute to frame/GC measurements.

The original scene's equivalent six-by-six workload measured soil/plant/bed frames of 4.30/4.60/2.90 ms. Original walking averaged 3.63 ms, max 6.27 ms; candidate walking averaged 6.85 ms with a 130.19 ms outlier. Both current walking fixtures use 1.6 seconds of input. The earliest Neo baseline used 120 frames, so its walking rows are not a matched duration comparison. Original scene content and available planting coordinates differ from NeoWorld; this comparison establishes regression magnitude rather than identical-world causality.


### Repeat after complete local export and background-input setup

A second focused run passed the full sandbox UI check, both benchmarks, and clock/currency/lighting validation (4/4). The local export now also includes dialogue-node aggregates and localization inputs. Both movement fixtures explicitly enable and restore `Application.runInBackground` so an unfocused editor continues simulation. Raw samples are `sandbox-runtime-repeat.csv` and `sandbox-runtime-legacy-repeat.csv`.

At 30–36 plants, mean action/frame costs were soil 2.11/39.32 ms, planting 150.20/200.57 ms, chest 49.62/106.50 ms, and bed 76.73/86.36 ms. The planting maximum frame was 456.98 ms. Neo walking averaged 4.57 ms, max 17.03 ms; the original averaged 3.59 ms, max 6.75 ms. Original soil/plant/bed frames were 3.87/3.92/3.94 ms. This repeat confirms the remaining regression and variability; the earlier table is not a reliable upper bound.

## Attribution and remaining work

- Indexed generated-class inference removes unrelated saved-value searches. Keyed app subscriptions and `ContentChangedCells` stop order-only changes from refreshing every plant.
- Incremental isolated insertion retains occupied-cell indexes and validates only the new footprint. Bed validation fell from roughly 32–36 ms to about 2 ms. Bed cost is now flat at 0/12/36 plants, and a distant bed refreshes no plant scores.
- Bound delegate equality eliminates repeated listener removal/re-registration during inventory initialization. Chest panel creation remains roughly 40 ms.
- Row-keyed alias refresh, scalar reads, cached schema dispatch, native placement-record queries, query-only pattern transforms and immediate-call bookkeeping reduce interpreter allocations without changing effects or ownership.
- `HasTrait` tests one trait directly; same-species matching rejects a different species before evaluating its stage. Authored tests compare this against the complete active-trait list across species, stages and harvest states.

The candidate still misses the 10 ms target. Dense soil operations spend roughly 31 ms refreshing scores, including 25 ms in preference measurement. Bed construction/replay still costs roughly 37 ms across detached construction, cloned data replacement and Save adoption. A shared evaluation-context experiment produced no reliable whole-frame improvement and was removed. Global lookup binding was measured and was not the gardening bottleneck. Neither side-effecting getters nor constructor effects were suppressed.

[SDK #184](https://github.com/ryanbliss/neo-compose-dotnet/issues/184) retains the unresolved sandbox regression, including the walking outlier and the user's chest hang that the fresh fixture did not reproduce. [#183](https://github.com/ryanbliss/neo-compose-dotnet/issues/183) tracks startup/animation timing.

## Validation

Full SDK EditMode suite: 2,357 passed, 0 failed, 11 skipped. Focused tests cover exact alias refresh, scalar live reads, delegate identity, direct versus stored patterns, query ordering/dependencies, no generated-wrapper requirement, multi-link insertion, occupied-cell rejection and unrelated index retention. The Neowyn benchmark passed its semantic assertions; see the PR for the final full Neowyn suite result.
