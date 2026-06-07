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
            "\"values\":" + values + ",\"attributeValueOverrides\":{}," +
            "\"createdAt\":1,\"updatedAt\":2}";

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
        public Exception? getThrows;
        public RemoteGameSave? cloneResult;
        public readonly List<string> archivedSaves = new();
        public readonly List<string> archivedSnapshots = new();

        public Awaitable<NeoSaveFileList> ListSavesAsync(string? targetReleaseChannelId) =>
            NeoAwaitable.FromResult(list);

        public Awaitable<RemoteGameSave> GetSaveAsync(string customId)
        {
            if (getThrows != null) throw getThrows;
            if (getResult == null)
            {
                throw new InvalidOperationException($"No remote save for \"{customId}\".");
            }

            return NeoAwaitable.FromResult(getResult);
        }

        public Awaitable<IReadOnlyList<RemoteGameSave>> GetSaveSnapshotsAsync(string customId)
        {
            IReadOnlyList<RemoteGameSave> empty = new List<RemoteGameSave>();
            return NeoAwaitable.FromResult(empty);
        }

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

        public Awaitable ArchiveSaveAsync(string customId)
        {
            archivedSaves.Add(customId);
            return NeoAwaitable.Completed();
        }

        public Awaitable<RemoteGameSave> ArchiveSnapshotAsync(string customId, string snapshotId)
        {
            archivedSnapshots.Add(snapshotId);
            return NeoAwaitable.FromResult(getResult ?? NeoSaveTestSupport.Remote(customId, "s", "h"));
        }
    }
}
