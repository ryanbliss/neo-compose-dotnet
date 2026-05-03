# Neo Compose Unity C# SDK

## User spec (do not edit)

The Neo Compose web tool's purpose is to compose schema for static data assets, dynamic save file assets, and branching dialogue trees for use in games — starting with Unity. The reason it is in a web tool at all is because building high quality UX is much easier in web, with the added benefit of being a standalone platform decoupled from a single game engine. Because building data-driven branching dialogues in a UI requires having the data model well understood by the dialogue builder (e.g., `if (npc.friendshipPoints > 100) ? showDialogA() : showDialogB()`), Neo Compose asks developers to build their schema in its UI, treating it as the "source of truth" for their data. But of course, that data is still needed within the game. That's where the C# Unity SDK comes in.

While each Neo project has its own schema, the underlying types are standardized via `IProject`, `IAttribute`, `IAttributeValue`, `IEnum`, and `ICustomType`. A project's schema will be exported to a JSON file containing `IProjectUnityExport` — in [project-unity-export-types](../src/models/exports/project-unity-export-types.ts) and [project-unity-export-utils](../src/models/exports/project-unity-export-utils.ts) — which will need to deserialize via Newtonsoft (due to the complexity of the JSON).

While the developer will be able to access attributes, values, etc. if they wish, that is not the primary way developers will use the SDK. Alongside the JSON, Neo Compose will generate C# code using the project's schema, which internally will use the SDK to read and write values in the generic format that the SDK expects. This allows the developer to use the hierarchal data structures they defined in the web tool, despite the underlying data models being relatively flat. By keeping the data models flat, the SDK can serialize/deserialize data without knowing anything about the hierarchy of each project.

Let's take the following example project:

```
project:
    name = "HelloWorld"

types / attributes / values (default):
    Assets
        computedText: ComputedText (Custom)
            baseText: String = "Hello"
            optionalSuffix: String? = null
            fullText: NSGetter = $"return "{this.BaseText} {root.save.world}{this.optionalSuffix ?? ""}"
    Save
        world: Planet (Enum) = [Planet.earth]
        visited: List<VisitedPlanet> = [
            VisitedPlanet {
                world: Planet.earth,
                date: "2025-04-03"
            }
        ]
    VisitedPlanet
        world: Planet (Enum)
        date: String

enums:
    Planet
        mercury = Mercury
        venus = Venus
        earth = Earth
        mars = Mars
        ...
```

`Assets` are unchanging, whereas `Save` is dynamic. The `save` attribute is used to determine the initial value of a save file, which contains overridden save values and `attribute.valueId`s.

### Generated types

The generated types leverage the `NeoClient` (loaded via `NeoLoader`), which wraps the JSON data and save file into `NeoAttribute`s, consolidating values, attributes, types, etc. under a single umbrella. Each attribute type has its own `NeoAttribute*`.

The generated types would look something like this:

