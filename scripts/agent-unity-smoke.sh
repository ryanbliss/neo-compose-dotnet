#!/usr/bin/env bash
#
# Rig Unity smoke (P53 §6, §10).
#
# Proves the whole SDK half of a rig against the rig's own deployment:
#
#   1. headless sign-in for the rig origin (a no-op when a usable credential is
#      already stored),
#   2. headless project synchronization of the HelloWorld sample,
#   3. the acceptance criterion binding a rig has to satisfy — synchronizing a
#      bound rig changes NO tracked Unity config or generated output, because
#      the rig overlay is ephemeral and the canonical sample IDs never move.
#
# Approval is external: batchmode has no browser, so step 1 logs a verification
# URL and user code and waits. Approve it in the rig's browser session.
#
# The rig app has to be serving first — this script drives Unity, not the app;
# start it with `npm run agent:dev` in the rig's neo-compose worktree.
#
# Only one batchmode Unity may hold a project directory at a time, so the two
# editor runs below are strictly serial. Do not run this while the sample is
# open in the Editor.
#
#   scripts/agent-unity-smoke.sh [--force-login] [--skip-login]

set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project_path="$repository_root/samples/HelloWorld"
force_login=0
skip_login=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --force-login) force_login=1 ;;
    --skip-login) skip_login=1 ;;
    *)
      echo "Unknown argument: $1 (expected --force-login or --skip-login)" >&2
      exit 2
      ;;
  esac
  shift
done

fail() {
  echo "$1" >&2
  exit 1
}

for tool in node curl git; do
  command -v "$tool" >/dev/null 2>&1 || fail "$tool was not found on PATH; the rig smoke needs it."
done

# --- rig -------------------------------------------------------------------
# Same resolution order Unity itself uses: the explicit environment override
# wins, otherwise the gitignored pointer at this worktree's root.
manifest_path="${NEO_COMPOSE_RIG_MANIFEST:-}"
if [[ -z "$manifest_path" ]]; then
  pointer_path="$repository_root/.neo-rig"
  [[ -f "$pointer_path" ]] || fail "$repository_root carries no .neo-rig pointer. Run scripts/agent-setup first."
  manifest_path="$(node -e '
const { readFileSync } = require("node:fs");
const pointer = JSON.parse(readFileSync(process.argv[1], "utf8"));
if (typeof pointer.manifestPath !== "string" || pointer.manifestPath === "") {
  console.error(`${process.argv[1]} has no manifestPath.`);
  process.exit(1);
}
process.stdout.write(pointer.manifestPath);
' "$pointer_path")"
fi
[[ -f "$manifest_path" ]] || fail "Rig manifest $manifest_path does not exist. Rerun scripts/agent-setup."

