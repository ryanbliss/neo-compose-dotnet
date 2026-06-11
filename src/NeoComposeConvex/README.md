# NeoCompose Convex Realtime

Optional realtime sync for NeoCompose, on a Convex websocket: live save lists
and cloud-head pushes for the runtime, live version lists and synchronization
hot-reload for the Unity editor. Everything degrades — without this package
(or while disconnected) the SDK behaves exactly as the REST/local build does.

See `specs/convex-realtime-sync.md` for the full design.

## Install

UPM cannot resolve git dependencies declared by a package, so add **both**
lines to your project's `Packages/manifest.json`:

```json
"com.ryanbliss.neocompose.convex": "<this package>",
"com.ryanbliss.convex-dotnet-unity": "https://github.com/ryanbliss/convex-dotnet-unity.git#v0.1.0"
```

The second package is the vendored (unofficial) Convex .NET client this plugin
drives; it resolves into `Library/PackageCache` like any other dependency.

## Runtime: realtime save sync

Registration is explicit — build the provider and hand it to the project
store. The provider derives its socket credential (a short-lived Convex JWT)
from the same `NeoAuthentication` the store uses, so sign-in state stays
single-sourced, and the backend enforces the device grant's scopes on every
subscription and mutation exactly as it does over REST.

```csharp
var config = NeoComposeConfig.LoadDefault();
NeoAuthentication auth = null;
INeoRealtimeProvider realtime = null;

#if UNITY_EDITOR || DEVELOPMENT_BUILD // production-capable; gate is the recommendation
if (config.enableOAuthCloudSync
    && !string.IsNullOrWhiteSpace(config.convexUrl)
    && config.TryBuildAuthenticationOptions(out var authOptions))
{
    auth = new NeoAuthentication(authOptions);
    realtime = new ConvexRealtimeProvider(new ConvexRealtimeOptions(
        config.convexUrl, config.apiBaseUrl, config.projectId, auth.AccessTokenProvider));
}
#endif

var store = new NeoProjectStore(
    config: config, authentication: auth, realtimeProvider: realtime);
await store.LoadAsync();
```

What you get while connected:

- The browse list updates live (`NeoProjectStore.OnListChanged`, the event a
  save menu already listens to).
- Each loaded `NeoSaveSynchronizer` watches its cloud head: pushes prime the
  fresh-remote cache, and `OnRemoteHeadChanged` fires when another device
  commits — opt-in, never auto-applied.
- Commits go through the socket (same typed conflict contract); any socket
  failure falls back to one REST attempt, so a flaky connection never costs a
  save.

Lifecycle: the store connects during `LoadAsync` when already signed in; call
`store.ConnectRealtimeAsync()` after a fresh sign-in and
`store.DisconnectRealtimeAsync()` before signing out. You own
`realtime.Dispose()`. A credential rejection parks the provider in the
`Denied` state (no auto-retry) until an explicit reconnect.

## Editor: live lists + hot reload

Zero setup. With this package installed, the Neo Compose window connects
automatically once you are signed in and the project has been synchronized
(the export carries the Convex deployment URL into
`NeoComposeConfig.convexUrl`). Release-channel/version lists stay current, and
when someone commits changes to the selected version you get the same
confirmation as pressing Synchronize — or enable "Auto-sync on remote
changes" in the window to skip the prompt. A "Live sync" status row next to
the Synchronize button shows the connection state with Connect/Disconnect
controls.

## Platform notes

- WebGL is not supported (the transport is
  `System.Net.WebSockets.ClientWebSocket`); the provider constructor throws a
  clear error there.
- Sample wiring lives in `samples/HelloWorld` (`HelloWorldMenu.cs`).
