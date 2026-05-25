# Project text localization runtime implementation tasks

## Status

Draft task ledger for implementing
[project-text-localization-runtime.md](./project-text-localization-runtime.md).

Active chunk: Phase 1 Unity package/DTO/config setup is partially in place.
Next chunk should add the web export contract/model changes before moving into
locale file synchronization.

Use this file as the source of implementation status. Mark a task complete only
after its implementation and relevant tests/verification are done. If a task is
split during implementation, add child tasks under the original task instead of
reusing the task id for a different scope.

## Status report format

When reporting progress, summarize:

- Current phase and chunk.
- Completed task ids since the last report.
- Blocked task ids, with the blocking decision or failing test.
- Next recommended task ids.
- Verification run, including Unity Test Runner coverage.

Example:

```txt
Phase 2: Runtime localization loader
Completed: LRT-021, LRT-022, LRT-026
Blocked: LRT-030 needs StreamingAssets mock loader approved
Next: LRT-027, LRT-028
Verification: src/NeoComposeUnity/Tests runtime tests passed in HelloWorld Unity Test Runner
```

## Phase 1: Export contract and package setup

Goal: define the Unity runtime localization wire contract, add the Unity
Localization package dependency, and make Unity DTOs tolerant of localization
metadata before changing runtime behavior.

### Web export contract

- [ ] LRT-001 Add Unity export localization metadata model types in the web repo.
- [ ] LRT-002 Add compact locale runtime file model types in the web repo.
- [ ] LRT-003 Add `localization` metadata to `IProjectUnityExport`.
- [ ] LRT-004 Add `localizationFiles` to the Unity editor export response.
- [ ] LRT-005 Build locale file names with stable, safe locale-code naming.
- [ ] LRT-006 Export root locale and supported locale fallback metadata.
- [ ] LRT-007 Export raw locale values with `null` preserved for missing translations.
- [ ] LRT-008 Exclude localization status/comment/link metadata from runtime locale files.
- [ ] LRT-009 Add web tests for Unity export localization metadata shape.
- [ ] LRT-010 Add web tests for locale runtime file JSON shape.

### Package and DTO setup

- [x] LRT-011 Add `com.unity.localization` as a hard dependency in `src/NeoComposeUnity/package.json`.
- [x] LRT-012 Add Unity C# DTOs for `ProjectData.localization` metadata.
- [x] LRT-013 Add Unity C# DTOs for locale runtime file JSON.
- [x] LRT-014 Add editor API models for `localizationFiles`.
- [x] LRT-015 Add config fields for localization Resources directory, StreamingAssets directory, streaming opt-in, preload behavior, and locale override.
- [x] LRT-016 Validate localization Resources and StreamingAssets directories in `NeoComposeSynchronizer.ValidateConfig`.
- [x] LRT-017 Add tests for config path validation.

### Phase 1 verification

- [ ] LRT-018 Run focused web Unity export tests.
- [x] LRT-019 Run focused Unity DTO/config tests from the HelloWorld Unity Test Runner.

## Phase 2: ICU to SmartFormat export diagnostics

Goal: convert web-authored ICU messages into SmartFormat-compatible runtime
strings for Unity, and report diagnostics when conversion is unsupported or
lossy.

### Conversion

- [ ] LRT-020 Add ICU parser/converter utility for Unity SmartFormat output in the web repo.
- [ ] LRT-021 Convert literal text and escaped braces safely.
- [ ] LRT-022 Convert simple named placeholders.
- [ ] LRT-023 Convert supported number formats to culture-aware SmartFormat/.NET formats.
- [ ] LRT-024 Convert supported date/time formats to culture-aware SmartFormat/.NET formats.
- [ ] LRT-025 Convert plural expressions where SmartFormat can preserve behavior.
- [ ] LRT-026 Convert select expressions where SmartFormat can preserve behavior.
- [ ] LRT-027 Convert nested plural/select expressions when selector ordering remains unambiguous.
- [ ] LRT-028 Preserve Neo dialogue variable tokens as literal text during ICU conversion.
- [ ] LRT-029 Add converter tests for every supported ICU construct.