```c#
using NeoCompose.Runtime;

namespace Assets.Scripts.Neo
{
    public class HelloWorldClient : NeoNode
    {
        public Assets assets { get; protected init; }
        public Save save { get; protected init; }

        public HelloWorldClient(NeoClient client) : base(client)
        {
            assets = new(client, client.assets);
            save = new(client, client.save);
        }
    }

    public class Assets : NeoNode
    {
        private NeoAttributeCustom thisNode;

        public ComputedText computedText { get; protected init; }

        public Assets(NeoClient client, NeoAttributeCustom node) : base(client)
        {
            thisNode = node;
            NeoAttributeCustom computedTextNode = thisNode.Get("computedText");
            computedText = new(client, computedTextNode);
        }
    }

    public class Save : NeoNode
    {
        private NeoAttributeCustomSaved thisNode;

        private NeoAttributeEnumSaved worldNode;
        public Planet world {
            get => Planet.FromOptionId(worldNode.Selected[0]);
            set {
                worldNode.Set([value]);
            };
        }
        public string worldDisplayText => world.GetDisplayText();

        public NeoList<VisitedPlanet> visited { get; protected set; }

        public Save(NeoClient client, NeoAttributeCustomSaved node) : base(client)
        {
            thisNode = node;
            worldNode = thisNode.Get("world");
            NeoAttributeListSaved visitedNode = thisNode.Get("visited");
            visited = new(client, visitedNode);
        }
    }

    public class ComputedText : NeoNode
    {
        private NeoAttributeCustom thisNode;

        private NeoAttributeString baseTextNode;
        public string baseText => baseTextNode.value.value;

        private NeoAttributeString optionalSuffixNode;
        public string? optionalSuffix => optionalSuffixNode.value?.value;

        private NeoAttributeNSGetter fullTextNode;
        public NSGetterResult<string> fullText => fullTextNode.Compute();

        public ComputedText(NeoClient client, NeoAttributeCustom node) : base(client)
        {
            thisNode = node;
            baseTextNode = thisNode.Get("baseText");
            optionalSuffixNode = thisNode.Get("optionalSuffix");
            fullTextNode = thisNode.Get("fullText");
        }
    }

    public class Planet : IEquatable<Planet>
    {
        private readonly string _value;
        private Planet(string value) => _value = value;

        public static Planet mercury => new Planet("mercury");
        public static Planet venus => new Planet("venus");
        public static Planet earth => new Planet("earth");
        public static Planet mars => new Planet("mars");

        private static Dictionary<string, Planet> values = new Dictionary<string, Planet>
        {
            ["mercury"] = mercury,
            ["venus"] = venus,
            ["earth"] = earth,
            ["mars"] = mars
        };

        public static Planet FromOptionId(string optionId)
        {
            if (values.TryGetValue(optionId, out Planet planet))
            {
                return planet;
            }
            values.Add(optionId, new Planet(optionId));
        }

        public string GetDisplayText(NeoAttributeEnum node)
        {
            return node.GetOption(_value).text;
        }

        public bool Equals(Planet other)
        {
            if (other is null)
            {
                return false;
            }

            return _value == other._value;
        }

        public override bool Equals(object obj) => Equals(obj as Planet);

        public override int GetHashCode() => _value.GetHashCode();

        public static bool operator ==(Planet a, Planet b)
        {
            if (a is null || b is null)
            {
                return Equals(a, b);
            }

            return a.Equals(b);
        }

        public static bool operator != (Planet a, Planet b) => !(a == b);
    }

    public class VisitedPlanet : NeoNode
    {
        private NeoAttributeCustom thisNode;

        private NeoAttributeEnumSaved worldNode;
        public Planet world {
            get => Planet.FromOptionId(worldNode.Selected[0]);
            set {
                worldNode.Set([value]);
            };
        }
        public string worldDisplayText => world.GetDisplayText();

        private NeoAttributeString dateNode;
        public string date => dateNode.value.value;

        public VisitedPlanet(NeoClient client, NeoAttributeCustomSaved node) : base(client)
        {
            thisNode = node;
            worldNode = thisNode.Get("world");
            dateNode = thisNode.Get("date");
        }
    }
}
```

The types are intended to be very strong, using the information about the schema and known constraints to make assumptions about the shape that is generated. For example, `assets` is static, so all child types do not have value setters, whereas `save` children do. In cases where `required: true` on an attribute, the value return types are non-nullable (e.g., `string` vs `string?`). And for things like `Enum`, if `multiselect: false`, the value is `valueKey: EnumType`, but if `multiselect: true` then it is `valueKey: EnumType[]`. For collection types like `List` and `Dictionary`, the SDK will expose a new `NeoList` and `NeoDictionary` (or if not saveable, `NeoReadOnlyList` and `NeoReadOnlyDictionary`) class that the generated types will use, which should fulfill the `List` and `Dictionary` interfaces accordingly.

