# Project text localization runtime implementation tasks

## Status

Draft task ledger for implementing
[project-text-localization-runtime.md](./project-text-localization-runtime.md).

Active chunk: Phase 2 ICU-to-SmartFormat export conversion/diagnostics, Phase
3 editor synchronization, Phase 4 runtime locale loading, Phase 5 string
attribute runtime/NSGetter behavior, Phase 6 generated C# facades, and most of
Phase 7 dialogue/enum localization are in place. Next chunk should add explicit
coverage for SmartFormat formatting after dialogue Neo variable interpolation.

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

- [x] LRT-001 Add Unity export localization metadata model types in the web repo.
- [x] LRT-002 Add compact locale runtime file model types in the web repo.
- [x] LRT-003 Add `localization` metadata to `IProjectUnityExport`.
- [x] LRT-004 Add `localizationFiles` to the Unity editor export response.
- [x] LRT-005 Build locale file names with stable, safe locale-code naming.
- [x] LRT-006 Export root locale and supported locale fallback metadata.
- [x] LRT-007 Export raw locale values with `null` preserved for missing translations.
- [x] LRT-008 Exclude localization status/comment/link metadata from runtime locale files.
- [x] LRT-009 Add web tests for Unity export localization metadata shape.
- [x] LRT-010 Add web tests for locale runtime file JSON shape.

### Package and DTO setup

- [x] LRT-011 Add `com.unity.localization` as a hard dependency in `src/NeoComposeUnity/package.json`.
- [x] LRT-012 Add Unity C# DTOs for `ProjectData.localization` metadata.
- [x] LRT-013 Add Unity C# DTOs for locale runtime file JSON.
- [x] LRT-014 Add editor API models for `localizationFiles`.
- [x] LRT-015 Add config fields for localization Resources directory, StreamingAssets directory, streaming opt-in, preload behavior, and locale override.
- [x] LRT-016 Validate localization Resources and StreamingAssets directories in `NeoComposeSynchronizer.ValidateConfig`.
- [x] LRT-017 Add tests for config path validation.

### Phase 1 verification

- [x] LRT-018 Run focused web Unity export tests.
- [x] LRT-019 Run focused Unity DTO/config tests from the HelloWorld Unity Test Runner.

## Phase 2: ICU to SmartFormat export diagnostics

Goal: convert web-authored ICU messages into SmartFormat-compatible runtime
strings for Unity, and report diagnostics when conversion is unsupported or
lossy.

### Conversion

- [x] LRT-020 Add ICU parser/converter utility for Unity SmartFormat output in the web repo.
- [x] LRT-021 Convert literal text and escaped braces safely.
- [x] LRT-022 Convert simple named placeholders.
- [x] LRT-023 Convert supported number formats to culture-aware SmartFormat/.NET formats.
- [x] LRT-024 Convert supported date/time formats to culture-aware SmartFormat/.NET formats.
- [x] LRT-025 Convert plural expressions where SmartFormat can preserve behavior.
- [x] LRT-026 Convert select expressions where SmartFormat can preserve behavior.
- [x] LRT-027 Convert nested plural/select expressions when selector ordering remains unambiguous.
- [x] LRT-028 Preserve Neo dialogue variable tokens as literal text during ICU conversion.
- [x] LRT-029 Add converter tests for every supported ICU construct.

### Diagnostics

- [x] LRT-030 Emit diagnostics with text id, locale, original ICU text, link context, and specific conversion failure.
- [x] LRT-031 Treat unsupported ICU conversion diagnostics as `error` severity.
- [x] LRT-032 Include ICU conversion diagnostics in the Unity editor export response.
- [x] LRT-033 Reuse the Unity synchronizer "continue anyway" confirmation flow for ICU conversion errors.
- [x] LRT-034 Add tests that unsupported ICU blocks by default but can continue through confirmation.

### Phase 2 verification

- [x] LRT-035 Run focused web ICU conversion/export diagnostics tests.
- [x] LRT-036 Run focused Unity synchronizer diagnostics tests.

## Phase 3: Editor localization synchronization

Goal: write synchronized locale JSON files to the configured Unity locations
alongside generated C# and `project.json`.

### File synchronization

