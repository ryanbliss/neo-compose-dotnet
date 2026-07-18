---
name: neocompose-cli
description: >-
  Edit and synchronize a Neo Compose format-4 project as native .neo and
  .neoflow source plus tracked image/audio files. Use for classes, interfaces,
  enums, members, values, templates, localization, dialogue flows, files,
  migrations, NeoScript, or batch content edits. The working copy is a neo/
  directory; run `neo` from the published package or `node cli/bin/neo.mjs` in
  the web repository.
---

# Neo Compose native project-source CLI (`neo`)

A format-4 `neo/` working copy is a Git-like checkout of one Neo Compose
project version. Native `.neo`/`.neoflow` source and the web UI are peer
authoring surfaces over the same granular records; the server remains the
shared source of truth. The complete contract is
`specs/neo-project-source-authoring.md`.

There is no C# schema project, Roslyn compiler, authoring DLL, .NET discovery,
generated SDK, per-member script sidecar tree, `ValueRegistry`, or checked-in
dialogue JSON source format.

## Non-negotiable workflow

1. Run `neo pull` before editing and again before pushing after time has passed.
2. Edit tracked `.neo`, `.neoflow`, and managed binary files. Never edit
   `.neo/state.json`, analysis caches, or `.neo/conflicts` as source.
3. Run `neo status` and `neo diff`; inspect every change, including changes
   already present when the task began.
4. Run `neo dialogue dryrun <ref>` after dialogue changes and
   `neo push --dry-run` before every real push.
5. Push one reviewed atomic change with `neo push`.

**Pull followed immediately by status or push dry-run must be a semantic
no-op.** If it reports project changes, stop and report a round-trip bug rather
than pushing it.

The compiler reports file:line:column diagnostics and fails closed. Unknown
authorable server fields require a CLI contract upgrade; never preserve them
through opaque JSON or guessed syntax.

## Setup and clean-break format

```sh
npm i -g @neocompose/cli
neo login [--api <url>] [--profile editor|release] [--save-project <id>]
neo init --project <id> [--version <id>] [--dir neo]
neo doctor
```

Repository development uses `node cli/bin/neo.mjs`. Agents should pass
explicit flags and IDs. Interactive terminals may show pickers and confirms;
non-TTY/CI operation must never wait for input.

`neo.json` must contain `formatVersion: 4`. The compiler has no format-3 C#
reader or upgrader. Push intended format-3 edits with the old CLI, preserve
any local source needed for reference, then reconstruct authoritative records:

```sh
neo init --project <id> [--version <id>] [--dir neo]
```

For an existing format-4 workspace, `neo pull --reset` regenerates canonical
source and managed binaries. `neo pull --force` discards local edits in favor
of the server. Use neither casually.

Neo never installs, discovers, or requires .NET. `neo doctor` validates the
format-4 browser compiler, VS Code analysis contract/cache, source bundle, and
managed-file capabilities.

In Unity projects, `neo.json` may use `unityConfigPath` instead of
`projectId`/`versionId`. The referenced `NeoComposeConfig.asset` is then the
single source of truth, and branch/version switches update it. Do not duplicate
the IDs in `neo.json` in this mode.

## Working-copy layout

```text
neo/
  neo.json
  Project.neo
  Root.neo
  Classes/
  Interfaces/
  Enums/
  Relations.neo
  Templates/
  Localization.neo
  LocalizationStatuses/
  Files/
    Images.neo
    AudioClips.neo
    Images/
    AudioClips/
  DialogueGroups/
  Dialogues/*.neoflow
  Migrations/*.neo
  .neo/
    state.json
    conflicts/
```

All non-hidden `.neo`, `.neoflow`, and supported managed binaries are tracked.
`.neo/` is ignored private state. Normal pull preserves declaration placement
and spelling by stable ID when possible. Reset may regroup source into the
canonical layout.

## Native project declarations

Classes, interfaces, and enums are legal only at the top level of definition
`.neo` files. Do not declare them inside another type, function, accessor,
lambda, initializer, migration, or `.neoflow`. A `.neoflow` file has the one
deliberate exception described below.

Use the shared typed declaration grammar for every top-level project object:

```neo
Type Name = expression;
Type Name = new(arguments) {
  // Typed owned declarations supported by Type.
}
```

There are no lowercase record-kind declaration keywords such as `project`,
`values`, `files`, or `relations`.

Schema identifiers are persisted names; there is no separate technical/display
name. `@id` preserves stable persisted identity. Keep assigned IDs through
rename, reorder, and file moves. Omit an ID only to create a record: a
successful real push assigns it and canonically inserts the annotation.
Dry-run, validation failure, upload failure, and CAS failure leave tracked
source unchanged.

