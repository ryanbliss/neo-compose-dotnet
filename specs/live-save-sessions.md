# Live Save Sessions

## Status

Implemented — all five phases landed and verified (Convex + SDK + web test
suites green; live fork → in-place patch verified end-to-end against the dev
deployment from the Unity editor).

## Depends on

- [convex-realtime-sync.md](./convex-realtime-sync.md) (shipped): the realtime
  provider seam (`INeoRealtimeProvider` + `ConvexRealtimeProvider`), the
  save-head subscription feeding `NeoSaveSynchronizer.OnRemoteHeadChanged`, and
  the socket commit path through `gameSaves.commit`.
- The save overlay model (shipped): a snapshot's `valuesJson` is the canonical
  JSON of `{ values }`, a sparse stable-id → entry map where an entry may carry
  a `mark: "removed"` tombstone; resolution is `values[id] ?? authored`.
- The web repo's in-Convex scope enforcement (shipped): `gameSaves.*` public
  functions gate on `save:read` / `save:write` for both the web and service
  (runtime-token) front doors.
- The web save viewer's draft mode (shipped): per-snapshot in-memory overlay
  over `ProjectDatabaseVM.valueById`, localStorage persistence, banner commit.

## Owns

- A **live session** save mode: while realtime-connected, the game stops
  appending a snapshot per save and instead forks one **live snapshot** per
  play session, then streams throttled per-key **patches** into it.
- Two new Convex mutations (`gameSaves.forkLiveSnapshot`,
  `gameSaves.patchLiveSnapshot`, plus `ForService` variants) with server-side
  per-key merge into `valuesJson`.
- SDK: delta computation, debounced flush, offline patch queueing, and
  auto-apply of inbound live edits into the running save.
- Web: write-through live editing of a live head snapshot (draft mode
  bypassed), a recency-based LIVE badge, and reactive live visualization of
  game writes.
- Retention: automatic archiving of old live-session snapshots beyond a cap.

## Non-goals

- **No change for non-realtime players.** Without a connected realtime
  provider, save behavior is exactly today's: local store + REST commit, one
  appended snapshot per cloud save. Live mode is a dev/editor-facing feature
  surfaced by the same `#if` gate that registers the provider in the sample.
- **No CRDT / operational transform.** Concurrency is resolved per stable-id
  key, last-writer-wins at patch granularity. Two writers editing the _same_
  key race; editing _different_ keys both survive. That is the contract.
- **No multi-game concurrency on one save.** A second game session forking the
  head moves it; the first session's next flush hits the existing typed
  conflict contract and surfaces through `OnConflict`. We do not attempt to
  merge two live game sessions.
- **No presence infrastructure.** Liveness is inferred from write recency
  (server-set `synchronizedAt`), not from socket connection tracking.
- **No schema/container edits from web live mode.** Same constraint as draft
  mode: scalar leaves only.
- **No new policy knobs in v1.** The retention cap is a server constant with a
  named seam for a future `saveFilePolicies` field.

## Decisions (locked during review)

1. **Fork timing — lazy, on first flush.** Loading a save targets the existing
   head unchanged. The live snapshot is created when the first change actually
   flushes. A session that changes nothing leaves zero snapshot debris, and
   every snapshot in history represents a real change.
2. **Write model — delta patches with server-side merge.** The SDK and the web
   send only the keys that changed; the server merges them into the snapshot's
   `valuesJson`, re-canonicalizes, and re-hashes. Whole-snapshot replace was
   rejected because within a session the head id never moves, so `commit`'s
   id-based conflict check can never fire and concurrent web/game writes would
   silently clobber each other.
3. **Web behavior — write-through on live snapshots.** When the viewer's
   target head is a live snapshot, draft mode is bypassed: edits flush
   (debounced) straight to the snapshot so the running game picks them up.
   Frozen snapshots keep the existing draft + banner-commit UX.
