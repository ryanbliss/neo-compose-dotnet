# Crowded Neowyn overnight processing

This benchmark measures `World.ProcessSleep()` and `Time.SetToNextMorning()`
with the real Neowyn scene loaded. It includes SDK evaluation, spawning,
placement validation, bookkeeping, notifications, and the game's synchronous
callbacks. It does not measure walking, frame rate, or the sleep UI transition.

## Reproduce

Use an isolated checkout of Neowyn, never the active game checkout. The probes
use its committed bundled export and local saves, with cloud synchronization
disabled by `TestProjectSource`.

1. Copy `NeowynOvernightSeed.cs` and `NeowynOvernightBenchmark.cs` into
   `Assets/Tests/Editor` in the isolated checkout.
2. Add `NEO_COMPOSE_NEOWYN_TESTS` to the active target's scripting defines.
   Remove `NEO_COMPOSE_PROFILING` for timing measurements.
3. Point `com.ryanbliss.neocompose` in `Packages/manifest.json` to the baseline
   SDK using an absolute `file:` path.
4. Generate the crowded save once. The generator refuses an existing directory.

   ```sh
   NEO_OVERNIGHT_SEED=/tmp/neowyn-overnight-seed \
     unity test /absolute/path/to/neowyn-copy --mode EditMode \
     --filter NeowynOvernightSeed --output /tmp/overnight-seed.xml --timeout 1800
   ```

5. Run the baseline benchmark, then change only the package reference to the
   revised SDK and run it again. Run serially without other test/build processes.

   ```sh
   NEO_OVERNIGHT_SEED=/tmp/neowyn-overnight-seed \
     unity test /absolute/path/to/neowyn-copy --mode EditMode \
     --filter NeowynOvernightBenchmark --output /tmp/overnight.xml --timeout 900
   ```

The generator advances 56 days using the actual sleep/spawn functions. Each
benchmark copies that seed to its own temporary directory, loads the game,
resets both Unity and SDK random generators, and measures five nights. It records
the object count before and after each night, saves, returns to the menu, reloads,
and checks that the count survives. It removes its temporary directory afterward.
Do not regenerate the seed between baseline and revised runs.

The crowded fixture blocks Shopkeeper's authored morning navigation waypoint.
The probe permits that specific navigation error, preserves it in test output,
and fails on other logged errors or exceptions. Animation getter warnings remain
visible in the output. These measurements do not assert that every game-authored
animation or navigation configuration is valid.

`NEO_OVERNIGHT` lines in the NUnit XML contain the measurements. The first night
is retained, not discarded as warmup. These are Unity Editor batch-mode timings
on one machine, not shipping-player timings or a guarantee for every save.
SDK profiler/deep-profile captures are diagnostic evidence only and must not be
compared with the unprofiled timings.

## Implementation

The changes avoid repeated schema and inheritance scans and repeated full-store
scans when inferring a child's ordered parents. Concrete class fields answer
ownership checks directly when their type is already settled. Variant application
uses cached grid layer discovery rather than rebuilding the placement graph. A validated simple spawn updates the cached
placement graph with the new object, and a validated visual-only object refresh
keeps the rest of that graph. Writes affecting other placement dependencies
retain the full rebuild path. Fresh Session variant initialization records its
recipe without immediately reconstructing the same object. Empty unordered-list
payloads return the resolved membership list directly instead of copying it again.

The validation paths, synchronous notifications, and authored overnight routines
remain active. No Neowyn gameplay changes are required.

## Recorded results, November 1, 2026

Intel Core i7-9700K, Linux, Unity 6000.6.4f1. Neowyn
`cfcad1c1` from `ryanbliss/dig-spots`, with its committed export. Baseline SDK
`e3967e9313a37b6839df95a4727b79974e0384e4` (0.81.4); revised SDK is this change.
Both use the same save after 56 actual overnight cycles. Its `save-crowded.json`
SHA-256 is `bd7f988e219de06215c5e63230082b767bbbccd18e4ea39ea55f682fc0f8188b`.
The seed was generated before adding the explicit SDK seed to the generator;
regenerating it reproduces the workload, not that exact save. Each timed run
sets both random generators to 314159 after loading the fixed seed.

Two baseline runs and two runs of the final runtime changes were performed
serially, with profiling disabled and no other test/build process running.
The table includes both sleep and morning bookkeeping. Each cell is one night,
in milliseconds. No slow samples were discarded.

| SDK run | Night 1 | Night 2 | Night 3 | Night 4 | Night 5 | Mean |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Baseline 1 | 289.934 | 263.173 | 248.889 | 228.675 | 382.119 | 282.558 |
| Baseline 2 | 296.666 | 262.682 | 251.397 | 328.458 | 498.737 | 327.588 |
| Revised 1 | 222.096 | 166.517 | 150.104 | 132.118 | 152.836 | 164.734 |
| Revised 2 | 232.307 | 287.095 | 140.126 | 125.693 | 145.542 | 186.153 |

Across ten nights per version, mean overnight time decreased from **305.073 ms
to 175.443 ms**, a **42.5% time reduction / 1.74× speedup**. Median time decreased
from 276.554 ms to 151.470 ms. The revised run still had a 287 ms sample. This is
not a 95% reduction, and does not consistently reach a 95% speed increase either.
The original performance target remains unmet.

Every run started with 184 primary objects in the test area and ended with 199,
adding three per night. Both final revised runs and the second baseline run
saved, returned to the menu, reloaded, and retained all 199 objects. The first
baseline used the same timed loop before the reload assertion was added.
All runs completed without unexpected errors. Neowyn gameplay source was not
changed.

The corresponding local NUnit evidence files are
`/tmp/crowded-final-baseline1.xml`, `/tmp/crowded-final-baseline2.xml`,
`/tmp/crowded-final-revised2.xml`, and `/tmp/crowded-final-revised3.xml`.
These paths describe this run's evidence; regenerate results with the commands
above on another machine. An experimental broader membership cache was measured
separately and removed because it showed no clear additional benefit.
