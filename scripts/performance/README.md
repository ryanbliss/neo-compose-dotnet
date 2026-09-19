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
Baseline SDK `6243222`; revised SDK is this change. Both serial runs used the
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

The final 30-second diagnostic run with SDK markers measured 1,560.6 FPS idle
and 1,472.1 FPS walking, walking median 0.5896 ms, p99 2.6210 ms, maximum
29.8192 ms, and one player body throughout 120 units of movement. Periodic turns
still caused roughly 15–20 ms frames: segment re-resolution accounted for
11–14 ms and script row refresh about 0.9–1.2 ms. The direct facing setter and
steady-state tick measurements do not include that deferred resolution cost.

[Issue #172](https://github.com/ryanbliss/neo-compose-dotnet/issues/172) tracks the
next experiment: profile and optimize segment getter re-resolution, then
target script alias-cache refresh, and attribute the larger idle stalls with
a CPU timeline. These are unresolved, separate from the grid and notification
improvements measured here.

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
