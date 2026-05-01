# NeoCompose Unity Sample — HelloWorld

A minimal Unity 6 project consuming the `com.ryanbliss.neocompose` package
via a local-path dependency, for end-to-end smoke testing of the package
during development.

## Setup

1. Open this project in Unity 6000.0.40f1.
2. The package is referenced at `file:../../../src/NeoComposeUnity` in
   `Packages/manifest.json` — edits to the package source are picked up
   on the next domain reload.
3. Drop a `HelloWorldBehaviour` component on any GameObject and enter
   Play mode. The console logs a "Hello from NeoCompose" message confirming
   the package loaded.

## Tests

Open **Window → General → Test Runner**. You'll see two assemblies:

- `NeoCompose.Unity.Tests` — the package's own tests (live in
  `src/NeoComposeUnity/Tests/`).
- `HelloWorld.Tests` — the sample's tests demonstrating downstream
  consumption of the package.

Both run from this same Test Runner window.
