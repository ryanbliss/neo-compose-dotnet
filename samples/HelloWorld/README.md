# NeoCompose Unity Sample — HelloWorld

A minimal Unity 6 project consuming the `com.ryanbliss.neocompose` package
via a local-path dependency, for end-to-end smoke testing of the package
during development.

## Setup

1. Open this project in Unity 6000.5.4f1.
2. The package is referenced at `file:../../../src/NeoComposeUnity` in
   `Packages/manifest.json` — edits to the package source are picked up
   on the next domain reload.
3. Drop a `HelloWorldBehaviour` component on any GameObject and enter
   Play mode. The console logs a "Hello from NeoCompose" message confirming
   the package loaded.

## Schema authoring

The tracked `neo/` directory is this sample's Neo Schema Authoring v2
workspace. `neo/neo.json` declares `formatVersion: 3`, and
`neo/NeoCompose.Schema.csproj` gives editors a real C# 11 project targeting
`netstandard2.1` for complete C# and Neo schema IntelliSense.

- Types, inheritance, overrides, nullability, defaults, ordering, and
  function signatures are authored as native C# under `neo/Classes/`,
  `neo/Interfaces/`, and `neo/Enums/`.
- Root-level and otherwise loose members are authored in the `[NeoRegistry]`
  declaration at `neo/Root.cs`.
- Stable server identities remain explicit through `[NeoSchemaClass]`,
  `[NeoMember]`, and the other typed Neo members.
- Computed-property and NeoScript functions are tracked as complete contextual
  `.neo` documents under `neo/Scripts/<DeclaringType>/<Member>.neo`. Their
  outer signature mirrors C# and includes `<DeclaringType> this, Root root`;
  canonical C# links back with a `// NeoScript: Scripts/...` comment.
- Template and localization configuration use strongly typed C# settings
  objects, including workflow statuses under `neo/LocalizationStatuses/`.
  There are no embedded JSON schema carriers.
- `.neo/tooling` and `.neo` sync state are local generated artifacts. The CLI
  hydrates them and discovers Unity's compatible runtime first, then a system
  `dotnet`; it never installs or downloads a runtime.

Run schema commands from `samples/HelloWorld/neo/` (or any descendant):

```sh
neo doctor
neo status
neo pull
neo push --dry-run
```

`neo pull --reset` is the intentional clean-reconstruction path for an old or
discardable working copy. It rewrites the canonical v2 C# and sidecars, so
commit or stash hand-authored work first.

The schema workspace is distinct from the Unity runtime export. The latter is
materialized as `Assets/Resources/Neo/project.json`, and its generated C# API
is committed at `Assets/Scripts/Neo/NeoGeneratedTypes.cs`; do not edit that
generated file by hand.

## Tests

Open **Window → General → Test Runner**. You'll see two assemblies:

- `NeoCompose.Unity.Tests` — the package's own tests (live in
  `src/NeoComposeUnity/Tests/`).
- `Tests` — the sample's tests demonstrating downstream
  consumption of the package.

Both run from this same Test Runner window.
