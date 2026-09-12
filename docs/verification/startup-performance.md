# Unity SDK startup performance

Measured on 2026-09-12 UTC with Hello World's committed export at baseline
`5f74c20`, Unity 6000.5.4f1, Linux x86_64, Editor Mono, and an Intel Core i7-9700K.
Both versions ran in batchmode with `-nographics`. The baseline used the unchanged
runtime and the same benchmark workload. [Raw samples](startup-performance.json)
include the first sample and every measured repeat.

| Measurement | Before | After |
| --- | ---: | ---: |
| First-sample SDK startup in PlayMode | 2,404 ms | 1,068 ms |
| Warm SDK startup in PlayMode | 1,815 ms | 337 ms |
| Warm client preparation in PlayMode | 1,529 ms | 175 ms |
| Preparation frame advances | 157 | 14 |
| Rendering frame advances | 6 | 2 |
| Hello World JSON parse, 3.66 million characters | 265 ms | 127 ms |
| JSON parse with 100,000 added rows, 13.40 million characters | 1,483 ms | 1,109 ms |
| Warm client preparation in EditMode | 8,058 ms | 552 ms |

Warm numbers are medians excluding sample zero. Startup sums the timed store
load, client preparation, world-content resolution, and rendering stages for
each sample before taking the median. It excludes scene import, application
boot, the sample's gameplay UI, and cloud save requests. Stores use an in-memory
local save store. Parser measurements exclude reading the file and constructing
the synthetic input. The large input retains Hello World and appends detached
numeric rows to `values`; it tests deserialization, not a larger playable world.

The test runner is unthrottled. Frame advances count actual yields, but its frame
rate is not representative of a game capped at 60 FPS. EditMode's update cadence
makes frequent `Task.Yield` calls particularly expensive. The first render still
costs about 120 ms in these runs. This does not measure GPU performance, IL2CPP,
or target-device startup.

The changes remove repeated work at four points:

- Nested JSON converters borrow the existing token subtree instead of rebuilding
  it. Validation and typed population still run. Text readers still construct
  their own object, and the project-level schema-version gate stays intact.
- Constructor replay builds an authored child-to-parent index once per replay
  operation. It preserves parent ordering and scans writable overlays live.
  Partition changes invalidate the index, and scope exit releases it.
- Node registration maintains a value-to-node index, including missing override
  bindings and subsequent value rebinding. Cleanup visits affected nodes instead
  of scanning the complete registry for every replayed root. Generated wrappers
  retain their existing disposal handling.
- Rendering uses an 8 ms cooperative budget across layers. Tile submission still
  batches 512 cells per native call. Explicit tile/object limits remain available,
  and yields occur before more work, without mandatory waits after every layer
  or final batch. Existing targets still get their deferred-destruction frame.

The default Resources source also parses on a worker thread in PlayMode on
platforms with thread support. Resource access and the store's continuation stay
on Unity's main thread. Reusing a source reuses its parsed schema. Editor reads
and WebGL players use synchronous parsing. Disposal during an outstanding parse
cannot publish a newly loaded store.

The previous renderer defaults were 512 tiles and 8 objects per frame. The new
defaults use the time budget without count limits. Existing explicit limits are
still honored across layers:

```csharp
await renderer.RenderAsync(content);

await renderer.RenderAsync(content, new NeoTileGridRenderOptions
{
    MaxMillisecondsPerFrame = 4,
    MaxTilesPerFrame = 512,
    MaxObjectsPerFrame = 8,
});
```

A snapshot or one object spawn can exceed the time budget because it is an
indivisible operation. Positive infinity disables the time limit. No claim of
absolute optimality follows from these measurements: reading JSON requires work
proportional to its length, and materializing rows and rendering cells require
work proportional to their counts. Indexing removes repeated full-corpus scans;
it does not prove the smallest possible constant cost. The root JSON tree,
typed records, native tile submission, object construction, and generated-wrapper
cleanup still consume time and memory.

Validation included the full EditMode suite with 2,048 passing tests and three
existing skips, all 14 PlayMode tests, and the focused final parsing, store,
registry, and benchmark tests. Regression coverage checks reader positioning,
input ownership, rebinding, same-key replacement, replay and partitions,
main-thread continuation, overlapping loads sharing parsing, disposal during load,
exact tile and object budgets across layers, and yielding after a callback exceeds
the time budget. `agent:verify` passed against the isolated Hello World rig.

Run the profiles from the SDK checkout:

```sh
unity test samples/HelloWorld --mode EditMode --filter StartupPerformanceTests --output /tmp/startup-edit.xml -- -nographics
unity test samples/HelloWorld --mode PlayMode --filter StartupPlayModePerformanceTests --output /tmp/startup-play.xml -- -nographics
```

The converter uses Json.NET's documented
[JTokenReader.CurrentToken](https://www.newtonsoft.com/json/help/html/T_Newtonsoft_Json_Linq_JTokenReader.htm)
and `Skip` behavior. Background parsing follows Unity's
[Task continuation rules](https://docs.unity3d.com/6000.0/Documentation/Manual/async-awaitable-continuations.html).
