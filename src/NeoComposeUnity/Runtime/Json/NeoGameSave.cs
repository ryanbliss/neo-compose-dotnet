// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using Newtonsoft.Json;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Payload-light save-list row. Lists carry only identity, head digests,
    /// display metadata, and lifecycle state; opening a row fetches the full
    /// <see cref="RemoteGameSave"/> detail payload separately.
    /// </summary>
    public sealed class RemoteGameSaveSummary
    {
        public string serverId = "";
        public string id = "";
        public string snapshotId = "";
        public string snapshotHash = "";
        public long snapshotRevision;

        public string projectId = "";
        public string releaseChannelId = "";
        public VersionData version = new VersionData();
        public string name = "";
        public string snapshotName = "";
        public NeoSaveActor author = new NeoSaveActor();
        public NeoSaveActor actor = new NeoSaveActor();
        public NeoTimestamp createdAt;
        public NeoTimestamp updatedAt;
        public NeoTimestamp synchronizedAt;
        public double? archivedAt;
        public double? snapshotArchivedAt;
        public string? liveSessionId;

        public static RemoteGameSaveSummary FromRemote(RemoteGameSave save) => new()
        {
            serverId = save.serverId,
            id = save.id,
            snapshotId = save.snapshotId,
            snapshotHash = save.snapshotHash,
            snapshotRevision = save.snapshotRevision,
            projectId = save.projectId,
            releaseChannelId = save.releaseChannelId,
            version = save.version,
            name = save.name,
            snapshotName = save.snapshotName,
            author = save.author,
            actor = save.actor,
            createdAt = save.createdAt,
            updatedAt = save.updatedAt,
            synchronizedAt = save.synchronizedAt,
            archivedAt = save.archivedAt,
            snapshotArchivedAt = save.snapshotArchivedAt,
            liveSessionId = save.liveSessionId,
        };
    }

    /// <summary>
    /// The actor (user / organization / system) recorded against a save's
    /// authorship and last write. Mirrors the server-side actor identifier.
    /// </summary>
    public sealed class NeoSaveActor
    {
        public string kind = "";
        public string id = "";
    }

    /// <summary>
    /// Content shared by every save shape — the parts that describe the saved
    /// game state rather than where it lives. The authored project export remains
    /// the immutable default graph; a save is a sparse overlay carrying only the
    /// value rows runtime code created or changed (<see cref="values"/>, opaque),
    /// each keyed by its stable value id (<c>save.values[id] ?? authored</c>).
    /// </summary>
    public abstract class NeoGameSaveBase
    {
        /// <summary>Human-readable save name.</summary>
        public string name = "";

        /// <summary>Stable project id this save belongs to.</summary>
        public string projectId = "";

        /// <summary>The project export version this save was created against.</summary>
        public VersionData version = new VersionData();

        /// <summary>
        /// Runtime-owned value rows, kept opaque until something needs typed
        /// access. See <see cref="NeoSaveValues"/>.
        /// </summary>
        public NeoSaveValues values = NeoSaveValues.Empty;

        /// <summary>
        /// Sparse static-member binding overrides keyed by member id.
        /// Missing keys inherit authored bindings; present null values are
        /// explicit tombstones. The target rows stay in <see cref="values"/>.
        /// </summary>
        public Dictionary<string, string?> staticBindings = new();

        /// <summary>
        /// Storage-partition presentation of the overlay
        /// (specs/list-member-and-tilegrid-scaling.md §6): the same rows as
        /// <see cref="values"/> split by their <c>mapKey</c> stamp, keyed by
        /// partition key. <see cref="values"/> remains the merged overlay and
        /// is what the SDK loads from; this sibling is informational on read
        /// and null when there are no non-main partitions.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, NeoSaveValues>? valuePartitions;

        /// <summary>Platforms that wrote this save, or null when diagnostics are off.</summary>
        public List<GameRuntimePlatform>? platforms;

        /// <summary>Systems that wrote this save, or null when diagnostics are off.</summary>
        public List<GameSystemInfo>? systems;

        /// <summary>Input devices captured while writing, or null when diagnostics are off.</summary>
        public List<GameInputDeviceInfo>? inputDevices;

        public NeoTimestamp createdAt;
        public NeoTimestamp updatedAt;

        /// <summary>
        /// Convenience over <see cref="NeoSaveValues.TryDeserialize"/>: attempts to
        /// materialize <see cref="values"/> into typed rows, leaving them opaque on
        /// failure.
        /// </summary>
        public bool TryDeserializeValues(out Dictionary<string, MemberValue> deserialized) =>
            values.TryDeserialize(out deserialized);
    }

    /// <summary>
    /// A save as it exists in the cloud: shared content plus server identity and
    /// synchronization metadata. Returned by the runtime save API. The head
    /// snapshot content is assembled from record state; the snapshot id and
    /// <see cref="snapshotRevision"/> drive synchronization and conflict detection.
    /// </summary>
    [JsonConverter(typeof(Schema8SaveEnvelopeConverter<RemoteGameSave>))]
    public sealed class RemoteGameSave : NeoGameSaveBase
    {
        /// <summary>Opaque server document id.</summary>
        public string serverId = "";

        /// <summary>Stable client-facing save id (the <c>customId</c>).</summary>
        public string id = "";

        /// <summary>The head snapshot id this content came from.</summary>
        public string snapshotId = "";

        /// <summary>
        /// Legacy local-artifact field. Cloud synchronization uses
        /// <see cref="snapshotRevision"/> instead.
        /// </summary>
        public string snapshotHash = "";

        /// <summary>Monotonic record-delta cursor for this snapshot.</summary>
        public long snapshotRevision;

        /// <summary>Client-side assembly cache; never part of cloud metadata.</summary>
        [JsonIgnore]
        public GameSaveRecordCache recordCache = new();

        /// <summary>The release channel this save is bound to.</summary>
        public string releaseChannelId = "";

        /// <summary>Display name of the head snapshot.</summary>
        public string snapshotName = "";

        /// <summary>Actor that created the save.</summary>
        public NeoSaveActor author = new NeoSaveActor();

        /// <summary>Actor that produced the head snapshot.</summary>
        public NeoSaveActor actor = new NeoSaveActor();

        /// <summary>Server time of the last synchronization.</summary>
        public NeoTimestamp synchronizedAt;

        /// <summary>
        /// Archive time as epoch milliseconds, or null when the save is active.
        /// Modeled as a plain nullable so a JSON <c>null</c> round-trips to null
        /// (a <see cref="NeoTimestamp"/> converter would collapse null to 0).
        /// </summary>
        public double? archivedAt;

        /// <summary>Archive time of this snapshot, or null while active.</summary>
        public double? snapshotArchivedAt;

        /// <summary>
        /// The play session that forked the head snapshot, or null for classic
        /// commit/clone snapshots. Non-null marks the head as a live snapshot,
        /// patchable in place via the live-session flow (see
        /// <c>specs/live-save-sessions.md</c>).
        /// </summary>
        public string? liveSessionId;
    }

    /// <summary>
    /// A save as it is persisted locally on the device. Carries the same content
    /// as a cloud save plus the identity/sync fields needed to reconcile against
    /// the cloud: the client-owned <see cref="customId"/> is always present, while
    /// the server identity (<see cref="serverId"/> / <see cref="snapshotId"/> /
    /// <see cref="snapshotHash"/> / <see cref="synchronizedAt"/>) is null until the
    /// save has been synchronized at least once (a from-scratch local save is
    /// local-only until its first commit).
    /// </summary>
    [JsonConverter(typeof(Schema8SaveEnvelopeConverter<LocalGameSave>))]
    public sealed class LocalGameSave : NeoGameSaveBase
    {
        /// <summary>Stable client-generated save id; the primary local key.</summary>
        public string customId = "";

        /// <summary>Release channel this save targets (set even for local-only saves).</summary>
        public string releaseChannelId = "";

        /// <summary>Display name of the locally-staged snapshot, if any.</summary>
        public string? snapshotName;

        /// <summary>Server document id once synchronized; null for local-only saves.</summary>
        public string? serverId;

        /// <summary>Last synchronized head snapshot id; null for local-only saves.</summary>
        public string? snapshotId;

        /// <summary>
        /// Legacy opaque local-artifact field retained so existing disk files
        /// continue to round-trip without loss.
        /// </summary>
        public string? snapshotHash;

        /// <summary>Last fully-applied cloud record revision.</summary>
        public long snapshotRevision;

        /// <summary>
        /// Persisted record descriptor and payload cache. It is additive to
        /// the opaque local save artifact, so older developer-controlled disk
        /// files still load with an empty cache.
        /// </summary>
        public GameSaveRecordCache recordCache = new();

        /// <summary>
        /// Last successful cloud sync time as epoch milliseconds; null when never
        /// synced. Plain nullable so a JSON <c>null</c> round-trips to null.
        /// </summary>
        public double? synchronizedAt;

        /// <summary>
        /// Local-store-only marker: true when this persisted copy is exactly
        /// the server-acknowledged state of a live-session flush (nothing was
        /// staged after it). Lets a later load adopt an in-place co-edited
        /// live snapshot silently instead of raising a conflict — there is
        /// nothing local to lose. Never sent to the server (commit requests
        /// are built field by field).
        /// </summary>
        public bool liveFlushed;

        /// <summary>True when this save has never been synchronized to the cloud.</summary>
        public bool IsLocalOnly => string.IsNullOrEmpty(serverId);

        /// <summary>
        /// Projects a freshly-fetched cloud save into the local persisted shape,
        /// copying content and recording the server identity / sync metadata.
        /// </summary>
        public static LocalGameSave FromRemote(RemoteGameSave remote)
        {
            return new LocalGameSave
            {
                customId = remote.id,
                releaseChannelId = remote.releaseChannelId,
                name = remote.name,
                projectId = remote.projectId,
                version = remote.version,
                values = remote.values,
                staticBindings = remote.staticBindings,
                valuePartitions = remote.valuePartitions,
                platforms = remote.platforms,
                systems = remote.systems,
                inputDevices = remote.inputDevices,
                createdAt = remote.createdAt,
                updatedAt = remote.updatedAt,
                snapshotName = remote.snapshotName,
                serverId = remote.serverId,
                snapshotId = remote.snapshotId,
                snapshotHash = remote.snapshotHash,
                snapshotRevision = remote.snapshotRevision,
                recordCache = remote.recordCache,
                synchronizedAt = remote.synchronizedAt.EpochMilliseconds,
            };
        }
    }
}