### Diagnostics

- [ ] LRT-030 Emit diagnostics with text id, locale, original ICU text, link context, and specific conversion failure.
- [ ] LRT-031 Treat unsupported ICU conversion diagnostics as `error` severity.
- [ ] LRT-032 Include ICU conversion diagnostics in the Unity editor export response.
- [ ] LRT-033 Reuse the Unity synchronizer "continue anyway" confirmation flow for ICU conversion errors.
- [ ] LRT-034 Add tests that unsupported ICU blocks by default but can continue through confirmation.

### Phase 2 verification

- [ ] LRT-035 Run focused web ICU conversion/export diagnostics tests.
- [ ] LRT-036 Run focused Unity synchronizer diagnostics tests.

## Phase 3: Editor localization synchronization

Goal: write synchronized locale JSON files to the configured Unity locations
alongside generated C# and `project.json`.

### File synchronization

- [ ] LRT-037 Add localization file writing to `NeoComposeSynchronizer`.
- [ ] LRT-038 Always write root locale JSON to the configured Resources localization directory.
- [ ] LRT-039 Write non-root locale JSON to Resources by default.
- [ ] LRT-040 When streaming is enabled, write non-root locale JSON to the configured StreamingAssets localization directory.
- [ ] LRT-041 Include existing localization files in replacement confirmation copy.
- [ ] LRT-042 Delete stale synchronized locale files only inside configured Neo localization directories.
- [ ] LRT-043 Refresh Unity assets after locale file writes and deletes.
- [ ] LRT-044 Report locale file synchronization failures without hiding successful generated/project file writes.

### Editor UI

- [x] LRT-045 Add localization path fields to the Neo Compose editor window.
- [x] LRT-046 Add toggle for StreamingAssets non-root locale synchronization.
- [x] LRT-047 Add locale override field to the Neo Compose editor window.
- [ ] LRT-048 Show a warning when streaming mode requires explicit async preload for non-root locale behavior.
- [ ] LRT-049 Add editor tests for localization sync path selection.
- [ ] LRT-050 Add editor tests for stale locale cleanup.

### Phase 3 verification

- [ ] LRT-051 Run focused Unity editor synchronization tests from the HelloWorld Unity Test Runner.

## Phase 4: Runtime locale loading and fallback

Goal: add `NeoLocalization`, load root Resources locale synchronously, support
progressive in-memory loading, and support optional async StreamingAssets
loading.

### Loader abstractions

- [ ] LRT-052 Add `INeoLocalizationFormatter`.
- [ ] LRT-053 Add default formatter wrapping `UnityEngine.Localization.SmartFormat`.
- [ ] LRT-054 Add locale file source abstraction for Resources and StreamingAssets reads.
- [ ] LRT-055 Load root locale synchronously from Resources during `NeoLoader.Load`.
- [ ] LRT-056 Add `NeoLocalizationOptions`.
- [ ] LRT-057 Add `NeoClient.Localization`.
- [ ] LRT-058 Add `NeoLocalization.RootLocale`, `CurrentLocale`, `SupportedLocales`, and `LoadedLocales`.
- [ ] LRT-059 Add tests for root locale loading.

### Locale selection

- [ ] LRT-060 Choose initial locale from explicit options, config override, system locale, then root locale.
- [ ] LRT-061 Match locale codes exactly before language-only fallback.
- [ ] LRT-062 Fall back to root locale when no configured locale matches.
- [ ] LRT-063 Add `SetLocale`.
- [ ] LRT-064 Add tests for exact locale matching.
- [ ] LRT-065 Add tests for language-only matching.
- [ ] LRT-066 Add tests for root fallback matching.

### Resolution and caching

