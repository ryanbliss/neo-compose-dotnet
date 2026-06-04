# Authentication Unity editor implementation tasks

## Status

Draft task ledger for implementing the "Full spec (code gen, Unity export, C#
runtime)" section of the web repo spec
`specs/authentication-projects-spec.md` (Neo Compose web repository). That
section owns the Unity Editor extension's OAuth device-authorization flow,
SDK-side token storage, bearer-token wiring for Unity export / project-list /
Unity-settings-edit calls, and a storage-only stub for the project-scoped
runtime API key.

Active chunk: Phases 1, 2, 4, 5 complete and verified. Phase 3's session
controller (`NeoComposeEditorAuthController`) is implemented and tested (state
transitions, sign-in, 401→expired+clear, 403→stay-signed-in). Remaining: wire the
controller into `NeoComposeEditorWindow` IMGUI (the auth cell + control gating,
UAUTH-023–027, 029), then Phase 6 disconnect/revoke, Phase 7 runtime-key stub,
Phase 8 verification. The editor still calls the web API with no `Authorization`
header until the window wiring lands.

Scope reminders from the spec:

- Runtime sync is out of scope. The shipped runtime (`NeoClient`/`NeoLoader`)
  stays offline and bundled-JSON driven. This pass only reserves storage for the
  project-scoped runtime API key.
- "Code gen" in the spec title refers to web-side Convex codegen and is not part
  of this repo's work. No generated C# carries tokens, secrets, or auth plumbing.
- The `neo-compose-unity` device client does not issue refresh tokens. Expiry
  forces re-authentication.
- The device token is a user-account token (covers every project the user can
  access), not a project-scoped token. The web backend re-validates project
  policies on every call.

Use this file as the source of implementation status. Mark a task complete only
after its implementation and relevant tests/verification are done. If a task is
split during implementation, add child tasks under the original task instead of
reusing the task id for a different scope.

## Status report format

When reporting progress, summarize:

- Current phase and chunk.
- Completed task ids since the last report.
- Blocked task ids, with the blocking decision or failing test.
- Next recommended task ids.
- Verification run, including Unity Test Runner coverage.

Example:

```txt
Phase 2: Device-authorization flow client
Completed: UAUTH-014, UAUTH-015, UAUTH-016
Blocked: UAUTH-018 needs a fake device-token transport approved
Next: UAUTH-017, UAUTH-019
Verification: src/NeoComposeUnity/Tests device-flow tests passed in HelloWorld Unity Test Runner
```

## Phase 1: SDK-side token storage

Goal: add a secure, OS-native token store abstraction and stored-token model so
the credential never lands under `Assets/`, in a committed `ScriptableObject`,
or in plaintext `EditorPrefs`. Editor-only; never referenced from the runtime
assembly.

### Token model and abstraction

- [x] UAUTH-001 Add `NeoComposeStoredToken` carrying access token, absolute expiry, granted scopes, issuing auth base URL, and signed-in display identity (name/email).
- [x] UAUTH-002 Add `INeoComposeTokenStore` with `Load()`, `Save(token)`, and `Clear()`.
- [x] UAUTH-003 Add a non-secret UI hint store (display name + expiry only) usable without unlocking the secret store, persisted outside `Assets/`.

### OS-native storage implementations

- [x] UAUTH-004 Add macOS Keychain implementation, keyed per auth base URL.
- [x] UAUTH-005 Add Windows Credential Manager implementation, keyed per auth base URL.
- [x] UAUTH-006 Add Linux Secret Service/libsecret implementation, keyed per auth base URL.
- [x] UAUTH-007 Select the platform implementation at runtime behind `INeoComposeTokenStore`.
- [x] UAUTH-008 Add restricted-permission per-user file fallback outside the project tree when no native store is available, with a "secure storage unavailable" warning and no in-project/committed downgrade.
- [x] UAUTH-009 Ensure `Clear()` removes every artifact, including the non-secret hints.

### Phase 1 verification

