# Neo Attributes spec

## User spec (do not edit)

look at `NeoAttribute`,`NeoAttributeCustom`, and `NeoAttributeString`. It isn't working yet and it needs to get cleaned up, but I hope you can see what I'm trying to do. At a high level, I am working on getting a hierarchal representation of the project, where `NeoAttribute` contains the attribute, value, etc. The types are a bit finicky, such as `childAttributes` not working with polymorphic child `NeoAttribute` instances. I need you to fix this and then add a `NeoAttribute*` for each other type. Feel free to ask questions before you get started.

## Full spec

### Goal

Provide a hierarchical, runtime-typed wrapper layer over the wire-shape DTOs (`Attribute` / `AttributeValue` and their typed subclasses) so consumers can navigate a project's attribute tree without manually traversing the `data.attributes` and `data.values` maps by id. Each `NeoAttribute*` instance binds together:

- The defining `Attribute` (typed per attribute kind — `StringAttribute`, `CustomAttribute`, etc.).
- The current `AttributeValue` (typed per shape — `StringAttributeValue`, `ObjectAttributeValue`, etc.), looked up via the binding `valueId` (or an override).
- Any **child** `NeoAttribute*` instances for non-leaf attribute types (Custom / Dictionary / List).
- Mutation methods (only on the `*Saved` variants, which support write-through to the live save).

The wire-shape DTO layer (`Runtime/Json/`) stays the source-of-truth read model; the `NeoAttribute*` layer is a navigation / mutation overlay on top.

### Hierarchy

Two tiers of base classes plus one concrete pair per attribute kind.

```
NeoNode                                                           (just holds NeoClient)
└── NeoAttribute                                                  (non-generic abstract — the polymorphic root)
    │   public abstract Attribute attribute { get; }
    │   public abstract AttributeValue? value { get; }
    │   public static NeoAttribute Create(NeoClient, Attribute, string? overrideValueId)
    │
    └── NeoAttribute<TAttribute, TValue>                          (typed intermediate)
        │   where TAttribute : Attribute
        │   where TValue : AttributeValue
        │   typed `attribute` / `value` via covariant overrides
        │
        ├── NeoAttributeNull       : NeoAttribute<NullAttribute,       NullAttributeValue>
        ├── NeoAttributeBool       : NeoAttribute<BoolAttribute,       BoolAttributeValue>
        ├── NeoAttributeInt        : NeoAttribute<IntAttribute,        NumberAttributeValue>
        ├── NeoAttributeFloat      : NeoAttribute<FloatAttribute,      NumberAttributeValue>
        ├── NeoAttributeString     : NeoAttribute<StringAttribute,     StringAttributeValue>
        ├── NeoAttributeDictionary : NeoAttribute<DictionaryAttribute, ObjectAttributeValue>
        ├── NeoAttributeList       : NeoAttribute<ListAttribute,       ArrayAttributeValue>
        ├── NeoAttributeCustom     : NeoAttribute<CustomAttribute,     ObjectAttributeValue>
        ├── NeoAttributeEnum       : NeoAttribute<EnumAttribute,       ArrayAttributeValue>
        ├── NeoAttributeLookup     : NeoAttribute<LookupAttribute,     ArrayAttributeValue>
        └── NeoAttributeNSGetter   : NeoAttribute<NSGetterAttribute,   NullAttributeValue>
```

For each non-NSGetter, non-Null leaf, a writeable variant exists:

```
NeoAttributeBool       ← NeoAttributeBoolSaved       (Set(bool? value))
NeoAttributeInt        ← NeoAttributeIntSaved        (Set(int? value))
NeoAttributeFloat      ← NeoAttributeFloatSaved      (Set(float? value))
NeoAttributeString     ← NeoAttributeStringSaved     (Set(string? value))
NeoAttributeDictionary ← NeoAttributeDictionarySaved (Set<TValue>(string key, TValue?))
NeoAttributeList       ← NeoAttributeListSaved       (Add / RemoveAt / Set entries)
NeoAttributeCustom     ← NeoAttributeCustomSaved     (Set<TValue>(string key, TValue?))
NeoAttributeEnum       ← NeoAttributeEnumSaved       (Set(string[]?) — selected option ids)
NeoAttributeLookup     ← NeoAttributeLookupSaved     (Set(string[]?) — selected ids in target collection)
```