The generated types should be exposed in a new tab of `ProjectPageContainer.tsx` in the `neo-compose` web app. Right now that page exposes just the project json, so a new "Unity types" tab should be exposed to view the text of the generated C# classes. The "Copy to clipboard" button should be updated to copy the text of the currently open tab. Alongside the "Copy to clipboard" button, we should also show a new button for "Download files", which should download a ZIP file with a `project.json` file and `NeoGeneratedTypes.cs` for the generated C# code.

The unit tests should support using the existing test fixture scripts (including the real-world json) to copy the generated C# code for the appropriate JSON into the C# project, with unit tests then using those statically generated types (obviously tests would need to be updated after copied in).

## Full spec

### Goals

- Keep the Unity export JSON as the stable data contract. The web app continues to emit `IProjectUnityExport` using native `Record` / array / primitive shapes from `toUnityExport`; the C# SDK deserializes that with Newtonsoft into `ProjectData`.
- Add a generated C# facade on top of the generic SDK runtime so game code can use project-specific types, properties, enums, and collections without manually navigating `NeoAttribute` maps.
- Generate the C# facade in the web app as the primary mechanism. Unity receives `project.json` plus `NeoGeneratedTypes.cs`.
- Add a repeatable TypeScript fixture script that generates `NeoGeneratedTypes.cs` from the same JSON fixtures used by Unity tests and copies it into the SDK/sample test projects.
- Add SDK runtime support classes (`NeoList`, `NeoDictionary`, read-only variants, and change notifications) that generated code can rely on instead of duplicating collection logic.

### Non-goals

- A Unity Editor importer/generator UI. The web app is the source of generated files for this iteration.
- A new JSON wire shape. Do not flatten or reshape the export for codegen convenience.
- Full mod/package distribution strategy. Unknown ids and future schema additions must be tolerated, but loading external mod bundles is future scope.
- Dialogue runtime APIs. Dialogue needs this foundation, but this spec only covers project export, generated C# data access, save mutation, and NSGetter access.

### Existing architecture to preserve

The web app already owns the project authoring model:

- Domain models live under `/Users/ryanbliss/Documents/Development-Personal/Web/neo-compose/src/models`.
- `toUnityExport` in `/Users/ryanbliss/Documents/Development-Personal/Web/neo-compose/src/models/exports/project-unity-export-utils.ts` converts a `ProjectViewModel` to `IProjectUnityExport`.
- `ProjectPageContainer.tsx` currently renders a copyable JSON export.
- TypeScript tests in `project-unity-export-utils.test.ts` lock in the "plain record keyed by id" export shape.

The Unity SDK already owns generic runtime behavior:

- `Runtime/Json/*` mirrors the TS domain model and deserializes polymorphic attributes, values, type info, and NSGetter IR.
- `NeoLoader` loads `ProjectData` from JSON.
- `NeoClient` owns the project data, save data, root `assets` / `save` attributes, id-keyed lookups, save overrides, and a flat node registry.
- `NeoAttribute*` wrappers provide typed runtime navigation and `*Saved` mutation methods.
- `NeoAttributeCustom` resolves custom type inheritance via merged schemas.
- `NeoAttributeNSGetter.Compute(...)` evaluates compiled NeoScript IR through the C# evaluator.

Generated code must sit above this surface rather than replacing it.

---

### Generated output contract

The web app generates two files:

1. `project.json` — the pretty-printed `IProjectUnityExport`.
2. `NeoGeneratedTypes.cs` — one C# source file containing all project-specific generated types.

The generated C# namespace is hardcoded to:

```csharp
namespace Assets.Scripts.Neo
```

Users who want a different namespace can rename it after download. This keeps the first iteration simple and matches the common Unity convention of placing gameplay scripts under `Assets/Scripts`.

`NeoGeneratedTypes.cs` should include:

- One root client class named `{ProjectName}Client`.
- One generated class for the assets root custom type.
- One generated class for the save root custom type.
- One generated class per custom type.
- One generated wrapper type per enum.
- Any helper glue needed by generated code, unless that helper belongs in the SDK runtime and is reusable across projects.

The web app "Overview" page should expose two tabs:

- `Project JSON`
- `Unity types`

