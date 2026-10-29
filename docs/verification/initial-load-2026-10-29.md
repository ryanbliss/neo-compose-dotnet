# Initial load and evaluator performance

Measured in the Linux Unity 6000.6.4f1 Editor against Neowyn `ryanbliss/more-destructables-and-tools`, commit `05c43b32`. The SDK baseline is `00112be8`, including the previous resolved-clone optimization. Neowyn source was exported locally with CLI 0.92.3; no source was pushed to Neo.

| Phase | Baseline warm median | Updated warm median | Reduction |
| --- | ---: | ---: | ---: |
| Project store load | 1,096.28 ms | 937.60 ms | 14.5% |
| Client initialization | 1,805.60 ms | 1,444.81 ms | 20.0% |
| Authored startup | 116.89 ms | 110.41 ms | 5.5% |
| Combined load | 3,000.14 ms | 2,484.42 ms | 17.2% |

Combined load is the median of each trial's total, not the sum of phase medians. Each trial creates a fresh project store and client. The four warm trials share process-level JIT and serializer caches. The first trial in each Unity process measured 5,352.46 ms before and 4,928.55 ms after. Those are single cold-process observations, not a reliable estimate of cold-start improvement.

[Raw measurements](initial-load-2026-10-29.json) contain all five trials per version. Benchmark runs had no other SDK/Neowyn test jobs running. These are Editor timings, not player-build or frame-time guarantees. Allocation counters returned unusable values on this Unity Mono runtime, so no allocation reduction is claimed.

## Reproduction

Use the same Neowyn checkout and exported resources for both SDK versions, changing only its local package reference. Disable OAuth cloud sync on a cloned config. Fetch `TestProjectSource.instance.Source.ReadProjectJsonAsync()` once before timing so the fixture's JSON rewriting is excluded equally. Supply that JSON through an `IProjectDataSource` whose partition reader delegates to the same test source.

In one EditMode test, run five trials. For each trial:

1. Start a stopwatch, create `NeoProjectStore` with the source, cloned config and a new `NeoInMemoryLocalSaveStore`, then await `store.LoadAsync()`.
2. Record the store time, restart the stopwatch and await `new NeoLoader().Load(store.CreateNew())`.
3. Record the client time, restart the stopwatch and create `new NeowynNeo(rawClient)`. Assert `neo.Save.World.CurrentArea` is non-null before recording startup time.
4. Dispose the generated client and store before starting the next trial.

Run baseline and candidate in separate Unity processes. Preserve authored startup hooks and sum all three timed phases, so moving work from initialization to first use cannot masquerade as a speedup.

## Changes measured

Nested JSON union converters populate through their existing token reader instead of walking the subtree once to skip it and again to populate it. Short strings use a per-read cache before the global intern pool. Closed generic construction plans are reused by binding identity. Stored constructor replay returns its resolved rows directly, creating wrappers only when a variant or incoming override needs one. Constructor provenance serializes fixed-shape vector/color leaves directly.

Generated class reads bind descendant wrappers on access or subscription. After virtual replay, existing class wrappers still refresh, while newly constructed descendant wrappers already reflect that expansion and are not rebuilt immediately. Root loading and its asynchronous replay budget retain their existing behavior.

These changes save about half a second on warm fresh loads here. They do not make initial load instantaneous or establish a multi-fold overall speedup.
