---
name: neocompose-cli
description: >-
  Edit and synchronize a Neo Compose format-v3 schema as real C# 11 plus
  tracked NeoScript sidecars. Use for classes, interfaces, members,
  enums, templates, localization, schema-aware NeoScript, or batch content
  edits. The working copy is a neo/ directory; run `neo` from the published
  package or `node cli/bin/neo.mjs` in the web repository.
---

# Neo Compose schema-as-code CLI (`neo`)

A `neo/` working copy is a Git-like checkout of one Neo Compose project
version. C# and NeoScript are peer authoring surfaces beside the web UI; the
server remains the shared source of truth. The current contract is format v3
and is documented in `specs/schema-as-code-cli.md`.

## Non-negotiable workflow

1. Run `neo pull` before editing and before pushing after time has passed.
2. Edit real C# 11 and the tracked `.neo` sidecars. Never edit `.neo/state.json`
   or `.neo/tooling`.
3. Run `neo status` or `neo diff`, then `neo push --dry-run`.
4. Run `neo script check --all` after a schema rename or signature change.
5. Push one reviewed atomic change with `neo push`.

**Pull followed immediately by status/push must be a no-op.** If it reports
schema changes, stop and report a round-trip bug rather than pushing it.

The compiler reports file:line:column diagnostics and never guesses. Unknown
authorable server fields fail with “CLI contract upgrade required”; they never
hide in an opaque JSON carrier.

## Setup and format requirement

```sh
npm i -g @neocompose/cli@0.3.0
neo login [--api <url>] [--profile editor|release] [--save-project <id>]
neo init --project <id> [--version <id>] [--dir neo]
neo doctor
```

Repository development uses `node cli/bin/neo.mjs`. Agents should always pass
explicit flags/IDs. Human terminals may show pickers and confirms; non-TTY/CI
use fails with an actionable missing-flag diagnostic rather than blocking.

`neo.json` must contain `formatVersion: 3`. Format 2 and every other marker are
unsupported. There is no upgrader or compatibility reader. Preserve any source
you need, then refresh a format-3 working copy from the authoritative server:

```sh
neo init --project <id> [--version <id>] [--dir neo]
```

`neo pull --reset` is only for an existing schema workspace; it reconstructs
CLI-managed source from the server and refreshes the compiler SDK and analyzer.

Neo never downloads or installs .NET. Compiler host discovery is:

1. explicit `NEO_DOTNET_HOST`;
2. a compatible Unity 6000 runtime;
3. a compatible system `dotnet`.

The validated selection is cached under `.neo`; `neo doctor` reports its
path, kind, runtime/Unity version, cache status, and compiler/SDK contract
compatibility. An invalid explicit override is a hard error.

In Unity projects, `neo.json` may use `unityConfigPath` instead of
`projectId`/`versionId`. The referenced `NeoComposeConfig.asset` is then the
single source of truth, and branch switches update it. Never duplicate the IDs
in `neo.json` in this mode.

## Working-copy layout

```text
neo/
  neo.json
  NeoCompose.Schema.csproj        # netstandard2.1, C# 11, IDE-only
  Classes/                        # schema classes
  Interfaces/                     # interface declarations
  Enums/
  Root.cs                         # [NeoRegistry] roots and loose members
  Templates/                      # typed texture/audio settings
  Localization.cs                 # typed localization settings
  LocalizationStatuses/*.cs      # typed localization workflow statuses
  Scripts/<Class>/<Member>.neo    # computed properties and NeoScript methods
  Migrations/*.neo
  .neo/
    state.json                    # bases/CAS hashes; never hand-edit
    dotnet-host.json
    tooling/                      # bundled SDK, analyzer, generator, compiler
```

The schema project is not referenced by the game. `.neo/`, `bin/`, and `obj/`
are gitignored; source and scripts are tracked.

## Authoring real C#

Native C# carries native meaning:

- Plain, `virtual`, `abstract`, and `override` members map directly to schema
  behavior. An override's base record identity comes from the resolved symbol.
- Base classes, interfaces, generic parameters/arguments, and `where`
  constraints use ordinary syntax.