```neo
@id("enum-item-rarity-id")
enum ItemRarity {
  @id("rarity-common-id")
  Common = "Common text",
}

@id("interface-named-id")
interface INamed {
  @id("named-name-id")
  string Name { get; }

  @id("named-use-id")
  void Use(SomeClass context);
}

@id("class-inventory-item-id")
@storage(allowed: .Immutable)
abstract class InventoryItem<
  @id("inventory-context-generic-id")
  TContext extends SomeClass
> : INamed {
  @id("item-name-id")
  @settings(localizable: true, searchKey: true)
  virtual string Name = "";

  @id("item-rarity-id")
  abstract ItemRarity Rarity;

  @id("item-stack-size-id")
  @settings(min: 1, max: 999)
  int StackSize = 1;

  @id("item-display-name-id")
  string DisplayName {
    get {
      return $"{Name} ({Rarity})";
    }
  }

  @id("item-use-id")
  abstract void Use(TContext context);

  @id("item-load-id")
  native async bool Load(TContext context);
}
```

- Plain, `virtual`, `abstract`, and `override` declarations carry their native
  meaning.
- Interfaces are non-generic. Class generics and constraints use ordinary Neo
  syntax; do not put `@id` on inferred list/dictionary type entries.
- Field type and nullability determine member kind and optionality; the
  initializer is the default.
- Inline getters, setters, and implemented functions are NeoScript. A bodyless
  function must be `abstract`, `native`, or an interface contract. `async`
  alone does not make a bodyless function valid.
- `@settings(...)` and `@storage(...)` are context-aware typed contracts.
  Numeric limits are numeric literals. A class supports only
  `@storage(allowed: ...)`; storage keys are member-only.
- Keep focused annotations such as `@locked`, `@system`, and `@relations`
  separate from contextual settings.
- `.EnumCase` works only when one expected enum type is known. Use explicit
  `EnumType.Case` when context is ambiguous.
- Defaults and settings must be statically analyzable. Project source is parsed
  and compiled, never executed to discover declarations.

`@relations(...)` belongs on the source class for specialized direct
relations. `Relations.neo` holds generic project relations as typed top-level
declarations. Structurally owned descriptors and concrete generic bindings
derive their identities from stable owner roles; do not invent `@id`
annotations for them.

## Root and authored values

`Root.neo` contains a compiler-owned envelope of this form:

```neo
// Root member declarations are read only. Their values are editable.

Root root = new() {
  @id("root-assets-member-id")
  @locked
  @storage(allowed: .Immutable)
  Assets Assets =
    @id("assets-root-value-id")
    new() {
      Outposts = [Assets.Capitol],
    };

  @id("root-save-member-id")
  @locked
  @storage(allowed: .Save)
  Save Save =
    @id("save-root-value-id")
    new();

  @id("root-session-member-id")
  @locked
  @storage(allowed: .Session)
  Session Session =
    @id("session-root-value-id")
    new();
}
```

The language service protects the root member name, type, member ID, lock, and
storage metadata. Edit only the value initializer after `=`. The three root
values use the same nested construction syntax as any other authored value.

Reusable values are stored static members on real classes; there is no
`ValueRegistry` or compiler-defined `Values` global:

```neo
@id("assets-class-id")
@storage(allowed: .Immutable)
class Assets {
  @id("capitol-member-id")
  static Outpost Capitol =
    @id("capitol-value-id")
    new {
      Name = "Capitol",
      Image = Images.Capitol.Slice(0),
    };

  @id("home-getter-id")
  static Outpost Home {
    get {
      return Assets.Capitol;
    }
  }
}
```

The member ID anchors the replaceable stored binding; the initializer ID is
the actual value row. A computed static getter is an alias and creates no
binding or value row. Values may also live directly on their domain class.

Omitted members materialize the current defaults once during creation. Later
default changes do not mutate existing values. Removing an explicitly
materialized field from an existing initializer requests reset through the
current default, which must appear in `neo diff`.

List items use native inline identity, never a wrapper:

```neo
Tags = [
  @id("story-tag-item-id")
  "story",

  @id("key-tag-item-id")
  "key",
];
```

A bare new element is pending-create shorthand. The compiler never matches an
existing ordered item by index or payload.

Logical project references use the native `Reference` intrinsic:

```neo
Reference(Assets.Capitol)
Reference<Outpost>(id: "capitol-value-id")
Reference<CapitolColdBoot>(CapitolColdBoot)
Reference<Dialogue>(id: "capitol-dialogue-id")
```

Use symbols when available. Use the generic ID overload only when the ID is
the target information. The expected lookup/dialogue member contract still
validates collection membership, multiplicity, group, and assignability.

