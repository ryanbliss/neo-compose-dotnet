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
stalls. The later native investigation below uses a standalone development
player and thread CPU time to separate game work from time spent waiting. It
attributes a newly captured presentation stall, not the uncaptured historical
half-second frames.

## First movement after a fresh world load

The warmed benchmark above missed the first position and facing overrides.
`NeowynColdMovementBenchmark.cs` adds a separate path with no setter or animation
warmup and no discarded frames. Copy it into the isolated game's
`Assets/Tests/Editor`, then run:

```sh
unity test /absolute/path/to/neowyn-copy --mode EditMode \
  --filter NeowynColdMovementBenchmark --output /tmp/neowyn-cold.xml --timeout 400
```

It starts a fresh local game, applies real gamepad input for ten seconds, then
stops and restarts twice for two seconds each. It asserts movement and reports
`NEO_COLD` frame timings, including the first twelve frames. Profiling is disabled
for these timing samples. The same batch-mode limitations above apply.

On merged SDK `052918e`, the first facing override replayed 308 constructor roots
and the first position override replayed 301. CPU traces attributed the stalls
to candidate constructor reconstruction during those writes. Both changed an
Asset-owned default to its first Save-owned override. The ownership guard ran
before leaf reuse, incorrectly treating these overrides as structural changes.
Later writes were already Save-owned and avoided the expensive path.

The ownership guard now follows the scalar and Enum/Lookup/DialogueLookup leaf
checks. A leaf can gain an override without retiring an owned subtree. Real
constructor read dependencies still invalidate because these writes are not
marked unchanged. Structural changes and external writes retain replay.

Serial runs using the same Neowyn content and machine as above:

| Cold movement measurement | Merged baseline | Fixed | Fixed, fresh-load repeat |
| --- | ---: | ---: | ---: |
| First frame | 1,831.699 ms | 6.431 ms | 6.900 ms |
| Second frame | 1,735.245 ms | 0.787 ms | 0.721 ms |
| Maximum over first ten seconds | 1,831.699 ms | 20.821 ms | 189.227 ms |
| Distance over first ten seconds | 28.400 | 40.000 | 40.000 |

The first baseline/fixed pair used an inactive CPU-trace helper. The repeat used
the committed benchmark without that helper. Temporary game-side profiler
markers remained identical. No other test suite ran concurrently.

