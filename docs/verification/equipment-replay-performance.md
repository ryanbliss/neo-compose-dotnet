# Equipment replay performance (#178)

Measured in the actual Neowyn Editor Play Mode on an Apple M3 Max with Unity
6000.5.4f1. Baseline SDK: `5249681cc7bfc13af62ef6ac7c4dde0d0ebb912a`
(PR #179). The local Neowyn scenes, generated types, and export were retained.
The original game's `WorldScene` was measured separately on the same machine.

## Workload and measurement

The checked-in `scripts/performance/NeoInventoryPerformanceTests.cs` is a
Neowyn test harness, not a HelloWorld test. `RealWorldInventoryActions` creates
a fresh save, exercises the actual menus and Dev Tools, adds wood and an axe,
and alternates their inventory slots with the live toolbar, character, and
animation renderer active. No UI layout, prefab, or authored Neo data changed
for this SDK fix. Neowyn changes remain local.

Each action reports synchronous elapsed time and the delta of Unity's
`GC Allocated In Frame` recorder during that action. It then measures three
wall-clock frame gaps, beginning at action start. These gaps include subsequent
rendering, UI work, and Editor scheduling; they are not isolated SDK timings.
The allocation column does not include all subsequent-frame allocations.
Timing runs use disabled profiling and no concurrent test/deploy jobs. The
original game's test uses the same measurement helper and its test save mode.
There are three cycles per run; these are individual samples, not p99 estimates.

The CSV files preserve every measured action, including menus outside #178.
The legacy comparison only covers equipment changes.

## Results

| Equipment action | SDK baseline action | Candidate action | Original game action | Candidate allocation | Candidate maximum frame gap |
| --- | ---: | ---: | ---: | ---: | ---: |
| First tool equip | 248.098 ms | 7.525 ms | 2.886 ms | 0.744 MB | 15.653 ms |
| First return to wood / empty hands | 177.056 ms | 12.983 ms | 1.981 ms | 1.588 MB | 36.085 ms |
| Repeated tool equip | 128.852–130.857 ms | 3.427–4.331 ms | 0.857–0.914 ms | 0.708–0.710 MB | 9.854–10.788 ms |
| Repeated return to wood / empty hands | 125.054–131.860 ms | 2.611–3.473 ms | 0.656–1.003 ms | 0.666–0.669 MB | 9.920–18.877 ms |

Repeated actions are approximately 30–48 times faster and allocate about 98.6%
less than the SDK baseline (~50 MB per action). They remain slower and more
allocating than the original game (35–37 KB per repeated action). The first
unequip and multiple whole-frame samples still exceed the requested 10 ms
budget. **The budget is not met.** These results establish the enclosing-replay
improvement, not native parity.

Raw results: [baseline](equipment-replay-baseline.csv),
[candidate](equipment-replay-candidate.csv), [original game](equipment-replay-legacy.csv).
The final candidate is a standalone run after removing diagnostic probes. An
earlier run of this same benchmark before the final metadata-dependency fix had
17.425/30.210 ms first equip/unequip timings; cold results varied substantially.
No arbitrary repeated-equipment warmup was added to hide this cost.

The broader harness still reports menu/inventory costs beyond #178: first
Friends tab 59.021 ms, repeated Inventory tab 29.933–30.596 ms, and first tool
addition 29.989 ms. Those results are preserved in the candidate CSV and are
not covered by an equipment-only improvement claim.

## Why the enclosing replay was expensive

A held-item read inside an attack-layer constructor previously invalidated the
whole CharacterBody construction. Repeated switches rebuilt roughly 2,317 rows
through 101 declared constructors, allocating about 50 MB per action even after
#179. The dependency is necessary: suppressing it leaves the sprite stale.

The SDK now records independent leaf-constructor boundaries and replays their
outputs in the original enclosing ID namespace. Class references track identity
and the field links actually read; scalar and collection payload reads keep
value dependencies. A reference argument no longer treats every config write
as a read of that entire config.

A leaf is separated only when it has no nested construction, external writes,
or parent reads/writes of its result, and its call-site fields can be restored
without rerunning the enclosing evaluator. Other cases retain the enclosing
replay. Explicit overrides survive replay; changed constructor recipes discard
the previous call-site initializers. Replaced descendants are disposed while
same-class root views remain live. Null Save overlays retire their writable
children while retaining immutable authored defaults for Asset views.

The remaining changes remove work exposed by that smaller replay:

- Read-suppression scopes no longer allocate throwaway sets and closures.
- Placement validation follows owning geometry edges, rather than visiting
  world objects through config/lookups; changed replayed geometry is validated.
- Equal complete overlays retain their existing construction. Equal parent
  scaffolding written with a child does not publish duplicate class events;
  explicit whole-value and leaf notifications retain their behavior.
- Orphan cleanup first finds actual writable removals, skips empty removals,
  and shares reachability across the children released by a null assignment.
- On Mono, field-listener registration prepares the SDK callback method. This
  moves a measured 17–20 ms first-use JIT cost into binding/setup. It does not
  invoke the handler or read its field, and it does not remove compilation work.
  IL2CPP excludes this preparation; IL2CPP performance was not measured here.

Permanent profiler markers cover commit, replay preparation, replay installation,
and individual root replay. Temporary per-listener/scene probes were removed.

## Validation and limits

Full SDK EditMode validation: 2,227 passed, one existing ignored test, no failures.
Rig validation: codegen, typecheck, repository doctor, 6,056 unit tests, clean
CLI status/no-op push, and browser smoke passed.

The SDK suite covers dependency changes, direct child overrides, constructor
side effects, call-site initializer retention/replacement, stable IDs, null
Save overlays, ownership, disposal, collection cleanup, notifications, placement
validation, and semantic numeric comparisons.

The actual Neowyn visual test switches two attack tools and a fishing rod,
returns to empty hands, waits real game time for the animation, checks the
rendered sprite against the selected item's animation, checks enabled layers,
and verifies that torso/equipment renderer identities are retained.

This fixes the enclosing CharacterBody replay bottleneck. It does not establish
native-speed parity or an everywhere-under-10-ms frame guarantee. Cold frame
gaps and the menu/first-item costs in the CSV remain visible. #172 tracks the
separate animation/idle-stall investigation. No full-game green claim is made:
this change validates the specified inventory/equipment integration paths.
