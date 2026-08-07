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

<!-- BEGIN:git -->

Use worktrees. Always finish by posting a PR. Attach code snippets of SDK API changes. Ensure `gh` is escalated outside sandbox.

<!-- END:git -->
