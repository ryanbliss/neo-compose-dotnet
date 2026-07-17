// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using Newtonsoft.Json;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Bounded, normalized live-save patch. Each logical record appears at
    /// most once and supplies its own optimistic-concurrency base.
    /// </summary>
    public sealed class NeoSavePatch
    {
        public List<GameSaveRecordChange> changes = new();

        // Local projection retained for source compatibility with games that
        // inspected staged patches. It is never serialized to the cloud wire.
        [JsonIgnore] public Dictionary<string, Newtonsoft.Json.Linq.JToken> entries = new();
        [JsonIgnore] public List<string> restoredToAuthored = new();
        [JsonIgnore] public Dictionary<string, string?> baseMapKeys = new();
        [JsonIgnore] public Dictionary<string, string?> staticBindings = new();
        [JsonIgnore] public List<string> restoredStaticBindingsToAuthored = new();

        [JsonIgnore]
        public bool IsEmpty => changes.Count == 0;
    }

    public sealed class NeoLiveForkRequest
    {
        public string customId = "";
        public string liveSessionId = "";
        public string baseSnapshotId = "";
        public long baseSnapshotRevision;
        public VersionData version = new();
        public NeoSavePatch patch = new();
        public List<GameRuntimePlatform>? platforms;
        public List<GameSystemInfo>? systems;
        public List<GameInputDeviceInfo>? inputDevices;
        public NeoTimestamp updatedAt;
    }

    public sealed class NeoLivePatchRequest
    {
        public string customId = "";
        public string snapshotId = "";
        public NeoSavePatch patch = new();
        public NeoTimestamp updatedAt;
    }

    public enum NeoLivePatchOutcome
    {
        Patched,
        StaleTarget,
        Conflict,
    }

    /// <summary>
    /// Record-head patch result. A writer immediately receives changed
    /// descriptors and the new revision, so it need not wait for its own
    /// realtime signal echo.
    /// </summary>
    public sealed class NeoLivePatchResult
    {
        private NeoLivePatchResult(NeoLivePatchOutcome outcome)
        {
            Outcome = outcome;
        }

        public NeoLivePatchOutcome Outcome { get; }
        public string SnapshotId { get; private set; } = "";
        public long SnapshotRevision { get; private set; }
        public NeoTimestamp SynchronizedAt { get; private set; }
        public List<GameSaveRecordDescriptor> ChangedDescriptors { get; private set; } = new();
        public GameSaveSnapshotRevisionSignal? ServerHead { get; private set; }
        public GameSaveRecordConflict? Conflict { get; private set; }

        [System.Obsolete("Use SnapshotRevision.")]
        public string SnapshotHash { get; private set; } = "";

        public bool IsStaleTarget => Outcome == NeoLivePatchOutcome.StaleTarget;
        public bool IsConflict => Outcome == NeoLivePatchOutcome.Conflict;

        public static NeoLivePatchResult Patched(
            string snapshotId,
            long snapshotRevision,
            NeoTimestamp synchronizedAt,
            List<GameSaveRecordDescriptor>? changedDescriptors) =>
            new NeoLivePatchResult(NeoLivePatchOutcome.Patched)
            {
                SnapshotId = snapshotId,
                SnapshotRevision = snapshotRevision,
                SynchronizedAt = synchronizedAt,
                ChangedDescriptors = changedDescriptors ?? new List<GameSaveRecordDescriptor>(),
            };

        [System.Obsolete("Use the snapshot-revision patch result.")]
        public static NeoLivePatchResult Patched(
            string snapshotId,
            string snapshotHash,
            NeoTimestamp synchronizedAt) =>
            new NeoLivePatchResult(NeoLivePatchOutcome.Patched)
            {
                SnapshotId = snapshotId,
                SnapshotHash = snapshotHash,
                SynchronizedAt = synchronizedAt,
            };

        public static NeoLivePatchResult StaleTarget(GameSaveSnapshotRevisionSignal serverHead) =>
            new NeoLivePatchResult(NeoLivePatchOutcome.StaleTarget)
            {
                ServerHead = serverHead,
            };

        [System.Obsolete("Use a payload-free server-head signal.")]
        public static NeoLivePatchResult StaleTarget(RemoteGameSave serverHead) =>
            StaleTarget(new GameSaveSnapshotRevisionSignal
            {
                snapshotId = serverHead.snapshotId,
                snapshotRevision = serverHead.snapshotRevision,
            });

        public static NeoLivePatchResult RecordConflict(GameSaveRecordConflict conflict) =>
            new NeoLivePatchResult(NeoLivePatchOutcome.Conflict)
            {
                Conflict = conflict,
            };
    }
}
