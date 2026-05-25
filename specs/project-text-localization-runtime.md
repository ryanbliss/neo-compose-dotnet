# Project Text Localization Runtime

## Status

Draft for review. This spec describes the Unity export, Unity editor
synchronization, generated C# facade, and runtime changes needed to consume the
web project's versioned text localization records.

## Depends on

- [project-versioning-unity-runtime.md](./project-versioning-unity-runtime.md)
- [neo-compose-sdk.md](./neo-compose-sdk.md)
- Web spec: `../../Web/neo-compose/specs/project-text-localization.md`
- Web task ledger:
  `../../Web/neo-compose/specs/project-text-localization-tasks.md`

## Owns

- Unity export additions for localization config and locale runtime files.
- Unity editor synchronization of localization JSON files.
- Runtime loading, locale selection, fallback resolution, and formatting.
- Generated C# accessors for localized string attributes and enum display text.
- Dialogue runtime localization before existing Neo dialogue variable
  interpolation.
- Optional async loading from `Assets/StreamingAssets/Neo/Localization`.

## Non-goals

- No Unity-side editing of translations, statuses, source comments, or locale
  comments.
- No Unity `StringTable` authoring workflow in V1.
- No Addressables or remote download pipeline in V1.
- No localized images, audio, wiki content, NeoScript string literals, or
  locale-specific dialogue variable metadata.
- No runtime mutation of localized text values.

## Current Architecture Summary

Unity synchronization currently writes:

```text
Assets/Scripts/Neo/NeoGeneratedTypes.cs
Assets/Resources/Neo/project.json
```

The runtime loads `project.json` through `NeoLoader`, deserializes into
`ProjectData`, and exposes values through `NeoClient`, `NeoAttribute*`, and the
generated facade. String attributes, enum option text, dialogue descriptions,
dialogue node text, and dialogue option text are currently treated as literal
strings. After the web migration, localizable fields will store localized text
ids in the structural project export.

The web app is already migrating localized authoring records into:

- `localization-config`
- `localization-status`
- `localized-text`

The Unity runtime should not consume the full authoring records directly.
Runtime files should contain only the data needed in a build: locale config,
fallback chains, text ids, raw locale values, and the compiled formatting
syntax needed by Unity.

## Recommendation

Use Neo-owned JSON files as the canonical Unity synchronization artifact.
Do not require Unity `StringTable` assets for V1.

Unity's Localization package remains valuable because its Smart Strings are
based on SmartFormat and support pluralization, conditional logic, lists, and
culture-aware formatting. However, forcing the export through `StringTable`
assets would make progressive loading and editor synchronization more complex
than needed. The runtime can load simple JSON maps by locale and pass resolved
strings through a SmartFormat-compatible formatter.

This leads to the following default:

- Root locale JSON is always written under `Assets/Resources/Neo/Localization`.
- All locale JSON files are also written under
  `Assets/Resources/Neo/Localization` by default.
- Non-root locales can optionally be written under
  `Assets/StreamingAssets/Neo/Localization`.
- Synchronous generated getters always return a string. If async locale loading
  is enabled and the requested locale has not been loaded, getters fall back
  through already-loaded locale packs and finally to the root locale.
- Developers that opt into StreamingAssets should call an async preload API
  before reading localized runtime data.

## Export Contract

`IProjectUnityExport` should gain runtime localization metadata without
embedding every locale value directly into `project.json`.

```ts
interface IProjectUnityExport {
  // existing fields...
  localization?: IProjectUnityRuntimeLocalizationExport;
}

interface IProjectUnityRuntimeLocalizationExport {
  schemaVersion: 1;
  rootLocale: string;
  supportedLocales: IProjectUnityRuntimeLocale[];
  textIds: string[];
  rootLocaleFileName: string;
  localeFileNames: Record<string, string>;
  formatting: {
    syntax: "smart-format";
    sourceSyntax: "icu";
  };
}

interface IProjectUnityRuntimeLocale {
  locale: string;
  sourceLocale: string | null;
  archivedAt?: string | null;
}
```

The structural records in `project.json` should keep localized text ids in
localizable fields:

- `StringAttributeValue.value`
- `StringAttribute.defaultValue.value`
- `Dialogue.description`
- `DialogueTextNode.text`
- `DialogueTextOption.text`
- `EnumOption.text`

Each locale file should be a compact JSON map:

```json
{
  "schemaVersion": 1,
  "projectId": "project-id",
  "versionId": "version-id",
  "locale": "es-ES",
  "sourceLocale": "en-US",
  "formattingSyntax": "smart-format",
  "values": {
    "text-id-a": "Hola",
    "text-id-b": null
  }
}
```

`null` must mean "no authored value for this locale." Runtime fallback should
resolve through the source chain. Export must not materialize fallback strings
into raw locale files.

