// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// The save payload sent on a commit. Mirrors the server commit validator:
    /// <see cref="targetReleaseChannelId"/> chooses the destination channel and
    /// <see cref="baseSnapshotId"/> is the optimistic-concurrency base (a moved
    /// head returns a typed conflict, never a silent overwrite).
    /// </summary>
    public sealed class NeoSaveCommitRequest
    {
        public string customId = "";
        public string name = "";
        public VersionData version = new VersionData();
        public string targetReleaseChannelId = "";
        public NeoSaveValues values = NeoSaveValues.Empty;
        public Dictionary<string, TileGridDeltaContent> tileGridDeltas = new();
        public List<GameRuntimePlatform>? platforms;
        public List<GameSystemInfo>? systems;
        public List<GameInputDeviceInfo>? inputDevices;
        public NeoTimestamp createdAt;
        public NeoTimestamp updatedAt;

        /// <summary>The head snapshot this edit was based on, or null for a new save.</summary>
        public string? baseSnapshotId;

        /// <summary>Optional snapshot display name; the server generates one when null.</summary>
        public string? snapshotName;

        /// <summary>
        /// Live save sessions: a live session creating a brand-new save passes
        /// its session id so the created head is a live snapshot from snapshot
        /// one (the session then patches it in place). The server honors this
        /// only on the create branch; commits to an existing save stay classic.
        /// </summary>
        public string? liveSessionId;
    }

    /// <summary>Optional inputs to a clone: rename, source snapshot, destination channel.</summary>
    public sealed class NeoCloneRequest
    {
        public string? cloneName;
        public string? snapshotId;
        public string? targetReleaseChannelId;
    }

    /// <summary>
    /// A page of saves visible to the caller for a (optional) target channel, plus
    /// a per-save <see cref="cloneRequired"/> flag: true when a save is bound to a
    /// different channel and must be cloned before it can load on the target.
    /// </summary>
    public sealed class NeoSaveFileList
    {
        public List<RemoteGameSave> saves = new();
        public Dictionary<string, bool> cloneRequired = new();

        /// <summary>True when the named save must be cloned to load on the target channel.</summary>
        public bool RequiresClone(string customId) =>
            cloneRequired.TryGetValue(customId, out var required) && required;
    }

    public enum NeoCommitOutcome
    {
        /// <summary>The cloud accepted the commit; <see cref="NeoCommitResult.CommittedSave"/> is the new head.</summary>
        Committed,

        /// <summary>The server head moved; <see cref="NeoCommitResult.ServerHead"/> must be resolved against.</summary>
        Conflict,
    }

    /// <summary>
    /// Outcome of a commit. On <see cref="NeoCommitOutcome.Committed"/> the cloud
    /// accepted the write and returns the new head; on
    /// <see cref="NeoCommitOutcome.Conflict"/> the write was rejected and the
    /// current server head is returned so the SDK can resolve.
    /// </summary>
    public sealed class NeoCommitResult
    {
        private NeoCommitResult(
            NeoCommitOutcome outcome,
            RemoteGameSave? committedSave,
            RemoteGameSave? serverHead)
        {
            Outcome = outcome;
            CommittedSave = committedSave;
            ServerHead = serverHead;
        }

        public NeoCommitOutcome Outcome { get; }

        /// <summary>The committed head, or null on conflict.</summary>
        public RemoteGameSave? CommittedSave { get; }

        /// <summary>The current server head on conflict, or null on success.</summary>
        public RemoteGameSave? ServerHead { get; }

        public bool IsConflict => Outcome == NeoCommitOutcome.Conflict;

        public static NeoCommitResult Committed(RemoteGameSave save) =>
            new NeoCommitResult(NeoCommitOutcome.Committed, save, null);

        public static NeoCommitResult Conflict(RemoteGameSave serverHead) =>
            new NeoCommitResult(NeoCommitOutcome.Conflict, null, serverHead);
    }
}