- [x] UAUTH-010 Add tests that the store round-trips and clears tokens through a fake `INeoComposeTokenStore`.
- [x] UAUTH-011 Add tests asserting the default store never writes under `Assets/` or to plaintext `EditorPrefs`.
- [x] UAUTH-012 Run focused Unity token-store tests from the HelloWorld Unity Test Runner.

## Phase 2: Device-authorization flow client

Goal: consume the `neo-compose-unity` OAuth 2.0 Device Authorization Grant
exposed by the web app's Better Auth handler, resolving all endpoints from
`NeoComposeConfig.apiBaseUrl` (single origin).

### Device flow

- [x] UAUTH-013 Add a device-authorization client that requests a device code with `client_id=neo-compose-unity` and the registered scopes (`openid`, `profile:read`, `project:list`, `project:read`, `unity:export`, `unity:settings:write`).
- [x] UAUTH-014 Derive device-code, device-token, and verification endpoints from `NeoComposeConfig.apiBaseUrl` so one value retargets localhost/production.
- [x] UAUTH-015 Display the `user_code` as copyable text and open the verification URI (`/auth/device`) via `Application.OpenURL`.
- [x] UAUTH-016 Poll the device-token endpoint at the server `interval`, honoring `authorization_pending`, `slow_down`, `access_denied`, and `expired_token` (RFC 8628).
- [x] UAUTH-017 Enforce a hard overall timeout matching the device code `expires_in` with an actionable failure message.
- [x] UAUTH-018 Make polling cancelable (window close, user cancel, expiry) and never poll faster than the server `interval`.
- [x] UAUTH-019 On success, capture the bearer token, expiry, granted scopes, and `profile:read` identity and persist through the token store.

### Phase 2 verification

- [x] UAUTH-020 Add tests for the poller honoring `authorization_pending`, `slow_down`, `access_denied`, `expired_token`, and overall timeout using a fake clock and fake transport.
- [x] UAUTH-021 Add tests that a successful flow persists token, expiry, scopes, and identity through a fake token store.
- [x] UAUTH-022 Run focused Unity device-flow tests from the HelloWorld Unity Test Runner.

## Phase 3: Editor auth cell and feature gating

Goal: add a sign-in cell at the top of `NeoComposeEditorWindow` and gate
auth-sensitive controls on token validity, without modal prompts or
self-launched browsers.

### Auth cell UX

- [ ] UAUTH-023 Render an auth cell above project search/actions in `NeoComposeEditorWindow`.
- [ ] UAUTH-024 Signed-out state: show "Sign in to Neo Compose" call to action with a short explanation; do not steal focus or open a browser automatically.
- [ ] UAUTH-025 Signed-in state: show identity (name/email hint) and a "Disconnect" action.
- [ ] UAUTH-026 Expired state: show "Session expired, sign in again" with the same gating as signed-out, preserving the selected project/version config.
- [ ] UAUTH-027 Disable/grey project search, version metadata, export/sync, and settings-edit controls while signed out or expired.

### Phase 3 verification

- [x] UAUTH-028 Add editor tests for cell state transitions (signed-out, signed-in, expired) and control gating.
- [ ] UAUTH-029 Run focused Unity editor window tests from the HelloWorld Unity Test Runner.

## Phase 4: Bearer-token wiring into the API client

Goal: attach the bearer token to every authenticated request in
`NeoComposeEditorApiClient` and remove the unauthenticated path.

### Request authorization

- [x] UAUTH-030 Attach `Authorization: Bearer <token>` to `ListProjects`, `ListReleaseChannels`, `ListVersions`, `ListVersionStatuses`, `GetVersionMetadata`, `UpdateProjectExportSettings`, `ExportProject`, and `ExportProjectFileDownloads`.
- [x] UAUTH-031 Add a fail-fast typed "not signed in" error when no valid, unexpired token exists, instead of issuing an unauthenticated request.
- [x] UAUTH-032 Keep pre-signed file download URLs (`DownloadFileAsync`) bearer-free, since they are storage URLs.
- [x] UAUTH-033 Do not attempt per-route or per-project token minting; rely on the account token carrying all `neo-compose-unity` scopes.

### Phase 4 verification

