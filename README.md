# NeoCompose

A Unity C# package — `com.ryanbliss.neocompose`.

## Repository layout

```
src/
  NeoComposeUnity/        Unity package (Runtime + Editor + Tests)
samples/
  HelloWorld/             Unity 6000.5.4f1 project consuming the package
                          via a local file: dependency
    neo/                  format-4 Neo Compose workspace for the sample
```

The package source lives entirely under `src/NeoComposeUnity/` (Runtime
scripts, Editor scripts, and Unity Test Framework tests) — there's no
separate raw-.NET library or precompiled `.dll`.

## Setup

1. Open `samples/HelloWorld/` in Unity 6000.5.4f1.
2. The `com.ryanbliss.neocompose` package is referenced via a local path
   in `Packages/manifest.json`, so edits in `src/NeoComposeUnity/` are
   picked up live.

The Hello World project also serves as the downstream format-4 authoring
sample. Its tracked `.neo` source lives in `samples/HelloWorld/neo/`; see the
[sample README](./samples/HelloWorld/README.md#the-neo-workspace) for the
authoring and synchronization workflow.

## Tests

- **Compilation preflight** — before opening the sample, verify that its
  generated code and local package compile together:

  ```bash
  UNITY_EDITOR=/path/to/Unity scripts/verify-unity-compile.sh
  ```

- **Package tests** — open the sample in Unity, then **Window → General →
  Test Runner**. The package's `NeoCompose.Unity.Tests` assembly shows up
  alongside the sample's `Tests` assembly.
- **Sample tests** — same Test Runner window; the `Tests`
  assembly demonstrates how a downstream project consumes + tests against
  the package.

## License

MIT. See [LICENSE](./LICENSE).
