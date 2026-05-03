# Neo Editor Panel

## User spec

The SDK's [Editor](../src/NeoComposeUnity/Editor) package should expose a new window that:

1. Searches projects using the `/api/projects` projects api (in `neo-compose`, which exposess next.js rest endpoints). Package should have an env variable for API base path, currently set to `localhost:3000`.
2. Once project is selected, cache the `projectId` in a neo configuration file in the project. Dev should be able to move the file around their project, but ideally it would default to the same `Resources` directory as `project.json` since future runtime scenarios may need it.
3. If project is already selected, hide the project search in panel, show the title of the project, and allow them to remove it.
4. Show a "Synchronize" button, which would pull the project's `project.json` file and `NeoGeneratedTypes.cs`. Have the default path be `Assets/Scripts/Neo` for types and `Assets/Resources/Neo` for the json (create dir(s) if does not exist on click), but allow them to override the directories (path would get written to neo config file). You may need to make a new API endpoint to pull both the `project.json` and `NeoGeneratedTypes.cs` file strings (e.g., `/project/[projectId]/export`).
5. If assets already exist at that directory path when they click "Synchronize", replace the existing files (with a warning confirmation dialog).

## Full spec

### Goals

- Add a Unity Editor window to the SDK package that can discover Neo Compose projects from the local web app and synchronize the selected project into the Unity project.
- Keep the web app as the source of generated output. Unity should download `project.json` and `NeoGeneratedTypes.cs`; it should not duplicate the TypeScript code generator.
- Store the Unity-side synchronization settings in a project asset that can be tracked in git and moved by the developer.
- Make the default output paths work for a normal Unity project:
  - `Assets/Scripts/Neo/NeoGeneratedTypes.cs`
  - `Assets/Resources/Neo/project.json`
  - `Assets/Resources/Neo/NeoComposeConfig.asset`
- Allow both output directories to be overridden and persisted in config.

### Non-goals

- Authentication, multi-user sessions, or cloud-hosted project discovery.
- Generating C# inside Unity.
- Automatically persisting runtime save files.
- Deleting synchronized files when a project is unlinked.
- Handling package distribution beyond the local-path package workflow already used by the sample.

### Existing architecture to preserve

The SDK package already has an empty Editor assembly:

```text
src/NeoComposeUnity/Editor/NeoCompose.Unity.Editor.asmdef
```

The runtime assembly already deserializes and loads the exported JSON:

- `Runtime/Json/ProjectData.cs`
- `Runtime/NeoLoader.cs`
- `Runtime/NeoClient.cs`

The web app already owns Unity export generation:

- `toUnityExport(...)` builds `IProjectUnityExport`.
- `generateUnityTypes(...)` builds `NeoGeneratedTypes.cs`.
- `ProjectPageContainer.tsx` already exposes generated C# and project JSON for browser download.
- Existing API routes use `POST` under `/api/projects/...`, so the editor integration should follow that convention.

### Web API contract

Add a new web endpoint:

```text
POST /api/projects/[projectId]/export
```

Response shape:

```ts
export interface IProjectUnityEditorExportResponse {
  projectId: string;
  projectName: string;
  projectJson: string;
  generatedTypes: string;
  diagnostics: IUnityCodegenDiagnostic[];
}
```

The endpoint should:

- Load the project, attributes, types, values, and enums using the same database helpers used by the project layout.
- Build the same `IProjectUnityExport` shape as `toUnityExport(...)`. Prefer extracting a shared plain-data export helper so the endpoint does not need browser/view-model state.
- Pretty-print `projectJson` with four-space indentation.
- Call `generateUnityTypes(...)` and return its `code` as `generatedTypes`.
- Return diagnostics exactly enough for Unity to render severity, path, and message.
- Return `404` when the project does not exist.
- Return `500` for unexpected export failures, with a focused error message.

The existing project list endpoint may accept an optional query parameter:

```text
POST /api/projects?query=hello
```

The server may filter by project name when `query` is provided, but the Unity client should only send `query` when the search text length is greater than one. For the first version, fetching all projects and filtering locally is acceptable if it keeps the endpoint smaller.

### Unity configuration

Create a Unity `ScriptableObject` config asset, defaulting to:

```text
Assets/Resources/Neo/NeoComposeConfig.asset
```

