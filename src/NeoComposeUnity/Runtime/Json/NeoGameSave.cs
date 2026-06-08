// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;

namespace NeoCompose.Runtime.Json
{
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
        public bool TryDeserializeValues(out Dictionary<string, AttributeValue> deserialized) =>
            values.TryDeserialize(out deserialized);
    }

    /// <summary>
    /// A save as it exists in the cloud: shared content plus server identity and
    /// synchronization metadata. Returned by the runtime save API. The head
    /// snapshot's content is inlined; <see cref="snapshotHash"/> drives optimistic
    /// concurrency and conflict detection.
    /// </summary>
    public sealed class RemoteGameSave : NeoGameSaveBase
    {
        /// <summary>Opaque server document id.</summary>
        public string serverId = "";

        /// <summary>Stable client-facing save id (the <c>customId</c>).</summary>
        public string id = "";

        /// <summary>The head snapshot id this content came from.</summary>
        public string snapshotId = "";

        /// <summary>Content hash of the head snapshot; used to detect conflicts.</summary>
        public string snapshotHash = "";

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

        /// <summary>Hash of the last synchronized snapshot; the conflict base.</summary>
        public string? snapshotHash;

        /// <summary>
        /// Last successful cloud sync time as epoch milliseconds; null when never
        /// synced. Plain nullable so a JSON <c>null</c> round-trips to null.
        /// </summary>
        public double? synchronizedAt;

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
                platforms = remote.platforms,
                systems = remote.systems,
                inputDevices = remote.inputDevices,
                createdAt = remote.createdAt,
                updatedAt = remote.updatedAt,
                snapshotName = remote.snapshotName,
                serverId = remote.serverId,
                snapshotId = remote.snapshotId,
                snapshotHash = remote.snapshotHash,
                synchronizedAt = remote.synchronizedAt.EpochMilliseconds,
            };
        }
    }
}
