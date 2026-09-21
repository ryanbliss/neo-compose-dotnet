# Inventory runtime performance

Measured September 20, 2026 in Neowyn Editor Play Mode on an Apple M3 Max,
Unity 6000.5.4f1. Baseline SDK: `346f8aab7b54764f379aaba47e0472532f0bfc55`.
The scenario runs the actual NeoMenu -> new game -> NeoWorld path, generated
bindings, game UI listeners, developer commands, inventory methods, and renderer.
Neowyn is `e8d0c1a` plus the developer's local inventory/menu port. This SDK PR
builds on #177; the game uses the generator fixes in Compose #992.

## Matched scenario

Start with a fresh temporary save. Repeat three times: open the menu; select
Friends, Crafting, Dev Tools, and Inventory; add one wood through the developer
command while the menu is open; close the menu; add wood through the command;
add wood through the generated inventory API; equip the stone axe; equip wood.
The first iteration also inserts the axe. Assert inventory quantities, equipped
items, and the rendered toolbar quantity. Return to the menu and remove the
temporary save. The local `NeoInventoryPerformanceTests.RealWorldInventoryActions`
keeps the reproduction in Neowyn; no private game export is copied into the SDK.

Each operation records synchronous action duration, Unity's `GC Allocated In
Frame` counter delta during the action, and the largest frame gap over the next
three frames. The latter includes deferred UI work. Allocated bytes are **action
allocations**, not total allocations across those following frames or retained
heap size. These are Editor observations, not player-build guarantees or broad
percentiles. Temporary attribution scopes were removed for both final timing
runs. No other test/deploy jobs ran during the final candidate measurement.

Raw samples: [baseline](inventory-runtime-baseline.csv) and
[candidate](inventory-runtime-candidate.csv). Repeated results below use the
median of the specified samples; direct additions use all three, other repeated
actions use iterations 1 and 2. MB means decimal megabytes.

| Operation | Action ms before -> after | Action MB before -> after | Following max frame ms before -> after |
| --- | ---: | ---: | ---: |
| First menu open | 27.377 -> 26.313 | 5.58 -> 4.23 | 47.875 -> 47.388 |
| Repeated Inventory tab | 143.438 -> 37.260 | 25.80 -> 6.11 | 176.290 -> 71.691 |
| First developer item add | 4953.344 -> 48.251 | 987.24 -> 6.01 | 4975.853 -> 68.835 |
| Repeated developer item add, menu open | 66.132 -> 7.026 | 15.72 -> 1.60 | 73.183 -> 18.599 |
| Direct item addition | 63.918 -> 5.737 | 15.50 -> 1.51 | 73.429 -> 10.641 |
| First tool insertion | 4980.733 -> 35.004 | 971.33 -> 6.36 | 4995.763 -> 44.609 |
| First tool equip | 4553.103 -> 296.281 | 778.92 -> 96.84 | 4579.465 -> 323.014 |
| Repeated tool equip | 2244.910 -> 146.137 | 381.49 -> 49.97 | 2272.396 -> 168.993 |
| Repeated wood equip | 2156.058 -> 143.084 | 381.56 -> 49.95 | 2181.159 -> 165.064 |

## Causes and fixes

- Binding NeoScript's root names eagerly read Assets, Save, and Session. Every
  constructor consequently captured dependencies on all three global roots,
  even when its script never read them. Resolve each root only when accessed.
  Real reads still enter the ordinary dependency tracker.
- Action/delegate listener updates were treated as structural subtree changes.
  They are leaf payloads containing references, so local writes can retain their
  enclosing expansion while real constructor dependencies still invalidate.
- An enclosing replay queued unchanged nested instances for independent replay.
  Preserve already constructed siblings when their rows, constructor inputs,
  ownership graph, and dependencies remain valid. Keep external changes and
  structural writes conservative. Avoid inventing a second expansion namespace
  for a nested instance already completely built by its enclosing constructor.
- Comparing thousands of typed rows by converting both to JSON produced avoidable
  work. Compare common typed maps, arrays, scalars, and listener payloads directly;
  retain the established semantic JSON fallback for other shapes. Regression
  tests compare the typed result against the JSON semantics.
- Creating a small stack repeatedly scanned all parent rows to check ownership.
  Reuse the existing placement parent index to narrow candidates, then perform
  the same schema ownership checks, including both Save and Session shadows.
  Eight searches fell from about 100 ms to under 1 ms in the attribution run.
- Declared NeoScript constructors created entire C# wrapper trees, subscribed
  them, refreshed them, then immediately disposed them. Construct their rows
  through the same four-step constructor sequence and create wrappers only for
  the C# API that returns one. The canonical CellPattern construction uses this
  path too. No public SDK API changes.
- A read-only generated interface over Save/Session data incorrectly inherited
  Asset context for computed fields/methods. Preserve its backing ownership while
  keeping the interface read-only. Coalesced UI rendering exposed this bug when
  no earlier writable wrapper had populated the cache.

The game-side changes are local: cache collection/count reads within a UI refresh,
refresh once before rendering after a burst of inventory notifications, and stop
rebinding unchanged button handlers. `SetQuantityListener` returns early when the
same listener is already attached. That one Neo method was pushed and regenerated;
all UI layouts and prefabs are unchanged by this performance work. The totals
measure these SDK and game improvements together, not an SDK-only A/B.

Baseline export SHA-256:
`6a2e63e69d5cc2c412498d0360847e7f0bbe66f1f86169d08adf8c2e4e44217a`.
Candidate export SHA-256:
`1c7a4bee3831b220683bdf832c1d081a130449e1aea309345f0cd144428ab569`.

## Remaining cost

Equipment changes still replay the character body because a nested attack sprite
constructor reads the held item. The action remains roughly 140 ms warm and
allocates about 50 MB. Suppressing that dependency would leave the equipment art
stale; finer replay boundaries need separate correctness coverage, tracked in
[#178](https://github.com/ryanbliss/neo-compose-dotnet/issues/178). First menu
open and Friends rendering were not materially improved. The measurements do
not establish a frame-budget guarantee.

## Validation

- Complete SDK EditMode suite: 2,222 passed, one existing ignored test, zero
  failures (157.6 seconds).
- Focused constructor, ownership, semantic comparison, and P75 regressions:
  237 passed, zero failures.
- Neowyn inventory UI integration: both tests passed, including shortcut/input
  events, split/move/cancel, storage, crafting, shop buy/sell, and save/continue.
- Neo specifications: 71 passed; post-push semantic status is clean.

- Full Neowyn EditMode: 337 passed, one existing animation-turn timing failure
  (18.1 ms against 16.7 ms).
- Full Neowyn PlayMode: 19 passed, four existing failures (day-end income and
  three legacy gifting tests), matching the prior full-suite artifacts.

- `agent:verify` passed: doctor, guarded push/migrations, codegen, typecheck,
  repository doctor, 6,056 unit tests, clean CLI evidence, and browser smoke.
  The first attempt missed an unchanged search timing threshold (78.2 versus
  75 ms); the unchanged rerun passed.
