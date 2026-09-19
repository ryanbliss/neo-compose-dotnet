# Neowyn movement benchmark

`NeowynMovementBenchmark.cs` exercises the actual Neowyn menu, bundled export,
new local save, generated character setters, walk animation and player input.
It belongs outside the SDK test assembly because it depends on Neowyn types.

## Reproduce

1. Make an isolated copy of the current Neowyn checkout, including its local
   content edits. Do not run this in a developer's active game checkout.
2. Copy `NeowynMovementBenchmark.cs` into the copy's `Assets/Tests/Editor`.
3. Set `com.ryanbliss.neocompose` in the copy's `Packages/manifest.json` to the
   baseline SDK, then to the revised SDK using an absolute `file:` package path.
   Keep all other content and settings identical.
4. Run each version serially, with no other test suite running:

   ```sh
   unity test /absolute/path/to/neowyn-copy --mode EditMode \
     --filter NeowynMovementBenchmark --output /tmp/neowyn-movement.xml --timeout 900
   ```

The test enters Play Mode, starts a fresh game, measures warmed position/facing
assignments and animation ticks, then records eight seconds each of idle and
alternating left/right input. The first second of each frame sample is discarded.
It asserts that the player actually moves and reports distinct player body
instances to catch accidental respawns. `NEO_MOVEMENT` lines in the NUnit XML
contain the measurements. Neowyn's local menu mode disables OAuth cloud sync;
the test uses its own temporary save directory and removes it after a successful
run. Failed runs can leave that temporary save directory behind.

These are uncapped Unity batch-mode measurements, not visible-editor or shipping
build FPS. The loop includes test instrumentation. The idle/walking comparison
does not isolate SDK overhead from physics and gameplay, and is not a comparison
against a historical Neowyn build without the SDK.

Set `NEO_MOVEMENT_DIAGNOSTICS=1` to extend each phase to 30 seconds and record
slow frames, completed GC collections, heap size, and selected
[ProfilerRecorder](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorder.html)
timings. Diagnostics add instrumentation; compare normal runs to normal runs.

## Recorded results, September 19, 2026

Apple M3 Max, Unity 6000.5.4f1. Neowyn `aedf02c` plus its current local content;
export SHA-256 `afa5b194b4d62c9d08dd015407c3dfbd12b68781edacb17ecb758343a1429f1f`.
Baseline SDK `6243222`; the initial revised SDK was `a98f753`. Both serial runs used the
same isolated game copy, with no other test suite running.

| Measurement | Baseline | Revised |
| --- | ---: | ---: |
| Position assignment, mean | 14.5179 ms | 0.2169 ms |
| Facing assignment, mean | 17.2356 ms | 0.0985 ms |
| Steady-state animation tick, mean | 4.2038 ms | 0.5835 ms |
| Idle, mean FPS | 1,557.6 | 1,612.2 |
| Walking, mean FPS | 94.9 | 1,095.8 |
| Walking frame, median | 1.3536 ms | 0.6040 ms |
| Walking frame, p95 | 34.2422 ms | 1.3673 ms |
| Walking frame, p99 | 51.7868 ms | 3.0547 ms |
| Walking frame, maximum | 75.1494 ms | **631.4865 ms** |
| Distinct player bodies during walking | 28 | 1 |

The revised run's 631 ms outlier is retained in these figures. A later 30-second
diagnostic phase reproduced a 652 ms stall while idle. Its cause remains
unattributed; this change does not claim to eliminate every hitch. Earlier runs
with concurrent SDK tests reproduced 14 FPS walking on the baseline and about
1,026 FPS after the first implementation. Use the serial comparison above for
the primary result, since background load differed.

The initial 30-second diagnostic run with SDK markers measured 1,560.6 FPS idle
and 1,472.1 FPS walking, walking median 0.5896 ms, p99 2.6210 ms, maximum
29.8192 ms, and one player body throughout 120 units of movement. Periodic turns
still caused roughly 15–20 ms frames: segment re-resolution accounted for
11–14 ms and script row refresh about 0.9–1.2 ms. The direct facing setter and
steady-state tick measurements do not include that deferred resolution cost.

Further profiling isolated 2.8–3.0 ms per turn in removing and recreating
animation dependency subscriptions. Tracks now retain subscriptions to unchanged
value IDs and only add/remove changed dependencies. This avoids delegate copying
and allocation while preserving getter execution, side effects, dependency
switching, and disposal. Getter-result caching was not chosen because these
getters can have side effects.

A subsequent serial 30-second diagnostic run measured 1,810.1 FPS idle and
1,666.5 FPS walking, walking median 0.5100 ms, p99 1.5357 ms, maximum
32.5672 ms, and one player body. Ordinary turn frames remained roughly 11–13 ms;
segment re-resolution was typically 8.5–11.1 ms. These are separate diagnostic
runs with different instrumentation from the table above, not a controlled
estimate of the subscription change's FPS gain. The change removes confirmed
unnecessary work, but does not eliminate turning hitches.

Two CPU timeline runs, each with 60 seconds idle and 60 seconds walking, did
not reproduce the original half-second stall. Their largest measured frames
were 38.6 ms and 29.9 ms. One captured 39.0 ms profiler frame spent 36.5 ms in
`GarbageCollector.CollectIncremental`. That attributes that smaller stall only;
it does not establish the cause of the 631–652 ms events. One additional full
capture failed inside Unity's native profiler and was excluded.

[Issue #172](https://github.com/ryanbliss/neo-compose-dotnet/issues/172) remains
open for getter execution, script alias-cache refresh, and the larger idle
stalls. The next isolation experiment is a standalone development player with
CPU timelines and thread CPU time, to separate game work from editor overhead
and time spent waiting or descheduled. No fix for the half-second stall is
claimed here.

## Architectural changes

- Member nodes and animation segment sources subscribe by the value IDs they
  read. A leaf write no longer invokes every member in the loaded world.
- Existing scalar replacements retain ancestry checks but skip full placement
  rebuilding when their graph structure is unchanged. Structural edits,
  constructor replay and tile Cell edits retain the existing validation path.
- Position changes validate and patch only the moved object's footprints and
  projected tiles. Subcell movement does not publish a grid delta. Conflicting
  moves fail before any index is patched; multi-object swaps remain atomic.
- Movement deltas retain rendered GameObjects while reevaluating lifecycle
  visibility filters. Controllers and animations survive cell crossings.
- Generated class setters skip equal explicit scalar overrides. First writes
  still pin inherited values, and payloads carrying additional rows still commit.

No public signatures change. Ordinary generated assignments use the same API.
Animation batching and native component mirrors were considered, but would
change intermediate-write visibility or introduce another state owner. The
measured regression is addressed at the shared subscription and spatial-index
boundaries instead.