4. **Game behavior — auto-apply while live.** While a live session is active,
   the synchronizer applies inbound remote value changes into the running save
   (echo-suppressed; locally-dirty keys win until flushed). Live mode is
   already opt-in via provider registration, so no additional flag gates this.
5. **Retention — auto-archive beyond N.** On fork, live-session snapshots for
   the save beyond the newest `N = 10` are archived server-side (restorable
   via the existing archive/restore flow). Manually created snapshots
   (clones, "Save as") are never auto-archived. `N` should be a configurable
   Convex flag.
6. **Live signal — follow head + recency badge, 60-second window.** The web
   viewer already follows the save head, so it lands on a new live snapshot
   automatically. A LIVE badge shows while the save's server-set
   `synchronizedAt` is within `LIVE_RECENCY_MS = 60_000` of now, and fades
   when writes stop. (Reviewed up from 10s — generous beats flickery.)
   Client-determined `updatedAt` is **not** trusted for the badge.
7. **Disconnect — queue deltas, flush on reconnect.** Per-key deltas compose,
   so the synchronizer keeps writing locally while offline and flushes one
   merged patch on reconnect into the same live snapshot. If the head moved
   while offline, the flush resolves through the typed conflict contract.
8. **Session end — implicit.** There is no end-of-session mutation. A live
   snapshot freezes naturally the moment it stops being the head (the next
   session forks past it). A live _head_ remains web-patchable even when no
   game is running — editing it is editing the save's current state, and the
   next game load picks it up. The badge, not a flag, communicates activity.
9. **Throttle — SDK-owned.** Trailing debounce 500 ms with a 2 s max-latency
   cap, and a guaranteed synchronous-best-effort flush on teardown
   (`Dispose`, application pause/quit, return to menu). Constants are
   internal-tunable, not public API.

## Terms

- **Play session**: one continuous run of a loaded save while
  realtime-connected. Identified by a client-generated `liveSessionId` (GUID)
  minted by the synchronizer at load.
- **Live snapshot**: a `gameSaveSnapshots` row stamped with the
  `liveSessionId` that forked it. Mutable in place via patches while it is the
  save's head; frozen forever once the head moves past it.
- **Patch**: a per-key delta against the overlay map:

  ```jsonc
  {
    // upsert: stable value id → full entry (an override OR a
    // `mark: "removed"` tombstone — tombstones are just entries)
    "entries": {
      "<stableId>": {
        /* entry */
      },
    },
    // delete the overlay key entirely → value falls back to authored
    "restoredToAuthored": ["<stableId>"],
  }
  ```

  A key may appear in only one of the two collections; overlap is rejected
  with a distinct error.

## Server design (Convex, neo-compose repo)

### Schema

`gameSaveSnapshots` gains one optional field:

- `liveSessionId: v.optional(v.union(v.null(), v.string()))` — the play
  session that forked this snapshot; `null`/absent on classic snapshots.
  Stamped at fork, never mutated. Its presence marks a "session snapshot" for
  retention and for the web's write-through eligibility check.

No new tables. No index changes expected (forks and patches resolve the save
by the existing `projectId + customId` path and the head by id).

### `gameSaves.forkLiveSnapshot` (+ `forkLiveSnapshotForService`)

First flush of a session. Args: `projectId`, `customId`, `liveSessionId`,
`baseSnapshotId`, `patch`, plus the same version/telemetry envelope `commit`
takes.

1. Resolve save; require `save:write` (same gates as `commit`; the create
   path does not apply — forking requires an existing save).
2. If `baseSnapshotId !== headSnapshotId` → return the typed conflict result
   with `serverHead` (same contract as `commit`).
3. Materialize the head's `values`, apply the patch, canonicalize, hash.
4. Insert the new snapshot with `liveSessionId` stamped and a generated name
   (`"Live session — <timestamp>"` via the existing generated-name path);
   set it as head; bump `synchronizedAt`.
5. **Retention sweep**: among the save's non-archived snapshots with a
   non-null `liveSessionId`, archive all but the newest `N = 10`
   (`LIVE_SNAPSHOT_RETENTION`, a named server constant). Snapshots without
   `liveSessionId` are never touched.