The web export should include locale file content in the editor export
response, not as remote URLs. Localization JSON files are small text artifacts
and should synchronize with generated C# and `project.json`.

```ts
interface IProjectUnityLocalizationFile {
  locale: string;
  fileName: string;
  content: string;
}

interface IProjectUnityEditorExportResponse {
  // existing fields...
  localizationFiles?: IProjectUnityLocalizationFile[];
}
```

## ICU To SmartFormat

The web app remains ICU-compatible for authoring. Unity export should compile
or convert ICU messages into SmartFormat-compatible strings before writing
locale JSON. Conversion should run on the TypeScript server, where ICU parsing
and diagnostics are already available.

V1 should support as much ICU as practical:

- Literal text.
- Simple named placeholders.
- Number and date/time formatting when an equivalent SmartFormat or
  culture-aware .NET format can be emitted.
- Plural and select expressions when they map cleanly to SmartFormat choices.
- Nested plural/select expressions when the converter can preserve behavior
  without ambiguous selector ordering.

Export diagnostics should report every unsupported or lossy conversion with:

- Text id.
- Locale.
- Source authoring context from localized text links when available.
- Original ICU text.
- Specific unsupported feature or parse error.

Diagnostics for unsupported ICU should be `error` severity by default because
runtime output would otherwise diverge from web preview. Unity synchronization
should reuse the existing generated C# diagnostics confirmation path: show the
errors, warn that affected localized strings may format incorrectly, and allow
the developer to continue writing the files anyway.

## Unity Dependencies

V1 should declare `com.unity.localization` as a hard UPM dependency and use the
SmartFormat implementation it ships under
`UnityEngine.Localization.SmartFormat`. Do not add `SmartFormat.NET` as a
separate dependency in V1, because projects that already use Unity Localization
would otherwise carry two SmartFormat implementations.

Add the dependency to `src/NeoComposeUnity/package.json` so Unity installs it
with the Neo Compose package:

```json
{
  "dependencies": {
    "com.unity.localization": "1.5.9"
  }
}
```

Use the current released Unity 6-compatible package version at implementation
time if `1.5.9` is no longer the appropriate choice.

Neo should still hide formatter details behind a small runtime abstraction:

```csharp
public interface INeoLocalizationFormatter
{
    string Format(string template, string locale, object? args = null);
}
```

The default implementation should wrap Unity Localization's SmartFormat API.
Neo should not force locale storage through Unity `StringTable` assets just to
use that formatter; JSON files remain the canonical synchronized data format.

The runtime should not expose Unity Localization package types in generated
project code or common gameplay APIs. This keeps developers insulated from the
underlying formatter and leaves room for a future "no Unity Localization
package" variant that could embed or reference `SmartFormat.NET` behind the
same interface.

## Unity Configuration

Add localization sync/loading fields to `NeoComposeConfig`:

```csharp
public string localizationResourcesDirectory =
    "Assets/Resources/Neo/Localization";
public string localizationStreamingAssetsDirectory =
    "Assets/StreamingAssets/Neo/Localization";
public bool useStreamingAssetsForNonRootLocales = false;
public bool preloadSystemLocale = true;
public string localeOverride = "";
```

Rules:

- `localizationResourcesDirectory` must be an `Assets/Resources/...` path.
- `localizationStreamingAssetsDirectory` must be an
  `Assets/StreamingAssets/...` path.
- Root locale is always synchronized to `localizationResourcesDirectory`.
- When `useStreamingAssetsForNonRootLocales` is false, every locale file is
  synchronized to `localizationResourcesDirectory`.
- When `useStreamingAssetsForNonRootLocales` is true, non-root locale files are
  synchronized to `localizationStreamingAssetsDirectory`.
- `localeOverride`, when non-empty, is the initial requested locale before
  game code changes it at runtime.

## Editor Synchronization

`NeoComposeSynchronizer` should write localization files during the same manual
synchronization that writes `project.json`, generated C#, sprites, and audio.

Synchronization steps:

1. Export project and localization files from the versioned web endpoint.
2. Validate `project.json` and localization file payloads are internally
   consistent.
3. Confirm replacement when any existing generated, project, or localization
   files will be overwritten.
4. Ensure configured localization directories exist.
5. Write root locale JSON to `Assets/Resources/Neo/Localization`.
6. Write non-root locale JSON to Resources or StreamingAssets based on config.
7. Delete previously synchronized locale files that are no longer in the
   export, but only within the configured Neo localization directories.
8. Save `NeoComposeConfig`.
9. Schedule post-synchronize processing.

The existing asset synchronization failure behavior should extend to
localization files: project files can still be written, but sync should report
which locale files failed.

## Runtime Loading

Add a runtime localization service owned by `NeoClient`:

```csharp
public sealed class NeoLocalization
{
    public string RootLocale { get; }
    public string CurrentLocale { get; }
    public IReadOnlyList<string> SupportedLocales { get; }
    public IReadOnlyList<string> LoadedLocales { get; }

    public void SetLocale(string locale);
    public string ResolveText(string? textId, object? args = null);
    public string? TryResolveText(string? textId, object? args = null);
    public string? GetTextId(NeoLocalizedField field);
    public bool IsLocaleLoaded(string locale);
    public void LoadLocale(string locale);
    public Awaitable LoadLocaleAsync(string locale);
    public Awaitable LoadAsync(string? locale = null);
}
```

`NeoClient` should expose:

```csharp
public NeoLocalization Localization { get; }
```

`NeoLoader.Load` should accept optional localization options:

```csharp
public sealed class NeoLocalizationOptions
{
    public string? LocaleOverride { get; set; }
    public bool PreloadSystemLocale { get; set; } = true;
    public bool UseStreamingAssetsForNonRootLocales { get; set; }
    public INeoLocalizationFormatter? Formatter { get; set; }
}
```

Default load behavior:

- Parse localization metadata from `project.json`.
- Load the root locale synchronously from `Resources`.
- Choose initial requested locale in this order:
  1. `NeoLocalizationOptions.LocaleOverride`
  2. `NeoComposeConfig.localeOverride`
  3. System locale.
  4. Root locale.
- Match locale by exact code first, then language-only match, then root locale.
- If `preloadSystemLocale` is true and the selected locale is in Resources,
  synchronously load the selected locale and its fallback chain.
- If selected/fallback locale files live in StreamingAssets, do not block
  `NeoLoader.Load`. Developers should call `await client.Localization.LoadAsync()`.

Synchronous fallback behavior:

1. Try the current requested locale if loaded.
2. Walk loaded fallback locales.
3. Use root locale.
4. Return `null` or empty string depending on the caller contract.

Async load behavior:

- `LoadAsync(locale)` loads the matched locale and every source locale in its
  fallback chain.
- Loaded locale files are cached for the life of the `NeoClient`.
- V1 does not need unload APIs.

StreamingAssets note: on Android and WebGL, `Application.streamingAssetsPath`
can be a URL rather than a direct filesystem path, so async loading should use
UnityWebRequest-compatible reads rather than `File.ReadAllText`.

## Generated C# Access

Generated code should continue to feel like ordinary C# string access.

For localizable string attributes:

```csharp
public string Name => client.Localization.ResolveText(nameNode.value?.value);
```

For nullable localizable strings:

```csharp
public string? Subtitle => client.Localization.TryResolveText(subtitleNode.value?.value);
```

For non-localizable string attributes, generated code should continue returning
the literal stored value.

Writable localizable string attributes should still support runtime `set` in
save/session paths, but setting the property must not mutate the project
localized text table. Instead, the generated setter should create or update a
local save/session override whose string payload is treated as a literal. That
override becomes "detached" from localization for that value row until it is
cleared or replaced by another explicit value id.

Generated writable code should therefore look conceptually like:

```csharp
public string Name
{
    get => client.Localization.ResolveStringAttributeValue(nameNode);
    set => nameNode.SetLiteralOverride(value);
}
```

Runtime string attribute values need enough metadata to distinguish exported
localized text ids from runtime-authored literal overrides. Do not infer this
only by checking whether the string happens to match a known text id, because a
player-authored literal could collide with a text id.

Proposal: add an optional SDK-owned field to C# `StringAttributeValue`:

```csharp
public enum NeoStringLocalizationMode
{
    TextId = 0,
    Literal = 1,
}

public class StringAttributeValue : AttributeValue<string?>
{
    public NeoStringLocalizationMode? neoLocalizationMode;
}
```

The JSON save/session shape for runtime-authored literal overrides becomes:

```json
{
  "id": "save-value-id",
  "value": "Custom sword name",
  "neoLocalizationMode": 1
}
```

Rules:

- Exported localizable string values/defaults omit `neoLocalizationMode` and
  are interpreted as text ids.
- Runtime-created overrides for localizable string attributes are interpreted
  as literals when marked `NeoStringLocalizationMode.Literal`.
- Non-localizable string attributes always interpret values as literals.
- Clearing a runtime override should restore the inherited exported text id and
  localization behavior.
- Setting a localizable string to `null` should follow existing required/null
  validation, then store a literal `null` override if valid.
- The web export does not need to understand or produce literal override
  markers; they are save/session runtime data only.

Generated code should provide field-token helpers instead of one-off text-id
properties for every string. Proposal: reuse the existing generated
`NeoField<T>` tokens from change subscriptions and add generated
`GetLocalizedTextId<T>(NeoField<T> field)` methods on generated custom value
wrappers.

