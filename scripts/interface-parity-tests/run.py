#!/usr/bin/env python3
"""Run the SDK's typed generated-interface NUnit pins on .NET 10, without Unity."""
import argparse
from pathlib import Path
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default=shutil.which("dotnet"))
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--unity-contents", type=Path)
    args = parser.parse_args()
    if not args.dotnet:
        parser.error(".NET 10 SDK is required; pass --dotnet or add it to PATH")
    properties = ET.parse(ROOT / "Directory.Build.props")
    editor_version = properties.find(".//UnityEditorVersion").text
    contents = args.unity_contents or Path(f"/Applications/Unity/Hub/Editor/{editor_version}/Unity.app/Contents")
    managed = contents / "Resources/Scripting/Managed/UnityEngine"
    if not managed.is_dir():
        parser.error(f"Unity managed references are missing: {managed}")
    project = ROOT / "src/NeoComposeUnity/NeoComposeUnity.Tests.csproj"
    output = args.output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    # Use the test project's own NUnit/Newtonsoft versions, not a second pin.
    packages = ET.parse(project).findall(".//PackageReference")
    with tempfile.TemporaryDirectory(prefix="neo-p127-typed-") as temporary:
        work = Path(temporary)
        version = subprocess.check_output([args.dotnet, "--version"], text=True, cwd=work).strip()
        if not version.startswith("10."):
            parser.error(f".NET 10 SDK is required; selected {version}")
        subprocess.run([
            args.dotnet, "build", str(project),
            '-p:DefineConstants="UNITY_6000_3_OR_NEWER;UNITY_EDITOR"',
            f"-p:UnityEditorContentsDir={contents}",
        ], cwd=work, check=True)

        xml = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
        group = ET.SubElement(xml, "PropertyGroup")
        for key, value in {"TargetFramework": "net10.0", "OutputType": "Exe", "ImplicitUsings": "enable", "Nullable": "enable", "EnableDefaultCompileItems": "false"}.items():
            ET.SubElement(group, key).text = value
        group = ET.SubElement(xml, "ItemGroup")
        ET.SubElement(group, "Compile", Include=str(HERE / "Program.cs"))
        for package in packages:
            ET.SubElement(group, "PackageReference", **package.attrib)
        runner = work / "TypedParity.csproj"
        ET.ElementTree(xml).write(runner, encoding="unicode")
        package_root = work / "Packages"
        package_root.mkdir()
        (package_root / "com.ryanbliss.neocompose").symlink_to(ROOT / "src/NeoComposeUnity", target_is_directory=True)
        subprocess.run([args.dotnet, "build", str(runner)], cwd=work, check=True)
        return subprocess.run([
            args.dotnet, str(work / "bin/Debug/net10.0/TypedParity.dll"),
            str(ROOT), str(managed), str(output),
        ], cwd=work).returncode


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except subprocess.CalledProcessError as error:
        raise SystemExit(error.returncode)