The existing `Copy to clipboard` button copies the active tab contents. A new `Download files` button downloads a ZIP containing `project.json` and `NeoGeneratedTypes.cs`.

---

### Web code generator

Add a pure generator module in the web app, e.g.:

```
src/models/exports/unity-codegen/
  generate-unity-types.ts
  csharp-identifiers.ts
  csharp-type-resolver.ts
  generate-unity-types.test.ts
  index.ts
```

Primary API:

```ts
export interface IGeneratedUnityTypesResult {
  code: string;
  diagnostics: IUnityCodegenDiagnostic[];
}

export function generateUnityTypes(
  project: IProjectUnityExport,
): IGeneratedUnityTypesResult;
```

The generator should accept plain `IProjectUnityExport`, not `ProjectViewModel`, so tests and scripts can feed it fixture JSON without Retree. `ProjectPageContainer` can call `toUnityExport(vm)` first and pass the result to the generator.

Diagnostics are non-fatal warnings/errors surfaced in the UI above the generated code. Fatal diagnostics should still return code when possible, but the code may intentionally contain a top-of-file comment explaining that generation could not complete safely.

Important diagnostics:

- Missing root assets/save attributes.
- Root attributes not `Custom`.
- Missing custom type for a custom attribute.
- Missing entry attribute for list/dictionary.
- Missing enum for enum attribute.
- Circular custom type inheritance.
- Invalid C# identifier schema key or enum option id.
- Duplicate generated member names after reserved-keyword handling.

### Identifier rules

Generated property names use schema keys exactly.

Exception: if a schema key is a C# reserved keyword, append `Value`.

Examples:

| Schema key | Generated member |
| ---------- | ---------------- |
| `baseText` | `baseText`       |
| `world`    | `world`          |
| `class`    | `classValue`     |
| `event`    | `eventValue`     |

Do not silently convert arbitrary invalid identifiers. If a schema key contains spaces, punctuation, starts with a number, or otherwise is not a valid C# identifier, emit a diagnostic. This preserves the rule that game code names reflect schema names instead of hiding a lossy rename.

Generated type names use project/custom type/enum names converted to valid PascalCase C# identifiers because C# type names cannot safely preserve arbitrary display names. Collisions are resolved deterministically by suffixing a stable short id fragment.

### Generated root client

For project `HelloWorld`, generate:

```csharp
public class HelloWorldClient : NeoNode
{
    public Assets assets { get; }
    public Save save { get; }

    public HelloWorldClient(NeoClient client) : base(client)
    {
        assets = new Assets(client, client.assets);
        save = new Save(client, client.save);
    }
}
```

`assets` is always read-only. `save` is always writeable.

The generated client constructor accepts an already-loaded `NeoClient`, rather than loading files directly. File IO remains game-specific and stays in user code / sample code.

---

### Generated custom types

Each generated custom type wraps a `NeoAttributeCustom` or `NeoAttributeCustomSaved` node. The node field should be protected so derived generated classes can reuse it.

For a non-inherited type:

```csharp
public class ComputedText : NeoNode
{
    protected NeoAttributeCustom thisNode;

    public ComputedText(NeoClient client, NeoAttributeCustom node) : base(client)
    {
        thisNode = node;
        baseTextNode = thisNode.Get<NeoAttributeString>("baseText");
    }
}
```

For a type with `extendsTypeId`, generate C# inheritance:

```csharp
public class ToolItem : InventoryItem
{
    public ToolItem(NeoClient client, NeoAttributeCustom node) : base(client, node)
    {
        miningPowerNode = thisNode.Get<NeoAttributeInt>("miningPower");
    }
}
```

If `ICustomType.isAbstract === true`, generate `abstract class`.

Lowest-level types whose `extendsTypeId` is unset extend `NeoNode`. Derived types extend the generated C# class for their parent type. The generated class hierarchy mirrors `ICustomType.extendsTypeId`.

Inherited fields should be available through normal C# inheritance. A derived class only emits members for schema keys it owns/overrides; inherited members come from its base generated class.

