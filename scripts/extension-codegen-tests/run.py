#!/usr/bin/env python3
"""Compile real generator output against freshly verified Unity SDK assemblies."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

HERE = Path(__file__).resolve().parent
REPOSITORY = HERE.parent.parent
SAMPLE = REPOSITORY / "samples/HelloWorld"
VERSION = json.loads((HERE / "global.json").read_text())["sdk"]["version"]


def fail(message):
    raise SystemExit(message)


def run(arguments, cwd=HERE, **kwargs):
    return subprocess.run([str(arg) for arg in arguments], cwd=cwd, check=True, **kwargs)


def main():
    dotnet = shutil.which("dotnet")
    if not dotnet:
        fail(f"Install .NET SDK {VERSION} from https://dotnet.microsoft.com/download/dotnet/10.0 and add dotnet to PATH. This harness never downloads it.")
    selected = subprocess.run([dotnet, "--version"], cwd=HERE, capture_output=True, text=True)
    if selected.returncode or selected.stdout.strip() != VERSION:
        fail(f"This harness requires exactly .NET SDK {VERSION}. Install it from https://dotnet.microsoft.com/download/dotnet/10.0; no roll-forward is allowed.")
    listing = run([dotnet, "--list-sdks"], capture_output=True, text=True).stdout.splitlines()
    sdk = next((Path(line.split("[", 1)[1].rstrip("]")) / VERSION for line in listing if line.startswith(VERSION + " ")), None)
    compiler = sdk / "Roslyn/bincore/csc.dll" if sdk else None
    if compiler is None or not compiler.is_file():
        fail(f"Roslyn csc.dll is missing from SDK {VERSION}.")
    editor = Path(os.environ.get("UNITY_EDITOR", ""))
    if not editor.is_file():
        fail("Set UNITY_EDITOR to the sample's pinned Unity editor executable.")
    run([REPOSITORY / "scripts/verify-unity-compile.sh"], cwd=REPOSITORY)
    responses = list((SAMPLE / "Library/Bee").rglob("NeoCompose.Unity.rsp"))
    if not responses:
        fail("Unity compilation produced no NeoCompose.Unity.rsp under Library/Bee.")
    response = max(responses, key=lambda path: path.stat().st_mtime_ns)
    references = {}
    for line in response.read_text().splitlines():
        if not line.startswith("-r:"):
            continue
        path = Path(line[3:].strip('"'))
        if not path.is_absolute():
            path = SAMPLE / path
        built = SAMPLE / "Library/ScriptAssemblies" / path.name.replace(".ref.dll", ".dll")
        if built.exists():
            path = built
        if not path.is_file():
            fail(f"Unity reference is missing: {path}")
        references[path.name] = path.resolve()
    runtime = SAMPLE / "Library/ScriptAssemblies/NeoCompose.Unity.dll"
    if not runtime.is_file():
        fail("The freshly compiled NeoCompose.Unity.dll is missing.")
    references[runtime.name] = runtime
    compose = Path(os.environ.get("NEO_COMPOSE_SOURCE", REPOSITORY.parent / "neo-compose"))
    generator = compose / "scripts/extension-codegen-fixtures.ts"
    if not generator.is_file():
        fail("Set NEO_COMPOSE_SOURCE to the neo-compose checkout containing scripts/extension-codegen-fixtures.ts.")
    with tempfile.TemporaryDirectory(prefix="neo-extension-codegen-") as temporary:
        output = Path(temporary)
        run(["node", "--import", "tsx", generator, output], cwd=compose)
        for version in ("9.0", "14.0"):
            sources = sorted((output / version).rglob("*.cs")) + [HERE / "Consumers9.cs"]
            if version == "14.0":
                sources.append(HERE / "Consumers14.cs")
            arguments = ["-nologo", "-target:library", "-nostdlib+", "-nullable:enable", f"-langversion:{version}", f'-out:"{output / (version + ".dll")}"']
            arguments += [f'-r:"{path}"' for path in references.values()]
            arguments += [f'"{source}"' for source in sources]
            rsp = output / f"{version}.rsp"
            rsp.write_text("\n".join(arguments) + "\n")
            run([dotnet, compiler, "@" + str(rsp)], cwd=SAMPLE)
        negative = output / "unsupported9.rsp"
        negative.write_text((output / "9.0.rsp").read_text() + f'"{HERE / "Consumers14.cs"}"\n')
        rejected = subprocess.run([dotnet, str(compiler), "@" + str(negative)], cwd=SAMPLE, capture_output=True, text=True)
        if rejected.returncode == 0:
            fail("C# 14-only convenience APIs unexpectedly compiled in the C# 9 projection.")
        if "error CS1061" not in rejected.stdout:
            fail("The negative C# 9 case failed for an unexpected reason:\n" + rejected.stdout + rejected.stderr)
        readonly_consumer = output / "ReadonlySetter.cs"
        readonly_consumer.write_text('using ExtensionFixtures; class ReadonlySetter { void Set(IReadOnlyPerson value) { value.FullName = "Changed"; } }')
        readonly_response = output / "readonly14.rsp"
        readonly_response.write_text((output / "14.0.rsp").read_text() + f'"{readonly_consumer}"\n')
        readonly_rejected = subprocess.run([dotnet, str(compiler), "@" + str(readonly_response)], cwd=SAMPLE, capture_output=True, text=True)
        if readonly_rejected.returncode == 0 or "error CS0200" not in readonly_rejected.stdout:
            fail("Read-only extension property setter was not rejected:\n" + readonly_rejected.stdout + readonly_rejected.stderr)
        reflection = output / "ProjectionMembers.exe"
        reflection_response = output / "reflection.rsp"
        reflection_response.write_text("\n".join([
            "-nologo", "-target:exe", "-nostdlib+", "-langversion:9.0", f'-out:"{reflection}"',
            *[f'-r:"{path}"' for path in references.values()], f'-r:"{output / "9.0.dll"}"', f'"{HERE / "ProjectionMembers.cs"}"',
        ]) + "\n")
        run([dotnet, compiler, "@" + str(reflection_response)], cwd=SAMPLE)
        mono = editor.parent / "Data/MonoBleedingEdge/bin/mono"
        if not mono.is_file():
            fail(f"Unity's Mono runtime is missing: {mono}")
        environment = dict(os.environ)
        environment["MONO_PATH"] = os.pathsep.join(sorted({str(path.parent) for path in references.values()}))
        run([mono, reflection, output / "projection-members.json"], env=environment)
        print("Generated extension wrappers compile against Unity references at C# 9 and C# 14.")


if __name__ == "__main__":
    try:
        main()
    except subprocess.CalledProcessError as error:
        sys.exit(error.returncode)