- Property type determines Neo kind; nullable annotation determines whether it
  is optional; initializer determines its default.
- Declaration order determines schema/enum order unless an explicit typed
  ordering attribute is present.
- Method parameters and return type are the function signature.
  `Task`/`Task<T>` means deferred execution.
- A C# `static` property or method is class-owned. Stored static properties use
  one replaceable binding and are excluded from instance construction;
  receiver kind cannot be changed in place.
- Concrete `[NeoScript]`/`[NeoFunction]` methods are C# 11 partial
  declarations. The bundled source generator adds inert compile-only bodies.
  Abstract functions are ordinary abstract methods.

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using NeoCompose.Schema;

[NeoEnum("enum-item-type")]
public enum ItemType
{
    [NeoEnumOption("option-food", Text = "Food")]
    Food,
}

[NeoSchemaClass("class-inventory-item", AllowedStorage = NeoAllowedStorage.Immutable)]
[NeoSchemaOrder(nameof(Name), nameof(Type), nameof(Tags), nameof(UsePrimary))]
public abstract partial class InventoryItem<
    [NeoId("generic-stack")] TStack>
    where TStack : class
{
    [NeoMember("member-name"), NeoText(SearchKey = true)]
    public virtual string Name { get; init; } = "";

    [NeoMember("member-type")]
    public abstract ItemType Type { get; init; }

    [NeoMember("member-tags"), NeoList(Kind = NeoListKind.Unordered)]
    [NeoEntries(nameof(TagEntries))]
    public virtual IReadOnlyList<string> Tags { get; init; } = new List<string>();

    private static readonly NeoEntrySettings[] TagEntries =
    {
        new()
        {
            Id = "entry-tag",
            Path = "[]",
            Kind = NeoEntryKind.String,
            Text = new NeoTextSettings { Localizable = false },
        },
    };

    [NeoMember("function-use-primary"), NeoScript]
    public virtual partial Task<bool> UsePrimary(InventoryItem<TStack> target);
}
```

Stable IDs are visible in `[NeoSchemaClass]`, `[NeoInterface]`, `[NeoEnum]`,
`[NeoEnumOption]`, `[NeoId]`, and `[NeoMember]`. Omit a nullable positional ID
only when creating a record; a successful push assigns it and canonically
rewrites the source. Once assigned, preserve it through renames.

`[NeoMember]` owns identity/common options. Use narrow typed attributes for
Neo behavior: `NeoText`, `NeoNumber`, `NeoDictionary`, `NeoList`, `NeoEntries`,
`NeoIndex`, `NeoColumn`, `NeoLookup`, `NeoDialogue`, `NeoFile`, `NeoComputed`,
`NeoScript`, `NeoFunction`, `NeoSystem`, and explicit schema/generic binding
attributes. Use the typed settings classes for nested entries, template import
options, localization, and system metadata. This is intentional: every valid
option should appear in C# IntelliSense.

A bodyless `[NeoComputed]` property needs no initializer or `#pragma`. The
bundled analyzer suppresses only its sidecar-backed `CS8618`; ordinary nullable
warnings remain enabled.

Allowed values are statically analyzable literals, enum values, `nameof`,
`typeof`, arrays/collections, object initializers, approved Neo value types,
and `Neo.Ref<T>`, `Neo.Lookup<T>`, `Neo.Dialogue`, `Neo.Sprite`, `Neo.Audio`,
or `Neo.Member` references. Authored code is never executed.

Never introduce opaque carriers or identity escapes: `ExtraJson`, `DefaultJson`,
`RetJson`, `ArgsJson`, `EntryChainJson`, `SchemaKeyOrderJson`,
`GenericParamIds`, `ChainJson`, `SignatureJson`, `ExtendsId`, `isVirtual`, or
`isAbstract`.

Define each localization workflow status in
`LocalizationStatuses/<Name>.cs` using `[NeoLocalizationStatus("stable-id")]`
and a static `NeoLocalizationStatusSettings` object. The marker owns identity;
typed properties cover slug, display metadata, archive state, transition
rules, automatic transitions, and system restrictions. Keep the order in
`NeoLocalizationSettings.StatusIds`.

## NeoScript sidecars