The repeat's 189 ms outlier is retained; these results establish removal of the
reproducible first-write freeze, not the elimination of all intermittent stalls.
A separate 30-second CPU timeline run captured 42,783 frames, with a maximum
profiler frame of 27.357 ms. It did not reproduce the 189 ms outlier. The earlier
631–652 ms events are still not attributed to this defect.
[Issue #172](https://github.com/ryanbliss/neo-compose-dotnet/issues/172) stays open.

Prewarming was rejected because it merely moves this unnecessary reconstruction
into loading. Skipping all constructor invalidation would break copied/computed
defaults. The fix changes the shared leaf reuse rule and preserves actual
constructor dependencies. Regression tests cover first Save and Session
overrides for scalar and selection leaves, retain an unrelated default's row
identity, and check a dependent copied scalar updates after the override.

## Native stall investigation

The intermittent stalls were investigated further after the cold-write fix.
A rendered macOS development player reproduced a 1,033.289 ms walking interval
with 13.846 ms of main-thread CPU and 23.933 ms of process CPU. An external
heartbeat watchdog started native sampling after 62 ms without a new frame.
The trigger's last frame was 248860 and the measured interval was frame 248861.

The matching native capture recorded 648 of 759 main-thread samples in:

```text
PlayerRender
  GfxDeviceClient::BeginFrame
    GfxDeviceClient::WaitForPendingPresent
      Semaphore::WaitForSignal
```

The CVDisplayLink thread recorded 657 samples in:

```text
CVDisplayLink::performIO
  MetalSurfaceCallback
    MetalCopyFromTextureToDrawable
      CAMetalLayer.nextDrawable
        CAMetalLayerPrivateNextDrawableLocked
          semaphore_timedwait_trap
```

This locates the captured stall in the legacy Metal presentation/drawable
acquisition path. It does not prove why the drawable was unavailable or that
the timed wait expired. Sampling began after the stall started and could affect
its remaining duration. The previously uncaptured 189 ms Editor frame and the
631–652 ms frames cannot retrospectively be assigned this cause.

Unity 6000.5 supports a replacement Mac Player path through the
`metalUseMetalDisplayLink` project setting or `UNITY_USE_METAL_DISPLAY_LINK=1`.
Unity documents improved frame pacing and reduced stutter with this path.
[Unity release notes](https://unity.com/releases/editor/alpha/6000.5.0a8).

Serial runs of the same player binary used fresh saves and 60 seconds each of
walking, idle, and resumed walking. Real gamepad input alternated direction every
0.6 seconds. The only player launch change was the environment switch, and the
log confirmed `CAMetalDisplayLink created`.

| Phase | Legacy maximum | MetalDisplayLink maximum |
| --- | ---: | ---: |
| Walking | 21.062 ms | 35.055 ms |
| Idle | 15.883 ms | 24.623 ms |
| Resumed walking | 1,033.289 ms | 31.347 ms |

Both new-path walking phases covered 240.005 units, versus 232.084 during the
interrupted legacy resumed walk. No >60 ms watchdog event occurred in the
new-path run. The new path ran at roughly 118–120 FPS; the legacy path was
uncapped at roughly 1,340–1,615 FPS. These results support changing presentation
and pacing together, not an increase in SDK throughput. They do not prove that
all hitches are eliminated, and the Mac Player setting does not affect the Editor.

The serialized-setting rebuild confirmed activation without an environment
override, but exposed a background-execution problem. After a normal first
walking minute with a 29.948 ms maximum, its idle phase stopped at frame 9548.
Native samples showed the main thread idle in the AppKit event loop, with the
first sample also recording an active-space change. Raising the player window
resumed it immediately; the resumed interval measured 72,678.638 ms. Neowyn and
the probe both had `runInBackground` enabled. The initial visibility transition
was not deliberately controlled, so the exact visibility condition still needs
isolation. This was a different wait from the legacy presentation capture. Its resumed
walking phase also contained 195.834, 347.799 and 251.016 ms intervals. The
watchdog triggered on the first, but native sampling did not start until 365 ms
after the trigger, too late to attribute that interval. Those later samples and
the adjacent frames are potentially perturbed by the sampler and remain
unattributed; they further prevent treating this repeat as a successful fix.

The setting change was rejected pending that investigation. It is not part of
this PR, and the incomplete/visibility-interrupted repeat is not counted as a
clean performance run. A clean first run was insufficient evidence to ship it.
The next engine experiment should control visible, covered, minimized and
other-Space states on both paths, then compare legacy pacing limits if preserving
background simulation remains a requirement. This is separate from reducing
SDK animation allocations and getter work.

An unprofiled three-minute Editor run with the same native watchdog measured
40.598 / 37.175 / 39.418 ms maxima for walking, idle and resumed walking across
288,989 frames. Both walking phases covered 240.005 units. No watchdog event
occurred, so that run supplies no attribution for the historical Editor outlier.

The machine and content match the earlier samples, with macOS 26.6.2 and SDK
`936d7f9`. The isolated game copy needed four existing player-compilation issues
corrected before either build could run: two unused UnityEditor imports, an
editor-only dirty call, and resource-load name resolution. Neither comparison
changed these repairs. The original game checkout remained untouched.

A separate instrumented Editor run captured an 84.748 ms CPU frame containing
79.965 ms in `NeoAnimationRunner.Update`, including segment resolution and a
9.378 ms `GC.Collect` sample. Another frame spent 43.884 ms in incremental GC.
These are distinct from the native presentation wait. Allocation callstack
recording then identified animation subscriptions, expression contexts and
`RefreshCachedRowAfterWrite` among allocation sources; that instrumentation
slowed the game enough to induce catch-up work, so its allocation totals and
frame times are not ordinary gameplay measurements. It does not establish that
one of those sites caused the original 189 ms event. Remaining SDK work stays
tracked in [issue 172](https://github.com/ryanbliss/neo-compose-dotnet/issues/172).

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


## Stored property reads in Editor Play Mode

The generated accessors recreated bound vector/sprite/color and list/dictionary
views on every access. Generated classes and single enum options already reused
instances, but class cache hits still allocated a captured factory and composed
registry key. Single class lookups also allocated a temporary selection list and
registry key. These allocations were unnecessary for unchanged stored reads.

Generated instance accessors now cache live views on their owning generated
class, distinguished by schema key, wrapper type, and exact backing node. A
changed backing node replaces that entry. Retargeting or disposing the owner
clears its views. The views continue reading the current node; no stored values
are copied into a second state store. Writable and read-only views retain their
existing permission callbacks. Class factories use a noncapturing overload and
reuse the node's registry key. Single lookups resolve current target ownership
and consult the active registry, including writable compatibility, while reusing
the previous selection's key when applicable.

Measured in the real Neowyn generated code, Unity 6000.5.4f1 Editor Play Mode on
M3 Max/macOS 26.6.2. Each operation was warmed once, then read 10,000 times inside
a dedicated CPU Profiler marker. Bytes are the sum of `GC.Alloc` metadata under
that marker, not the duration reported by an allocation recorder. The final
repeat reused the same instance on all 10,000 reads for each operation.

| Stored property | Before bytes/read | After bytes/read |
| --- | ---: | ---: |
| Body.Position | 48 | 0 |
| Body.Children | 584 | 0 |
| Body.SortingGroup | 410 | 0 |
| Config.Eyes | 768 | 0 |
| Config.Facing | 0 | 0 |
| Body.Name | 0 | 0 |

The test project was regenerated with the companion Unity generator change.
The sample retains its existing generated output to exercise compatibility with
older generated callers. An unrelated native-function dispatch difference in
the fixture generator was excluded from the committed fixture update.

These are scoped read-allocation results, not gameplay FPS or a claim that all
stored access paths allocate zero. Collection element conversion, multiselection
materialization, static property wrappers, and NeoScript evaluation have separate
paths. In particular, extending evaluator caches across frames requires preserving
read-dependency capture and invalidation; this change does not do that.

The detailed Editor investigation also captured 1,652.571 ms and 1,656.770 ms
frames dominated by the MCP diagnostic command handler (1,631.547 ms and
1,636.198 ms). Those runs are contaminated for hitch/FPS analysis. The scoped
read markers execute later, independently of those commands. They do not explain
the user's historical 189 ms event. Subsequent gameplay measurements use an
explicit start-file barrier after setup/focus commands, with no automation calls
during the measurement window. The older hidden-EditorLoop capture remains
unattributed. Background presentation waits are not evidence clearing the SDK.

Raw captures and the diagnostic probe are retained in the session rig's
`artifacts/stored-read-reuse-2026-09-19` directory. The unresolved gameplay
investigation remains [issue 172](https://github.com/ryanbliss/neo-compose-dotnet/issues/172).


An additional unprofiled, gated Editor comparison used 20 seconds per phase,
real gamepad input, fresh private saves, VSync off and a 300 FPS target. Both
first-walking and idle phases remained focused throughout. Resumed walking lost
focus in both scenes and is excluded from comparison.

| Scene / phase | Mean ms | P99 ms | Maximum ms | Allocated MB/s |
| --- | ---: | ---: | ---: | ---: |
| NeoWorld walking | 6.186 | 19.893 | 44.534 | 13.45 |
| WorldScene walking | 8.344 | 15.072 | 40.612 | 3.63 |
| NeoWorld idle | 6.817 | 17.205 | 42.021 | 4.33 |
| WorldScene idle | 7.831 | 16.263 | 32.540 | 2.12 |

Neither run reproduced the historical 189 ms event. The scenes have different
content (23 versus 122 renderers, both three cameras), so their means cannot
establish SDK overhead or parity. Both also fall short of the user's reported
~280 FPS walking baseline; this experiment does not claim to reproduce that
throughput. The allocation difference still warrants investigation. Next targets
are evaluator projection reuse with correct dependency capture and the write
path's reverse-row alias copies. Profiling must establish their contribution
before assigning an intermittent hitch to either.
