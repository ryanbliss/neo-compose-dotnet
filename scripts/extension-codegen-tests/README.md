# Extension codegen compiler tests

Install .NET SDK **10.0.113** from [Microsoft's .NET 10 downloads](https://dotnet.microsoft.com/download/dotnet/10.0). This directory pins that SDK with roll-forward disabled. The harness does not install tools. The package and Unity sample keep their existing production compiler pins.

Run from the SDK checkout:

```sh
UNITY_EDITOR=/path/to/6000.6.4f1/Editor/Unity \
NEO_COMPOSE_SOURCE=/path/to/neo-compose \
python3 scripts/extension-codegen-tests/run.py
```

Set `PATH` and `DOTNET_ROOT` if the test SDK lives outside the default installation. The sibling `neo-compose` checkout is used when `NEO_COMPOSE_SOURCE` is omitted. Its dependencies must already be installed.

The harness verifies the sample with `scripts/verify-unity-compile.sh`, reads Unity's compiler response file, and compiles fresh generated fixtures with explicit C# 9 and C# 14 targets and `-nostdlib+`. It references the real SDK and Unity dependencies, never .NET 10 runtime references or stub SDK types. It also verifies that C# 14-only APIs are absent from the baseline and reflects over the projected CLR, Unity, and SDK types to check the wrapper conflict table.

A missing compiler, editor, response file, assembly, or required compiler marker attribute fails the check. Fixture files and compiler outputs live in a temporary directory.
