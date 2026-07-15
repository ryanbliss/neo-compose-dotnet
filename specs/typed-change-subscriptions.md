# Typed Change Subscriptions

## Goal

Generated class C# wrappers should expose type-safe change subscriptions for schema fields while preserving a concise "anything changed" subscription for a generated object.

## API Shape

The SDK exposes field tokens and changed-args primitives:

```csharp
public interface INeoField
{
    string Key { get; }
    Type ValueType { get; }
}

public sealed class NeoField<T> : INeoField
{
    public string Key { get; }
    public Type ValueType { get; }
}

public sealed class NeoChangedArgs<TFields>
{
    public IReadOnlyDictionary<INeoField, object?> Changes { get; }
    public bool Has<T>(NeoField<T> field);
    public bool TryGet<T>(NeoField<T> field, out T value);
}
```

Each generated class emits a nested `Fields` marker class with one static `NeoField<T>` per schema key:

```csharp
public sealed class Fields
{
    private Fields() {}

    public static readonly NeoField<string> Name = new("Name");
    public static readonly NeoField<int> Bits = new("Bits");
}
```

Each generated class exposes exact-field subscriptions:

```csharp
public IDisposable OnChanged<T>(NeoField<T> field, Action<T, NeoChangeSource> handler);
```

Writable generated classes also expose a batch subscription
(`NeoChangedArgs` carries the same `Source`):

```csharp
public IDisposable OnChanged(Action<NeoChangedArgs<Fields>> handler);
```

`NeoChangeSource` tells subscribers where the change came from:

```csharp
public enum NeoChangeSource
{
    Local,    // a write made by this process
    External, // content applied from outside (e.g. a live save session co-editor)
}
```

Usage:

```csharp
neo.Save.OnChanged(Save.Fields.Bits, (bits, source) => Debug.Log($"{bits} ({source})"));

neo.Save.OnChanged(args =>
{
    if (args.Source == NeoChangeSource.External) RefreshHud();
    if (args.TryGet(Save.Fields.Inventory, out var inventory))
    {
        Debug.Log(inventory.Count);
    }
});
```

## Semantics

- Subscriptions are immediate and synchronous; no debounce or transaction coalescing is implied.
- Field subscriptions fire when the backing child node for that field reports a change.
- Read-only generated classes expose only exact-field subscriptions. This keeps read-only wrappers observable for targeted cases without implying every read-only object is broadly reactive.
- Writable generated classes expose object subscriptions that receive a `NeoChangedArgs<TFields>` containing the changed field when the runtime can identify it.
- If a class object changes in a way that cannot be mapped to one child field, the object subscription receives a snapshot of all known generated fields.
- `IDisposable` returned from subscription removes the handler.
- The old generated-class `event Action? OnChanged` is replaced by these methods. Collection classes (`NeoReadOnlyList<T>`, `NeoReadOnlyDictionary<T>`, `NeoReadOnlyLookupSet<T>` and their writable subclasses) align to the same shape: `IDisposable OnChanged(Action<TCollection, NeoChangeSource> handler)` — no `event` form.

## Implementation Notes

- `Fields` must be a nested sealed class, not a static class, so it can be used as a generic type argument in `NeoChangedArgs<Fields>`.
- Runtime support should live in the SDK, with generated code only supplying field tokens and field readers.
- Class-node refresh must preserve child wrappers when schema key, member id, and value id are unchanged so subscriptions survive save materialization.