When a runtime value row has `typeId` set to a more-derived type than the attribute's declared `customTypeId`, factory creation should return the generated class matching the runtime type id. This is required for lists/dictionaries of an abstract or base type to expose derived members when the concrete row uses a derived `typeId`.

### Generated custom type factories

Each generated custom type should expose a factory method for constructing new generated values in game code. This is the ergonomic path for:

- Setting a save-backed custom attribute to a new object.
- Adding a new custom object to a `NeoList<T>`.
- Assigning a dictionary entry in `NeoDictionary<T>`.
- Creating a derived object for a base/abstract collection.

Factory shape:

```csharp
public class ToolItem : InventoryItem
{
    public static ToolItem factory(
        NeoClient client,
        string name,
        int miningPower,
        Material material)
    {
        // Builds a save-side ObjectAttributeValue graph using the
        // generated schema, registers child values, sets typeId to
        // ToolItem's custom type id, and returns the generated wrapper.
    }
}
```

The method name is intentionally `factory` (lowercase) so it follows the same schema-key-as-code style as generated properties and stays visually distinct from constructors.

Factories should:

- Accept one argument per settable generated field owned by the type and its inherited base types, using generated C# property names.
- Omit NSGetter fields because they are computed.
- Accept nullable arguments for optional attributes.
- Use generated enum wrapper types for enum fields.
- Use `IEnumerable<T>` / dictionary-friendly shapes for list and dictionary fields.
- Set `typeId` on the created custom value row to the generated type's custom type id.
- Create all required child value rows.
- Register created rows through `NeoClient` / SDK helper APIs rather than mutating `ProjectSaveData` directly.
- Return the generated wrapper bound to the newly-created value row.

Factories should be generated for abstract custom types only when they are useful for base initialization. An abstract type factory must not instantiate that abstract type directly; it may expose protected/shared helper logic used by derived factories.

Generated constructors remain wrapper constructors around existing `NeoAttributeCustom` nodes. Generated factories are separate creation helpers that materialize new save-side value graphs.

### Static vs saved custom wrappers

Generated classes should distinguish read-only and saved contexts:

- Classes under `assets` wrap `NeoAttributeCustom`.
- Classes under `save` wrap `NeoAttributeCustomSaved`.
- Collection entries inherit the writeability of their parent collection.
- NSGetter results and other synthetic read-only values wrap read-only nodes.

Implementation options:

1. Generate one class with overloaded constructors for read-only/saved nodes plus private nullable saved fields.
2. Generate paired classes (`Foo` / `FooSaved`) when a type can appear in both contexts.

Prefer paired generated classes if it keeps generated setters and collection types simpler. The public shape should stay intuitive: save-backed properties expose setters; asset-backed properties do not.

---

### Generated attribute members

Primitive attributes:

| Attribute type | Read-only C# type        | Saved setter value type |
| -------------- | ------------------------ | ----------------------- |
| Null           | `object?` or `null`      | none                    |
| Bool           | `bool` / `bool?`         | `bool?`                 |
| Int            | `int` / `int?`           | `int?`                  |
| Float          | `float` / `float?`       | `float?`                |
| String         | `string` / `string?`     | `string?`               |
| Enum           | generated enum wrapper   | generated enum wrapper  |
| Lookup         | generated target wrapper | selected id wrapper     |
| NSGetter       | `NSGetterResult`         | none                    |

Nullability is derived from `attribute.required`:

- `required: true` produces non-nullable return types where the runtime can reasonably guarantee the value exists.
- `required: false` produces nullable return types.
- If data is missing at runtime for a required attribute, generated code should throw a focused exception naming the schema key and value id rather than returning a misleading default.

Saved primitive properties call the corresponding `NeoAttribute*Saved.Set(...)` method in their setter. These methods already write through `NeoClient` save state. The SDK runtime should expose change notifications so generated wrappers and collection wrappers can refresh cached children after set/remove/add operations.

NSGetter attributes expose computation, not a setter:

```csharp
private NeoAttributeNSGetter fullTextNode;
public NSGetterResult fullText => fullTextNode.Compute();
```

If/when `NSGetterResult<T>` is added, generated code may use the typed result:

```csharp
public NSGetterResult<string> fullText => fullTextNode.Compute<string>();
```

Until that generic API exists, generated code uses the current non-generic `NSGetterResult`.

### Enum attributes

Generate one wrapper class per `IEnum`:

```csharp
public sealed class Planet : System.IEquatable<Planet>
{
    private readonly string value;

    private Planet(string value)
    {
        this.value = value;
    }

    public string optionId => value;

    public static readonly Planet mercury = new Planet("mercury");
    public static readonly Planet venus = new Planet("venus");
    public static readonly Planet earth = new Planet("earth");

    private static readonly Dictionary<string, Planet> values =
        new Dictionary<string, Planet>
        {
            ["mercury"] = mercury,
            ["venus"] = venus,
            ["earth"] = earth,
        };

    public static Planet FromOptionId(string optionId)
    {
        if (values.TryGetValue(optionId, out var known)) return known;
        var unknown = new Planet(optionId);
        values[optionId] = unknown;
        return unknown;
    }
}
```

Unknown option ids are supported. This is required for modded data and forward compatibility. Unknown ids do not have generated static members, so user code should handle `default` cases when using switch expressions/statements.

`GetDisplayText(NeoAttributeEnum node)` should use the runtime enum metadata when available. If the option id is unknown to the enum metadata, return the raw option id.

For `multiselect: false`, generated property type is `Planet` / `Planet?`.

For `multiselect: true`, generated property type is `IReadOnlyList<Planet>` in read-only contexts and a settable collection-friendly shape in saved contexts. Setter input should accept `IEnumerable<Planet>` or `Planet[]` and convert to `string[]` option ids.

### Lookup attributes

Lookup values store selected target value ids. Generated lookup access should expose the looked-up target rows, not just raw ids.

For `multiselect: false`:

- Read-only property: target generated type or nullable target generated type.
- Saved setter: accept a target generated type, a `NeoLookupSelection`, or another SDK type that exposes the selected `valueId`.

For `multiselect: true`:

- Read-only property: `IReadOnlyList<TTarget>`.
- Saved property: a collection wrapper that can set the selected target ids.

The SDK should expose a small reusable selection abstraction so generated lookup code does not need to rely on protected/internal details of target wrappers. A generated custom wrapper should be able to reveal its backing value id for lookup selection without exposing mutable DTO internals.

---

### Runtime collection classes

Add reusable SDK runtime collection wrappers:

```
NeoReadOnlyList<T>
NeoList<T>
NeoReadOnlyDictionary<T>
NeoDictionary<T>
```

These are real SDK classes, not generated per-project classes. Generated code uses them for list/dictionary attributes.

Suggested constructor shape:

```csharp
public sealed class NeoReadOnlyList<T> : IReadOnlyList<T>
{
    public NeoReadOnlyList(
        NeoClient client,
        NeoAttributeList node,
        Func<NeoClient, NeoAttribute, T> createItem);
}

public sealed class NeoList<T> : IList<T>
{
    public NeoList(
        NeoClient client,
        NeoAttributeListSaved node,
        Func<NeoClient, NeoAttribute, T> createItem,
        Func<T, object?> serializeItem);
}
```

Dictionary equivalents should implement `IReadOnlyDictionary<string, T>` and, for saved dictionaries, a practical mutation surface such as indexer set, `Add`, `Remove`, and `ContainsKey`.

Generated code supplies item factory delegates:

```csharp
visited = new NeoList<VisitedPlanet>(
    client,
    visitedNode,
    (client, attr) => new VisitedPlanet(client, (NeoAttributeCustomSaved)attr),
    item => item.ToNeoValuePayload());
```

Collection wrappers are responsible for:

- Refreshing count/index/key caches when the underlying `NeoAttributeListSaved` / `NeoAttributeDictionarySaved` changes.
- Calling SDK saved methods (`Add`, `Set`, `RemoveAt`, `Remove`) rather than mutating DTOs directly.
- Raising change notifications when their shape changes.
- Preserving runtime `typeId` for custom entries.
- Accepting generated custom type instances produced by `TypeName.factory(...)` for add/set operations.

Do not ask generated code to duplicate child-cache synchronization.

---

### Reactivity and save mutation

`NeoAttribute*Saved.Set(...)` updates `NeoClient` save state today, but generated code needs a reliable way to react to changes.

Add SDK-level change notifications:

- `NeoClient.OnSaveValueChanged(valueId)`
- `NeoClient.OnSaveOverrideChanged(attributeId, valueId?)` (already exists)
- Collection-level events on `NeoAttributeListSaved` / `NeoAttributeDictionarySaved` / `NeoAttributeCustomSaved`, or a shared `NeoAttribute.OnChanged`

Saved setters and collection mutations should:

1. Validate required/null constraints.
2. Create or mutate the correct save-side `AttributeValue`.
3. Notify the affected attribute/value.
4. Refresh wrapper child caches.
5. Let the host decide when to persist to disk.

`NeoClient` should expose a public persistence method:

```csharp
public void Save();
public string SerializeSaveData();
```

The current `EmitHandleSave()` / `SerializeSaveData()` behavior is protected. Generated code does not need to call save automatically, but user code needs a supported public API for explicit persistence after a batch of runtime mutations.

---

### Web UI changes

Update `/Users/ryanbliss/Documents/Development-Personal/Web/neo-compose/src/app/projects/[projectId]/ProjectPageContainer.tsx`:

- Compute `unityExportText` as today.
- Compute generated C# with `generateUnityTypes(toUnityExport(vm))`.
- Render Fluent `TabList` with `Project JSON` and `Unity types`.
- Render a shared code viewer for active tab text.
- `CopyToClipboard` receives active tab text.
- Add `Download files` button.

ZIP generation can be client-side. If adding a dependency is acceptable, use a small maintained ZIP library. If avoiding a dependency, implement a minimal ZIP writer for two UTF-8 files in the web app. The output file name should default to `{project.name}-unity-export.zip`.

The `Download files` button includes:

- `project.json`
- `NeoGeneratedTypes.cs`

If codegen has fatal diagnostics, disable download or include a clear warning above the button. Non-fatal diagnostics can still allow download.

---

### Fixture scripts and cross-project tests

The primary codegen fixture script lives in the web app:

```
scripts/dump-unity-generated-types.ts
```

It should:

1. Load or build the same fixture data used for `dump-synth-export.ts`.
2. Call `toUnityExport`.
3. Call `generateUnityTypes`.
4. Write `NeoGeneratedTypes.cs` to stdout by default.
5. Optionally copy the generated file into the SDK/sample project when passed an explicit flag.

Example:

```bash
npx tsx scripts/dump-unity-generated-types.ts \
  --fixture synth \
  --copy-to /Users/ryanbliss/Documents/Development-Personal/Libraries/NeoComposeDotnet/samples/HelloWorld/Assets/Scripts/Neo/NeoGeneratedTypes.cs
```

Add a second mode for the real-world fixture:

```bash
npx tsx scripts/dump-unity-generated-types.ts \
  --fixture project-example \
  --copy-to /Users/ryanbliss/Documents/Development-Personal/Libraries/NeoComposeDotnet/samples/HelloWorld/Assets/Scripts/Neo/NeoGeneratedTypes.cs
```

Do not silently write into the SDK repo by default. Copying generated files into another project should be opt-in so normal web tests do not mutate a sibling repo.

Unity-side tests should include:

