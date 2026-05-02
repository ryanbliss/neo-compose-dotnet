# Agent Policy

NeoCompose is a Unity 6 C# package. Package source in `/src/NeoComposeUnity` (Runtime, Editor, and Tests folders), and a local-path-referenced sample in `/samples/HelloWorld`. There is no raw .NET library — the package ships scripts directly, no precompiled `.dll`.

## Reuse Preference

- Avoid changes that reduce existing reuse across shared schemas, contracts, or utilities unless explicitly requested.
- When simplifying outputs or logic, prefer preserving shared abstractions and references over duplicating equivalent inline structures.

## Testing policy

- Always test your changes to verify no regressions.
- There should not be any pre-existing failures, so don't blame anybody else.
- Fix root causes of failures. Do not cheat on tests.
- Tests live in two places — `src/NeoComposeUnity/Tests/` (package's own Unity Test Framework tests) and `samples/HelloWorld/Assets/Tests/` (downstream-consumer demonstration). Both run from the sample's Unity Test Runner.