Scripted member bodies live at exactly
`Scripts/<DeclaringClass>/<Member>.neo`.

A computed property uses one complete contextual file. The outer signature
matches the linked C# declaration's semantic result (`Task<T>` functions use
`T`) and adds the implicit `this` and `root` execution parameters:

```neoscript
string Description(InventoryItem this, Root root) {
  get {
    return $"{this.Name} ({this.Type})";
  }

  set(string value) {
    this.Name = value;
  }
}
```

A concrete `[NeoScript]` method uses the same outer contextual signature and
puts its declared arguments and executable body in a nested unit:

```neoscript
bool UsePrimary(InventoryItem this, Root root) {
  (InventoryItem target) {
    return target.IsStackable;
  }
}
```

The complete wrapper is tracked on disk and displayed identically in editors;
Monaco protects the signature/braces while leaving the getter, setter, or
function body editable. Abstract scripted members have no file.

Missing, duplicate, case-colliding, ambiguous, and orphaned sidecars are hard
diagnostics. A sidecar for an abstract member is also invalid. The stable C#
member ID lets canonical renames move the file safely; include C# and sidecar
renames in the same reviewed change.

Canonical C# places `// NeoScript: Scripts/<Class>/<Member>.neo` above the
linked declaration. Go-to-definition, references, and rename use the Roslyn
source identity plus stable member ID to navigate between C# and `.neo`.

NeoScript is C#-flavored. Locals may use an explicit type
(`string name = ...;`) or inference (`var name = ...;`); inferred locals retain
the same static type and editor intelligence. `this` is the containing
instance; roots include `root.Assets`, `root.Save`, and `root.Session`.

Every executable NeoScript unit uses the same mutation-capable resolver and
executor. Getter and dialogue-condition bodies may write just like actions,
setters, Functions, and migrations; the resolved target's Immutable, Save,
Session, or runtime ownership—not the body kind—decides whether the write is
legal. The non-code Condition builder remains a restricted read form.

Concrete, closed, writable classes expose the same public constructor
shape as generated C#. Use `new ClassName(required, optional, ...)`; required
parameters come first, optional parameters follow in generated-code order,
and `null` skips an optional positional slot. Arguments remain positional;
editor parameter-name hints are previews, not named-argument syntax.
Constructor graphs begin as parentless Session values. They survive only when
attached, returned, or otherwise escaped from the invocation; abandoned
graphs are reclaimed.

Static members use a class receiver (`GameRules.EncounterCount`), while
instance members use a value receiver. Stored static members resolve through
their authored/Save/Session binding layer and are never copied into class
instances.

```sh
neo script check --all
neo script check --this Outpost --returns string 'return $"{this.Name}!";'
neo script check --mode setter --member ComputedName 'root.Session.Name = value;'
neo script check --mode nsfunction --member Outpost.RefreshUnlock 'return this.Level > 0;'
neo script compile --mode nsfunction --member Outpost.RefreshUnlock 'return this.Level > 0;'
neo script eval --returns string 'return root.Assets.Outposts[0].FullDisplayText;'
neo script eval --function Outpost.RefreshUnlock --this-value <id> --args '[3]'
neo script apply --mode action '...'
```

`eval` uses authored values and the same evaluator as the web app. `apply` is
preview-only unless its command explicitly supports a commit; it reports
return values, ordered write intents, and surviving constructor rows in
`createdSessionValues`. Prefer `--json` for automation.

## Conflicts and version bumps

Overlapping concurrent schema edits produce explicit local/server conflict
markers. They intentionally fail compilation. Edit source to the desired final
state and push; that push is the resolution. `neo resolve --mine|--theirs` is
whole-side convenience only.

For `base-hash-conflict`, pull, resolve, and push again. Never seek a force
bypass. For `version-bump-required`, inspect the classification and repeat
with `neo push --accept-bump` only when intended.

Script conflicts block validation in the same way and must be resolved in the
tracked `.neo` file.

## Content stays in commands

Values, dialogue instances, localized text, and project files are records, not
schema source files:

```sh
neo records query [--kind <recordKind>]
neo records get <kind> <id>
neo values list [memberId]
neo values get <valueId>
neo values set <valueId> '<raw-json-value>'
neo values bind <staticMemberId> <valueId>
neo values unbind <staticMemberId>
neo values create '<raw-json-value>' [--class <classId>] --bind <staticMemberId>
neo loc locales
neo loc list
neo loc set <textId> <locale> "text"
```

Write verbs accept a JSON-array batch on stdin or `--file`; use one batch when
edits must be atomic. A `values set` payload is the raw value, not
`{"value": ...}`. `values bind` and `unbind` edit only a stored static
member's authored binding; `create --bind` can atomically create and bind a
complete class/collection graph supplied as one batch. For collection rows
use `values add-entry` on the live container value; a member's default
container ID may not be an instance's container.

### Dialogue authoring

Always run `neo dialogue dryrun [ref]` after editing dialogue. It traverses
option paths from a fresh save and applies the runtime's mutation-ownership
rules. Exit 1 means the authored graph would fail on device.

`neo dialogue export <ref>` and `neo dialogue apply <spec.json> [--dry-run]`
round-trip a whole dialogue. `neo dialogue compile` compiles condition/action
blocks with their dialogue/node context. Localized node fields hold text IDs;
create/edit those records through `neo loc` with their links.

Lookup collection mutation is a common trap: mutating a collection through a
lookup may resolve to an authored asset collection and be forbidden. The
dialogue dry run exists to catch this and related save/session ownership bugs.

## Branches, releases, and migrations

```sh
neo branch list
neo branch create <name> [--from <ref>]
neo branch switch <nameOrId>
neo merge <branch> [--dry-run] [--migrate]
neo release cut [--bump major|minor|patch] [--dry-run]

neo migrate new <name> --target <Class>
neo migrate list
neo migrate check
neo migrate run [--dry-run] [--skip-invalid]
```

Branches are copy-on-write forks. Releases are immutable snapshots; their bump
floor is derived from transaction history and may only be raised. Release
operations require the release login profile.

Migrations remain NeoScript action files under `Migrations/`, with stable
headers and pinned compiled IR. The server runner tracks per-branch applied
state; merge may chain pending migrations before validation.

## Editor and implementation contract

The browser-safe `neoscript-language` service is shared by the compiler, CLI,
web Monaco editor, and VS Code LSP. Do not add language intelligence directly
to an environment adapter.

Monaco's existing highlighting, comments/interpolation, bracket behavior,
protected scaffolds, context-aware completion/hover, and accurate diagnostics
are a release-blocking compatibility floor. The shared service includes recovery completion,
signature help, C# and script definitions, references, rename, document
symbols, semantic tokens, quick fixes, formatting, richer diagnostics,
contextual ranking, and incremental analysis. Project updates must refresh
context without accumulating stale providers.

The VS Code extension consumes the same service and grammar/token spec. Its
LSP supports the same diagnostics, completion, hover, signature, navigation,
rename, symbols, semantic tokens, and formatting. The VSIX is built in-repo;
marketplace publication is separate.

## Agent implementation notes

- The Node CLI talks directly to Convex through typed `api.*` calls and uses
  the same session-gated CAS commit path as the web app.
- `SchemaManifestV3` is the only C# source/document boundary. It uses `classes`,
  `members`, `"class"`, `"member"`, `classId`, `memberId`, and `MemberKind`.
  The contract registry classifies authored, derived, and volatile fields;
  derived server fields come from the pulled base and absent/null equivalents
  normalize.
- The npm package bundles the Roslyn app, dependencies, SDK, analyzer, and
  generator under `dist/tooling`; it does not bundle a runtime.
- The schema compiler does static analysis only. Never execute an authored
  assembly to obtain settings.
- A change is incomplete until typecheck/tests/builds, package-content and
  contract-version checks, Monaco parity/browser-bundle suites, VSIX
  packaging, Unity sample tests, live `neowyn` no-op verification, and final
  `npm run doctor` succeed.

Tokens use the macOS Keychain/OS credential store when available, a protected
file only as fallback, and `NEO_COMPOSE_TOKEN` or `--token-stdin` in CI. The
editor profile cannot publish releases; server-enforced scopes are the
security boundary.