Localizable string initializers contain the main-locale text, not a
localized-text ID. Pull/lower preserves other locales, comments, statuses,
archive state, and unrelated links.

## Project configuration

Use typed top-level globals for configuration:

- `Project Project = new(...)` for project defaults;
- `TextureTemplate` and `AudioTemplate` declarations under `Templates/`;
- `LocalizationStatus` and `Localization` declarations;
- `PriorityGroup Name = new() { PriorityOption ... }`;
- `DialogueGroup Name = new(...)`, with inline functions/conditions when
  supported;
- typed generic relation globals in `Relations.neo`.

Named arguments and contextual enum literals come from generated contracts.
Do not invent raw persistence fields or generic property bags. Translations
other than the main locale, layout, compiled IR, hashes, storage stamps, and
upload metadata are not project source.

## Project files

Typed editable registries own project files:

```neo
ImageRegistry Images = new() {
  @id("sword-image-file-id")
  @settings(template: PixelArt)
  NeoImage Sword = new("Files/Images/Sword.png");
}

AudioClipRegistry AudioClips = new() {
  @id("sword-hit-audio-file-id")
  @settings(template: SoundEffect)
  NeoAudioClip SwordHit = new("Files/AudioClips/SwordHit.wav");
}
```

Use `Images.Sword.Slice(0)` for sprite members and
`AudioClips.SwordHit` for audio members. The annotation is the project-file ID;
renaming/moving local presentation or replacing bytes retains it.

A supported binary dropped under `Files/Images/` or `Files/AudioClips/` is a
pending file. The language service injects a provisional deterministic symbol.
Status/diff/dry-run do not change registry source; a successful push creates
the record, uploads verified bytes, and materializes the declaration and ID.
`neo files add <path> [--template <Name>]` may scaffold that declaration
explicitly before push.

Pull and push use server-verified SHA-256, not storage ETags. Divergent
local/remote changes keep local bytes and write the verified remote side under
`.neo/conflicts/files/<file-id>/`. A missing binary with a retained declaration
is an error; remove the declaration to request deletion.

## NeoFlow dialogues

Each `.neoflow` contains exactly one top-level `sealed class Name : Dialogue`.
It must be non-generic, directly derive from the compiler-owned system
`Dialogue`, and override the required `Trigger Trigger` exactly once. No other
class, interface, enum, nested type, or local type is legal in NeoFlow.

```neoflow
@id("capitol-dialogue-id")
@settings(
  name: "Capitol: cold boot",
  description: "Capitol recognizes a returning player.",
  saveOptionChoices: true
)
sealed class CapitolColdBoot : Dialogue {
  @primary Outpost capitol = Assets.Capitol;
  Player player = Player.Current;

  @id("can-start-function-id")
  bool CanStart() {
    return !player.HasSeenCapitol;
  }

  @id("trigger-id")
  override Trigger Trigger = new(
    group: CapitolDialogues.High,
    when: [
      @id("can-start-use-id")
      CanStart,
    ]
  ) => Welcome;

  @id("welcome-node-id")
  Text Welcome = new(name: "Welcome!") {
    """
    Hello, {player.Name}. Welcome to {capitol.Name}.
    """

    @id("continue-option-id")
    Option Continue = new() {
      """
      Tell me more.
      """
      return Remember;
    }
  }

  @id("remember-node-id")
  Actions Remember = new() {
    @id("seen-mutation-id")
    player.HasSeenCapitol = true;

    @id("pause-id")
    Pause(reason: "remember", duration: 0.5);

    return Finish;
  }

  @id("finish-node-id")
  Text Finish = new() {
    """
    Until next time.
    """
  }
}
```

- `@primary` explicitly selects the primary value. Ordinary typed bindings are
  manual linked values. Primary is never inferred.
- Dialogue bindings are visible throughout the flow; node bindings are scoped
  to that node and its owned children.
- Direct and transitive function references derive Logic linked values by
  stable dependency graph. Do not add redundant manual links.
- Inline functions are reusable. A function definition has one ID; every
  persisted condition use, action invocation, mutation, and pause has its own
  owner-scoped ID.
- Triple-quoted prose is main-locale localized text with typed interpolation.
- Graph constructor bodies are NeoFlow-only optional escaping trailing bodies
  with contextual `void | Node`. `=> Next` is the concise form; a block may
  finish with `return Next;`. There is no authorable `to:` argument.
- Triggers, options, and outcomes require a node destination. Terminal text and
  actions may fall through. Text with options and conditions with outcomes do
  not also return a direct destination.
- `Actions Empty = new();` is valid. Put all action statements inside its body.
- Web layout and compiled IR are derived state and do not dirty source.

After dialogue edits:

```sh
neo dialogue dryrun <dialogue-ref>
```

