# NeoCompose Unity Sample — HelloWorld

A Unity 6 project consuming the `com.ryanbliss.neocompose` package via a
local-path dependency. It doubles as the end-to-end smoke test for the package
and as the reference downstream consumer of a Neo Compose format-4 project.

## Prerequisites

- Unity 6000.5.4f1.
- The `neo` CLI — `npm i -g @neocompose/cli`, or `node cli/bin/neo.mjs` from a
  `neo-compose` repository checkout.

## Running the sample

1. Open `samples/HelloWorld/` in Unity 6000.5.4f1. The package is referenced at
   `file:../../../src/NeoComposeUnity` in `Packages/manifest.json`, so edits to
   the package source are picked up on the next domain reload.
2. Open `Assets/Scenes/MainScene.unity` and enter Play mode. `HelloWorldMenu`
   drives the save-file menu, spawns `HelloWorldGameplay` for the selected save,
   and `LandingSceneGameplay` / `LandingSceneUI` render the landing scene.

## The `neo/` workspace

`samples/HelloWorld/neo/` is a format-4 Neo Compose checkout — a Git-like
working copy of one project version. `neo/neo.json` declares
`formatVersion: 4`. Tracked native source is the CLI authoring source of truth;
there is no C# schema project, no generated authoring SDK, and no per-member
script sidecars.

```text
neo/
  neo.json
  Project.neo             Project settings (default texture template, priority group)
  Root.neo                Root-level stored bindings
  Classes/                Class declarations (*.neo) + colocated *.spec.neo tests
  Interfaces/             Interface declarations
  Enums/                  Enum declarations
  Templates/              Texture templates
  PriorityGroups/
  Localization.neo        Locales, statuses, main locale
  LocalizationStatuses/
  Files/                  Images.neo / AudioClips.neo registries + their binaries
  DialogueGroups/
  Dialogues/*.neoflow     NeoFlow dialogues
```

Schema declarations, values, and NeoScript live in `.neo`; dialogues live in
`.neoflow`; tests live in colocated `.spec.neo` files, which are test-only and
never enter status, source hashes, pull, or push. `.neo/` inside the workspace
is private CLI state — never edit it as source.

### Identity lives in the Unity config asset

`neo.json` uses `unityConfigPath: ../Assets/Resources/Neo/NeoComposeConfig.asset`
instead of inline `projectId`/`versionId`. That asset owns the project and
version IDs and is updated by branch and version switches, so the IDs are never
duplicated in `neo.json`. `neo.json` also sets `apiBaseUrl`, `convexUrl`,
`profile: editor`, and `prePushHook: "neo test"`.

## The sync loop

1. Edit tracked `.neo` / `.neoflow` source under `neo/`.
2. `neo push` from `samples/HelloWorld/neo/` (or any descendant). The configured
   `prePushHook` runs `neo test` first. `neo push --dry-run` rehearses the full
   local emission and the server's preparation phase without pushing; note that
   the pre-push hook is a real-push hook and does not run for a dry run.
3. In Unity, run the synchronize pipeline from **Tools → Neo Compose** (also
   available under **Window → Neo Compose**). The sample additionally exposes
   **Neo Compose → Headless Sync** (`Assets/Editor/NeoHeadlessSync.cs`), a menu
   command that runs the same pipeline without opening the editor window. It is
   not batchmode-safe: it can raise confirmation dialogs and never exits the
   editor, so it is not a CI entry point.
4. Commit the regenerated outputs.

Synchronization writes committed artifacts into the Unity project:

- `Assets/Resources/Neo/project.json` — the runtime project export.
- `Assets/Scripts/Neo/NeoGeneratedTypes.cs` — the generated C# API. Do not edit
  by hand; extend it with hand-authored partials in
  `Assets/Scripts/Neo/NeoClassesExtended.cs`.
- `Assets/Resources/Neo/Localization/*.json` — per-locale strings.
- `Assets/Resources/Neo/Files/{Sprites,Audio}/` — imported managed binaries.

## Common CLI commands

Run from `samples/HelloWorld/neo/` or any descendant:

```sh
neo doctor
neo status
neo diff
neo pull
neo test
neo push --dry-run
neo push
```

Pull before editing, and pull again before pushing if time has passed.
`neo pull --reset` and `neo pull --force` are destructive reconstruction paths;
use them only when that effect is intended.

## Runtime auth

`NeoComposeConfig.asset` carries the runtime OAuth client ID and scopes and
enables OAuth cloud sync. The companion
`Assets/Resources/Neo/NeoComposeRuntimeSecret.asset` is bundled into builds and
deliberately gitignored (see `Assets/Resources/Neo/.gitignore`), so a fresh
clone has no runtime secret until one is provisioned locally.

## Tests

Open **Window → General → Test Runner**. The visible assemblies are the
package test assemblies exposed through `testables` in `Packages/manifest.json`
(`NeoCompose.Unity.Tests` and `NeoCompose.Unity.Convex.Tests`, living under
`src/`) plus the sample's own: `Tests` (EditMode) and
`HelloWorld.PlayMode.Tests` (PlayMode) under `Assets/Tests/`.

All of them run from this same Test Runner window. Schema-level tests are
separate: they are the `.spec.neo` files under `neo/Classes/`, run by
`neo test`.
