# Replay cleanup and default construction performance

This pass removes repeated graph discovery when discarding temporary replay allocations and reuses the existing closed-generic class plans when constructing or copying defaults. It does not change authored initialization or skip lifecycle and index removal hooks.

Measured against Neowyn `ryanbliss/more-destructables-and-tools` at `05c43b32`, using the same locally exported resources and Unity 6000.6.4f1 Linux Editor for both versions. The SDK baseline is `8b35be28`, after the previous initial-load improvements and transition notification fix.

| Phase | Baseline warm median | Updated warm median | Change |
| --- | ---: | ---: | ---: |
| Project store load | 944.90 ms | 946.89 ms | 0.2% slower |
| Client initialization | 1,419.59 ms | 1,326.05 ms | 6.6% faster |
| Authored startup | 121.77 ms | 160.07 ms | 31.4% slower |
| Combined load | 2,496.73 ms | 2,459.67 ms | 1.5% faster |

Each version ran in a separate process with no competing verification jobs. The four warm trials each created a fresh store and client; combined load is the median of trial totals. The first cold-process observation was 4,982.45 ms before and 4,854.06 ms after. These small samples do not establish a reliable cold-start improvement. Startup and GC timing varied, and the overall improvement is modest. These are Editor measurements, not player-build or frame-time guarantees.

[Raw measurements](initial-load-replay-cleanup-2026-10-29.json) preserve all five trials. Follow the [initial-load reproduction steps](initial-load-2026-10-29.md#reproduction), with the baseline above. No profiling instrumentation was present in these runs.

Separate instrumented runs isolated replay cleanup. Across five trials, the baseline cleanup phase took 163.59–211.46 ms and the allocation-ledger implementation took 21.12–22.47 ms. Those phase timings establish where work was removed, but must not be extrapolated to whole-load speedups.

## Correctness coverage

The new nested replay test retains a modified Session clone, repeatedly fails construction in a nested default, then successfully replays. It checks that temporary allocations disappear and existing Session rows and their values survive. Existing tests exercise closed generic construction, schema invalidation, constructor replay, collections, and listeners.

Neowyn validation uses the current door transition body, including reuse of the existing main character, save/reopen, subsequent transitions, inventory notifications, NPC scheduling, and tooltip lifecycle. Validation changes remain local to the test checkout; no Neo source is pushed.

Final validation passed with 5,429 SDK tests passed, zero failures, and 14 skipped. All 13 selected Neowyn regressions passed. Rig verification and C# formatting verification passed.