Because this asset lives under `Resources` and may be needed in future runtime scenarios, the config `ScriptableObject` type must live in the runtime assembly rather than the editor-only assembly. Editor-only helpers that discover, create, or mutate the asset still live in the Editor assembly.

Suggested class:

```csharp
namespace NeoCompose.Runtime
{
    public sealed class NeoComposeConfig : ScriptableObject
    {
        public string apiBaseUrl = NeoComposeDefaults.ApiBaseUrl;
        public string projectId = "";
        public string projectName = "";
        public string generatedTypesDirectory = "Assets/Scripts/Neo";
        public string projectJsonDirectory = "Assets/Resources/Neo";
    }
}
```

The API base URL should be a tracked code default, not a secret. This compiled value seeds newly-created config assets:

```csharp
public static class NeoComposeDefaults
{
    public const string ApiBaseUrl = "http://localhost:3000";
}
```

After the config asset exists, `NeoComposeConfig.apiBaseUrl` is the source of truth for editor requests. Developers can override it by selecting the ScriptableObject in the Unity Inspector, editing the field in the Neo Compose editor window, or committing a changed config asset to git. The compiled default should not overwrite an existing config asset's value during normal discovery.

Config discovery should:

1. Search the asset database for `NeoComposeConfig`.
2. Use the first matching asset when exactly one exists.
3. If none exists, create the default folder and asset.
4. If multiple exist, let the user choose or show a focused warning and use the first deterministic path sorted alphabetically.

Moving the asset should continue to work because discovery is asset-type based rather than path-only.

### Editor window

Add a menu item:

```text
Tools/Neo Compose
```

Opening it shows a `NeoComposeEditorWindow`.

The first version can use IMGUI for speed and package simplicity. UI Toolkit is acceptable later, but this spec does not require it.

The window has two states.

#### No Project Selected

Show:

- API base URL text field.
- Project search box.
- Project list results.
- Refresh/Search button.
- Loading and error states.

Search behavior:

- If search text length is `0` or `1`, do not send a query parameter.
- If search text length is greater than `1`, send `query`.
- Results are project id + project name.
- Selecting a project writes `projectId` and `projectName` to config and saves the asset.

The editor should use Unity editor-safe HTTP APIs. `UnityWebRequest` is preferred because the package already targets Unity and the sample includes the UnityWebRequest module.

#### Project Selected

Hide project search. Show:

- Selected project title.
- Project id in small/read-only text.
- API base URL field.
- Generated types directory field.
- Project JSON directory field.
- Browse/select folder buttons for both directories when practical.
- Remove/Unlink project button.
- Synchronize button.
- Last sync status.

Unlinking a project only clears `projectId` and `projectName` in config. It must not delete `project.json` or `NeoGeneratedTypes.cs`.

### Synchronization flow

When the user clicks `Synchronize`:

1. Validate config:
   - API base URL is not empty.
   - `projectId` is not empty.
   - both output directories are under `Assets/`.
2. Call:

   ```text
   POST {apiBaseUrl}/api/projects/{projectId}/export
   ```

3. If diagnostics include errors, render a confirmation dialog listing those errors and ask whether to continue.
   - If the user cancels, do not write files.
   - If the user continues, write files.
4. If either output file already exists, show a replacement confirmation dialog.
   - If the user cancels, do not write files.
   - If the user continues, replace existing files.
5. Create output directories if needed.
6. Write:

   ```text
   {generatedTypesDirectory}/NeoGeneratedTypes.cs
   {projectJsonDirectory}/project.json
   ```

7. Call `AssetDatabase.ImportAsset(...)` or `AssetDatabase.Refresh()` so Unity compiles the new C# file.
8. Save the config asset.
9. Show success or focused failure state in the window.

Diagnostics dialog should include at least:

- Severity.
- Optional path.
- Message.

If many diagnostics exist, show the first group in the dialog and write the complete set to the Unity Console.

### File and path rules

- Directory fields are Unity project-relative paths beginning with `Assets/`.
- Absolute paths are not accepted for synchronized files.
- File names are fixed:
  - `NeoGeneratedTypes.cs`
  - `project.json`
- Paths are stored in the config asset without trailing slash normalization noise.
- The synchronizer may normalize separators to `/`.

