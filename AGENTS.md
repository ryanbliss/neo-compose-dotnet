<!-- BEGIN:code-style -->

NeoCompose is a Unity 6 C# package. Package source in `/src/NeoComposeUnity` (Runtime, Editor, and Tests folders), and a local-path-referenced sample in `/samples/HelloWorld`. There is no raw .NET library — the package ships scripts directly, no precompiled `.dll`.

<!-- END:project-info -->

<!-- BEGIN:code-style -->

- Avoid changes that reduce existing reuse across shared schemas, contracts, or utilities unless explicitly requested.
- When simplifying outputs or logic, prefer preserving shared abstractions and references over duplicating equivalent inline structures.

<!-- END:code-style -->

<!-- BEGIN:testing -->

- Always test your changes to verify no regressions.
- There should not be any pre-existing failures, so don't blame anybody else.
- Fix root causes of failures.
- Tests live in two places — `src/NeoComposeUnity/Tests/` (package's own Unity Test Framework tests) and `samples/HelloWorld/Assets/Tests/` (downstream-consumer demonstration). Both run from the sample's Unity Test Runner.
- `dotnet build` doesn't work due to Unity dependencies. Use Unity MCP to test SDK + sample builds.

<!-- END:testing -->

<!-- BEGIN:git -->

Use worktrees. Always finish by posting a PR. Attach code snippets of SDK API changes. Ensure `gh` is escalated outside sandbox.

<!-- END:git -->
