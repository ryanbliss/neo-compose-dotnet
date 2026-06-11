# Convex Realtime Sync

## Status

Draft — for review. No implementation has started.

## Depends on

- The neo-compose web repo's in-Convex scope enforcement (shipped): every public
  Convex function resolves the caller through
  `resolveNeoInternalCtx(ctx, requiredScope)`, the central session guard rejects
  scoped (device-grant) sessions on any function that does not declare a scope,
  and the public `gameSaves.*` functions already accept a runtime session
  carrying the templated `project:<id>:save:{read,write,archive}` scopes.
- `@convex-dev/better-auth`'s convex plugin: `GET /api/auth/convex/token`
  (session bearer in `Authorization`) mints a Convex-verifiable JWT
  (default 15-minute expiry) whose claims include `sessionId`, so the scoped
  session is visible to `ctx.auth` over the websocket exactly as it is over
  REST.
- The existing device-flow auth core shared by editor and runtime
  (`NeoComposeDeviceAuthorizationFlow`, `INeoComposeTokenStore`,
  `INeoComposeAccessTokenProvider`, `NeoComposeSessionRefresher`).
- [zakstam/convex-dotnet-unofficial](https://github.com/zakstam/convex-dotnet-unofficial)
  (MIT), reviewed for security in a prior pass; vendored at a pinned commit.

## Owns

- A new UPM package `com.ryanbliss.neocompose.convex` under
  `/src/NeoComposeConvex` (Runtime, Editor, Tests) that wraps a vendored copy of
  the unofficial Convex .NET client.
- A realtime provider seam in the core package (`NeoCompose.Unity`) that the
  Convex package plugs into via explicit registration — core never references
  Convex types.
- Runtime integration: live save-list and save-head subscriptions feeding
  `NeoProjectStore`/`NeoSaveSynchronizer`, and a commit path that goes through
  the public `gameSaves.commit` Convex mutation when connected.
- Editor integration: live release-channel/version lists and a
  synchronization "hot reload" signal in `NeoComposeEditorWindow`.
- The Convex JWT token provider (mint + refresh from the stored device-flow
  session token).

## Non-goals

- No realtime requirement for shipping games: everything degrades to the
  existing REST + local behavior when the plugin is absent, disconnected, or
  denied. Local saves never depend on the socket.
- No WebGL support initially (the vendored client uses
  `System.Net.WebSockets.ClientWebSocket`).
- No play-mode live swapping of authored project data inside a running game.
  Editor "hot reload" re-runs the existing import pipeline; it does not mutate
  in-memory generated objects.
- No usage metering or billing enforcement. The design leaves server-side
  seams (see [Metering seams](#metering-seams)) but specs nothing.
- No automatic conflict resolution changes: the `OnConflict` /
  `OnMigrationRequired` / clone continuation contracts are unchanged.

## Decisions (locked during review)

1. **Use cases**: realtime queries for both the runtime (save list updating
   live on a menu, save head freshness) and the editor (live version/channel
   lists, sync hot-reload signal). Save mutation remains player/developer
   initiated — realtime never writes on its own.
2. **Ship target**: production-capable, but the primary surface is editor +
   development builds. The sample demonstrates disabling it in release builds.
   We do not optimize for every platform now.
3. **Vendoring** *(amended during phase 1)*: the converted client lives in its
   own public repo — `ryanbliss/convex-dotnet-unity` — as a standalone UPM
   package (`com.ryanbliss.convex-dotnet-unity`, assembly `Convex.Client`),
   built from upstream commit `cc0759f5c5c3261dd898a355dde149096f853c9e`,
   trimmed and down-converted, MIT + NOTICE preserved. NeoComposeDotnet
   commits **no third-party code**: project manifests reference the repo by
   git URL and UPM resolves it into `Library/PackageCache` (gitignored),
   node_modules-style. UPM cannot express git-URL dependencies inside a
   package's `package.json`, so SDK consumers add both git URLs to their
   project manifest (standard Unity-ecosystem practice; documented in the
   README, phase 6).
4. **Socket auth**: mint the Convex JWT via better-auth's
   `GET /api/auth/convex/token` using the stored device-flow session token, and
   refresh it before expiry.
5. **Registration**: explicit. The game/editor constructs the Convex provider
   and hands it to core. No auto-discovery, no static registry.
6. **Write path**: when connected, `NeoSaveSynchronizer` commits through the
   public `gameSaves.commit` Convex mutation (same typed conflict contract);
   otherwise through the existing REST client. One contract, two transports.
7. **Convex functions**: reuse the web's public functions by relying on their
   (already shipped) multi-scope auth. New functions only where the response
   shape is meaningfully different (the lightweight sync signal). Optional
   inputs are fine; drastically different outputs are not.

## Terms

- **Provider** — the core-facing realtime abstraction
  (`INeoRealtimeProvider`); the Convex package supplies the only
  implementation.
- **Session token** — the better-auth session token the device flow stores in
  the platform token store; the REST bearer.
- **Convex JWT** — the short-lived (15 min) JWT minted from a session token by
  `/api/auth/convex/token`; the only credential the Convex websocket accepts.
  Its claims carry `sessionId`, which is how in-Convex scope enforcement sees
  the device grant.
- **Scoped session** — a better-auth session stamped with `clientId` +
  `scopes` by the device-code flow. The web's central guard rejects it on any
  Convex function that doesn't declare a required scope; functions that do
  declare one admit it when the grant covers that scope.
- **Sync signal** — a lightweight Convex query (head/version/content-hash
  shape, no payload) the editor subscribes to in order to learn that a re-sync
  is worthwhile.

## Security model

ZERO TRUST: the client is untrusted. Nothing in this package grants access —
all authorization happens inside Convex, per function, against the session the
JWT resolves to:

- The JWT is minted by better-auth from a real session; the websocket cannot
  fabricate one.
- The JWT carries `sessionId`; the in-Convex guard sees the scoped grant and
  enforces the function's declared scope plus the OAuth client's curated scope
  set, identically to the REST path.
- The runtime client's grant is narrow (templated `project:<id>:save:*`,
  `project:<id>:runtime:read`); the editor client's grant is the static editor
  set (`project:list`, `project:read`, `unity:export`,
  `project:version:read`, …). A subscription to a function whose scope the
  grant lacks fails server-side; the plugin surfaces it and does not retry it
  as if transient.
- Vendored code keeps its no-unsolicited-requests property: the only endpoints
  it may contact are the configured Convex deployment URL and (in our wrapper,
  not vendored code) the configured `apiBaseUrl` for JWT minting.

## Package layout

Two packages. The third-party client lives in its own public repo
(`github.com/ryanbliss/convex-dotnet-unity`) so NeoComposeDotnet commits no
third-party code:

```
convex-dotnet-unity/                 separate repo, MIT, resolved via UPM git URL
  package.json                       com.ryanbliss.convex-dotnet-unity
  LICENSE.md / NOTICE.md             upstream MIT + provenance, trim log, edit log
  Runtime/
    Convex.Client.asmdef             noEngineReferences, scoped precompiled refs
    csc.rsp / link.xml
    Convex.Client/                   trimmed, C#11-converted source (pinned commit)
    Polyfills/                       init/required compiler attributes
    Plugins/                         bundled dependency DLLs (Rx, STJ, …)

src/NeoComposeConvex/                this repo
  package.json                       com.ryanbliss.neocompose.convex
  Runtime/
    NeoCompose.Unity.Convex.asmdef   references NeoCompose.Unity + Convex.Client
    ConvexRealtimeProvider.cs        INeoRealtimeProvider implementation
    ConvexJwtTokenProvider.cs        IAuthTokenProvider over /api/auth/convex/token
    MainThreadDispatcher.cs          marshals client callbacks onto the Unity main thread
    ...
  Editor/
    NeoCompose.Unity.Convex.Editor.asmdef
    ...                              editor provider wiring + hot-reload UI hooks
  Tests/
    NeoCompose.Unity.Convex.Tests.asmdef
```

The sample (`samples/HelloWorld`) references `com.ryanbliss.neocompose.convex`
by local path (like `com.ryanbliss.neocompose`) and
`com.ryanbliss.convex-dotnet-unity` by git URL — UPM resolves the latter into
`Library/PackageCache` (gitignored). Both packages' tests run from the sample
(existing testing policy).

### Vendoring and trimming (the convex-dotnet-unity repo)

Package only `src/Convex.Client` from the upstream repo (182 files / ~38k LOC
before trimming). Delete at vendor time, recording each deletion in
`NOTICE.md`:

> **Language-version constraint (discovered in phase 1):** Unity 6000.0.40f1
> ships Roslyn 4.3.1, which tops out at C#11-preview even with a
> `csc.rsp -langversion` override — upstream is C#13. The vendored copy is
> mechanically down-converted (collection expressions, primary constructors,
> a handful of .NET 6+/8+ BCL calls); the exact transformations live in the
> NOTICE edit log. Re-vendoring re-runs the same conversion. If the sample
> project ever moves to a Unity version with Roslyn ≥ 4.8, most of the
> conversion becomes unnecessary and a faithful re-vendor is preferable.

- `Convex.BetterAuth` (email/password auth — we bring our own token provider),
  `Convex.Client.AspNetCore`, `Convex.Client.Blazor`, the analyzers, and the
  source generator: never vendored.
- `Auth/Clerk/**` (Clerk token services), `Extensions/ExtensionMethods/
  ConvexWpfMauiExtensions.cs`, `DependencyInjection/**` (we construct clients
  directly), `DeveloperTools/**`.
- Evaluate during phase 1 and trim if unused by our provider: `Files`,
  `VectorSearch`, `Scheduler`, batching/testing/performance extension
  surfaces. Bias toward deleting — every vendored line is a line we own.

What must remain: `ConvexClient`/builder + options, the websocket client and
protocol (`Infrastructure/Internal/WebSocket`, `ConvexWebSocketProtocol`), the
subscription engine behind `Observe<T>`, one-shot `Query/Mutate`, the
authentication slice (`IConvexAuthentication`, `SetAuthTokenProviderAsync`,
`IAuthTokenProvider`), and serialization.

### Dependency strategy

This repo's "no precompiled DLLs" policy holds for every package here. The
converted client targets `netstandard2.1` and references NuGet packages with
no practical source-vendoring path, so the managed dependency DLLs ship in
the external `convex-dotnet-unity` repo under `Runtime/Plugins/`, restricted
to what survives trimming. Expected tail:

- `System.Reactive` (+ `System.Reactive.Linq`) — load-bearing for
  `Observe<T>`'s operator pipeline.
- `System.Text.Json` (+ its transitive netstandard companions) — the vendored
  serializer. The core package continues to use Newtonsoft; the two never mix
  across the seam (the provider converts to core DTOs at the boundary).
- `System.Threading.Channels`.
- `Microsoft.Extensions.Logging.Abstractions` / `Options` / `ObjectPool` —
  trim the vendored code's usage where cheap; bundle what remains.

Phase 1 produces the exact DLL list + versions and records them in
`NOTICE.md`. Acceptance: the HelloWorld sample compiles with no assembly
version validation errors and both test asmdefs run green. An IL2CPP
`link.xml` preserving `System.Reactive` and `System.Text.Json` ships in the
package (production-capable decision), with a device IL2CPP smoke deferred to
a later phase.

## Core seam (`NeoCompose.Unity`)

Core gains a provider interface plus DTO-level events. Core has zero Convex
knowledge; everything is expressed in existing core types (`NeoSaveFileList`,
`RemoteGameSave`, `NeoSaveCommitRequest`, `NeoCommitResult`).

```csharp
namespace NeoCompose.Runtime
{
    public enum NeoRealtimeConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        /// <summary>Server rejected auth or a subscription scope; no auto-retry.</summary>
        Denied,
    }

    /// <summary>
    /// Optional realtime transport plugged into the project store. All callbacks
    /// are invoked on the Unity main thread. Implementations are explicitly
    /// registered; core never discovers one on its own.
    /// </summary>
    public interface INeoRealtimeProvider : IDisposable
    {
        NeoRealtimeConnectionState State { get; }
        event Action<NeoRealtimeConnectionState> OnConnectionStateChanged;

        Awaitable ConnectAsync();
        Awaitable DisconnectAsync();

        /// <summary>Live save list for the configured project + channel.</summary>
        IDisposable SubscribeSaveList(
            string? targetReleaseChannelId, Action<NeoSaveFileList> onChanged);

        /// <summary>Live cloud head for one save.</summary>
        IDisposable SubscribeSaveHead(string customId, Action<RemoteGameSave> onChanged);

        /// <summary>
        /// True when the provider is connected and can commit. The synchronizer
        /// checks per commit; a disconnect between check and call falls back to REST.
        /// </summary>
        bool CanCommit { get; }

        /// <summary>Commit through the realtime transport. Same contract as
        /// <see cref="INeoApiClient.CommitAsync"/> including typed conflicts.</summary>
        Awaitable<NeoCommitResult> CommitAsync(NeoSaveCommitRequest request, bool replaceSnapshot);
    }
}
```

Registration is a new optional on the store options (exact name aligned with
the existing options type during implementation):

```csharp
var provider = new ConvexRealtimeProvider(new ConvexRealtimeOptions(
    convexUrl: config.convexUrl,
    apiBaseUrl: config.apiBaseUrl,
    projectId: config.projectId,
    tokenStore: authentication.TokenStore));   // exposed read-only for plugins

var store = NeoProjectStore.Create(config, authentication, options with
{
    RealtimeProvider = provider,
});
```

Notes:

- `NeoAuthentication` (and the editor auth controller) must expose its token
  store (or an `INeoComposeAccessTokenProvider`) read-only so a plugin can
  mint JWTs without re-implementing credential storage. That is the only core
  API addition auth needs.
- Editor and runtime construct separate providers (different token stores,
  different OAuth grants); the provider is auth-agnostic — it only needs "give
  me the current session token".
- The editor seam mirrors this interface with editor-shaped subscriptions
  (version/channel list, sync signal); it lives in the core Editor asmdef so
  `NeoComposeEditorWindow` stays Convex-free. Exact shape settled in phase 5,
  following the same rules (main-thread callbacks, DTOs from the existing
  editor models, explicit registration).

## Auth: ConvexJwtTokenProvider

Implements the vendored `IAuthTokenProvider` and is installed via
`client.Auth.SetAuthTokenProviderAsync(...)`.

- Mint: `GET {apiBaseUrl}/api/auth/convex/token` with
  `Authorization: Bearer {sessionToken}` (session token read from the token
  store at call time, never cached past the store). Response: `{ token }`.
- Cache the JWT until 60 seconds before its `exp`; re-mint on demand. The
  vendored client asks the provider for a token whenever it (re)authenticates
  the socket, so proactive refresh is a timer that nudges the client to
  re-authenticate before expiry, not a parallel token pipeline.
- A 401/expired session from the mint endpoint maps to the existing
  `NeoComposeNotSignedInException` semantics: the provider transitions to
  `Denied`, tears down the socket, and the host surfaces sign-in exactly as the
  REST path does (`NeoAuthentication.HandleApiException`).
- Sign-out: the host calls `DisconnectAsync` (and `client.Auth.ClearAuthAsync()`
  internally) before clearing the store, so no socket outlives its credential.

Error messages are distinct per failure: missing `convexUrl`, mint HTTP
failure, mint 401, socket auth rejection, and per-subscription scope denial
each throw/log their own message naming the failing step.

## Convex deployment URL

The websocket needs the deployment URL (`https://<deployment>.convex.cloud`),
which the SDK does not have today.

- `NeoComposeConfig` gains `convexUrl`, **synced** from the export bundle the
  same way `runtimeOAuthClientId` is (overwritten on sync, honors
  `runtimeOAuthOverridden`-style manual override; the override flag widens to
  cover it or gains a sibling).
- Web-side: the unity export bundle includes the deployment URL (sourced from
  the deployment's public URL env). Until that lands, the field is hand-edited.

## Convex-side requirements (web repo)

Most of the surface already exists because the scope-enforcement sweep made
the public functions scoped-session-capable. This section is the checklist the
web repo implements/verifies against, with convex-test coverage mirroring the
existing `runtimeScopeGate` tests (scoped session admitted with the right
scope, rejected without).

1. **Saves — verify only, no new functions.** `gameSaves.list`, `get`,
   `getSnapshots`, `commit`, `clone`, `archive`, `archiveSnapshot` already
   resolve `buildTemplatedSaveScope(projectId, …)`. Verification items:
   - A runtime scoped session subscribing to `list`/`get` over the websocket
     succeeds with `project:<id>:save:read` and is rejected without it.
   - `commit` from a runtime scoped session passes the
     `requireCurrentAuthUserCanWriteSavesToReleaseChannel` create gate (the
     channel-read gate is inferred from the templated `save:write` scope; the
     REST `commitForService` path is exempt — confirm the public-mutation path
     behaves identically for the runtime grant, and fix the gate if not).
   - The typed conflict result (`kind: "committed" | "conflict"`) round-trips
     through the websocket client into `NeoCommitResult` losslessly.
2. **Editor lists — widen if needed.** `projectVersions.listMetadata` (+ the
   release-channel list query the editor window uses) must admit the editor
   grant's `project:version:read` / `project:release-channel:read` scopes. If
   the sweep already declared those scopes, this is verification; otherwise add
   the scope to the existing function (multiple scopes per function is normal —
   do not fork the query).
3. **Sync signal — one new query.** A lightweight
   `projectExportSignal`-style public query returning head metadata only
   (current version id, status, content hash, updated-at) for a project,
   declared with the editor grant's `unity:export` (or `project:version:read`)
   scope. New because the existing export endpoint's output (full bundle) is
   a drastically different shape than a signal; subscribing to the bundle
   would stream megabytes per keystroke. The existing `projectContentHash`
   machinery is the likely source of truth.
4. **Export bundle** carries the Convex deployment URL (see above).

No `*ForService` function is touched, and nothing about the REST routes
changes.

## Runtime integration

`InternalProjectStore` holds the optional provider and owns its lifecycle
(connect after sign-in when cloud sync is enabled; disconnect on sign-out and
on store disposal).

- **Save list**: while connected, the provider's `SubscribeSaveList` feed
  replaces polling — results flow through the same internal cache
  `RefreshListAsync` populates today, and a new `OnSaveListChanged` event on
  the store lets a menu UI re-render. `RefreshListAsync` keeps working
  unchanged (manual refresh remains correct whether or not realtime is up).
- **Active save head**: `NeoSaveSynchronizer` subscribes to its save's head on
  load. Incoming heads update the store's fresh-remote cache (the one
  `TryGetFreshRemote` reads), so the next load/commit sees the newest head
  without an extra fetch. A head that diverges from the active local state
  raises a new opt-in `OnRemoteHeadChanged(RemoteGameSave)` event — it does
  **not** auto-apply, auto-load, or auto-raise `OnConflict`; the existing
  conflict flow still triggers only at load/commit time.
- **Commit**: in `CommitToCloudAsync`, when `provider is { CanCommit: true }`,
  commit via `provider.CommitAsync` instead of `core.ApiClient.CommitAsync`;
  the conflict-resolution continuation logic is shared, not duplicated (the
  transport is a parameter, the flow is the flow). Any transport-level failure
  follows the existing best-effort/`RequireCloudCommit` semantics, with one
  addition: a provider failure that looks transient (socket dropped mid-call)
  falls back to one REST attempt before being treated as a cloud-commit
  failure.
- **Degradation**: disconnect ⇒ subscriptions go quiet, `CanCommit` goes
  false, everything routes through REST/local as today. Reconnect (exponential
  backoff with jitter, capped) re-establishes subscriptions and re-primes the
  list. `Denied` does not auto-reconnect.

## Editor integration

- The editor window registers an editor provider once signed in with a project
  selected; lifecycle is tied to the window and survives domain reload by
  reconstructing (no serialized sockets).
- **Live lists**: the release-channel/version metadata the window currently
  loads on demand re-renders when the corresponding subscription fires; the
  manual refresh button remains.
- **Hot reload**: subscribe to the sync signal. When the head/content hash
  changes relative to the last synchronized state, surface it through the
  existing confirmation seam (`INeoComposeConfirmationService`) — the same
  flow as pressing the sync button — and on confirm run
  `NeoComposeSynchronizer.SynchronizeAsync` (REST pull + import pipeline,
  untouched). An "Auto-sync on remote changes" toggle (off by default) skips
  the confirmation. Signal-then-pull is deliberate: the socket carries bytes
  proportional to change frequency, not bundle size, and the import pipeline
  stays single-sourced.

## Builds and platforms

- The sample shows the gate:

```csharp
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    options.RealtimeProvider = new ConvexRealtimeProvider(...);
#endif
```

  plus a `NeoComposeConfig` developer-owned bool (sibling of
  `enableOAuthCloudSync`) so the gate is data-driven where teams prefer that.
  Production use is supported — the gate is the recommendation, not a
  constraint.
- Desktop + mobile via `System.Net.WebSockets` on `netstandard2.1`. WebGL is
  explicitly unsupported (provider constructor throws a distinct error on
  WebGL rather than failing obscurely at connect time).

## Metering seams

Out of scope, by decision. The seams that make future enforcement possible
without client changes:

- Every realtime read/write is a Convex function call authorized per session +
  OAuth client — per-project entitlement checks slot into the same
  `resolveNeoInternalCtx` path server-side.
- The client treats a server-side denial as `Denied` (no retry storm), which
  is exactly the client behavior a future "free dev sync limit exceeded"
  response needs.

## Testing

- **Provider unit tests** (package `Tests/`, run from the sample's Unity Test
  Runner per repo policy): fake the vendored `IConvexClient` at the provider
  boundary — connection state transitions, JWT mint/refresh/expiry, sign-out
  teardown, main-thread marshaling, subscription fan-out, denial semantics.
- **Synchronizer tests** (core package `Tests/`): fake `INeoRealtimeProvider` —
  commit routes through the provider when `CanCommit`, falls back to REST when
  not, head updates prime the fresh-remote cache, `OnRemoteHeadChanged` does
  not auto-apply.
- **Vendored code**: not unit-tested here beyond compiling and one protocol
  smoke test through the fake transport; upstream owns its suite, `NOTICE.md`
  owns the diff.
- **Web repo**: convex-test coverage for the verification/widening items and
  the new signal query (scoped-session admitted/rejected matrix).

## Phasing

1. **Vendor + compile**: package skeleton, vendored/trimmed source, dependency
   DLL list, `NOTICE.md`, sample reference, both asmdefs compiling, EditMode
   smoke test.
2. **Auth + lifecycle**: `ConvexJwtTokenProvider`, connect/disconnect,
   refresh, sign-out teardown, state events. (Requires `convexUrl` config
   field; hand-edited until the export bundle carries it.)
3. **Web repo companion**: verification matrix from
   [Convex-side requirements](#convex-side-requirements-web-repo), the sync
   signal query, export-bundle `convexUrl`.
4. **Runtime saves**: core seam (`INeoRealtimeProvider`, store registration),
   save-list + head subscriptions, commit-over-socket with REST fallback.
5. **Editor**: editor provider registration, live lists, hot-reload signal +
   confirmation + auto-sync toggle.
6. **Sample + docs**: HelloWorld wiring, production-gate demonstration, README.

Phases 1–2 are pure additions to the new package; nothing in core changes
until phase 4, so they can land independently.

## Open questions

- Exact post-trim dependency DLL list and versions (phase 1 output).
- Whether `Microsoft.Extensions.*` usage in the vendored core is shallow
  enough to excise entirely (would shrink the DLL tail to Rx + STJ +
  Channels).
- Name of the store-options registration property and the editor-side seam
  type (settled in phases 4–5 against the real options types).
- Whether the sync signal should also gate on the *selected* version vs. the
  channel head (depends on how the editor's update-availability UX evolves).

## Tasks

`[ ]` pending · `[-]` partial · `[x]` complete

### Phase 1 — Vendor + compile

- [-] Package skeleton: `src/NeoComposeConvex` with `package.json`
      (`com.ryanbliss.neocompose.convex`), Runtime/Editor/Tests asmdefs
      (Editor asmdef deferred to phase 5 — an empty assembly is a Unity
      import warning)
- [x] Vendor `src/Convex.Client` at pinned commit `cc0759f`, apply trim list
      (plus a mechanical C#12→C#11 down-conversion — Unity 6000.0's Roslyn
      4.3.1 tops out at C#11-preview; see the NOTICE edit log)
- [x] `NOTICE.md` with provenance (repo, commit, MIT text) + trim/edit log
- [x] Dependency DLLs under the client package's `Runtime/Plugins/` +
      versions recorded in `NOTICE.md` (12 DLLs: the 8 planned + the
      netstandard2.0 compat facades Unsafe/Buffers/Memory/Tasks.Extensions
      that Unity doesn't provide)
- [x] `link.xml` preserving Rx + System.Text.Json for IL2CPP
- [x] Converted client extracted to a standalone UPM repo
      (`convex-dotnet-unity`, assembly `Convex.Client`) so no third-party
      code is committed to NeoComposeDotnet; committed + tagged `v0.1.0`
      locally
- [x] Publish `convex-dotnet-unity` to GitHub (public,
      `github.com/ryanbliss/convex-dotnet-unity`, tag `v0.1.0`) and swap the
      sample manifest to the `…git#v0.1.0` URL — resolves into
      `Library/PackageCache` (gitignored), no third-party code committed
- [x] HelloWorld sample references both packages + testables wired
- [x] Package compiles in the sample project (no console errors)
- [x] EditMode smoke test green from the sample's Unity Test Runner
      (3 smoke tests; full suite 374/374)
- [ ] Surface-trim follow-up: drop `Files`/`VectorSearch`/`Scheduling`/
      `HttpActions` (+ their `IConvexClient` members) kept in the first
      compile pass to avoid editing `ConvexClient` before green

### Phase 2 — Auth + lifecycle

- [ ] Core: expose the token store / access-token provider read-only from
      `NeoAuthentication` (+ editor auth controller) for plugin JWT minting
- [ ] `NeoComposeConfig.convexUrl` (hand-edited until the export bundle
      carries it)
- [ ] `ConvexJwtTokenProvider`: mint via
      `GET {apiBaseUrl}/api/auth/convex/token`, cache to `exp − 60s`,
      distinct errors per failure step
- [ ] `ConvexRealtimeProvider`: connect/disconnect, state events, reconnect
      backoff + jitter, `Denied` (no auto-retry) semantics
- [ ] Sign-out teardown (disconnect + `ClearAuthAsync` before store clear)
- [ ] Provider unit tests against a fake `IConvexClient`: state transitions,
      JWT mint/refresh/expiry, teardown, main-thread marshaling

### Phase 3 — Web repo companion (neo-compose)

- [ ] convex-test: scoped runtime session admitted/rejected matrix on public
      `gameSaves.list` / `get` (websocket-equivalent identity)
- [ ] convex-test: runtime grant passes the `commit` create gate
      (`requireCurrentAuthUserCanWriteSavesToReleaseChannel`); fix gate if not
- [ ] Sync-signal query (head/version/content-hash shape) + scope + tests
- [ ] Export bundle carries the Convex deployment URL
- [ ] Editor-list scope verification: `projectVersions.listMetadata` + channel
      list admit the editor grant's scopes

### Phase 4 — Runtime saves

- [ ] Core seam: `INeoRealtimeProvider` + `NeoRealtimeConnectionState` in
      `NeoCompose.Unity`
- [ ] Store-options registration property + `InternalProjectStore` lifecycle
      (connect after sign-in, disconnect on sign-out/disposal)
- [ ] Save-list subscription feeding the list cache + `OnSaveListChanged`
- [ ] Save-head subscription priming the fresh-remote cache +
      `OnRemoteHeadChanged` (opt-in, never auto-applies)
- [ ] Commit-over-socket when `CanCommit`, REST fallback, shared
      conflict-continuation flow (transport as parameter)
- [ ] Synchronizer tests against a fake `INeoRealtimeProvider`

### Phase 5 — Editor

- [ ] Editor seam + provider registration in `NeoComposeEditorWindow`
- [ ] Live release-channel/version lists (manual refresh retained)
- [ ] Hot-reload: sync-signal subscription → confirmation seam →
      `SynchronizeAsync`; "Auto-sync on remote changes" toggle (default off)

### Phase 6 — Sample + docs

- [ ] HelloWorld wiring + production-gate demonstration
      (`#if UNITY_EDITOR || DEVELOPMENT_BUILD` + config bool)
- [ ] Package README (setup, registration, degradation semantics, WebGL
      unsupported)
