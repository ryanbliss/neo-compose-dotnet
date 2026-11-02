# Charged pickaxe benchmark

These opt-in probes exercise Neowyn's committed `ryanbliss/dig-spots` branch.
The original measurements used `593e0f44`; the follow-up uses `03e8c06f`. They live outside the package test assembly because they use Neowyn's
generated types and scene fixtures. They do not modify game authoring or gameplay.

## Reproduce

Use an isolated Neowyn checkout. Copy `NeowynChargedPickaxeBenchmark.cs` and
`NeowynBulkHitBenchmark.cs` into `Assets/Tests/Editor`, and add
`NEO_COMPOSE_NEOWYN_TESTS` to the active target's scripting defines. Point
`com.ryanbliss.neocompose` in `Packages/manifest.json` to an absolute `file:` path
to the SDK package. Run baseline and candidate serially, without other builds or
test suites. Disable `NEO_COMPOSE_PROFILING` for timing comparisons.

```sh
unity test /path/to/neowyn-copy --mode EditMode \
  --filter NeowynChargedPickaxeBenchmark --output /tmp/pickaxe.xml --timeout 900
unity test /path/to/neowyn-copy --mode EditMode \
  --filter NeowynBulkHitBenchmark --output /tmp/bulk-hit.xml --timeout 900
```

The scene test starts a local game, enters the mine through its door, equips the
Wyn pickaxe at maximum charge, and fills its 35-cell pattern with iron rocks.
It saves and reloads before calling the real `GridToolValidator.Use` twice.
The first swing changes all stages; the second destroys all rocks. Assertions
check the immediate model and rendered sprites, removed occupancy, and persistence
after another save/reload. Native reward drops, sound, navigation, and rendering
remain active. Both random generators use seed 314159 before the swings.
The fixture allows only Brett's authored off-NavMesh mine waypoint error; other
errors fail the test. Each run uses and deletes its own temporary save directory.

The bulk-hit test constructs 35 small boulders using the real export and generated
SDK. It calls `Hit` in one transaction, with separate same-variant, stage-change,
and destruction cases. Native drops clone their stacks but omit Unity objects
and sound. This isolates SDK work but is not a rendered-game timing.

## Findings

Baseline SDK is `77ea01e6`, including the overnight optimizations from #498.
Measurements are Unity Editor batch-mode wall time on the same Linux machine,
not a shipping-player frame budget or a prediction for another computer.

The bulk-hit profile found 140 whole-world reachability walks during 35 boulder
variant changes, about 4.8 seconds in that instrumented run. Removed override
rows retained placement metadata even though no live slot referred to them.
The incoming-edge proof now distinguishes those detached slots from live edges.
Unknown proposed references still fall back to the collector.

Prepared variant replay already constructed its nested roots, but commit replayed
them again. Commit now reuses the complete prepared graph when its plan, revision,
roots, map keys, and ownership still match.

Ordinary object removal rebuilt entire placement layers for collision validation.
Removing a leaf object cannot add collisions. A conservative proof now retains
unrelated cached records and tiles, updates the removed records, and sends the
normal change events. Mixed edits, owned tiles, nested placements, and uncertain
new rows retain full validation. Cached object records are still compacted and
reindexed per removal; this removes graph reads, not every world-size cost.

Three fresh-editor runs per version, with profiling disabled:

| Operation, 35 rocks | Baseline median | Revised median |
| --- | ---: | ---: |
| Change damage stage | 89.557 ms | 65.002 ms |
| Destroy and drop rewards | 519.784 ms | 251.254 ms |

Raw stage/destruction pairs in milliseconds, in run order:

- Baseline: 89.557/625.173, 84.996/519.784, 158.062/461.241.
- Revised: 78.684/319.288, 65.002/171.524, 60.445/251.254.

All six runs passed sprite, occupancy, and save/reload assertions. This small
sample shows substantial timing variation, not a latency guarantee. Diagnostic
runs and superseded guard implementations are excluded from these comparisons.
The SDK removal regression also preserves unrelated records at layer sizes of
35, 350, and 1,400 objects; its cache compaction remains proportional to layer size.

**The single-frame target is not met.** The seeded destruction creates 117 dropped
items. Separate instrumented runs attributed about 25–28 ms to game-side drop
creation, 39–62 ms to SDK stack clones, and about 22 ms to retaining script aliases
on removal. These timings include nested work and must not be added together.
No async behavior or game-side workaround is introduced by this change.

## Clone and ownership follow-up

This round compares merged SDK `3f98d812` with the current changes, using the same
Neowyn export at `03e8c06f`. Three fresh-editor runs per version, no profiling or
concurrent builds. Development servers were stopped for the final candidate set.

| Operation, 35 rocks | Baseline median | Revised median |
| --- | ---: | ---: |
| Change damage stage | 61.443 ms | 59.283 ms |
| Destroy and drop rewards | 168.773 ms | 145.345 ms |

Raw stage/destruction pairs in milliseconds:

- Baseline: 61.443/168.773, 63.820/177.018, 58.666/166.142.
- Revised: 59.283/143.658, 63.242/164.384, 59.049/145.345.

The destruction median is 13.9% lower. All six comparisons passed sprite,
occupancy, and persistence assertions. This is a small, noisy editor sample,
not a single-frame guarantee. A candidate run while development servers were
still running measured 93.395/277.224 and is reported separately rather than
mixed into the quiet-run comparison. Earlier exploratory implementations are
excluded. Unity reported zero through `GC.GetAllocatedBytesForCurrentThread`,
so no allocation-byte reduction is claimed.

The SDK now skips absent listener traversal, reuses the copied object dictionary,
resolves constructor-reference type bindings only for arguments that can name a
row, and avoids temporary UTF-8/hash arrays for ordinary derived identities.
Complete fresh Session clones reuse constructor publication only when they have
no existing rows, immutable replay defaults, copied listeners, or tracked
lifecycle classes. Other clones keep the normal commit path. Pure removals can
use their cached safety proof before the general placement walk, and unrelated
Session writes no longer discard grid index versions.

Issue #501 is also fixed: placement adopts the original Session identity into
Save, and retained script locals follow its new ownership. No placement clone
is introduced. Shared, private, retired, and foreign evaluator contexts are
covered, including a write through a local compiled as Session-owned.

`NeowynSpawnOwnershipRegression.cs` is another opt-in probe. At Neowyn `c1961413`,
it creates a fresh in-memory save, sleeps three nights, verifies nonzero spawn
counters, commits and reloads, checks every counter, then sleeps again. The run
produced 30 counted spawns and passed persistence. This export includes the
correct `@storage(allowed: .Save)` declaration on `CurrentCount`; the CLI's silent
handling of positional `@storage(.Save)` is tracked separately in
[neo-compose#2437](https://github.com/ryanbliss/neo-compose/issues/2437).

The single-frame target remains unmet. Neowyn's real native drop path makes 117
Unity objects for this fixture. Each reward constructs an inventory stack whose
data is cloned, then the native drop handler clones that stack again. Further
work should measure explicit ownership transfer for newly created reward stacks
and pooling/batching the visual drops and destructive sounds. Simply deleting
the clone can violate the lifetime or independence of stacks supplied by callers.
No game-side change is included here.