- [ ] LRT-067 Build locale source chain indexes.
- [ ] LRT-068 Add `ResolveText` and `TryResolveText`.
- [ ] LRT-069 Resolve through current locale, source chain, and root locale.
- [ ] LRT-070 Cache every loaded locale for the life of the `NeoClient`.
- [ ] LRT-071 Log and recover from unknown text ids.
- [ ] LRT-072 Log and recover from invalid locale file JSON.
- [ ] LRT-073 Log and recover from SmartFormat runtime errors.
- [ ] LRT-074 Add tests for multi-hop fallback resolution.
- [ ] LRT-075 Add tests for loaded-locale caching.
- [ ] LRT-076 Add tests for error recovery behavior.

### StreamingAssets async loading

- [ ] LRT-077 Add `LoadLocale`.
- [ ] LRT-078 Add `LoadLocaleAsync`.
- [ ] LRT-079 Add `LoadAsync` that loads the selected locale and full fallback chain.
- [ ] LRT-080 Use UnityWebRequest-compatible reads for StreamingAssets async loading.
- [ ] LRT-081 Keep synchronous getters falling back to loaded locales/root when streaming locale files are not loaded yet.
- [ ] LRT-082 Add tests for async fallback-chain loading.
- [ ] LRT-083 Add tests for sync fallback before async load completes.

### Phase 4 verification

- [ ] LRT-084 Run focused Unity runtime localization tests from the HelloWorld Unity Test Runner.

## Phase 5: String attribute runtime behavior

Goal: make localizable string attributes resolve text ids by default while
preserving literal string behavior for non-localizable fields and runtime
save/session overrides.

### Attribute JSON and runtime support

- [ ] LRT-085 Add `StringAttribute.localizable` to Unity JSON DTOs.
- [ ] LRT-086 Add `NeoStringLocalizationMode` enum.
- [ ] LRT-087 Add optional `StringAttributeValue.neoLocalizationMode`.
- [ ] LRT-088 Interpret exported localizable string values/defaults as text ids when `neoLocalizationMode` is absent.
- [ ] LRT-089 Interpret runtime-created localizable string overrides as literals when `neoLocalizationMode` is `Literal`.
- [ ] LRT-090 Keep non-localizable string attributes literal-only.
- [ ] LRT-091 Add `NeoAttributeString.SetLiteralOverride`.
- [ ] LRT-092 Ensure clearing a writable override restores inherited localized resolution.
- [ ] LRT-093 Add tests for localizable string get resolution.
- [ ] LRT-094 Add tests for non-localizable string literal behavior.
- [ ] LRT-095 Add tests for literal override set/clear behavior.
- [ ] LRT-096 Add tests for null literal overrides and required validation.

### NSGetter integration

- [ ] LRT-097 Resolve localizable string dereferences in C# NSGetter using `NeoLocalization.CurrentLocale`.
- [ ] LRT-098 Preserve literal behavior for non-localizable string dereferences in C# NSGetter.
- [ ] LRT-099 Resolve localized enum option display text when C# NSGetter stringifies enum options.
- [ ] LRT-100 Add NSGetter tests for localized string dereference.
- [ ] LRT-101 Add NSGetter tests for localized enum stringification.

### Phase 5 verification

- [ ] LRT-102 Run focused Unity string attribute and NSGetter tests from the HelloWorld Unity Test Runner.

## Phase 6: Generated C# facade updates

Goal: update web-generated C# so developer-facing APIs remain simple strings
while using runtime localization internally.

### Generated properties

- [ ] LRT-103 Generate localized string getters that call `client.Localization`.
- [ ] LRT-104 Generate nullable localized string getters that return `string?`.
- [ ] LRT-105 Preserve literal getter generation for non-localizable string attributes.
- [ ] LRT-106 Generate writable localizable string setters that call `SetLiteralOverride`.
- [ ] LRT-107 Preserve existing setter behavior for non-localizable string attributes.
- [ ] LRT-108 Add generated-code tests for localizable string getters.
- [ ] LRT-109 Add generated-code tests for writable localizable string setters.

### Field-token text id lookup

