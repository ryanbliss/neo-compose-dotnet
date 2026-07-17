// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using Newtonsoft.Json;

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

        /// <summary>
        /// Sparse static-member binding overrides keyed by member id.
        /// Values are target value ids or null tombstones.
        /// </summary>
        public Dictionary<string, string?> staticBindings = new();

        /// <summary>
        /// Storage-partition split of the commit
        /// (specs/list-member-and-tilegrid-scaling.md §6): when set,
        /// <see cref="values"/> carries ONLY main-partition rows and each
        /// non-main row rides in its partition's overlay here, keyed by
        /// partition key. Null (omitted from the wire) when the overlay has
        /// no partition-stamped rows — the pre-partition commit shape.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, NeoSaveValues>? valuePartitions;

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
    /// Metadata-only first phase of a large save creation. Record changes are
    /// appended separately in bounded batches before activation.
    /// </summary>
    public sealed class NeoChunkedCreateRequest
    {
        public string customId = "";
        public string name = "";
        public VersionData version = new();
        public string targetReleaseChannelId = "";
        public string? snapshotName;
        public string? liveSessionId;
        public List<GameRuntimePlatform>? platforms;
        public List<GameSystemInfo>? systems;
        public List<GameInputDeviceInfo>? inputDevices;
        public NeoTimestamp createdAt;
        public NeoTimestamp updatedAt;
    }

    /// <summary>Hidden destination accepted for a chunked save creation.</summary>
    public sealed class NeoChunkedCreateTarget
    {
        public string customId = "";
        public string snapshotId = "";
        public long snapshotRevision;
    }

    public enum NeoCloneOutcome
    {
        Cloned,
        Transitioning,
    }

    /// <summary>
    /// The accepted result of a cloud clone. Small snapshots may complete in
    /// the clone request; large snapshots return a durable transition identity
    /// that must be polled by its new <see cref="CustomId"/>.
    /// </summary>
    public sealed class NeoCloneResult
    {
        private NeoCloneResult(
            NeoCloneOutcome outcome,
            RemoteGameSave? clonedSave,
            string customId,
            string targetSnapshotId)
        {
            Outcome = outcome;
            ClonedSave = clonedSave;
            CustomId = customId;
            TargetSnapshotId = targetSnapshotId;
        }

        public NeoCloneOutcome Outcome { get; }
        public RemoteGameSave? ClonedSave { get; }
        public string CustomId { get; }
        public string TargetSnapshotId { get; }
        public bool IsTransitioning => Outcome == NeoCloneOutcome.Transitioning;

        public static NeoCloneResult Cloned(RemoteGameSave save) =>
            new NeoCloneResult(
                NeoCloneOutcome.Cloned,
                save,
                save.id,
                save.snapshotId);

        public static NeoCloneResult Transitioning(
            string customId,
            string targetSnapshotId) =>
            new NeoCloneResult(
                NeoCloneOutcome.Transitioning,
                null,
                customId,
                targetSnapshotId);
    }

    public enum NeoSaveTransitionOutcome
    {
        Ready,
        Copying,
        Failed,
    }

    /// <summary>
    /// Poll result for a durable snapshot-copy transition. Partial snapshot
    /// records are never exposed; only <see cref="ReadySave"/> is loadable.
    /// </summary>
    public sealed class NeoSaveTransitionStatus
    {
        private NeoSaveTransitionStatus(
            NeoSaveTransitionOutcome outcome,
            RemoteGameSave? readySave,
            string customId,
            string targetSnapshotId,
            string? error)
        {
            Outcome = outcome;
            ReadySave = readySave;
            CustomId = customId;
            TargetSnapshotId = targetSnapshotId;
            Error = error;
        }

        public NeoSaveTransitionOutcome Outcome { get; }
        public RemoteGameSave? ReadySave { get; }
        public string CustomId { get; }
        public string TargetSnapshotId { get; }
        public string? Error { get; }

        public static NeoSaveTransitionStatus Ready(RemoteGameSave save) =>
            new NeoSaveTransitionStatus(
                NeoSaveTransitionOutcome.Ready,
                save,
                save.id,
                save.snapshotId,
                null);

        public static NeoSaveTransitionStatus Copying(
            string customId,
            string targetSnapshotId) =>
            new NeoSaveTransitionStatus(
                NeoSaveTransitionOutcome.Copying,
                null,
                customId,
                targetSnapshotId,
                null);

        public static NeoSaveTransitionStatus Failed(
            string customId,
            string targetSnapshotId,
            string? error) =>
            new NeoSaveTransitionStatus(
                NeoSaveTransitionOutcome.Failed,
                null,
                customId,
                targetSnapshotId,
                error);
    }

    /// <summary>
    /// A page of saves visible to the caller for a (optional) target channel, plus
    /// a per-save <see cref="cloneRequired"/> flag: true when a save is bound to a
    /// different channel and must be cloned before it can load on the target.
    /// </summary>
    public sealed class NeoSaveFileList
    {
        public List<RemoteGameSaveSummary> saves = new();
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
