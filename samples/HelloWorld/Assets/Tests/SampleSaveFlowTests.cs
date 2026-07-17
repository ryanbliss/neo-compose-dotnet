// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HelloWorld.Assets.Scripts;
using HelloWorld.Assets.Scripts.Neo;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace HelloWorld.Assets.Tests
{
    /// <summary>
    /// Exercises the sample's save-driven flow end to end through the project store:
    /// create a new game (local-only until the first commit), play, save, and see it
    /// listed on return to the menu; archive removes it; and a cloud-backed save
    /// round-trips to a second "device".
    /// </summary>
    public class SampleSaveFlowTests
    {
        private const string SampleProjectJson = "Assets/Resources/Neo/project.json";
        private const string Channel = "channel-1";
        private static readonly string SampleProjectSourceJson =
            File.ReadAllText(SampleProjectJson);

        private readonly List<string> tempDirs = new();
        private readonly List<NeoProjectStore> stores = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var store in stores) store.Dispose();
            stores.Clear();
            foreach (var dir in tempDirs)
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            tempDirs.Clear();
        }

        private string TempDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "neo-sample-" + Path.GetRandomFileName());
            tempDirs.Add(dir);
            return dir;
        }

        private NeoProjectStore Store(
            INeoLocalSaveStore localStore,
            INeoApiClient apiClient = null,
            string targetReleaseChannelId = "")
        {
            var store = new NeoProjectStore(
                dataSource: new NeoJsonProjectDataSource(SampleProjectSourceJson),
                localStore: localStore,
                apiClient: apiClient,
                targetReleaseChannelId: targetReleaseChannelId);
            stores.Add(store);
            return store;
        }

        [Test]
        public void CreateNew_IsLocalOnlyUntilCommit_ThenListedOnReturn()
        {
            var store = Store(new NeoFileLocalSaveStore(TempDir()));
            store.LoadAsync().GetAwaiter().GetResult();
            Assert.IsEmpty(store.Saves);

            var synchronizer = store.CreateNew("My Game");
            store.RefreshSavesAsync().GetAwaiter().GetResult();
            Assert.IsEmpty(store.Saves, "A new draft persists nothing until the first commit.");

            var neo = HelloWorldNeo.Load(synchronizer).GetAwaiter().GetResult();
            var destination = neo.Assets.Outposts.First(o => o.valueId != neo.Save.Location.valueId);
            neo.Save.World = destination.Planet;
            var worldId = neo.Save.World.optionId;
            neo.CommitAsync().GetAwaiter().GetResult();
            neo.Dispose();

            // Returning to the menu refreshes the list — the saved game now appears.
            store.RefreshSavesAsync().GetAwaiter().GetResult();
            Assert.AreEqual(1, store.Saves.Count);
            Assert.AreEqual(synchronizer.CustomId, store.Saves[0].customId);

            // Reopening it restores the played state.
            var reloaded = HelloWorldNeo.Load(store.Open(synchronizer.CustomId)).GetAwaiter().GetResult();
            Assert.AreEqual(worldId, reloaded.Save.World.optionId);
            reloaded.Dispose();
        }

        [Test]
        public void Archive_MarksSaveArchivedAndHidesItFromTheActiveList()
        {
            var store = Store(new NeoFileLocalSaveStore(TempDir()));
            store.LoadAsync().GetAwaiter().GetResult();

            var synchronizer = store.CreateNew("Doomed");
            var neo = HelloWorldNeo.Load(synchronizer).GetAwaiter().GetResult();
            neo.CommitAsync().GetAwaiter().GetResult();
            neo.Dispose();
            store.RefreshSavesAsync().GetAwaiter().GetResult();
            store.ArchiveAsync(synchronizer.CustomId).GetAwaiter().GetResult();

            Assert.IsTrue(
                store.Saves.Single(s => s.customId == synchronizer.CustomId).IsArchived,
                "Archiving marks the save archived so the menu can hide it.");
            var active = store.Saves.Where(s => !s.IsArchived).ToList();
            Assert.IsEmpty(active, "The menu's active list (non-archived) excludes archived saves.");
        }

        [Test]
        public void CloudRoundTrip_SyncsSaveToASecondDevice()
        {
            var cloud = new InMemoryCloud();

            // Device A: create + play + save with cloud sync wired.
            var storeA = Store(
                new NeoFileLocalSaveStore(TempDir()),
                cloud,
                Channel);
            storeA.LoadAsync().GetAwaiter().GetResult();

            var synchronizer = storeA.CreateNew("Cloud Game");
            var neoA = HelloWorldNeo.Load(synchronizer).GetAwaiter().GetResult();
            var destination = neoA.Assets.Outposts.First(o => o.valueId != neoA.Save.Location.valueId);
            neoA.Save.World = destination.Planet;
            var worldId = neoA.Save.World.optionId;
            neoA.CommitAsync().GetAwaiter().GetResult();
            var customId = synchronizer.CustomId;
            neoA.Dispose();

            Assert.IsTrue(cloud.saves.ContainsKey(customId), "Commit synced the save to the cloud.");

            // Device B: a fresh local store sharing the same cloud sees + loads the save.
            var storeB = Store(
                new NeoFileLocalSaveStore(TempDir()),
                cloud,
                Channel);
            storeB.LoadAsync().GetAwaiter().GetResult();

            Assert.IsTrue(
                storeB.Saves.Any(s => s.customId == customId),
                "The cloud save appears on a second device.");

            var neoB = HelloWorldNeo.Load(storeB.Open(customId)).GetAwaiter().GetResult();
            Assert.AreEqual(worldId, neoB.Save.World.optionId, "The played state round-trips through the cloud.");
            neoB.Dispose();
        }

        /// <summary>
        /// A minimal stateful in-memory <see cref="INeoApiClient"/> that behaves like a
        /// shared cloud: commits store a head snapshot, and reads serve it back — so two
        /// stores over the same instance simulate two devices syncing.
        /// </summary>
        private sealed class InMemoryCloud : INeoApiClient
        {
            public readonly Dictionary<string, RemoteGameSave> saves = new();

            public Awaitable<NeoSaveFileList> ListSavesAsync(string targetReleaseChannelId)
            {
                var list = new NeoSaveFileList();
                list.saves.AddRange(saves.Values.Select(RemoteGameSaveSummary.FromRemote));
                return NeoAwaitable.FromResult(list);
            }

            public Awaitable<RemoteGameSave> GetSaveAsync(string customId) =>
                saves.TryGetValue(customId, out var save)
                    ? NeoAwaitable.FromResult(save)
                    : throw new InvalidOperationException($"No cloud save \"{customId}\".");

            public Awaitable<IReadOnlyList<RemoteGameSaveSummary>> GetSaveSnapshotsAsync(
                string customId)
            {
                IReadOnlyList<RemoteGameSaveSummary> snapshots =
                    saves.TryGetValue(customId, out var save)
                        ? new List<RemoteGameSaveSummary>
                        {
                            RemoteGameSaveSummary.FromRemote(save),
                        }
                        : new List<RemoteGameSaveSummary>();
                return NeoAwaitable.FromResult(snapshots);
            }

            public Awaitable<RemoteGameSave> GetSaveSnapshotAsync(
                string customId,
                string snapshotId) =>
                GetSaveAsync(customId);

            public Awaitable<GameSaveRecordPage> GetSaveRecordManifestPageAsync(
                string customId, string snapshotId, GameSaveRecordPageRequest request) =>
                NeoAwaitable.FromResult(new GameSaveRecordPage { isDone = true });

            public Awaitable<GameSaveRecordPage> GetSaveRecordDeltaPageAsync(
                string customId, string snapshotId, GameSaveRecordDeltaPageRequest request) =>
                NeoAwaitable.FromResult(new GameSaveRecordPage { isDone = true });

            public Awaitable<IReadOnlyList<GameSaveRecordState>> GetSaveRecordStatesAsync(
                string customId, string snapshotId, IReadOnlyList<string> recordStateIds) =>
                NeoAwaitable.FromResult<IReadOnlyList<GameSaveRecordState>>(
                    new List<GameSaveRecordState>());

            public Awaitable<NeoCommitResult> CommitAsync(NeoSaveCommitRequest request, bool replaceSnapshot)
            {
                var mergedValues = request.values.Raw is JObject mainValues
                    ? (JObject)mainValues.DeepClone()
                    : new JObject();
                if (request.valuePartitions != null)
                {
                    foreach (var partition in request.valuePartitions.Values)
                    {
                        if (partition.Raw is not JObject partitionValues) continue;
                        foreach (var value in partitionValues.Properties())
                        {
                            mergedValues[value.Name] = value.Value.DeepClone();
                        }
                    }
                }
                var remote = new RemoteGameSave
                {
                    serverId = "server-" + request.customId,
                    id = request.customId,
                    snapshotId = Guid.NewGuid().ToString("N"),
                    snapshotRevision = 1,
                    releaseChannelId = Channel,
                    name = request.name,
                    projectId = "project-1",
                    version = request.version,
                    values = new NeoSaveValues(mergedValues),
                    createdAt = 1,
                    updatedAt = 2,
                    synchronizedAt = 3,
                };
                remote.recordCache.snapshotId = remote.snapshotId;
                remote.recordCache.snapshotRevision = remote.snapshotRevision;
                saves[request.customId] = remote;
                return NeoAwaitable.FromResult(NeoCommitResult.Committed(remote));
            }

            public Awaitable<NeoCommitResult> CommitSparseSnapshotAsync(
                string customId,
                NeoSparseSnapshotCommitRequest request)
            {
                var current = saves[customId];
                var values = current.values.Raw is JObject currentValues
                    ? (JObject)currentValues.DeepClone()
                    : new JObject();
                var bindings = new Dictionary<string, string?>(current.staticBindings);
                foreach (var change in request.changes)
                {
                    switch (change)
                    {
                        case GameSaveValueReplaceChange replace:
                            values[replace.valueId] = replace.value.DeepClone();
                            break;
                        case GameSaveValuePatchChange patch
                            when values[patch.valueId] is JObject row:
                            foreach (var field in patch.set)
                            {
                                row[field.Key] = field.Value.DeepClone();
                            }
                            foreach (var field in patch.unset) row.Remove(field);
                            break;
                        case GameSaveValueRestoreToAuthoredChange restore:
                            values.Remove(restore.valueId);
                            break;
                        case GameSaveStaticBindingSetChange binding:
                            bindings[binding.memberId] = binding.valueId;
                            break;
                        case GameSaveStaticBindingRestoreToAuthoredChange restoreBinding:
                            bindings.Remove(restoreBinding.memberId);
                            break;
                    }
                }
                current.snapshotId = Guid.NewGuid().ToString("N");
                current.snapshotRevision = 1;
                current.values = new NeoSaveValues(values);
                current.staticBindings = bindings;
                current.version = request.version;
                current.recordCache.snapshotId = current.snapshotId;
                current.recordCache.snapshotRevision = current.snapshotRevision;
                saves[customId] = current;
                return NeoAwaitable.FromResult(NeoCommitResult.Committed(current));
            }

            public Awaitable<NeoCommitResult> BeginStagedSnapshotAsync(
                string customId,
                NeoStagedSnapshotBeginRequest request) =>
                throw new NotSupportedException();

            public Awaitable<NeoCloneResult> CloneSaveAsync(
                string customId,
                NeoCloneRequest request) =>
                throw new NotSupportedException();

            public Awaitable<NeoChunkedCreateTarget> BeginChunkedCreateAsync(
                NeoChunkedCreateRequest request) =>
                throw new NotSupportedException();

            public Awaitable<NeoLivePatchResult> AppendChunkedCreateAsync(
                string customId,
                string resumeToken,
                long baseSnapshotRevision,
                IReadOnlyList<GameSaveRecordChange> changes,
                NeoTimestamp updatedAt) =>
                throw new NotSupportedException();

            public Awaitable<RemoteGameSave> CompleteChunkedCreateAsync(
                string customId,
                string resumeToken) =>
                throw new NotSupportedException();

            public Awaitable<NeoSaveTransitionStatus> GetSaveTransitionStatusAsync(
                string customId) =>
                throw new NotSupportedException();

            public Awaitable RetrySaveTransitionAsync(string customId) =>
                throw new NotSupportedException();

            public Awaitable ArchiveSaveAsync(string customId)
            {
                saves.Remove(customId);
                return NeoAwaitable.Completed();
            }

            public Awaitable<RemoteGameSave> ArchiveSnapshotAsync(string customId, string snapshotId) =>
                NeoAwaitable.FromResult(saves[customId]);
        }
    }
}