- [ ] LRT-110 Reuse existing generated `NeoField<T>` tokens for localized text-id lookup.
- [ ] LRT-111 Generate `GetLocalizedTextId<T>(NeoField<T> field)` for custom value wrappers with localizable fields.
- [ ] LRT-112 Return underlying text ids for localizable string fields that still use text ids.
- [ ] LRT-113 Return `null` for literal overrides.
- [ ] LRT-114 Match existing `OnChanged(NeoField<T>, ...)` behavior for fields not defined on the generated type.
- [ ] LRT-115 Add generated-code tests for `GetLocalizedTextId`.
- [ ] LRT-116 Regenerate Unity test fixture `NeoGeneratedTypes.cs`.

### Phase 6 verification

- [ ] LRT-117 Run generated-code tests in the web repo.
- [ ] LRT-118 Run Unity generated types tests from the HelloWorld Unity Test Runner.

## Phase 7: Dialogue and enum localization

Goal: resolve dialogue and enum text ids through `NeoLocalization` while
preserving existing dialogue variable interpolation behavior.

### Dialogue runtime

- [ ] LRT-119 Resolve `Dialogue.description` as localized text.
- [ ] LRT-120 Add `NeoDialogue.DescriptionTextId` for diagnostics.
- [ ] LRT-121 Resolve dialogue text node `text` as localized text before interpolation.
- [ ] LRT-122 Resolve dialogue option `text` as localized text before interpolation.
- [ ] LRT-123 Keep `{{neo-var:<id>}}` interpolation after localized text resolution.
- [ ] LRT-124 Run SmartFormat formatting after Neo variable interpolation.
- [ ] LRT-125 Keep target-locale-added dialogue variables unsupported in V1.
- [ ] LRT-126 Add dialogue description localization tests.
- [ ] LRT-127 Add dialogue text node localization tests.
- [ ] LRT-128 Add dialogue option localization tests.
- [ ] LRT-129 Add tests that Neo variable interpolation still runs after localization.
- [ ] LRT-130 Add tests for SmartFormat formatting after Neo interpolation.

### Enum display text

- [ ] LRT-131 Resolve `EnumOption.text` as localized text.
- [ ] LRT-132 Update generated enum display helpers to resolve through `NeoLocalization`.
- [ ] LRT-133 Preserve option id equality and selection behavior.
- [ ] LRT-134 Add enum display text localization tests.

### Phase 7 verification

- [ ] LRT-135 Run focused Unity dialogue and enum tests from the HelloWorld Unity Test Runner.

## Phase 8: Sample project, docs, and end-to-end verification

Goal: prove the feature through the downstream sample and document the intended
developer workflow.

### Sample and docs

- [ ] LRT-136 Add sample localization files to `samples/HelloWorld`.
- [ ] LRT-137 Update sample code to demonstrate `hero.Name` localized getter behavior.
- [ ] LRT-138 Update sample code to demonstrate writable literal override behavior.
- [ ] LRT-139 Add sample dialogue localization demonstration.
- [ ] LRT-140 Document Resources-only default workflow.
- [ ] LRT-141 Document StreamingAssets opt-in workflow and required `await client.Localization.LoadAsync()`.
- [ ] LRT-142 Document fallback behavior and locale matching.
- [ ] LRT-143 Document `GetLocalizedTextId` diagnostics/debug use.

### End-to-end verification

- [ ] LRT-144 Run full `src/NeoComposeUnity/Tests/` suite from the HelloWorld Unity Test Runner.
- [ ] LRT-145 Run full `samples/HelloWorld/Assets/Tests/` suite from the HelloWorld Unity Test Runner.
- [ ] LRT-146 Run web export/codegen tests impacted by Unity localization changes.
- [ ] LRT-147 Run sample synchronization manually against a localized project version.
- [ ] LRT-148 Verify generated files, Resources locale files, and optional StreamingAssets locale files are written as expected.
- [ ] LRT-149 Verify no pre-existing failures remain.