The dry run traverses option paths from a fresh save and applies runtime
mutation-ownership rules. Exit 1 means the graph would fail on device. Whole
dialogue JSON export/apply is not a supported source workflow. Low-level
`neo dialogue compile` remains available for repair/inspection of a logic
block.

## Inline NeoScript and snippets

Computed getters, setters, functions, dialogue logic, and group logic are
inline in their owning `.neo`/`.neoflow` declaration. The browser-safe shared
language service compiles them with the same project symbols used by Monaco,
VS Code, CLI, and the trusted server. There is no `Scripts/<Class>/<Member>.neo`
sidecar path.

Use snippet commands for focused checking and evaluation:

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

`eval` uses authored values and the same evaluator as the web app. `apply`
previews ordered write intents unless its explicit command contract supports a
commit. Prefer `--json` for automation. Runtime ownership—not body kind—decides
whether a write target is Immutable, Save, Session, or otherwise writable.

Migrations remain tracked NeoScript action files under `Migrations/`:

```sh
neo migrate new <name> --target <ClassName|project>
neo migrate list
neo migrate check
neo migrate run [--dry-run] [--skip-invalid]
neo migrate prune
```

## Conflicts, identity, and push

Pull performs a record-aware three-way merge by stable identity:

- unchanged local accepts server authored fields;
- unchanged server retains local edits;
- disjoint fields/children merge;
- collection and graph children merge by ID, never index;
- conflicting scalar, edge, order, delete/edit, or binary changes are explicit.

Conflict source intentionally fails compilation. Edit the desired final source
or use `neo resolve --mine|--theirs` as a whole-side convenience. Then pull,
review, dry-run, and push again. Never seek a force-CAS bypass.

`neo push` sends the complete hashed source bundle for trusted server
recompilation but writes only the semantic diff. One accepted push atomically
commits records, main-locale text, stored bindings, and staged file metadata
under CAS. Server compilation independently derives IR, linked values,
placements, structural rows, and generic/storage stamps; client products are
not trusted.

Pending IDs are assigned only inside a successful real commit. Source and
`.neo/state.json` are rewritten only after acceptance. Dry-run never uploads,
allocates a durable ID, or changes tracked bytes.

For `base-hash-conflict`, pull, resolve, and retry. For
`version-bump-required`, inspect the classification and pass
`neo push --accept-bump` only when intended.

## Low-level content operations

Checked-in source is the normal way to author project values, files, and
dialogues. Low-level commands remain peer mutation surfaces for automation and
repair:

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
neo dialogue list
neo dialogue show <ref>
neo files list
```

Write verbs accept a JSON-array batch on stdin or `--file` where supported;
use one batch for atomic related changes. A `values set` payload is the raw
value, not `{ "value": ... }`. Static bind/unbind changes the live stored
binding. These commands do not rewrite local project source. After any
low-level or web write, run `neo pull` before further source edits or push.

## Branches, releases, and history

```sh
neo branch list
neo branch create <name> [--from <ref>]
neo branch switch <nameOrId>
neo branch refresh [--dry-run]
neo merge <branch> [--dry-run] [--migrate]
neo release cut [--bump major|minor|patch] [--dry-run]
neo history inspect
neo history log
```

Branches are copy-on-write forks. Releases are immutable snapshots; the
server derives a minimum compatibility bump and it may only be raised. Release
operations require the release login profile.

## Editor and implementation contract

The browser-safe `@neocompose/neoscript-language` service is shared by the
compiler, CLI, trusted server, web Monaco editor, and VS Code LSP. Do not add
language intelligence directly to an environment adapter.

Monaco and VS Code must share project-aware diagnostics, recovery completion,
hover, signature help, definitions, references, rename, symbols, semantic
tokens, code actions, and formatting for `.neo` and `.neoflow`. Project updates
must refresh context without accumulating stale providers. The VSIX is built
in-repo; marketplace publication is separate.

The Node CLI talks directly to authenticated Convex APIs and commits through
the session-gated CAS boundary. Tokens use the macOS Keychain/OS credential
store when available and a protected file only as fallback. Use
`NEO_COMPOSE_TOKEN` or `--token-stdin` in CI. The editor profile cannot publish
releases; server-enforced scopes are the security boundary.

The npm package bundles the Node orchestrator and shared browser-safe
compiler/editor assets. It must not contain a `.csproj`, `.cs`, `.dll`, Roslyn
application, .NET host metadata, or generated C# SDK. Package-content tests
enforce that boundary.

A change is incomplete until focused tests, typecheck, builds, package-content
and contract checks, Monaco/browser parity, VSIX packaging and interactive
testing, sample no-op verification, and final `npm run doctor` succeed.
