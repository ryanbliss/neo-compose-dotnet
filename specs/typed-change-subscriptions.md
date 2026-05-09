# Typed Change Subscriptions

## Goal

Generated custom C# types should expose type-safe change subscriptions for schema fields while preserving a concise "anything changed" subscription for a generated object.

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

Each generated custom type emits a nested `Fields` marker class with one static `NeoField<T>` per schema key:

```csharp
public sealed class Fields
{
    private Fields() {}

    public static readonly NeoField<string> Name = new("Name");
    public static readonly NeoField<int> Bits = new("Bits");
}
```

Each generated custom type exposes exact-field subscriptions:

```csharp
public IDisposable OnChanged<T>(NeoField<T> field, Action<T> handler);
```

Writable generated custom types also expose a batch subscription:

```csharp
public IDisposable OnChanged(Action<NeoChangedArgs<Fields>> handler);
```

Usage:

```csharp
neo.Save.OnChanged(Save.Fields.Bits, bits => Debug.Log(bits));

neo.Save.OnChanged(args =>
{
    if (args.TryGet(Save.Fields.Inventory, out var inventory))
    {
        Debug.Log(inventory.Count);
    }
});
```

## Semantics

- Subscriptions are immediate and synchronous; no debounce or transaction coalescing is implied.
- Field subscriptions fire when the backing child node for that field reports a change.
- Read-only generated types expose only exact-field subscriptions. This keeps read-only wrappers observable for targeted cases without implying every read-only object is broadly reactive.
- Writable generated types expose object subscriptions that receive a `NeoChangedArgs<TFields>` containing the changed field when the runtime can identify it.
- If a custom object changes in a way that cannot be mapped to one child field, the object subscription receives a snapshot of all known generated fields.
- `IDisposable` returned from subscription removes the handler.
- The old generated-custom `event Action? OnChanged` is replaced by these methods. Collection helpers such as `NeoList<T>.OnChanged` and `NeoLookupSet<T>.OnChanged` remain unchanged.

## Implementation Notes

- `Fields` must be a nested sealed class, not a static class, so it can be used as a generic type argument in `NeoChangedArgs<Fields>`.
- Runtime support should live in the SDK, with generated code only supplying field tokens and field readers.
- Custom-node refresh must preserve child wrappers when schema key, attribute id, and value id are unchanged so subscriptions survive save materialization.