6. Return the committed wire (including the new `snapshotId` and
   `snapshotHash`).

### `gameSaves.patchLiveSnapshot` (+ `patchLiveSnapshotForService`)

Every subsequent flush, and the web's write-through edits. Args: `projectId`,
`customId`, `snapshotId`, `patch`.

1. Resolve save; require `save:write`.
2. Distinct thrown failures, each its own `if` + message: save missing;
   snapshot missing; snapshot belongs to another save; patch malformed; patch
   key in both collections.
3. **Typed, not thrown** *(refined during phase 3)*: a target that froze —
   no longer the head (a newer session forked past it), or the head but not a
   live snapshot — returns `{ kind: "staleTarget", serverHead }` instead of
   throwing. Convex redacts plain error messages in production, so a thrown
   variant would be unactionable; this mirrors `commit`'s typed conflict. The
   SDK reacts by re-forking on the returned head.
4. Parse `valuesJson`, merge: upsert each `entries` key, delete each
   `restoredToAuthored` key. Per-key last-writer-wins.
5. Re-canonicalize, re-hash, write back; bump save `synchronizedAt`.
6. Return `{ kind: "patched", snapshotId, snapshotHash, synchronizedAt }` —
   the hash is the caller's echo-suppression token.

Note the head check makes "head moved" the _only_ concurrency failure a
patcher can see: the game whose session was forked past discovers it on next
flush and surfaces it as a conflict; the web viewer discovers it reactively
(its target stops being head) and simply re-follows the new head.

### Validation & ZERO TRUST

- `patch.entries` must be an object keyed by non-empty strings; values are
  opaque (same trust level as `commit`'s `values` — the overlay layer is
  deliberately schema-agnostic at rest).
- `restoredToAuthored` must be an array of non-empty strings.
- Reject key overlap between the collections.
- Cap patch size (count + serialized bytes) with distinct errors; constants
  shared with a future rate-limit seam.
- All authorization through the existing `requireCurrentAuthUserSaveFileScope`
  chain; `ForService` variants mirror the existing dual-front-door pattern.

## SDK design (NeoComposeDotnet)

### Provider seam

`INeoRealtimeProvider` gains:

```csharp
Awaitable<NeoLiveForkResult> ForkLiveAsync(NeoLiveForkRequest request);
Awaitable<NeoLivePatchResult> PatchLiveAsync(NeoLivePatchRequest request);
```

with `NeoSavePatch` (entries + restoredToAuthored) expressed in core types.
There is **no REST fallback** for these: live mode requires the socket, and
disconnects queue (decision 7). `ConvexRealtimeProvider` maps them to the two
mutations. The fake provider in tests grows matching surface.

*(Refined during phase 5)*: mutation args cross the wire as a plain
dictionary/list/primitive graph (`ToWireArgs`), not a
`System.Text.Json.JsonElement`. The vendored Convex client's serializer
reflects public **properties** of unknown objects (our DTOs expose fields)
and serializes a `JsonElement` as `{"valueKind":…}` — which had silently
broken the existing realtime `commit` path too (masked by its REST
fallback); the fork's missing fallback surfaced it.

*(Also refined during phase 5 — new saves are live from snapshot one)*: a
live session creating a brand-new save passes its `liveSessionId` on the
classic create commit; the server stamps the **create-branch** head (and
names it like a fork) so the web's write-through engages the moment the save
is first viewed, and the session patches that head directly — no immediate
re-fork. Commits to an existing save never go live implicitly. Two bugs fixed
alongside: the synchronizer now merges its own server-identity record into
staged content (the game's serialized payload for a defaults-created save
carries none, which silently re-routed every flush through the classic append
path), and the web viewer seeds its follow-head tracker at selection time so
a fork landing immediately after the user picks a fresh save is still
followed.