```csharp
public string? GetLocalizedTextId<T>(NeoField<T> field);
```

The web code generator already emits stable field tokens:

```csharp
public static class Fields
{
    public static readonly NeoField<string> Name = new("Name");
}
```

Game code can then inspect:

```csharp
var textId = hero.GetLocalizedTextId(Hero.Fields.Name);
```

The generated implementation should keep a dictionary keyed by `INeoField`
whose reader returns the underlying text id only for localizable string fields.
Calling `GetLocalizedTextId` with a non-localizable field or a field not
defined on that generated type should return `null` or throw consistently with
the existing `OnChanged(NeoField<T> field, ...)` field validation behavior.

## Dialogue Runtime

Dialogue node and option text fields will store text ids after migration.
Dialogue runtime should resolve text before interpolation:

1. Resolve `node.text` or `option.text` as a localized text id through
   `client.Localization`.
2. Run the existing `{{neo-var:<id>}}` interpolation over the resolved text.
3. Run SmartFormat formatting over the interpolated text when formatting args
   are provided or the string contains SmartFormat placeholders.
4. Expose the final string through existing `NeoDialogueTextNode.Text` and
   `NeoDialogueTextOption.Text`.

Neo dialogue variable tokens should remain Neo-owned. Translators may move or
remove existing tokens. V1 should not let target locales introduce new variable
metadata that does not exist on the source dialogue node or option.

Hidden option diagnostics should also use localized text where possible, but
the hidden option model can keep raw text id diagnostics if localization fails.

`NeoDialogue.Description` should resolve through localization as well. If game
code needs the raw text id, expose a separate diagnostic property:

```csharp
public string? DescriptionTextId { get; }
```

## Enum Display Text

`EnumOption.text` will store a text id. Runtime and generated enum helpers
should resolve display text through `NeoLocalization`.

Generated enum helpers should accept either the enum node or client context
needed to reach localization:

```csharp
public string GetDisplayText(NeoClient client);
public string GetDisplayText(NeoAttributeEnum node);
```

The current `GetDisplayText(NeoAttributeEnum node)` pattern can remain the
preferred generated surface because the node already has a client reference.

## NSGetter Behavior

The web evaluator already resolves localized string value ids to root locale
values for authoring-time evaluation. The C# runtime should mirror runtime
locale behavior instead:

- Dereferencing a localizable string attribute from NSGetter should return the
  resolved string for `client.Localization.CurrentLocale`.
- Dereferencing a non-localizable string attribute should return the literal.
- Enum option stringification should use localized display text.

This keeps dialogue variable getters and generated computed strings consistent
with game-facing runtime output.

## Error Handling

Runtime resolution should be forgiving by default:

- Unknown text id: log warning, return the id in development builds or empty
  string in release builds.
- Missing locale file: log warning, fall back to root.
- Invalid locale file JSON: log error, fall back to root.
- SmartFormat runtime error: log error with text id and locale, return the
  unformatted resolved string.

Strict mode can be added later for tests and CI builds.

## Tests

Implementation should include Unity tests for:

- Deserializing localization metadata in `ProjectData`.
- Loading root locale JSON from Resources.
- Synchronous locale fallback through exact, language match, source chain, and
  root fallback.
- Cached multi-hop locale loading.
- StreamingAssets async loading path using a mocked loader.
- Localizable string attribute generated getters.
- Localizable writable string setters storing literal save/session overrides
  without mutating localized text ids.
- Clearing localizable string overrides restoring inherited localized
  resolution.
- Non-localizable string attributes preserving literal behavior.
- `GetLocalizedTextId` field-token lookup.
- Dialogue text resolving localized text before Neo variable interpolation.
- Dialogue option text and hidden option text localization.
- Enum display text localization.
- NSGetter localized string dereference.
- Export diagnostics for unsupported ICU to SmartFormat conversion.
- Editor synchronization writing root locale to Resources and non-root locales
  to the configured target.
- Editor synchronization deleting stale synchronized locale files only inside
  Neo localization directories.

Per repository policy, changes should be verified through the Unity Test Runner
from `samples/HelloWorld`, covering both `src/NeoComposeUnity/Tests/` and
`samples/HelloWorld/Assets/Tests/`.

## Resolved Design Decisions

- `com.unity.localization` is a hard UPM dependency declared by the Neo Compose
  Unity package.
- Runtime-authored localizable string overrides use
  `StringAttributeValue.neoLocalizationMode =
  NeoStringLocalizationMode.Literal`.
- Generated localized text-id inspection reuses existing `NeoField<T>` tokens
  through `GetLocalizedTextId<T>(NeoField<T> field)`.
- ICU conversion errors use the existing synchronization "continue anyway"
  confirmation path.