- [x] LRT-037 Add localization file writing to `NeoComposeSynchronizer`.
- [x] LRT-038 Always write root locale JSON to the configured Resources localization directory.
- [x] LRT-039 Write non-root locale JSON to Resources by default.
- [x] LRT-040 When streaming is enabled, write non-root locale JSON to the configured StreamingAssets localization directory.
- [x] LRT-041 Include existing localization files in replacement confirmation copy.
- [x] LRT-042 Delete stale synchronized locale files only inside configured Neo localization directories.
- [x] LRT-043 Refresh Unity assets after locale file writes and deletes.
- [x] LRT-044 Report locale file synchronization failures without hiding successful generated/project file writes.

### Editor UI

- [x] LRT-045 Add localization path fields to the Neo Compose editor window.
- [x] LRT-046 Add toggle for StreamingAssets non-root locale synchronization.
- [x] LRT-047 Add locale override field to the Neo Compose editor window.
- [x] LRT-048 Show a warning when streaming mode requires explicit async preload for non-root locale behavior.
- [x] LRT-049 Add editor tests for localization sync path selection.
- [x] LRT-050 Add editor tests for stale locale cleanup.

### Phase 3 verification

- [x] LRT-051 Run focused Unity editor synchronization tests from the HelloWorld Unity Test Runner.

## Phase 4: Runtime locale loading and fallback

Goal: add `NeoLocalization`, load root Resources locale synchronously, support
progressive in-memory loading, and support optional async StreamingAssets
loading.

### Loader abstractions

- [x] LRT-052 Add `INeoLocalizationFormatter`.
- [x] LRT-053 Add default formatter wrapping `UnityEngine.Localization.SmartFormat`.
- [x] LRT-054 Add locale file source abstraction for Resources and StreamingAssets reads.
- [x] LRT-055 Load root locale synchronously from Resources during `NeoLoader.Load`.
- [x] LRT-056 Add `NeoLocalizationOptions`.
- [x] LRT-057 Add `NeoClient.Localization`.
- [x] LRT-058 Add `NeoLocalization.RootLocale`, `CurrentLocale`, `SupportedLocales`, and `LoadedLocales`.
- [x] LRT-059 Add tests for root locale loading.

### Locale selection

- [x] LRT-060 Choose initial locale from explicit options, config override, system locale, then root locale.
- [x] LRT-061 Match locale codes exactly before language-only fallback.
- [x] LRT-062 Fall back to root locale when no configured locale matches.
- [x] LRT-063 Add `SetLocale`.
- [x] LRT-064 Add tests for exact locale matching.
- [x] LRT-065 Add tests for language-only matching.
- [x] LRT-066 Add tests for root fallback matching.

### Resolution and caching

- [x] LRT-067 Build locale source chain indexes.
- [x] LRT-068 Add `ResolveText` and `TryResolveText`.
- [x] LRT-069 Resolve through current locale, source chain, and root locale.
- [x] LRT-070 Cache every loaded locale for the life of the `NeoClient`.
- [x] LRT-071 Log and recover from unknown text ids.
- [x] LRT-072 Log and recover from invalid locale file JSON.
- [x] LRT-073 Log and recover from SmartFormat runtime errors.
- [x] LRT-074 Add tests for multi-hop fallback resolution.
- [x] LRT-075 Add tests for loaded-locale caching.
- [x] LRT-076 Add tests for error recovery behavior.

### StreamingAssets async loading

- [x] LRT-077 Add `LoadLocale`.
- [x] LRT-078 Add `LoadLocaleAsync`.
- [x] LRT-079 Add `LoadAsync` that loads the selected locale and full fallback chain.
- [x] LRT-080 Use UnityWebRequest-compatible reads for StreamingAssets async loading.
- [x] LRT-081 Keep synchronous getters falling back to loaded locales/root when streaming locale files are not loaded yet.
- [x] LRT-082 Add tests for async fallback-chain loading.
- [x] LRT-083 Add tests for sync fallback before async load completes.

### Phase 4 verification

- [x] LRT-084 Run focused Unity runtime localization tests from the HelloWorld Unity Test Runner.

## Phase 5: String attribute runtime behavior

Goal: make localizable string attributes resolve text ids by default while
preserving literal string behavior for non-localizable fields and runtime
save/session overrides.

### Attribute JSON and runtime support

