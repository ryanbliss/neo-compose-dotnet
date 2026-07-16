// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Tests
{
    /// <summary>Shared fakes + fixtures for the project-store / synchronizer tests.</summary>
    internal static class NeoSaveTestSupport
    {
        public const string TargetChannel = "channel-dev";

        public const string ProjectJson =
            "{\"metadata\":{\"projectId\":\"project-1\",\"versionId\":\"v1\"," +
            "\"semver\":{\"label\":\"1.0\"}}}";

        public static string SaveContent(string name, string values = "{}") =>
            "{\"name\":\"" + name + "\",\"projectId\":\"project-1\"," +
            "\"version\":{\"id\":\"v1\",\"label\":\"1.0\"}," +
            "\"values\":" + values + "," +
            "\"createdAt\":1,\"updatedAt\":2}";

        /// <summary>
        /// A local save that has already synced to the cloud (non-empty
        /// <c>serverId</c> ⇒ <see cref="LocalGameSave.IsLocalOnly"/> is false).
        /// Used to simulate an orphaned save whose cloud copy was later deleted.
        /// </summary>
        public static string SyncedSaveContent(string name) =>
            "{\"name\":\"" + name + "\",\"projectId\":\"project-1\"," +
            "\"releaseChannelId\":\"" + TargetChannel + "\"," +
            "\"serverId\":\"server-1\",\"snapshotId\":\"snap-1\",\"snapshotHash\":\"hash-1\"," +
            "\"synchronizedAt\":3," +
            "\"version\":{\"id\":\"v1\",\"label\":\"1.0\"}," +
            "\"values\":{},\"createdAt\":1,\"updatedAt\":2}";

        public static RemoteGameSave Remote(
            string id,
            string snapshotId,
            string snapshotHash,
            string channel = TargetChannel)
        {
            return new RemoteGameSave
            {
                serverId = "server-" + id,
                id = id,
                snapshotId = snapshotId,
                snapshotHash = snapshotHash,
                releaseChannelId = channel,
                name = "Cloud " + id,
                projectId = "project-1",
                version = new VersionData { id = "v1", label = "1.0" },
                values = NeoSaveValues.Empty,
                createdAt = 1,
                updatedAt = 2,
                synchronizedAt = 3,
            };
        }

        public static RemoteGameSaveSummary Summary(
            string id,
            string snapshotId,
            string snapshotHash,
            string channel = TargetChannel) =>
            RemoteGameSaveSummary.FromRemote(
                Remote(id, snapshotId, snapshotHash, channel));
    }

    /// <summary>An <see cref="IProjectDataSource"/> whose read completes on demand.</summary>
    internal sealed class ControllableProjectDataSource : IProjectDataSource
    {
        private readonly AwaitableCompletionSource<string> completion =
            new AwaitableCompletionSource<string>();

        public Awaitable<string> ReadProjectJsonAsync() => completion.Awaitable;

        public void Complete(string json) => completion.TrySetResult(json);
    }

    internal sealed class FakeApiClient : INeoApiClient
    {
        public NeoSaveFileList list = new NeoSaveFileList();
        public readonly Queue<NeoCommitResult> commitResults = new();
        public readonly List<(NeoSaveCommitRequest request, bool replaceSnapshot)> commits = new();
        public RemoteGameSave? getResult;
        public int getCalls;
        public Exception? getThrows;
        public RemoteGameSave? cloneResult;
        public readonly List<string> archivedSaves = new();
        public readonly List<string> archivedSnapshots = new();

        public Awaitable<NeoSaveFileList> ListSavesAsync(string? targetReleaseChannelId) =>
            NeoAwaitable.FromResult(list);

        public Awaitable<RemoteGameSave> GetSaveAsync(string customId)
        {
            getCalls++;
            if (getThrows != null) throw getThrows;
            if (getResult == null)
            {
                throw new InvalidOperationException($"No remote save for \"{customId}\".");
            }

            return NeoAwaitable.FromResult(getResult);
        }

        public Awaitable<IReadOnlyList<RemoteGameSaveSummary>> GetSaveSnapshotsAsync(
            string customId)
        {
            IReadOnlyList<RemoteGameSaveSummary> empty =
                new List<RemoteGameSaveSummary>();
            return NeoAwaitable.FromResult(empty);
        }

        public Awaitable<RemoteGameSave> GetSaveSnapshotAsync(
            string customId,
            string snapshotId) =>
            NeoAwaitable.FromResult(
                getResult ?? NeoSaveTestSupport.Remote(customId, snapshotId, "snapshot-hash"));

        public Awaitable<NeoCommitResult> CommitAsync(NeoSaveCommitRequest request, bool replaceSnapshot)
        {
            commits.Add((request, replaceSnapshot));
            return NeoAwaitable.FromResult(commitResults.Dequeue());
        }

        public Awaitable<RemoteGameSave> CloneSaveAsync(string customId, NeoCloneRequest request)
        {
            if (cloneResult == null)
            {
                throw new InvalidOperationException("No clone result configured.");
            }

            return NeoAwaitable.FromResult(cloneResult);
        }

        public Exception? archiveThrows;

        public Awaitable ArchiveSaveAsync(string customId)
        {
            archivedSaves.Add(customId);
            if (archiveThrows != null) throw archiveThrows;
            return NeoAwaitable.Completed();
        }

        public Awaitable<RemoteGameSave> ArchiveSnapshotAsync(string customId, string snapshotId)
        {
            archivedSnapshots.Add(snapshotId);
            return NeoAwaitable.FromResult(getResult ?? NeoSaveTestSupport.Remote(customId, "s", "h"));
        }
    }
}
