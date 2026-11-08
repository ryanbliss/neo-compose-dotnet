# Typed generated-interface parity on .NET 10

Run from the SDK checkout after HelloWorld has been imported by Unity:

```sh
python3 scripts/interface-parity-tests/run.py --dotnet /path/to/dotnet10 --output /tmp/typed-interface-parity.json
```

This builds the real SDK and delivered generated classes, then runs every `HistoricalGeneratedCases` row through the same NUnit method used by Unity. The delegates contain direct typed C# calls. It also runs the fixture's standalone generated aggregate tests. Missing fixtures, zero discovered cases and assertion failures fail the command. The JSON records every case and its runtime-specific pin.

CoreCLR uses the NeoScript/.NET pin. Unity Mono uses the documented Mono pin when one exists. Static interface members have no C# 9 call form and remain evaluator tests; exposed class-static cases in the provider run here too.

The runner needs .NET SDK 10 and the Unity reference/test assemblies already used by the SDK project. `--unity-contents` overrides the default editor from `Directory.Build.props`. It starts no Unity process. Its temporary project and package link are removed on exit; SDK build outputs remain under `build/` for the caller to clean up.
