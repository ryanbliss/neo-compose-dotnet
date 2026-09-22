# Complete-graph replay and allocation measurements

Follow-up to SDK 0.39.6 and issues #178, #183, and #184. These issues remain open. The changes reduce proved redundant work; they do not establish native-game parity or a universal 10 ms frame budget.

## Workloads

Use the real Neowyn checkout, bundled export, generated bindings, and NeoWorld through NeoMenu. The sandbox fixture places 0, 12, and 36 strawberries, edits soil, opens storage twice per stage, places a two-cell bed, and walks with actual input and a stationary watering-can cursor. It verifies plant counts, active trait contents, placement footprints, target movement, and zero plant-score refreshes for the distant bed. Saves are disposable. UI and gameplay calculations remain enabled.

Baseline and candidate use Unity 6000.5.4f1 on the same Mac and editor session. The baseline temporarily restores the unchanged 0.39.6 SDK source, then the candidate source is restored. Do not run timing samples alongside SDK tests, deployment, or temporary diagnostic logging. Profiler recording is off; bounded ProfilerRecorder samples and phase markers remain active. Allocated-byte samples cover the action and following observed frames, not just the synchronous method.

## Causes and changes

- A stored class replacement was treated like removal of constructor defaults. Neowyn's placed plants and beds often had a replay footprint but no virtual values or default-child links. Cloning stack data into the object and adopting it into Save therefore reran the enclosing constructor without supplying any missing data. Complete local graphs now reuse their stored rows. Changed constructor inputs bypass this path, even if the same transaction also supplies a complete replacement. Sparse fields, construction metadata changes, and remote updates retain replay.
- Completeness uses the constructor's stored-field predicate, excluding functions and computed properties. Existing empty expansions can also prove an intentionally absent field. Changed ancestor paths come from the parent index, so proving a replacement does not walk unchanged inventory/world siblings. Container membership itself is not a construction recipe change. Placement validation remains intact.
- Replay cleanup formerly copied all Session IDs, rescanned Session for the difference, and inspected unrelated wrappers. Nested allocation scopes now record only their own rows and wrappers. Cleanup runs on exceptions too.
- NeoScript Class.Clone formerly copied Session keys and scanned the store again to find newly allocated rows. Its write plan already contains that exact set. The resource budget is checked before publication, so an oversized clone cannot leave rows behind.
- Read-only NeoScript calls no longer eagerly allocate construction tracking sets, readonly-binding sets, or row-alias indexes. Context forks share invocation caches and budgets; persistent call frames avoid copying the entire call stack. Existing class-inheritance caches serve more runtime consumers.

The local Neowyn inventory panel also stops updating each slot twice while constructing the panel. It sets the lock state during the original initialization pass. Prefabs, layout, text, tooltips, selection, and input handlers are preserved. This app change is not part of the SDK commit.

## Remaining work

Initial variant construction/replay, rendered object adoption, dense preference evaluation, and storage UI construction still consume measurable frame time. No trait score, tooltip computation, placement rule, or input behavior was removed. There is no arbitrary getter memoization or new persistent constructor-result cache.

The reported 30 GB editor memory growth and ten-second chest hang were not reproduced by the clean fixture. After restarting with profiler recording disabled, repeated test runs stayed around 3 GB resident. That is an observation, not a proven cause or leak fix. Mono's per-thread allocation API returned zero in diagnostic probes and was rejected as allocation evidence.

## Getter memo and spawn reachability (same branch, later pass)

Same fixture (`NeoSandboxPerformanceTests.AccumulatedSandboxActions` over the real Neowyn checkout, 36 placements), same editor session, SDK profiling instrumentation removed before measuring. Median per sample; `frame` is the worst frame in the sample, `plantInfo` is the PlacedPlantInfo refresh that follows the action.

| action | before (ms) | after (ms) |
| --- | --- | --- |
| plant.place action | 58.8 | 14.4 |
| plant.place frame / plantInfo | 48 / 21 | 39 / 16.5 |
| bed.place action | 33.8 | 9.8–13.7 (3 samples) |
| soil.edit action | 2.0 | 1.9 |
| soil.edit frame / plantInfo | 22 / 17.7 | 20 / 12.9 |
| selectStage per sample | 3.6 | 0.8 |
| chest.open action | 44 | 44 (36 is game UI prefab construction; NeoScript share ~4) |

Causes fixed in this pass: two full Session reachability walks per spawn (adoption assertion and move-candidate check, ~2.7 ms each), per-node static-member resolution inside the owned-parent lookup, whole-collection scans inside the reachability walk, and getter results recomputed after every commit even when the commit touched unrelated rows (sprite frame writes and world-time ticks clear a whole-cache memo every frame; row-level dependency invalidation keeps 63 of 72 eligible getter dispatches per soil edit as hits).

Remaining per-frame cost is the interpreter's per-call overhead in `TraitPreference.Measure` (one grid query plus `Count` over neighbors calling `Matches` → `HasTrait`, roughly 230 µs per Measure) and NeoCellPattern constructions for `Grid.Translate` (about 150 µs each, 16–25 per sample).

## Interpreter per-call overhead (same branch, later pass)

Micro-benchmark over the bundled Neowyn export loaded through `NeoTestSaveStack.ClientFromSchema`, calling through `NeoNSFunctionRuntime.InvokeImmediate` with a warm client. 500 warmup calls, then 9 rounds of 4000 calls; the table reports the median round in microseconds per call. Unity Mono reports zero for per-thread allocated bytes, so allocation counts were not measured.

| call | before (µs) | after (µs) |
| --- | --- | --- |
| static `ResolveElapsedEventHours(5, 3)` (arithmetic only) | 3.89 | 2.31 |
| `tier.QualifiesForGrant(3)` (one member read) | 4.52 | 3.04 |
| `rank.Threshold(0)` (loop over 35 levels) | 55.5 | 46.2 |
| `rank.Eligible(0, 100)` (NeoScript → NeoScript call into Threshold) | 64.5 | 49.5 |
| `ExecuteResolved` with a resolved signature | 3.52 | 1.94 |

Causes fixed: a fresh execution option set, expression-handler closures, and a continuation `ExpressionResumeState` per immediate call (immediate frames never suspend, so a shared stateless state and client-wide cached handlers serve every call); two closures allocated by `NeoScriptExecutor.Execute` per call; a new loop body scope per iteration; a new result object for every fallthrough, break, continue, and normalized return; the parameter-default filler and diagnostic subject strings formatted on the success path; runtime dispatch cache keys concatenated per call; and read-only members re-materializing their declaration default row (a dictionary clone for structured defaults) on every read.

On the sandbox fixture (same editor session as the getter memo table, medians): plant.place PlantInfo refresh 16.5 → 14.3 ms with `Measure` 12.2 → 10.5 ms and `Matches` 3.8 → 3.1 ms; soil.edit PlantInfo refresh 12.9 → 11.6 ms; bed.place action 13.7 → 10.5 ms; plant.place action and chest.open unchanged at 14.3 and 44 ms.

Remaining cost in the loop case is row member reads: each `rank.Levels[i].PointsRelative` is about 3.6 µs (1.6 µs per hop) through the reverse index, inheritance chain, surface schema, member, and value-store lookups. That path has no redundant work to remove without a per-site inline cache keyed on schema generation, which is not on this branch.
