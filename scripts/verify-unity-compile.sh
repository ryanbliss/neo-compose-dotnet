#!/usr/bin/env bash

set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project_path="$repository_root/samples/HelloWorld"
unity_editor="${UNITY_EDITOR:-}"

if [[ -z "$unity_editor" ]]; then
  unity_editor="$(command -v Unity || true)"
fi

if [[ -z "$unity_editor" ]]; then
  echo "Unity was not found. Set UNITY_EDITOR to the Unity 6000.5.4f1 executable." >&2
  exit 2
fi

if [[ ! -x "$unity_editor" ]]; then
  echo "UNITY_EDITOR is not executable: $unity_editor" >&2
  exit 2
fi

log_file="$(mktemp "${TMPDIR:-/tmp}/neo-compose-unity-compile.XXXXXX.log")"
trap 'rm -f "$log_file"' EXIT

if ! "$unity_editor" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "$project_path" \
  -logFile "$log_file"; then
  echo "Unity compilation failed:" >&2
  grep -E 'error CS|Scripts have compiler errors|Aborting batchmode' "$log_file" >&2 || tail -n 200 "$log_file" >&2
  exit 1
fi

if grep -Eq 'error CS|Scripts have compiler errors' "$log_file"; then
  echo "Unity reported compiler errors:" >&2
  grep -E 'error CS|Scripts have compiler errors' "$log_file" >&2
  exit 1
fi

echo "Unity compilation succeeded."
