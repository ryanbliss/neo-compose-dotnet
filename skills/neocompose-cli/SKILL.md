---
name: neocompose-cli
description: >-
  Edit a Neo Compose project's schema as C# files and sync bidirectionally
  with the server using the `neo` CLI. Use when asked to add/change/remove
  custom types, attributes, or enums in a Neo Compose project, to validate or
  evaluate NeoScript, or to batch-edit values and localized strings. The
  working copy lives in a `neo/` directory (neo.json + Attributes/ + Enums/);
  commands are run via `node cli/bin/neo.mjs` (repo) or `npm run neo`.
---

# Neo Compose schema-as-code CLI (`neo`)

The `neo/` working copy is a **git-like checkout** of a Neo Compose project
version's schema: custom types and their attributes as one C# class per file
under `Attributes/`, enums under `Enums/`. The server is the source of truth;
files are a peer editor alongside the web UI. Spec:
`specs/schema-as-code-cli.md`.

## Core discipline

1. `neo pull` before editing (and before any push after time has passed).
2. Edit the C# files **within the constrained subset** (see below).
3. `neo status` / `neo diff` to review, `neo push --dry-run` to preview.
4. `neo push` to commit — atomically, with compare-and-swap base hashes.
5. **Pull → push with no edits is always a no-op.** If `neo status` reports
   changes right after a pull, that is a bug — report it, don't push.

The CLI never guesses: anything outside the subset is a file:line:column
error. Conflict markers in a file make it unparseable until resolved.

## The C# subset

- One top-level `class` (custom type) or `enum` per file.
- Properties carry exactly one `Neo*` attribute: `NeoBool`, `NeoInt`
  (`Min`/`Max`), `NeoFloat` (`+DecimalPoints`), `NeoString` (`Localizable`),
  `NeoDictionary`/`NeoList` (entry chains), `NeoObject` (custom type),
  `NeoEnum`, `NeoLookup` (`CollectionId`), `NeoGetter` (`Code`, `RetJson`),
  `NeoSprite`/`NeoAudio` (`TemplateId`), `NeoFunction`.
- The positional string argument is the stable record id. **Omit it to
  create**: `[NeoFloat(Min = 0)] public float? Weight { get; init; }` — push
  assigns the id and rewrites the file canonically.
- Property nullability IS the `required` flag: `int?` optional, `int`
  required. Property name is the schema key (`Key`/`Name` args override).
- Enum members: the member name is the option key (codegen symbol);
  `[NeoEnumEntry(Text = "...")]` carries the display/localized-text id.
- `ExtraJson` holds fields the projection doesn't express — edit it as JSON,
  never delete it casually.
- No method bodies, no initializers, no extra `using`s.

## Conflicts

Concurrent edits produce git-style `<<<<<<< local / ======= / >>>>>>> server`
markers at member granularity. **Edit the file to the desired final state and
push — the push IS the resolution** (it supersedes both sides). `neo resolve
--mine|--theirs` keeps one side wholesale. Markers break C# compilation on
purpose.

If push reports `base-hash-conflict`: run `neo pull` (merges or writes
markers), resolve, push again. Never bypass with `--force`-style flags.

If push reports `version-bump-required`: the server classified the change
(e.g. `requiredBump: major` for schema changes). Re-run with
`neo push --accept-bump` if the bump is intended.

## NeoScript

NeoScript is C#-flavored: typed declarations (`string x = ...;`, NOT `var`),
`this` = the containing type instance, `root.Assets` / `root.Save` /
`root.Session` roots. Validate before storing in a `NeoGetter` `Code` arg:

```
neo script check --this Outpost --returns string 'return $"{this.Name}!";'
neo script check --all              # recompile every stored getter (run after schema edits!)
neo script eval --returns string 'return root.Assets.Outposts[0].FullDisplayText;'
neo script apply --mode action '...'  # prints write intents; commits nothing
```

`eval` runs against authored values with the same evaluator the web UI uses.
`--json` everywhere for machine-readable output.

## Content (not in files)

Values, dialogue, and localized text are CLI verbs, not files:

```
neo records query [--kind <recordKind>]   # every record head in the version
neo records get <kind> <id>
neo values list [attributeId] / get <valueId> / set <valueId> '<json>'
neo loc locales / list / set <textId> <locale> "text"
```

Every `set` verb accepts a JSON-array batch on stdin or `--file` (e.g.
`[{"textId":"...","locale":"de-DE","value":"..."}]`) — use batches when
editing many records.

## Branches, merges, releases, migrations

```
neo branch list / create <name> [--from <ref>] / switch <nameOrId>
neo merge <branch> [--dry-run]     # field-level 3-way; conflicts reject with per-record+field payload
neo release cut [--bump ...] [--dry-run]   # bump floor DERIVED from transaction history; raise-only
neo migrate new <name> --target <Type>     # creates Migrations/NNNN-name.neo
neo migrate list / check / run [--dry-run] [--skip-invalid]
```

Branches are copy-on-write forks (cheap; edits isolated until merged).
Migrations are NeoScript actions in `.neo` files: `/// @migration <id>`,
`/// @target <Type|project>` headers, then the action body; `this` = each
instance of the target type. The v1 runner applies `this.<Field> = ...`
assignments as one atomic CAS transaction and marks `appliedAt` per version
(idempotent, per-branch). Sparse instances abort unless `--skip-invalid`.

## Setup (once per machine/repo)

```
npm i -g @neocompose/cli   # or run from the web repo: node cli/bin/neo.mjs
neo login [--api <url>] [--profile editor|release] [--save-project <id>]
neo init --project <id> [--version <id>] [--dir neo]
neo dev [--push]    # live: auto-pull on server change; 'p' push, 's' status
```

Agents run non-interactively: always pass explicit flags/ids. (In a human
terminal the same commands prompt with pickers and confirms; with no TTY the
CLI never blocks on a prompt — missing arguments raise an error that names
the flag to pass, and confirms fall back to the pre-prompt behavior.)

Tokens land in the macOS Keychain when available (0600 file elsewhere;
`NEO_COMPOSE_TOKEN` / `--token-stdin` for CI). `--save-project` adds a
narrow per-project save-read scope for `script eval --save`.

## Implementation notes agents should know

- The CLI talks to **Convex directly** with typed `api.*` calls; pushes go
  through the session-gated `commitFromSession` (same scope gates + CAS as
  the web). `--json` is available on status/diff/branch list/migrate
  list/script/records/values/loc.
- The working copy also projects `Templates/*.cs`, `Localization.cs`, and
  `Migrations/*.neo`. Migration pushes **pin compiled IR** on the record;
  `migrate run` replays the pinned IR (rename-proof). `migrate run --server`
  executes in the server runner; `neo merge --migrate` chains migrations at
  merge, and `release cut` refuses while any migration is pending.
- Pull short-circuits on the sync-signal head ("Already up to date") when
  nothing changed server-side and the copy is clean.

The editor profile cannot publish releases; that requires `--profile release`
at login (server-enforced scopes — ZERO TRUST).
