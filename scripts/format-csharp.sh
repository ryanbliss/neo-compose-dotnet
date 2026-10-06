#!/usr/bin/env bash
set -euo pipefail

# Folder mode needs neither Unity nor its generated solution/project files.
cd "$(dirname "$0")/.."
root="$PWD"
directories=(
    "src/NeoComposeUnity/Runtime"
    "src/NeoComposeUnity/Editor"
    "src/NeoComposeUnity/Tests"
    "src/NeoComposeConvex/Runtime"
    "src/NeoComposeConvex/Tests"
    "samples/HelloWorld/Assets/Scripts"
    "samples/HelloWorld/Assets/Tests"
    "scripts/performance"
)
# Exclusions are relative to each folder workspace, not the repository root.
for directory in "${directories[@]}"; do
    dotnet format whitespace "$root/$directory" --folder "$@" \
        --exclude Generated Neo/Generated
done
