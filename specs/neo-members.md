# Neo Members spec

## User spec (do not edit)

look at `NeoMember`,`NeoMemberClass`, and `NeoMemberString`. It isn't working yet and it needs to get cleaned up, but I hope you can see what I'm trying to do. At a high level, I am working on getting a hierarchal representation of the project, where `NeoMember` contains the member, value, etc. The classes are a bit finicky, such as `childMembers` not working with polymorphic child `NeoMember` instances. I need you to fix this and then add a `NeoMember*` for each other type. Feel free to ask questions before you get started.

## Full spec

### Goal

Provide a hierarchical, runtime-typed wrapper layer over the wire-shape DTOs (`Member` / `MemberValue` and their typed subclasses) so consumers can navigate a project's member tree without manually traversing the `data.members` and `data.values` maps by id. Each `NeoMember*` instance binds together:

- The defining `Member` (typed per member kind — `StringMember`, `ClassMember`, etc.).
- The current `MemberValue` (typed per shape — `StringMemberValue`, `ObjectMemberValue`, etc.), looked up via the binding `valueId` (or an override).
- Any **child** `NeoMember*` instances for non-leaf member kinds (Class / Dictionary / List).
- Mutation methods (only on the `*Saved` variants, which support write-through to the live save).

The wire-shape DTO layer (`Runtime/Json/`) stays the source-of-truth read model; the `NeoMember*` layer is a navigation / mutation overlay on top.

### Hierarchy

Two tiers of base classes plus one concrete pair per member kind.

```
NeoNode                                                           (just holds NeoClient)
└── NeoMember                                                  (non-generic abstract — the polymorphic root)
    │   public abstract Member member { get; }
    │   public abstract MemberValue? value { get; }
    │   public static NeoMember Create(NeoClient, Member, string? overrideValueId)
    │
    └── NeoMember<TMember, TValue>                          (typed intermediate)
        │   where TMember : Member
        │   where TValue : MemberValue
        │   typed `member` / `value` via covariant overrides
        │
        ├── NeoMemberNull       : NeoMember<NullMember,       NullMemberValue>
        ├── NeoMemberBool       : NeoMember<BoolMember,       BoolMemberValue>
        ├── NeoMemberInt        : NeoMember<IntMember,        NumberMemberValue>
        ├── NeoMemberFloat      : NeoMember<FloatMember,      NumberMemberValue>
        ├── NeoMemberString     : NeoMember<StringMember,     StringMemberValue>
        ├── NeoMemberDictionary : NeoMember<DictionaryMember, ObjectMemberValue>
        ├── NeoMemberList       : NeoMember<ListMember,       ArrayMemberValue>
        ├── NeoMemberClass     : NeoMember<ClassMember,     ObjectMemberValue>
        ├── NeoMemberEnum       : NeoMember<EnumMember,       ArrayMemberValue>
        ├── NeoMemberLookup     : NeoMember<LookupMember,     ArrayMemberValue>
        └── NeoMemberNSGetter   : NeoMember<NSGetterMember,   NullMemberValue>
```

For each non-NSGetter, non-Null leaf, a writeable variant exists:

```
NeoMemberBool       ← NeoMemberBoolSaved       (Set(bool? value))
NeoMemberInt        ← NeoMemberIntSaved        (Set(int? value))
NeoMemberFloat      ← NeoMemberFloatSaved      (Set(float? value))
NeoMemberString     ← NeoMemberStringSaved     (Set(string? value))
NeoMemberDictionary ← NeoMemberDictionarySaved (Set<TValue>(string key, TValue?))
NeoMemberList       ← NeoMemberListSaved       (Add / RemoveAt / Set entries)
NeoMemberClass     ← NeoMemberClassSaved     (Set<TValue>(string key, TValue?))
NeoMemberEnum       ← NeoMemberEnumSaved       (Set(string[]?) — selected option ids)
NeoMemberLookup     ← NeoMemberLookupSaved     (Set(string[]?) — selected ids in target collection)
```

