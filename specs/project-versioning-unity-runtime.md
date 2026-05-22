# Project Versioning Unity Runtime

## Status

Draft for review. This spec describes the Unity package changes needed to use
Neo Compose project versions from the editor panel and runtime-facing config
without adding runtime remote patch downloads yet.

## Depends on

- [neo-editor-panel.md](./neo-editor-panel.md)
- [neo-compose-sdk.md](./neo-compose-sdk.md)
- Web spec: `../../Web/neo-compose/specs/project-versioning.md`

## Owns

- Unity-side project version selection metadata.
- Editor release channel and version pickers.
- Manual synchronization against a selected project version.
- Version-aware Unity export settings updates.
- Runtime-readable config fields needed by future runtime update checks.
- User-facing warnings when the pinned version drifts from the selected release
  channel or is archived/deprecated.

## Non-goals

- No runtime remote downloading or hot patching of `project.json`,
  generated C#, asset files, or binary assets.
- No automatic synchronization in play mode or build output.
- No new aggregate web endpoint for Unity editor metadata.
- No Unity-side implementation of project version lifecycle actions such as
  creating, publishing, archiving, restoring, or changing version status.
- No Unity-side code generation. The web app remains the source of generated
  `NeoGeneratedTypes.cs`.

## Current Architecture Summary

The Unity package stores editor and future runtime settings in
`NeoComposeConfig`, a runtime `ScriptableObject` discovered or created by the
editor at:

```text
Assets/Resources/Neo/NeoComposeConfig.asset
```

The editor window currently lets the developer:

- Set the API base URL.
- Search and select a project.
- Save Unity export settings back to the web app.
- Synchronize `project.json`, `NeoGeneratedTypes.cs`, and referenced project
  files from the web export endpoint.

The current API client calls unversioned routes:

```text
POST /api/projects
POST /api/projects/{projectId}/edit
POST /api/projects/{projectId}/export
POST /api/projects/{projectId}/export/files
```

The web app has already introduced version-aware records and routes. The Unity
editor should select a project version explicitly and include that version id
when it reads or writes versioned project data.

## Goals

- Store the selected release channel and pinned project version in
  `NeoComposeConfig.asset`.
- Display version semver labels in the editor UI while persisting project
  version ids.
- Default a newly selected project to the `Development` release channel and
  the highest-semver version exposed by that channel.
- Let the developer choose a target release channel by id.
- Let the developer choose a pinned version from the selected release channel.
- Preserve a pinned version even if it later drifts out of the selected release
  channel.
- Show clear warnings when the pinned version is no longer exposed by the
  selected release channel, is archived, or belongs to a deprecated status.
- Detect when the selected release channel has a higher-semver version than the
  pinned version.
- Offer an `Update to latest version` action that updates local config and then
  prompts the developer to synchronize.
- Synchronize generated files manually through the editor panel using the
  pinned version id.
- Save Unity export settings through the versioned project edit route.
- Disable settings-save controls when the pinned version status is not
  writable.

## Terms

**Target release channel**

The release channel id stored in Unity config. It represents the channel the
developer wants this Unity project to track, such as `development`, `staging`,
or `production`.

**Pinned version**

The project version id stored in Unity config. Synchronization uses this exact
version, even if the release channel later exposes a newer version.

**Latest channel version**

The highest semantic version among non-archived versions currently exposed by
the target release channel.

**Drift**

A state where the pinned version is not the latest version for the selected
release channel, or no longer belongs to the selected release channel at all.
Drift is allowed because developers may intentionally pin an older version.

## Unity Configuration

Update `NeoComposeConfig` with version-selection fields:

```csharp
public string targetReleaseChannelId = "";
public string versionId = "";
```

`versionId` stores the project version id, not the semver label. The editor
panel must display the selected version by semver label when the version
metadata is available.

`HasProject` should continue to mean `projectId` is populated. Version-aware
actions should additionally validate:

- `targetReleaseChannelId` is not empty.
- `versionId` is not empty.
- `versionId` resolves to a project version for the selected project.