The default `project.json` location is under `Assets/Resources/Neo` so runtime sample code can load it through Unity resources if desired. The generated C# default stays under `Assets/Scripts/Neo` so the namespace `Assets.Scripts.Neo` remains sensible.

### Error handling

The editor should show user-facing errors for:

- Web app not reachable.
- Invalid API base URL.
- Project not found.
- Export endpoint returns non-2xx.
- Export response is malformed.
- Output path outside `Assets/`.
- File write failure.
- Unity asset refresh/import failure.

Errors should also be logged to the Unity Console with enough detail for debugging.

### Suggested files

SDK package:

```text
src/NeoComposeUnity/Runtime/
  NeoComposeConfig.cs
  NeoComposeDefaults.cs

src/NeoComposeUnity/Editor/
  NeoComposeConfigProvider.cs
  NeoComposeEditorApiClient.cs
  NeoComposeEditorWindow.cs
  NeoComposeSynchronizer.cs
```

Web app:

```text
src/app/api/projects/[projectId]/export/route.ts
src/models/exports/project-unity-editor-export-response.ts
```

If the export endpoint needs shared server-side loading, add a helper rather than duplicating the project layout's data fetch shape.

### Testing

Web tests should cover:

- Export endpoint response contains `projectJson`, `generatedTypes`, `diagnostics`, `projectId`, and `projectName`.
- `projectJson` matches `toUnityExport(...)` pretty-printed.
- `generatedTypes` matches `generateUnityTypes(...)`.
- Missing project returns a non-success response.
- Optional project list query behavior, if filtering is implemented server-side.

Unity tests should cover editor logic where possible without depending on a live web server:

- Config provider creates the default config when none exists.
- Config provider can find a moved config asset by type.
- Path validation accepts `Assets/Scripts/Neo` and `Assets/Resources/Neo`.
- Config provider creates the default config at `Assets/Resources/Neo/NeoComposeConfig.asset`.
- Path validation rejects absolute paths and paths outside `Assets/`.
- Synchronizer writes both files to configured directories.
- Existing files require overwrite confirmation before replacement.
- Diagnostics with errors require an explicit continue decision.
- Unlinking a project clears config project fields without deleting synchronized files.

HTTP behavior can be tested through an injectable API client interface so the synchronizer tests can use a fake response. Manual/editor integration can verify the real `UnityWebRequest` client against `localhost:3000`.

Verification policy:

- Web changes finish with `npm run doctor` in `/Users/ryanbliss/Documents/Development-Personal/Web/neo-compose`.
- SDK/editor changes finish with Unity Test Runner coverage from the sample project, including both package tests and sample tests.

### Implementation phases

1. **Web export endpoint**
   - Add response type.
   - Add `POST /api/projects/[projectId]/export`.
   - Reuse existing export/codegen functions.
   - Add focused endpoint tests or helper tests.

2. **Unity config foundation**
   - Add defaults, config asset type, and config discovery/creation.
   - Add path validation helpers.
   - Add edit-mode tests for config and path behavior.

3. **Unity API client**
   - Add project list and project export request models.
   - Add `UnityWebRequest` implementation.
   - Keep an injectable interface for fake client tests.

4. **Synchronizer**
   - Validate config.
   - Fetch export.
   - Gate diagnostics and overwrite prompts through injectable confirmation callbacks.
   - Write files and refresh the asset database.
   - Add edit-mode tests for write and prompt behavior.

5. **Editor window**
   - Add `Tools/Neo Compose` menu item.
   - Render unselected and selected states.
   - Wire search, select, unlink, path edits, and synchronize.
   - Show loading, success, and error states.

6. **Manual integration**
   - Run the web app locally.
   - Open the Unity sample.
   - Select a project.
   - Synchronize into the default directories.
   - Confirm Unity imports generated types and `project.json`.

### Risks and notes

- The export endpoint should not create a second codegen path. It must call the same generator used by the project page.
- Unity Editor code should stay in the Editor assembly so none of the HTTP/config UI surface ships into runtime assemblies. The config data type is the exception: it should live in the runtime assembly because the config asset is under `Resources`.
- A config asset is intentionally tracked in git; the API base URL is not sensitive.
- The default `project.json` path under `Resources` is convenient, but teams can move it if they prefer direct file IO or Addressables later.
- Diagnostics are not always fatal. The Unity panel should make errors visible and require explicit user consent before writing questionable output.
