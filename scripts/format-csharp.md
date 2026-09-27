# C# formatting

Use the .NET SDK version pinned in `global.json`. Formatting runs without
Unity, package restore, or generated `.sln`/`.csproj` files.

```sh
./scripts/format-csharp.sh                     # Fix formatting
./scripts/format-csharp.sh --verify-no-changes # Check without changing files
```

The same check runs on pull requests and pushes to `main`. `.editorconfig`
sets four-space indentation, braces on new lines, and multiline blocks and
statements. Configure your editor to format on save using these settings.

The script lists owned source directories explicitly. Generated Neo bindings
are excluded; third-party assets and Unity caches are outside those directories.
Add new owned source roots to the script when introducing them. Formatting
changes whitespace only, leaving code behavior and literal contents intact.