`NeoAttributeNull` and `NeoAttributeNSGetter` have no Saved variant — Null carries nothing to set, NSGetter values are computed (see [NSGetter](#nsgetter) below).

### Why the non-generic base

C# generics are invariant. `NeoAttribute<CustomAttribute, ObjectAttributeValue>` is **not** assignable to `NeoAttribute<Attribute, AttributeValue>` even though `CustomAttribute : Attribute` and `ObjectAttributeValue : AttributeValue`. So a heterogeneous container of children can't use the typed intermediate as its element type — it has to use the non-generic root.

The non-generic `NeoAttribute` exposes two concrete properties (`attribute`, `value`) so any consumer holding a non-generic reference can still reach the underlying DTO. The typed intermediate **shadows** these (via `new`) with strongly-typed wrappers that route through the base storage:

```csharp
public abstract class NeoAttribute : NeoNode
{
    public Attribute attribute { get; protected set; } = null!;
    public AttributeValue? value { get; protected set; }
}

public abstract class NeoAttribute<TAttribute, TValue> : NeoAttribute
    where TAttribute : Attribute
    where TValue : AttributeValue
{
    public new TAttribute attribute
    {
        get => (TAttribute)base.attribute;
        protected set => base.attribute = value;
    }
    public new TValue? value
    {
        get => (TValue?)base.value;
        protected set => base.value = value;
    }
}
```

Why shadowing rather than covariant return types? Covariant returns require runtime support (.NET 5+). Unity's Mono target on `netstandard2.1` doesn't have it, so the override path doesn't compile. Shadowing achieves the same caller-side ergonomics — a pattern-matched typed reference resolves to the typed property; a plain `NeoAttribute` reference resolves to the base — without needing runtime covariance.

Consumers iterating a `Dictionary<string, NeoAttribute>` use pattern-matching to recover the typed view:

```csharp
foreach (var (key, child) in custom) {
    if (child is NeoAttributeString s) {
        // s.attribute is StringAttribute (typed via the shadowed property)
        // s.value is StringAttributeValue?
    } else if (child is NeoAttributeCustom c) {
        // c.attribute.customTypeId is reachable directly
    }
}
```

### Children

| Attribute kind  | Children container                           | Notes                                                           |
| --------------- | -------------------------------------------- | --------------------------------------------------------------- |
| Null            | none                                         | leaf                                                            |
| Bool            | none                                         | leaf                                                            |
| Int / Float     | none                                         | leaf                                                            |
| String          | none                                         | leaf                                                            |
| Dictionary      | `Dictionary<string, NeoAttribute>` keyed     | mirrors Custom — `IEnumerable<KeyValuePair<string, NeoAttribute>>` |
| List            | `IList<NeoAttribute>` ordered                | indexable; `IEnumerable<NeoAttribute>`                          |
| Custom          | `Dictionary<string, NeoAttribute>` keyed     | keys come from the `CustomType.schema`                          |
| Enum            | none (selected option ids on `Selected()`)   | options are static metadata on the linked `IEnum`               |
| Lookup          | none (selected ids on `Selected()`); helper  | `GetSelected()` returns `IList<NeoAttribute>` resolved from the looked-up collection |
| NSGetter        | none                                         | computed; see below                                             |

The children are populated in `Initialize(value)` and kept in sync with the underlying `value.value` map / list. Mutation through the `*Saved` variants both updates `client.SetSaveValue` and refreshes the `childAttributes` cache.

### NSGetter

`NeoAttributeNSGetter` exposes the compiled IR via `attribute.getter` (already typed via the wire-shape DTO layer). A `Compute()` method is declared but throws `NotImplementedException` for now — the actual IR evaluator is future scope. The class shape exists so consumer code can write against it today and the evaluator slot in later without an API change.

```csharp
public class NeoAttributeNSGetter : NeoAttribute<NSGetterAttribute, NullAttributeValue>
{
    public object? Compute() => throw new NotImplementedException(
        "NSGetter evaluation isn't implemented yet — track via the runtime evaluator work.");
}
```

### Factory

`NeoAttribute.Create(client, attribute, overrideValueId)` is a static factory that dispatches on the runtime type of `attribute` to instantiate the matching `NeoAttribute*` subclass. Centralising the dispatch in one place means:

- Adding a new attribute kind → add one switch arm, not 4–5 sites.
- Collection-type `Initialize` methods (Custom / Dictionary / List) just call `Create(...)` per child rather than maintaining their own per-type `if (childAttribute is X) {...}` chain.

```csharp
public static NeoAttribute Create(
    NeoClient client,
    Attribute attribute,
    string? overrideValueId)
{
    return attribute switch {
        NullAttribute n       => new NeoAttributeNull(client, n, overrideValueId),
        BoolAttribute b       => new NeoAttributeBool(client, b, overrideValueId),
        IntAttribute i        => new NeoAttributeInt(client, i, overrideValueId),
        FloatAttribute f      => new NeoAttributeFloat(client, f, overrideValueId),
        StringAttribute s     => new NeoAttributeString(client, s, overrideValueId),
        DictionaryAttribute d => new NeoAttributeDictionary(client, d, overrideValueId),
        ListAttribute l       => new NeoAttributeList(client, l, overrideValueId),
        CustomAttribute c     => new NeoAttributeCustom(client, c, overrideValueId),
        EnumAttribute e       => new NeoAttributeEnum(client, e, overrideValueId),
        LookupAttribute lk    => new NeoAttributeLookup(client, lk, overrideValueId),
        NSGetterAttribute ng  => new NeoAttributeNSGetter(client, ng, overrideValueId),
        _                     => throw new System.ArgumentException(
            $"Unknown attribute type {attribute.GetType().Name}", nameof(attribute)),
    };
}
```

A parallel `CreateSaved(...)` returns the writeable variant when the consumer is building a save sub-tree. The Saved variant uses the same per-kind dispatch, returning `NeoAttribute*Saved` for the kinds that have one and the read-only `NeoAttribute*` for those that don't (Null / NSGetter).

### Lifecycle

1. **Construction.** Each `NeoAttribute*` ctor takes `(NeoClient, TAttribute | string attributeId, string? overrideValueId)`. The base resolves the value via `client.TryGetValue(valueId, out TValue)` (where `valueId` falls back to `attribute.valueId` when no override). If a value is found, `Initialize(value)` runs (and walks children for Custom / Dictionary / List). If not, `BuildEmptyData()` runs (default no-op; Saved variants may pre-allocate).

2. **Read.** Consumers reach typed values via the typed `attribute` / `value` properties. For collection types, `child[key]` / `child[index]` returns a `NeoAttribute` that the caller pattern-matches on.

3. **Write.** `*Saved.Set(...)` mutates the underlying typed value, calls `client.SetSaveValue(value)` (which mirrors the change into `saveData.values`), and — for collection types creating a fresh entry — also calls `client.AddSaveValue` to register the new id under `attributeValueOverrides`.

4. **Persist.** `client.EmitHandleSave()` serialises `saveData` and hands the JSON back to the host (Unity) for storage. Not invoked automatically by `Set` — the host decides when to persist.

### Lookup helper

`NeoAttributeLookup.GetSelected()` resolves the stored `string[]` of selected ids against the lookup target's collection and returns `IList<NeoAttribute>`. The selected ids point into the `collectionAttributeId`'s value graph (a List or Dictionary attribute), so resolution walks:

1. Read `attribute.collectionAttributeId` to find the target attribute.
2. If `attribute.collectionValueId` is set, use it as the target value id; otherwise use the target attribute's own `valueId`.
3. Look up the target value, which is an `ArrayAttributeValue` (List/Lookup) or `ObjectAttributeValue` (Dictionary).
4. For each selected id in `value.value`, resolve to a `NeoAttribute` via the target's children.

If the lookup target hasn't been instantiated as a `NeoAttribute*` yet, `GetSelected` instantiates ad-hoc rather than caching globally — keeping the wrapper layer free of cross-tree pinning.

### NeoClient surface

`NeoClient` exposes two roots:

- `assets : NeoAttributeCustom` — the project's authored assets sub-tree (read-only).
- `save : NeoAttributeCustomSaved` — the per-player save sub-tree (writeable).

Both are constructed in the `NeoClient` ctor after `LoadOrCreateSafe()` populates `saveData`. Consumers walk the trees via the standard navigation patterns above. There is no separate `InitializeTree()` — the trees self-construct via the `*` ctors.

`NeoClient` continues to expose:

- `TryGetAttribute<T>(id, out T)` / `TryGetType(id, out CustomType)` / `TryGetValue<T>(id, out T)` / `TryGetEnum<T>(id, out T)` — id-keyed lookups into the underlying maps.
- `AddSaveValue<T>(attributeId, value)` — register a new value id under the save's `attributeValueOverrides` and store the value.
- `SetSaveValue<T>(value)` — update an existing save value.

The `*Saved` writeable methods funnel through these.

### Naming conventions

- `NeoAttribute` (non-generic abstract base) and `NeoAttribute<TAttribute, TValue>` (typed intermediate) share a name — standard C# pattern (mirrors `IEnumerable` / `IEnumerable<T>`).
- Per-attribute concretes are `NeoAttribute<Kind>` (e.g. `NeoAttributeString`).
- Writeable variants append `Saved` (e.g. `NeoAttributeStringSaved`).
- The non-generic `NeoAttribute` is `abstract`, so consumers can't accidentally instantiate it without a kind.

### Out of scope (for this iteration)

- NSGetter evaluation (`Compute()` throws `NotImplementedException`).
- Reactive / observable change notifications — `Set` mutates and registers with the save store, but doesn't emit events. Consumers re-read after their own mutations.
- Schema validation beyond required-vs-null. The wire-shape DTOs already enforce shape; the wrapper layer assumes valid input.
- Cross-tree caching of resolved `NeoAttribute` references (e.g., in `Lookup.GetSelected`). Each lookup re-resolves; a future caching layer can sit on top if profiling shows it matters.
