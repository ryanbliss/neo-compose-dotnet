<!-- BEGIN:code-style -->

NeoCompose is a Unity 6 C# package. Package source in `/src/NeoComposeUnity` (Runtime, Editor, and Tests folders), and a local-path-referenced sample in `/samples/HelloWorld`. There is no raw .NET library — the package ships scripts directly, no precompiled `.dll`.

<!-- END:project-info -->

<!-- BEGIN:code-style -->

- Avoid changes that reduce existing reuse across shared schemas, contracts, or utilities unless explicitly requested.
- When simplifying outputs or logic, prefer preserving shared abstractions and references over duplicating equivalent inline structures.

<!-- END:code-style -->

<!-- BEGIN:testing -->

- Test your changes.
- Fix root causes of failures, even if not yours.
- Tests live in — `src/NeoComposeUnity/Tests/` (package) and `samples/HelloWorld/Assets/Tests/` (sample). Both run from the sample's Unity Test Runner.
- `dotnet build` doesn't work due to Unity dependencies. Use Unity CLI to run tests `unity test` from `samples/HelloWorld`, or use MCP if Unity is open (e.g., after `unity open`).
- `unity help` for commands.

<!-- END:testing -->

<!-- BEGIN:agent-rigs -->

## Isolated agent rigs

Never edit this canonical checkout. Run `scripts/agent-setup` before editing
and work only in the neo-compose-dotnet worktree it reports; it is the same
rig as `npm run agent:setup -- --source neo-compose-dotnet` from the Neo
workspace. Every rig owns BOTH worktrees — `--source` picks the one you
implement in, and the neo-compose companion stays detached at main and
test-only. If doctor fails, rerun setup or report it; never copy credentials,
select another Convex deployment, or improvise an empty database.

Each rig owns its Convex deployment, seed, ports, browser context, and Neo CLI
credential namespace. Never target personal dev or production, never reuse
another rig's or your default browser/CLI state, and never run raw Convex
deploy/dev commands.

The sample consumes the rig through the gitignored `.neo-rig` pointer at the
worktree root (or `NEO_COMPOSE_RIG_MANIFEST`, which must agree with it): the
editor overlays the rig manifest onto a `DontSave` clone of the committed
`NeoComposeConfig` asset. Binding a rig must change no tracked config or
generated output — never patch the committed asset to reach a rig.

Headless rig entry points run in batchmode WITHOUT `-quit`; each exits the
editor itself and logs `end: success` or `end: failed`:

- `-executeMethod NeoCompose.Unity.Editor.NeoComposeBatchLogin.Run` — device
  authorization against the rig origin, logging the verification URL and user
  code under `[NeoComposeBatchLogin]` for external approval.
- `-executeMethod NeoCompose.Unity.Editor.NeoComposeBatchSync.Run` — headless
  synchronize, logging `[NeoComposeBatchSync]`.
- `scripts/agent-unity-smoke.sh` runs both serially against the rig app (start
  it with `npm run agent:dev` in the rig's neo-compose worktree) and fails when
  synchronizing dirtied tracked sample output.

<!-- END:agent-rigs -->

<!-- BEGIN:git -->

Use worktrees. Always finish by posting a PR. Attach code snippets of SDK API changes. Ensure `gh` is escalated outside sandbox.

<!-- END:git -->