read -r rig_id web_origin deployment_name seed_id < <(node -e '
const { readFileSync } = require("node:fs");
const manifest = JSON.parse(readFileSync(process.argv[1], "utf8"));
if (manifest.formatVersion !== 1) {
  console.error(`Rig manifest declares formatVersion ${manifest.formatVersion}; this script understands 1.`);
  process.exit(1);
}
process.stdout.write(
  `${manifest.rigId} ${manifest.web.origin} ${manifest.deployment.name} ${manifest.seed?.id ?? "(unseeded)"}\n`,
);
' "$manifest_path")

echo "Rig $rig_id → deployment $deployment_name ($web_origin), seed $seed_id"
echo "Manifest: $manifest_path"

# --- preconditions ---------------------------------------------------------
# curl writes "000" through -w on a connection failure AND exits non-zero, so
# an `|| echo` fallback would concatenate two statuses into one unusable
# string. Take whatever curl printed and require it to look like a served
# response instead.
app_status="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 10 "$web_origin/auth/sign-in" 2>/dev/null || true)"
if [[ ! "$app_status" =~ ^[1-4][0-9][0-9]$ ]]; then
  fail "The rig app at $web_origin is not serving (status ${app_status:-none}). Start it with \"npm run agent:dev\" in the rig's neo-compose worktree."
fi

unity_editor="${UNITY_EDITOR:-}"
if [[ -z "$unity_editor" ]]; then
  unity_editor="$(command -v Unity || true)"
fi
[[ -n "$unity_editor" ]] || fail "Unity was not found. Set UNITY_EDITOR to the Unity 6000.5.4f1 executable."
[[ -x "$unity_editor" ]] || fail "UNITY_EDITOR is not executable: $unity_editor"

log_dir="${TMPDIR:-/tmp}"
log_dir="${log_dir%/}"

# Runs one batchmode entry point to completion and reports the marker line.
# -quit is deliberately absent: both entry points exit the editor themselves
# once their asynchronous work settles.
run_batch_method() {
  local label="$1" method="$2" marker="$3" log_file="$4"
  echo
  echo "== $label =="
  echo "Unity log: $log_file"
  local started exit_code=0
  started="$(date +%s)"
  "$unity_editor" \
    -batchmode \
    -nographics \
    -projectPath "$project_path" \
    -executeMethod "$method" \
    -logFile "$log_file" || exit_code=$?
  local elapsed=$(( $(date +%s) - started ))
  if [[ $exit_code -ne 0 ]]; then
    echo "$label failed after ${elapsed}s (Unity exited $exit_code). Log tail:" >&2
    grep -F "$marker" "$log_file" >&2 || true
    tail -n 80 "$log_file" >&2
    exit 1
  fi
  if ! grep -qF "$marker end: success" "$log_file"; then
    echo "$label exited 0 but never logged \"$marker end: success\" after ${elapsed}s. Log tail:" >&2
    grep -F "$marker" "$log_file" >&2 || true
    tail -n 80 "$log_file" >&2
    exit 1
  fi
  echo "$label succeeded in ${elapsed}s:"
  grep -F "$marker" "$log_file"
}

# --- 1. headless sign-in ---------------------------------------------------
if [[ $skip_login -eq 1 ]]; then
  echo "Skipping headless sign-in (--skip-login); the stored credential must already cover $web_origin."
else
  if [[ $force_login -eq 1 ]]; then
    export NEO_COMPOSE_BATCH_LOGIN_FORCE=1
  fi
  echo
  echo "Approve the device authorization this run logs as \"[NeoComposeBatchLogin] verification url:\"."
  run_batch_method \
    "Headless sign-in" \
    "NeoCompose.Unity.Editor.NeoComposeBatchLogin.Run" \
    "[NeoComposeBatchLogin]" \
    "$log_dir/neo-compose-rig-login.$rig_id.log"
fi

# --- 2. headless synchronize ----------------------------------------------
# Drop the editor's export cache first. It remembers the last snapshot this
# project synchronized and short-circuits with "already synchronized", which
# would make step 3 below assert cleanliness over files nothing rewrote — a
# green that proves nothing. The cache lives under Library, is gitignored, and
# is rebuilt by the synchronize that follows.
export_cache="$project_path/Library/NeoCompose/ExportCache"
if [[ -d "$export_cache" ]]; then
  rm -rf "$export_cache"
  echo
  echo "Dropped $export_cache so the synchronize below really writes."
fi

run_batch_method \
  "Headless synchronize" \
  "NeoCompose.Unity.Editor.NeoComposeBatchSync.Run" \
  "[NeoComposeBatchSync]" \
  "$log_dir/neo-compose-rig-sync.$rig_id.log"

# --- 3. binding changes no tracked output ----------------------------------
# The claim under test is that nothing COMMITTED moved: not the config asset,
# not generated C#, not project.json, not the imported file assets, not the
# project settings. Untracked files are excluded on purpose — Unity's own
# Library/Temp output and the gitignored runtime secret are not part of it.
#
# The IDE solution/project files that live beside them are NOT part of it
# either: Unity rewrites them whenever it opens the project (`*.csproj` is
# gitignored for exactly that reason, `HelloWorld.slnx` is not), and that churn
# says nothing about the rig. It is reported, not failed on.
echo
echo "== Tracked Unity output =="
config_paths=(
  samples/HelloWorld/Assets
  samples/HelloWorld/ProjectSettings
  samples/HelloWorld/Packages
)
dirty="$(git -C "$repository_root" status --porcelain --untracked-files=no -- "${config_paths[@]}")"
if [[ -n "$dirty" ]]; then
  echo "Synchronizing the rig changed tracked sample output, which binding a rig must not do:" >&2
  echo "$dirty" >&2
  echo >&2
  git -C "$repository_root" --no-pager diff --stat -- "${config_paths[@]}" >&2
  echo >&2
  echo "Two very different causes look the same here. If the endpoints or the" >&2
  echo "project/version/channel identifiers moved, the rig overlay leaked into a" >&2
  echo "committed asset, and that is an SDK bug. If only project content moved," >&2
  echo "seed $seed_id and the committed sample disagree, and one of them has to be" >&2
  echo "reconciled. Diff before deciding which." >&2
  exit 1
fi
echo "Clean: synchronizing rig $rig_id changed no tracked config or generated output."

other="$(git -C "$repository_root" status --porcelain --untracked-files=no -- samples/HelloWorld ':(exclude)samples/HelloWorld/Assets' ':(exclude)samples/HelloWorld/ProjectSettings' ':(exclude)samples/HelloWorld/Packages')"
if [[ -n "$other" ]]; then
  echo "Note: opening the project also rewrote tracked IDE scaffolding (not rig-related):"
  echo "$other"
fi

echo
echo "Rig Unity smoke passed for $rig_id."