- Generated code compiles in the sample project.
- Generated root client can wrap a `NeoClient`.
- Generated read-only asset properties read expected values.
- Generated save properties call saved setters and observe updated values.
- Generated lists/dictionaries expose count/index/key access and mutation.
- Generated custom type factories create new object graphs that can be assigned to save fields and added to lists/dictionaries.
- Generated enum wrappers support known static members and unknown ids.
- Generated inherited custom types expose inherited members through C# inheritance.
- Abstract custom types generate `abstract class` and cannot be directly instantiated by generated factories.
- Runtime `typeId` dispatch returns the most-derived generated wrapper for custom values.
- NSGetter generated properties compute through `NeoAttributeNSGetter`.

Web-side tests should include:

- `generateUnityTypes` snapshot-style tests for a small fixture.
- Identifier rules: exact schema keys, reserved keyword suffix, invalid identifier diagnostics.
- Enum unknown-id support emitted in generated code.
- Inheritance codegen: base class, derived class, abstract class.
- Project page active-tab copy text behavior.
- Download file payload names and contents.

Verification policy:

- Web changes finish with `npm run doctor` in `/Users/ryanbliss/Documents/Development-Personal/Web/neo-compose`.
- SDK/runtime changes finish with Unity Test Runner coverage for both `src/NeoComposeUnity/Tests/` and `samples/HelloWorld/Assets/Tests/`.

---

### Implementation phases

1. **Web codegen core**
   - Add `generateUnityTypes`.
   - Generate root client, custom classes, primitive properties, enum wrappers.
   - Add unit tests for small fixtures and identifier behavior.

2. **SDK runtime collections**
   - Add `NeoReadOnlyList<T>`, `NeoList<T>`, `NeoReadOnlyDictionary<T>`, `NeoDictionary<T>`.
   - Add public read/mutation APIs needed by generated code.
   - Add Unity tests for collection behavior independent of generated code.

3. **SDK reactivity/persistence surface**
   - Add public `NeoClient.Save()` / `SerializeSaveData()`.
   - Add value/attribute change notifications.
   - Ensure saved setters and collection mutations refresh children and fire notifications.

4. **Generated saved/read-only split**
   - Generate save-backed setters and read-only asset properties.
   - Generate collection properties using the new runtime collection classes.
   - Generate custom type factories for new object creation.
   - Generate lookup access and selection helpers.

5. **Generated inheritance and runtime type dispatch**
   - Generate C# class inheritance from `extendsTypeId`.
   - Generate `abstract class` for abstract custom types.
   - Add generated factories that choose the most-derived wrapper based on row `typeId`.

6. **Project page export UI**
   - Add `Project JSON` / `Unity types` tabs.
   - Wire copy to active tab.
   - Add `Download files` ZIP output.
   - Show codegen diagnostics.

7. **Fixture script and downstream Unity tests**
   - Add `dump-unity-generated-types.ts`.
   - Copy generated files into sample test path with explicit flag.
   - Add Unity tests that use generated types.

8. **Real-world fixture hardening**
   - Run generation against `project-example.json`.
   - Fix diagnostics that should be supported.
   - Document any unsupported schema names or edge cases.

---

### Risks and design notes

- **Identifier exactness vs compile safety.** Schema keys should remain exact in generated property names. Invalid C# identifiers need diagnostics instead of silent renames, otherwise user code will not obviously match the web schema.
- **Generic variance.** Runtime collections and generated factories must avoid relying on generic covariance that Unity's runtime may not support. Use explicit delegates and non-generic `NeoAttribute` where needed.
- **Save persistence.** Runtime setters update save state, but persistence to disk should remain explicit. Generated code should make mutation easy, not hide file writes.
- **Unknown enum ids.** Unknown ids are normal in modded/forward-compatible data. Generated enum wrappers must never assume the static generated option set is exhaustive.
- **Custom type inheritance.** The runtime already merges schema for generic navigation. Generated code additionally needs C# inheritance so game code can use base/derived types naturally.
- **Runtime `typeId`.** Generated collection factories must respect value row `typeId`; otherwise lists of base/abstract types will lose derived behavior.
- **Cross-repo scripts.** Fixture scripts originate in the web app because codegen is a web concern, but copying into the SDK/sample project must be explicit and deterministic.