*(Also refined during phase 5 — inbound channel for created saves)*: a save
that never went through the load path (the new-draft branch returns before
`AttachRealtimeHead`) had no realtime head subscription, so web patches
streamed to the server but never reached the running game. The create flush
now attaches the subscription once the save has a cloud identity (both the
live and classic create paths). Alongside it: the synchronizer keeps a
dedicated authoritative record of the save's server identity (merged into
staged content at stage AND flush time — staged objects can race the create
round-trip), flushed locals persist only while still the newest staged
content and carry a local-store-only `liveFlushed` marker, and a load that
finds the **same live snapshot patched in place by a co-editor** adopts the
cloud copy silently when the local copy was fully flushed (no conflict
prompt — there is nothing local to lose; a dirty local still conflicts). The
web banner gained a "Syncing changes…" indicator (`liveFlushPending`) since
live mode bypasses the per-cell save status.
`DateParseHandling.None` (`NeoSaveJson.ContentSettings`, used by the
save-content loaders and the provider's payload parsers). Newtonsoft's
default coerces any date-looking string inside the opaque `values` overlay
into a `JTokenType.Date`, which both reformats the player's data on
round-trip and produced tokens the wire converter rejected ("unsupported
JSON token of type Date"). `ToWireArgs` additionally degrades already-coerced
Date/Guid/TimeSpan/Uri tokens to their Newtonsoft string form instead of
failing the flush (old local-store files).

### Synchronizer state machine

`NeoSaveSynchronizer` in live mode (realtime provider connected and configured;
`NeoSaveOptions.LiveSessions` defaults to enabled-when-realtime, with an
explicit opt-out):

- **Load** is unchanged (head resolve, migration, clone continuations). It
  additionally mints `liveSessionId` and snapshots the loaded `values` map as
  the **flush baseline**.
- **Auto-write trigger** *(added during phase 5 — the original ask)*: while a
  live session is active, the game never calls save. Every save-value write
  in `NeoClient` schedules an automatic commit through a short coalescing
  delay (0.3 s — batching the burst of writes one frame/action produces into
  a single serialize + stage); the flush throttle below then paces the
  network. Inbound applies (`ApplyExternalSaveContent`) are suppressed from
  re-triggering it, and the auto path skips the unlinked-values warning so
  the hint doesn't become spam. Explicit `CommitAsync` still works and is
  still required for classic (non-live) cloud saves.
- **`CommitSaveContentAsync` keeps its public signature** but becomes
  stage-and-throttle in live mode: the local store is still written
  immediately (offline durability unchanged); the cloud append is replaced by
  the flush pipeline. `replaceSnapshot` is ignored in live mode (the live
  snapshot _is_ the replace target).
- **Flush pipeline**: trailing debounce 500 ms, max-latency 2 s, immediate
  flush on `Dispose`/pause/quit. At flush time the delta is computed by
  diffing the staged content's `values` map against the baseline per key
  (canonical-JSON string compare per entry; keys absent → `restoredToAuthored`).
  First flush of the session calls `ForkLiveAsync` (conflict → existing
  `OnConflict` continuation); subsequent flushes call `PatchLiveAsync`. On
  success the baseline advances and the returned `snapshotHash` is recorded
  in a small recent-hash ring for echo suppression.
- **Offline**: flushes that fail on transport keep the dirty keys staged;
  deltas compose, so reconnect emits one merged patch. Head-moved on
  reconnect → conflict contract.
- **Inbound auto-apply**: the existing head subscription delivers the remote
  save. If its `snapshotHash` is in the recent-hash ring → echo, drop.
  Otherwise apply per-key into the active save's content, **skipping keys
  currently dirty-pending-flush** (local wins until flushed), update the
  local store, and raise a new `OnLiveContentChanged(string content)` event.
  The generated save layer consumes it through the typed-change-subscription
  surface (see [typed-change-subscriptions.md](./typed-change-subscriptions.md))
  so games observe individual value changes, not a whole-save reload.
- A per-key dirty-tracking seam in the generated save layer (avoiding the
  flush-time diff) is a noted future optimization, not v1.

## Web design (neo-compose repo)

- **Live detection**: the viewer already follows the save head reactively.
  When the head snapshot has a non-null `liveSessionId`, the save enters
  **live mode** in `GameSavesVM` / the value tree:
  - Draft overlay is bypassed (not destroyed — drafts are keyed per snapshot
    and remain attached to frozen snapshots).
  - Scalar-leaf edits call `patchLiveSnapshot` through a 500 ms debounce,
    batching keys edited in the window into one patch.
  - Inbound game writes arrive through the existing reactive query and flow
    into the `valueById` chokepoint (Retree-native; no clones/spreads).
  - Web-side echo suppression mirrors the SDK: returned `snapshotHash`es are
    kept in a ring and matching reactive updates do not disturb in-progress
    edit state (focused inputs are never stomped mid-keystroke).
- **LIVE badge**: shown on the save row and the viewer header while
  `now - save.synchronizedAt < 60_000`, re-evaluated on a coarse ticker;
  tooltip shows relative last-write time. All strings via
  `src/i18n/source/en-US.json` (badge label, tooltip, live-mode hint replacing
  the draft banner).
- **Frozen heads unchanged**: a non-live head keeps draft mode exactly as
  shipped. The web never forks a live snapshot.

## Failure & edge matrix

| Situation                                       | Behavior                                                                                                         |
| ----------------------------------------------- | ---------------------------------------------------------------------------------------------------------------- |
| Session changes nothing                         | No fork; no snapshot created (decision 1).                                                                       |
| Two game sessions, same save                    | Second fork moves head; first session's next flush → typed conflict → `OnConflict`.                              |
| Web edits while game offline mid-session        | Patches land on the live head; game's reconnect flush merges (different keys) or LWWs (same keys).               |
| Web edits a _stale_ live head (no game running) | Allowed by design (decision 8); next load picks it up.                                                           |
| Game crash                                      | No cleanup needed: no live flag to clear; badge fades via recency; snapshot freezes when the next session forks. |
| Patch on frozen snapshot                        | Typed `staleTarget` result carrying the current head; the SDK re-forks on it, the web re-follows the head.       |
| Realtime provider absent / release build        | Entire feature inert; classic snapshot-per-commit behavior.                                                      |

## Phases

Each phase lands with its own tests; `npm run doctor` (web) and the EditMode
suite (SDK) green before moving on.

- [x] **Phase 1 — Convex**: schema field, `forkLiveSnapshot` /
      `patchLiveSnapshot` (+ `ForService`), merge + retention + validation,
      `gameSaves.test.ts` coverage (fork conflict, patch merge, tombstone
      upsert, restoredToAuthored, overlap rejection, frozen-snapshot
      rejection, retention sweep sparing manual snapshots, scope gates on
      both front doors).
- [x] **Phase 2 — SDK transport**: provider seam methods, Convex
      implementation, fake-provider surface, patch/fork wire types.
- [x] **Phase 3 — SDK synchronizer**: live-mode staging, debounce/max-latency
      flush, delta diff, offline composition, echo ring, auto-apply with
      dirty-key skip, `OnLiveContentChanged`; full test matrix on the
      existing fake-socket stack.
- [x] **Phase 4 — Web viewer**: live detection, write-through debounce,
      draft-mode bypass, LIVE badge + i18n, echo/focus protection; VM tests.
- [x] **Phase 5 — Sample + E2E**: HelloWorld applies inbound live edits in
      place (`NeoClient.ApplyExternalSaveContent` ←
      `OnLiveContentChanged`); live fork → in-place patch verified
      end-to-end against the dev deployment from the Unity editor (snapshot
      stamped with `liveSessionId`, merged `valuesJson`, head moved); the
      package README notes the behavior change when realtime is registered.

## Open seams (explicitly deferred)

- Retention cap as a `saveFilePolicies` field instead of a constant.
- Server-side patch rate limiting / metering hooks.
- Per-key dirty tracking in the generated save layer.
- Presence-based (connection-accurate) live indicator.