- [x] LRT-085 Add `StringAttribute.localizable` to Unity JSON DTOs.
- [x] LRT-086 Add `NeoStringLocalizationMode` enum.
- [x] LRT-087 Add optional `StringAttributeValue.neoLocalizationMode`.
- [x] LRT-088 Interpret exported localizable string values/defaults as text ids when `neoLocalizationMode` is absent.
- [x] LRT-089 Interpret runtime-created localizable string overrides as literals when `neoLocalizationMode` is `Literal`.
- [x] LRT-090 Keep non-localizable string attributes literal-only.
- [x] LRT-091 Add `NeoAttributeString.SetLiteralOverride`.
- [x] LRT-092 Ensure clearing a writable override restores inherited localized resolution.
- [x] LRT-093 Add tests for localizable string get resolution.
- [x] LRT-094 Add tests for non-localizable string literal behavior.
- [x] LRT-095 Add tests for literal override set/clear behavior.
- [x] LRT-096 Add tests for null literal overrides and required validation.

### NSGetter integration

- [x] LRT-097 Resolve localizable string dereferences in C# NSGetter using `NeoLocalization.CurrentLocale`.
- [x] LRT-098 Preserve literal behavior for non-localizable string dereferences in C# NSGetter.
- [x] LRT-099 Resolve localized enum option display text when C# NSGetter stringifies enum options.
- [x] LRT-100 Add NSGetter tests for localized string dereference.
- [x] LRT-101 Add NSGetter tests for localized enum stringification.

### Phase 5 verification

- [x] LRT-102 Run focused Unity string attribute and NSGetter tests from the HelloWorld Unity Test Runner.

## Phase 6: Generated C# facade updates

Goal: update web-generated C# so developer-facing APIs remain simple strings
while using runtime localization internally.

### Generated properties

- [x] LRT-103 Generate localized string getters that call `client.Localization`.
- [x] LRT-104 Generate nullable localized string getters that return `string?`.
- [x] LRT-105 Preserve literal getter generation for non-localizable string attributes.
- [x] LRT-106 Generate writable localizable string setters that route through literal override support.
- [x] LRT-107 Preserve existing setter behavior for non-localizable string attributes.
- [x] LRT-108 Add generated-code tests for localizable string getters.
- [x] LRT-109 Add generated-code tests for writable localizable string setters.

### Field-token text id lookup

- [x] LRT-110 Reuse existing generated `NeoField<T>` tokens for localized text-id lookup.
- [x] LRT-111 Generate `GetLocalizedTextId<T>(NeoField<T> field)` for custom value wrappers with localizable fields.
- [x] LRT-112 Return underlying text ids for localizable string fields that still use text ids.
- [x] LRT-113 Return `null` for literal overrides.
- [x] LRT-114 Match existing `OnChanged(NeoField<T>, ...)` behavior for fields not defined on the generated type.
- [x] LRT-115 Add generated-code tests for `GetLocalizedTextId`.
- [x] LRT-116 Regenerate Unity test fixture `NeoGeneratedTypes.cs`.

### Phase 6 verification

- [x] LRT-117 Run generated-code tests in the web repo.
- [x] LRT-118 Run Unity generated types tests from the HelloWorld Unity Test Runner.

## Phase 7: Dialogue and enum localization

Goal: resolve dialogue and enum text ids through `NeoLocalization` while
preserving existing dialogue variable interpolation behavior.

### Dialogue runtime

- [x] LRT-119 Resolve `Dialogue.description` as localized text.
- [x] LRT-120 Add `NeoDialogue.DescriptionTextId` for diagnostics.
- [x] LRT-121 Resolve dialogue text node `text` as localized text before interpolation.
- [x] LRT-122 Resolve dialogue option `text` as localized text before interpolation.
- [x] LRT-123 Keep `{{neo-var:<id>}}` interpolation after localized text resolution.
- [x] LRT-124 Run SmartFormat formatting after Neo variable interpolation.
- [x] LRT-125 Keep target-locale-added dialogue variables unsupported in V1.
- [x] LRT-126 Add dialogue description localization tests.
- [x] LRT-127 Add dialogue text node localization tests.
- [x] LRT-128 Add dialogue option localization tests.
- [x] LRT-129 Add tests that Neo variable interpolation still runs after localization.
- [ ] LRT-130 Add tests for SmartFormat formatting after Neo interpolation.

### Enum display text

- [x] LRT-131 Resolve `EnumOption.text` as localized text.
- [x] LRT-132 Update generated enum display helpers to resolve through `NeoLocalization`.
- [x] LRT-133 Preserve option id equality and selection behavior.
- [x] LRT-134 Add enum display text localization tests.

### Phase 7 verification

- [x] LRT-135 Run focused Unity dialogue and enum tests from the HelloWorld Unity Test Runner.

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