- [x] UAUTH-034 Add tests that every auth-required request carries the bearer header and refuses to send without a valid token.
- [x] UAUTH-035 Add tests that pre-signed file downloads omit the bearer header.
- [x] UAUTH-036 Run focused Unity API client tests from the HelloWorld Unity Test Runner.

## Phase 5: Expiry and authorization-failure handling

Goal: distinguish authentication failure (`401`) from authorization failure
(`403`) and handle non-refreshable expiry.

### Failure handling

- [x] UAUTH-037 On `401`, treat as signed-out: clear in-memory token, mark stored token expired, gate controls, and prompt re-sign-in via the auth cell.
- [x] UAUTH-038 On `403`, keep the user signed in and surface a capability-specific, non-destructive message identifying the missing capability and affected project.
- [x] UAUTH-039 Give the `unity:settings:write` settings-edit `403` a message clearly stating the user lacks permission to edit this project's Unity settings, not a broken sign-in.
- [x] UAUTH-040 Do not retry `403` as an auth problem and do not loop the device flow on `403`.
- [x] UAUTH-041 Proactively check the stored absolute expiry before issuing a request to present the expired state without a guaranteed round trip, while still handling server `401` for early-revoked tokens.

### Phase 5 verification

- [x] UAUTH-042 Add tests that `401` drives the signed-out/expired state and gating.
- [x] UAUTH-043 Add tests that `403` keeps the user signed in and surfaces the capability-specific message, including the `unity:settings:write` case.
- [x] UAUTH-044 Run focused Unity failure-handling tests from the HelloWorld Unity Test Runner.

## Phase 6: Disconnect and revocation

Goal: add explicit sign-out that best-effort revokes server-side and always
clears local credentials.

### Disconnect

- [ ] UAUTH-045 Best-effort revoke the token server-side via Better Auth session sign-out/revoke using the current bearer token (no RFC 7009 endpoint exists; the device token is session-backed).
- [ ] UAUTH-046 Always call `INeoComposeTokenStore.Clear()` and reset to signed-out even when the server revoke fails or times out.
- [ ] UAUTH-047 Return the auth cell to its signed-out call to action after disconnect.

### Phase 6 verification

- [ ] UAUTH-048 Add tests that disconnect clears local storage even when the server revoke call fails.
- [ ] UAUTH-049 Run focused Unity disconnect tests from the HelloWorld Unity Test Runner.

## Phase 7: Runtime API key storage stub

Goal: store an optional project-scoped runtime API key for the later
runtime-sync feature, without using it yet, kept strictly separate from the user
OAuth token.

### Runtime key config

- [ ] UAUTH-050 Add an optional project-scoped runtime API key field to the committed project config surface (`NeoComposeConfig`/project config), distinct from the user OAuth token storage.
- [ ] UAUTH-051 Make the key optional so its absence blocks no editor flow in this pass.
- [ ] UAUTH-052 Store and round-trip the value with no validation, network use, or runtime wiring; never read it from the runtime assembly.
- [ ] UAUTH-053 Add editor help text noting it is a read-only, project-scoped runtime key for a future runtime-sync feature and a low-trust secret.

### Phase 7 verification

- [ ] UAUTH-054 Add tests that the runtime key persists in project config, is optional, and is never read by the runtime assembly in this pass.
- [ ] UAUTH-055 Run focused Unity runtime-key storage tests from the HelloWorld Unity Test Runner.

## Phase 8: End-to-end verification

Goal: prove the full device flow and authorized editor calls against a running
web app and confirm no regressions.

### Manual and suite verification

- [ ] UAUTH-056 Walk the full device flow end to end against a running web app, switching `apiBaseUrl` between localhost and production.
- [ ] UAUTH-057 Verify an authorized export/sync and a `unity:settings:write` settings edit succeed, and that a user lacking settings permission gets the specific `403` message.
- [ ] UAUTH-058 Run the full `src/NeoComposeUnity/Tests/` suite from the HelloWorld Unity Test Runner.
- [ ] UAUTH-059 Run the full `samples/HelloWorld/Assets/Tests/` suite from the HelloWorld Unity Test Runner.
- [ ] UAUTH-060 Verify no pre-existing failures remain.