Selecting a different project should clear stale version metadata before loading
that project's release channels and versions.

## Web API Usage

The Unity editor should keep using separate web API calls rather than adding a
Unity-specific aggregate endpoint.

### Project Search

Project search remains:

```text
POST /api/projects
POST /api/projects?query={query}
```

### Release Channels

After selecting or loading a project, fetch release channels:

```text
POST /api/projects/{projectId}/release-channels
```

Expected response:

```ts
{
  channels: IProjectReleaseChannel[];
}
```

### Versions

Fetch versions as needed with:

```text
POST /api/projects/{projectId}/versions
```

Expected response:

```ts
{
  versions: IProjectVersion[];
}
```

The editor needs version statuses to derive each version's release channel
membership:

```text
POST /api/projects/{projectId}/version-statuses
```

Expected response:

```ts
{
  statuses: IProjectVersionStatus[];
}
```

The editor should also load version/status metadata for the current pinned
version when it needs authoritative details or when the pinned version is not in
the normal dropdown set:

```text
POST /api/projects/{projectId}/versions/{versionId}
```

Expected response:

```ts
{
  version: IProjectVersion;
  versionStatus: IProjectVersionStatus;
  versionStatuses: IProjectVersionStatus[];
  releaseChannels: IProjectReleaseChannel[];
}
```

### Export

Synchronization should call the existing export route and include the pinned
version id:

```text
POST /api/projects/{projectId}/export
```

Request body:

```json
{
  "versionId": "project-version-id"
}
```

The editor must not call the export route with an empty body once version
selection is required.

The response already contains version metadata in addition to generated output.
Unity models should deserialize the fields it needs and tolerate additional
fields:

```ts
{
  projectId: string;
  projectName: string;
  projectJson: string;
  generatedTypes: string;
  diagnostics: IUnityCodegenDiagnostic[];
  version: IProjectVersion;
  versionStatus: IProjectVersionStatus;
  releaseChannels: IProjectReleaseChannel[];
  projectDocumentContentHash: string;
  codegenContractHash: string;
  runtimeDataContractHash: string;
}
```

After a successful sync, Unity should update local config from the response:

- `projectId`
- `projectName`
- `versionId` from `response.version.id`
- `namespaceForGeneratedTypes` from the exported project JSON.
- `singleton` from the exported project JSON.

The sync should not change `targetReleaseChannelId` unless the current value is
empty and the response exposes exactly one reasonable default. Normal channel
selection belongs to the editor version picker.

### Export Settings Write

Saving Unity export settings should switch to the versioned project edit route:

```text
POST /api/projects/{projectId}/versions/{versionId}/edit
```

Request body:

```json
{
  "exportSettings": {
    "unity": {
      "namespaceForGeneratedTypes": "Assets.Scripts.Neo",
      "singleton": true
    }
  }
}
```

The editor should disable the `Save to web` button when the pinned version's
status is not writable. The server remains authoritative and may still reject
the write if version metadata changed after the editor loaded it.

## Editor Data Loading

When a project is selected or the editor window opens with an existing selected
project:

1. Load release channels.
2. Load versions.
3. Load version statuses.
4. Load version/status metadata for the pinned version if config has one and
   the local channel/status data is insufficient to explain it.
5. If config has no `targetReleaseChannelId`, choose the `Development` channel
   by id when available; otherwise choose the first channel sorted by
   `sortOrder`, then name.
6. If config has no pinned version, choose the latest version exposed by the
   selected channel.
7. Save config only after a valid default channel/version is resolved.

The default channel for a newly selected project is `Development`. The default
version is the highest-semver non-archived version currently exposed through
that channel.

If no version is exposed by the selected channel, the editor should leave
`versionId` empty and show a warning. Synchronization and settings-save actions
remain disabled until a valid version is selected.

## Channel And Version Selection UI

Add a version-selection section to the selected-project editor panel.

The section should show:

- Release channel dropdown.
- Version dropdown.
- The selected version semver label.
- Status name for the selected version.
- Update availability warning and action when applicable.
- Drift/deprecated/archive warnings when applicable.

### Release Channel Dropdown

The release channel dropdown stores `channel.id` in
`NeoComposeConfig.targetReleaseChannelId`.

Changing the release channel should:

1. Update `targetReleaseChannelId`.
2. Rebuild the version dropdown from versions exposed by the selected channel.
3. If the current pinned version is exposed by the new channel, keep it.
4. Otherwise select the latest version in the new channel.
5. Save config.

If the developer intentionally needs to keep a version that is not exposed by
the selected channel, that state can occur after web-side status/channel changes.
The UI should preserve it on reload and warn instead of silently changing it.

### Version Dropdown

The version dropdown should display semver labels, sorted highest semver first.
It should persist the selected version id in `NeoComposeConfig.versionId`.

Normally, the dropdown includes only versions that:

- Belong to the selected project.
- Are exposed by the selected release channel.
- Are not archived.
- Are not in a deprecated status.

Exception: if the currently pinned version is archived, deprecated, or no longer
exposed by the selected release channel, include it as the selected item so the
developer can see and change it. Add a warning explaining why the pinned version
is unusual.

Deprecated means the selected version's status has no release channels or is the
project's deprecated status record. Since status names are project-owned, the
editor should prefer behavior over name where possible. If status metadata only
indicates that the version is exposed by no channels, treat it as not channel
targeted and warn. If the status name is exactly `Deprecated`, also warn.

### Update Availability

For the selected release channel, compute latest by highest semver among
eligible versions currently exposed by that channel.

If:

- a pinned version is selected,
- the pinned version has a semver label,
- the selected channel has a latest version, and
- latest semver is greater than pinned semver,

show:

```text
A newer update is available.
```

Also show an `Update to latest version` button.

Clicking the button should:

1. Set `NeoComposeConfig.versionId` to the latest version id.
2. Save `NeoComposeConfig.asset`.
3. Prompt the developer with an editor alert asking whether to synchronize now.
4. If the developer confirms, run the normal manual synchronization flow.

The update button must not synchronize without confirmation.

### Drift Warning

If the pinned version is not exposed by the selected release channel, show a
warning that:

- states the pinned version is not in the selected channel,
- lists the release channels the pinned version currently targets, and
- invites the developer to switch release channels or choose another version.

When the pinned version targets no channels, say that it is not exposed by any
release channel.

### Archived And Deprecated Warnings

If the pinned version has `archivedAt`, show a warning that the selected version
is archived.

If the pinned version appears deprecated, show a warning that the selected
version is deprecated or no longer channel-targeted.

Warnings do not automatically change config. The developer owns the pin.

## Synchronization Flow

Manual synchronization remains an editor action.

When the user clicks `Synchronize`:

1. Validate API base URL, project id, target release channel id, version id, and
   output asset directories.
2. Call:

   ```text
   POST {apiBaseUrl}/api/projects/{projectId}/export
   ```

   with:

   ```json
   {
     "versionId": "config.versionId"
   }
   ```

3. Show generated-code diagnostics as today.
4. Confirm replacement of existing generated files as today.
5. Write `NeoGeneratedTypes.cs` and `project.json`.
6. Preserve existing editor-side project file synchronization behavior for
   manual sync.
7. Save config with response metadata.
8. Refresh Unity assets.

This spec does not add runtime patch downloads. Existing manual editor sync may
continue downloading project files because that is part of explicit developer
synchronization, not runtime patching.

## Runtime Behavior

Runtime code can read `NeoComposeConfig.asset` from `Resources` in the future to
know:

- API base URL.
- Project id.
- Target release channel id.
- Pinned project version id.

For this spec, runtime loading should continue using the local synchronized
`project.json`. It should not call the web API, check for updates, download
new runtime data, or replace assets.

The version metadata exists now so later runtime update work can compare:

- selected release channel,
- pinned version id,
- project document content hash,
- codegen contract hash, and
- runtime data contract hash.

Later runtime update work can decide whether a remote `project.json` is safe to
download without regenerated code. That decision is out of scope here.

## Error Handling

Editor API errors should be shown as focused status text and logged to the
Unity Console with the underlying exception.

Expected user-facing failures:

- Project has no release channels.
- Selected release channel has no eligible versions.
- Pinned version no longer exists.
- Pinned version exists but has a missing status.
- Export route rejects the version id.
- Versioned edit route rejects the write because the version is read-only.
- API base URL is invalid or unreachable.

The editor should avoid silently clearing version fields when remote metadata is
temporarily unavailable. Temporary API failures should leave config untouched.

## Serialization Compatibility

The versioned web APIs strip Mongo storage metadata from most records before
returning them to clients. In particular, `_id` has been removed from many
versioned records, likely everything except the project record.

Unity runtime and editor DTOs should treat Mongo `_id` as optional unless the
specific API contract guarantees it. If existing generated/runtime models
declare `_id` as required for attributes, values, types, enums, dialogues,
files, import templates, release channels, statuses, or versions, update the
serialization layer before consuming versioned payloads.

The Unity package should prefer stable application ids (`id`) for lookups,
selection, config persistence, and asset database associations. It should not
use Mongo `_id` to identify release channels, versions, records, or project
files.

## Implementation Notes

### API Models

Add Unity editor DTOs for:

- `NeoComposeProjectVersion`
- `NeoComposeProjectVersionSemver`
- `NeoComposeProjectVersionStatus`
- `NeoComposeProjectReleaseChannel`
- release-channel list response
- version list response
- version-status list response
- version metadata response

Date fields can be strings for editor display and null checks. Unity does not
need to parse them unless sorting or comparison requires it. Semver comparison
should use numeric `major`, `minor`, and `patch` fields from the API response.

### API Client

Add methods:

```csharp
Task<NeoComposeProjectReleaseChannelListResponse> ListReleaseChannelsAsync(
    string apiBaseUrl,
    string projectId);

Task<NeoComposeProjectVersionListResponse> ListVersionsAsync(
    string apiBaseUrl,
    string projectId);

Task<NeoComposeProjectVersionStatusListResponse> ListVersionStatusesAsync(
    string apiBaseUrl,
    string projectId);

Task<NeoComposeProjectVersionMetadataResponse> GetVersionMetadataAsync(
    string apiBaseUrl,
    string projectId,
    string versionId);
```

Update existing methods:

```csharp
Task<NeoComposeProjectEditResponse> UpdateProjectExportSettingsAsync(
    string apiBaseUrl,
    string projectId,
    string versionId,
    string namespaceForGeneratedTypes,
    bool singleton);

Task<NeoComposeUnityExportResponse> ExportProjectAsync(
    string apiBaseUrl,
    string projectId,
    string versionId);
```

`ExportProjectAsync` should serialize `{ versionId }`.

### Editor State

The editor window should keep loaded channel/version/status lists in memory and
refresh them when:

- the window opens,
- the API base URL changes and the project is selected,
- the selected project changes,
- the user clicks a refresh action, or
- synchronization succeeds and response metadata may have changed.

### Tests

Add or update Unity edit-mode tests for pure helpers where possible:

- Semver sorting selects highest semver.
- Development channel default selection.
- Version filtering by selected release channel.
- Pinned drift preserves current version and reports targeted channels.
- Archived/deprecated pinned versions remain visible as the selected item.
- Update-to-latest changes only the local version id before sync confirmation.
- Synchronizer sends the selected version id in the export request model.
- Project settings updater sends the selected version id to the versioned edit
  route.
- Validation fails when project id, release channel id, or version id is empty.

Run both package and sample Unity tests from the sample Unity Test Runner after
implementation.

## Future Considerations

- Whether a future runtime updater should track exact release channel id or
  channel slug for cross-environment stability. For now, Unity stores the id
  because web release channel ids are the API contract.