`NeoMemberNull` and `NeoMemberNSGetter` have no Saved variant — Null carries nothing to set, NSGetter values are computed (see [NSGetter](#nsgetter) below).

### Why the non-generic base

C# generics are invariant. `NeoMember<ClassMember, ObjectMemberValue>` is **not** assignable to `NeoMember<Member, MemberValue>` even though `ClassMember : Member` and `ObjectMemberValue : MemberValue`. So a heterogeneous container of children can't use the typed intermediate as its element type — it has to use the non-generic root.

The non-generic `NeoMember` exposes two concrete properties (`member`, `value`) so any consumer holding a non-generic reference can still reach the underlying DTO. The typed intermediate **shadows** these (via `new`) with strongly-typed wrappers that route through the base storage:

```csharp
public abstract class NeoMember : NeoNode
{
    public Member member { get; protected set; } = null!;
    public MemberValue? value { get; protected set; }
}

public abstract class NeoMember<TMember, TValue> : NeoMember
    where TMember : Member
    where TValue : MemberValue
{
    public new TMember member
    {
        get => (TMember)base.member;
        protected set => base.member = value;
    }
    public new TValue? value
    {
        get => (TValue?)base.value;
        protected set => base.value = value;
    }
}
```

Why shadowing rather than covariant return types? Covariant returns require runtime support (.NET 5+). Unity's Mono target on `netstandard2.1` doesn't have it, so the override path doesn't compile. Shadowing achieves the same caller-side ergonomics — a pattern-matched typed reference resolves to the typed property; a plain `NeoMember` reference resolves to the base — without needing runtime covariance.

Consumers iterating a `Dictionary<string, NeoMember>` use pattern-matching to recover the typed view:

```csharp
foreach (var (key, child) in classNode) {
    if (child is NeoMemberString s) {
        // s.member is StringMember (typed via the shadowed property)
        // s.value is StringMemberValue?
    } else if (child is NeoMemberClass c) {
        // c.member.classId is reachable directly
    }
}
```

### Children

| Member kind  | Children container                           | Notes                                                           |
| --------------- | -------------------------------------------- | --------------------------------------------------------------- |
| Null            | none                                         | leaf                                                            |
| Bool            | none                                         | leaf                                                            |
| Int / Float     | none                                         | leaf                                                            |
| String          | none                                         | leaf                                                            |
| Dictionary      | `Dictionary<string, NeoMember>` keyed     | mirrors Class — `IEnumerable<KeyValuePair<string, NeoMember>>` |
| List            | `IList<NeoMember>` ordered                | indexable; `IEnumerable<NeoMember>`                          |
| Class           | `Dictionary<string, NeoMember>` keyed     | keys come from the `NeoSchemaClass.schema`                          |
| Enum            | none (selected option ids on `Selected()`)   | options are static metadata on the linked `IEnum`               |
| Lookup          | none (selected ids on `Selected()`); helper  | `GetSelected()` returns `IList<NeoMember>` resolved from the looked-up collection |
| NSGetter        | none                                         | computed; see below                                             |

The children are populated in `Initialize(value)` and kept in sync with the underlying `value.value` map / list. Mutation through the `*Saved` variants both updates `client.SetSaveValue` and refreshes the `childMembers` cache.

### NSGetter

`NeoMemberNSGetter` exposes the compiled IR via `member.getter` (already typed via the wire-shape DTO layer). A `Compute()` method is declared but throws `NotImplementedException` for now — the actual IR evaluator is future scope. The class shape exists so consumer code can write against it today and the evaluator slot in later without an API change.

```csharp
public class NeoMemberNSGetter : NeoMember<NSGetterMember, NullMemberValue>
{
    public object? Compute() => throw new NotImplementedException(
        "NSGetter evaluation isn't implemented yet — track via the runtime evaluator work.");
}
```

### Factory

`NeoMember.Create(client, member, overrideValueId)` is a static factory that dispatches on the runtime type of `member` to instantiate the matching `NeoMember*` subclass. Centralising the dispatch in one place means:

- Adding a new member kind → add one switch arm, not 4–5 sites.
- Collection-kind `Initialize` methods (Class / Dictionary / List) just call `Create(...)` per child rather than maintaining their own per-kind `if (childMember is X) {...}` chain.

```csharp
public static NeoMember Create(
    NeoClient client,
    Member member,
    string? overrideValueId)
{
    return member switch {
        NullMember n       => new NeoMemberNull(client, n, overrideValueId),
        BoolMember b       => new NeoMemberBool(client, b, overrideValueId),
        IntMember i        => new NeoMemberInt(client, i, overrideValueId),
        FloatMember f      => new NeoMemberFloat(client, f, overrideValueId),
        StringMember s     => new NeoMemberString(client, s, overrideValueId),
        DictionaryMember d => new NeoMemberDictionary(client, d, overrideValueId),
        ListMember l       => new NeoMemberList(client, l, overrideValueId),
        ClassMember c     => new NeoMemberClass(client, c, overrideValueId),
        EnumMember e       => new NeoMemberEnum(client, e, overrideValueId),
        LookupMember lk    => new NeoMemberLookup(client, lk, overrideValueId),
        NSGetterMember ng  => new NeoMemberNSGetter(client, ng, overrideValueId),
        _                     => throw new System.ArgumentException(
            $"Unknown member type {member.GetType().Name}", nameof(member)),
    };
}
```

A parallel `CreateSaved(...)` returns the writeable variant when the consumer is building a save sub-tree. The Saved variant uses the same per-kind dispatch, returning `NeoMember*Saved` for the kinds that have one and the read-only `NeoMember*` for those that don't (Null / NSGetter).

### Lifecycle

1. **Construction.** Each `NeoMember*` ctor takes `(NeoClient, TMember | string memberId, string? overrideValueId)`. The base resolves the value via `client.TryGetValue(valueId, out TValue)` (where `valueId` falls back to `member.valueId` when no override). If a value is found, `Initialize(value)` runs (and walks children for Class / Dictionary / List). If not, `BuildEmptyData()` runs (default no-op; Saved variants may pre-allocate).

2. **Read.** Consumers reach typed values via the typed `member` / `value` properties. For collection member kinds, `child[key]` / `child[index]` returns a `NeoMember` that the caller pattern-matches on.

3. **Write.** `*Saved.Set(...)` mutates the underlying typed value, calls `client.SetSaveValue(value)` (which mirrors the change into `saveData.values`), and — for collection member kinds creating a fresh entry — also calls `client.AddSaveValue` to register the new id under `memberValueOverrides`.

4. **Persist.** `client.EmitHandleSave()` serialises `saveData` and hands the JSON back to the host (Unity) for storage. Not invoked automatically by `Set` — the host decides when to persist.

### Lookup helper

`NeoMemberLookup.GetSelected()` resolves the stored `string[]` of selected ids against the lookup target's collection and returns `IList<NeoMember>`. The selected ids point into the `collectionMemberId`'s value graph (a List or Dictionary member), so resolution walks:

1. Read `member.collectionMemberId` to find the target member.
2. If `member.collectionValueId` is set, use it as the target value id; otherwise use the target member's own `valueId`.
3. Look up the target value, which is an `ArrayMemberValue` (List/Lookup) or `ObjectMemberValue` (Dictionary).
4. For each selected id in `value.value`, resolve to a `NeoMember` via the target's children.

If the lookup target hasn't been instantiated as a `NeoMember*` yet, `GetSelected` instantiates ad-hoc rather than caching globally — keeping the wrapper layer free of cross-tree pinning.

### NeoClient surface

`NeoClient` exposes two roots:

- `assets : NeoMemberClass` — the project's authored assets sub-tree (read-only).
- `save : NeoMemberClassSaved` — the per-player save sub-tree (writeable).

Both are constructed in the `NeoClient` ctor after `LoadOrCreateSafe()` populates `saveData`. Consumers walk the trees via the standard navigation patterns above. There is no separate `InitializeTree()` — the trees self-construct via the `*` ctors.

`NeoClient` continues to expose:

- `TryGetMember<T>(id, out T)` / `TryGetClass(id, out NeoSchemaClass)` / `TryGetValue<T>(id, out T)` / `TryGetEnum<T>(id, out T)` — id-keyed lookups into the underlying maps.
- `AddSaveValue<T>(memberId, value)` — register a new value id under the save's `memberValueOverrides` and store the value.
- `SetSaveValue<T>(value)` — update an existing save value.

The `*Saved` writeable methods funnel through these.

### Naming conventions

- `NeoMember` (non-generic abstract base) and `NeoMember<TMember, TValue>` (typed intermediate) share a name — standard C# pattern (mirrors `IEnumerable` / `IEnumerable<T>`).
- Per-member concretes are `NeoMember<Kind>` (e.g. `NeoMemberString`).
- Writeable variants append `Saved` (e.g. `NeoMemberStringSaved`).
- The non-generic `NeoMember` is `abstract`, so consumers can't accidentally instantiate it without a kind.

### Out of scope (for this iteration)

- NSGetter evaluation (`Compute()` throws `NotImplementedException`).
- Reactive / observable change notifications — `Set` mutates and registers with the save store, but doesn't emit events. Consumers re-read after their own mutations.
- Schema validation beyond required-vs-null. The wire-shape DTOs already enforce shape; the wrapper layer assumes valid input.
- Cross-tree caching of resolved `NeoMember` references (e.g., in `Lookup.GetSelected`). Each lookup re-resolves; a future caching layer can sit on top if profiling shows it matters.
