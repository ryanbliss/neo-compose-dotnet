// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Per-key delta against a live snapshot's sparse overlay map (see
    /// <c>specs/live-save-sessions.md</c>). <see cref="entries"/> upserts full
    /// overlay entries — an override or a <c>mark: "removed"</c> tombstone;
    /// tombstones are just entries — kept opaque as raw JSON tokens, the same
    /// trust level as <see cref="NeoSaveValues"/>. <see cref="restoredToAuthored"/>
    /// deletes the overlay key entirely so the value falls back to the authored
    /// default. The server rejects a key present in both value collections.
    /// </summary>
    public sealed class NeoSavePatch
    {
        public Dictionary<string, JToken> entries = new();
        public List<string> restoredToAuthored = new();

        /// <summary>
        /// Static-member binding upserts keyed by attribute id. Values are
        /// target value ids or null tombstones.
        /// </summary>
        public Dictionary<string, string?> staticBindings = new();

        /// <summary>
        /// Static-member bindings to delete from the sparse overlay so the
        /// authored <c>attribute.valueId</c> is visible again.
        /// </summary>
        public List<string> restoredStaticBindingsToAuthored = new();

        /// <summary>True when the patch changes nothing (the server rejects these).</summary>
        [JsonIgnore]
        public bool IsEmpty =>
            entries.Count == 0
            && restoredToAuthored.Count == 0
            && staticBindings.Count == 0
            && restoredStaticBindingsToAuthored.Count == 0;
    }

    /// <summary>
    /// First flush of a play session: forks the save's head into a new live
    /// snapshot with the patch applied. Mirrors the server fork validator.
    /// Same optimistic-concurrency contract as a commit — a moved
    /// <see cref="baseSnapshotId"/> returns a typed conflict, never a silent
    /// overwrite.
    /// </summary>
    public sealed class NeoLiveForkRequest
    {
        public string customId = "";

        /// <summary>Client-minted GUID identifying the play session owning the fork.</summary>
        public string liveSessionId = "";

        /// <summary>The head snapshot the session loaded.</summary>
        public string baseSnapshotId = "";

        public VersionData version = new VersionData();
        public NeoSavePatch patch = new NeoSavePatch();
        public List<GameRuntimePlatform>? platforms;
        public List<GameSystemInfo>? systems;
        public List<GameInputDeviceInfo>? inputDevices;
        public NeoTimestamp updatedAt;
    }

    /// <summary>
    /// In-place merge into the live head snapshot: every session flush after
    /// the fork, targeting the snapshot id the fork returned.
    /// </summary>
    public sealed class NeoLivePatchRequest
    {
        public string customId = "";
        public string snapshotId = "";
        public NeoSavePatch patch = new NeoSavePatch();
    }

    public enum NeoLivePatchOutcome
    {
        /// <summary>The merge landed; <see cref="NeoLivePatchResult.SnapshotHash"/>
        /// is the new head hash (and the echo-suppression token).</summary>
        Patched,

        /// <summary>The target snapshot froze — no longer the head, or the head
        /// but not a live snapshot. Typed rather than thrown (mirroring the
        /// commit conflict contract) so clients can re-fork or re-follow;
        /// <see cref="NeoLivePatchResult.ServerHead"/> is the current head.</summary>
        StaleTarget,
    }

    /// <summary>
    /// Outcome of an in-place live patch. On <see cref="NeoLivePatchOutcome.Patched"/>
    /// the returned hash is the caller's echo-suppression token: a
    /// head-subscription update carrying a hash this client produced is its own
    /// write coming back.
    /// </summary>
    public sealed class NeoLivePatchResult
    {
        private NeoLivePatchResult(
            NeoLivePatchOutcome outcome,
            string snapshotId,
            string snapshotHash,
            NeoTimestamp synchronizedAt,
            RemoteGameSave? serverHead)
        {
            Outcome = outcome;
            SnapshotId = snapshotId;
            SnapshotHash = snapshotHash;
            SynchronizedAt = synchronizedAt;
            ServerHead = serverHead;
        }

        public NeoLivePatchOutcome Outcome { get; }

        /// <summary>The patched head snapshot id, or empty on a stale target.</summary>
        public string SnapshotId { get; }

        /// <summary>The new head hash, or empty on a stale target.</summary>
        public string SnapshotHash { get; }

        public NeoTimestamp SynchronizedAt { get; }

        /// <summary>The current server head on a stale target, or null when patched.</summary>
        public RemoteGameSave? ServerHead { get; }

        public bool IsStaleTarget => Outcome == NeoLivePatchOutcome.StaleTarget;

        public static NeoLivePatchResult Patched(
            string snapshotId, string snapshotHash, NeoTimestamp synchronizedAt) =>
            new NeoLivePatchResult(
                NeoLivePatchOutcome.Patched, snapshotId, snapshotHash, synchronizedAt, null);

        public static NeoLivePatchResult StaleTarget(RemoteGameSave serverHead) =>
            new NeoLivePatchResult(
                NeoLivePatchOutcome.StaleTarget, "", "", default, serverHead);
    }
}
