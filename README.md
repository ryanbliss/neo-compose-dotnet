# NeoCompose

A Unity C# package — `com.ryanbliss.neocompose`.

## Repository layout

```
src/
  NeoComposeUnity/        Unity package (Runtime + Editor + Tests)
samples/
  HelloWorld/             Unity 6000.5.4f1 project consuming the package
                          via a local file: dependency
    neo/                  format-4 Neo Compose workspace for the sample
```

The package source lives entirely under `src/NeoComposeUnity/` (Runtime
scripts, Editor scripts, and Unity Test Framework tests) — there's no
separate raw-.NET library or precompiled `.dll`.

## Setup

1. Open `samples/HelloWorld/` in Unity 6000.5.4f1.
2. The `com.ryanbliss.neocompose` package is referenced via a local path
   in `Packages/manifest.json`, so edits in `src/NeoComposeUnity/` are
   picked up live.

The Hello World project also serves as the downstream format-4 authoring
sample. Its tracked `.neo` source lives in `samples/HelloWorld/neo/`; see the
[sample README](./samples/HelloWorld/README.md#the-neo-workspace) for the
authoring and synchronization workflow.

## Loading during a scene transition

Games can keep their loading screen updating while constructor defaults replay:

```csharp
var game = await HelloWorldNeo.Load(
    synchronizer,
    cancellationToken: destroyCancellationToken);
```

Call this from the Unity main thread. Replay yields between completed steps after
an 8 ms work budget; individual constructors remain atomic, so this is not a hard
frame-time cap. The returned client is fully initialized. Cancellation disposes
the partial client and throws `OperationCanceledException`.

Keep the loading scene visible until world rendering completes, then dispose the client
when its owning game session ends.

## Tile and object placements

A tile placement is a generated `NeoTile` value. Its `Cell` is writable;
`Name`, `Sprite`, and `SmartTile` come from readonly NeoScript declarations.
The renderer reuses one Unity tile asset per class.

On a writable grid, use the generated classes directly:

```csharp
var tile = saveContent.Background.GetTile<NeoTile>(cell);
bool converted = tile != null && tile.TryConvert<GlassFloorTile>();
var placed = saveContent.Background.TrySetTile<GlassFloorTile>(cell);

var marker = new PlayerSpawnObject();
var spawned = saveContent.Objects.TrySpawn(cell, marker);
```

`TryConvert<T>()` preserves the placement row ID, its container, and `Cell`.
`TryConvert(target)` selects the target's runtime class without copying its
fields. A successful `TrySpawn` adopts the supplied object's identity. Clone
an existing owned object explicitly before placing another copy.

Object `PlacementTiles` contains only `NeoPlacementTile` values. This standalone
footprint type retains its own `Cell` member. It does not inherit rendering
properties or tile conversion from `NeoTile`.

The SDK wires the system native grid methods automatically. Generated clients
can call `Cell`, `GetObjects`, and `GetTile` without registering native handlers.

Layer-link lists are the placement data. SDK and NeoScript list edits pass
through placement validation, and the renderer observes the data changes.
The flattened `Content` queries return generated tile and object values;
the public `NeoResolvedTileInstance` and `NeoResolvedObjectInstance` snapshots
are removed.

Use generated `ToVariant` methods to apply real variants of an object's class.
The old `TrySwapVariant` APIs and class/value spawn overloads are removed.
Replacing an object with another class requires removing the old placement
and spawning a new object. Tile conversion changes the existing row's class.

## Runtime performance

[Inventory runtime measurements](docs/verification/inventory-runtime-performance.md)
document the real-game scenario, before/after frame and allocation samples,
causes addressed, and remaining equipment-change cost.

## Tests

- **Compilation preflight** — before opening the sample, verify that its
  generated code and local package compile together:

  ```bash
  UNITY_EDITOR=/path/to/Unity scripts/verify-unity-compile.sh
  ```

- **Package tests** — open the sample in Unity, then **Window → General →
  Test Runner**. The package's `NeoCompose.Unity.Tests` assembly shows up
  alongside the sample's `Tests` assembly.
- **Sample tests** — same Test Runner window; the `Tests`
  assembly demonstrates how a downstream project consumes + tests against
  the package.

## License

MIT. See [LICENSE](./LICENSE).
