// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;

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
            "\"serverId\":\"server-1\",\"snapshotId\":\"snap-1\"," +
            "\"snapshotRevision\":1,\"synchronizedAt\":3," +
            "\"version\":{\"id\":\"v1\",\"label\":\"1.0\"}," +
            "\"values\":{},\"createdAt\":1,\"updatedAt\":2}";

        public static RemoteGameSave Remote(
            string id,
            string snapshotId,
            long snapshotRevision = 1,
            string channel = TargetChannel)
        {
            return new RemoteGameSave
            {
                serverId = "server-" + id,
                id = id,
                snapshotId = snapshotId,
                snapshotRevision = snapshotRevision,
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
            long snapshotRevision = 1,
            string channel = TargetChannel) =>
            RemoteGameSaveSummary.FromRemote(
                Remote(id, snapshotId, snapshotRevision, channel));
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
        public readonly Queue<NeoCommitResult> sparseCommitResults = new();
        public readonly List<(string customId, NeoSparseSnapshotCommitRequest request)>
            sparseCommits = new();
        public readonly Queue<NeoCommitResult> stagedBeginResults = new();
        public readonly List<(string customId, NeoStagedSnapshotBeginRequest request)>
            stagedBegins = new();
        public Exception? stagedBeginThrows;
        public RemoteGameSave? getResult;
        public int getCalls;
        public Exception? getThrows;
        public NeoCloneResult? cloneResult;
        public readonly Queue<NeoSaveTransitionStatus> transitionStatuses = new();
        public readonly List<string> cloneRequests = new();
        public readonly List<string> transitionStatusRequests = new();
        public readonly List<string> transitionRetryRequests = new();
        public NeoChunkedCreateTarget? chunkedCreateTarget;
        public Exception? chunkedBeginThrows;
        public int chunkedBeginCalls;
        public readonly List<string> chunkedBeginFingerprints = new();
        public readonly Queue<Exception?> chunkedAppendFailures = new();
        public readonly List<List<GameSaveRecordChange>> chunkedAppends = new();
        public readonly List<string> chunkedAppendResumeTokens = new();
        public readonly List<long> chunkedAppendBaseRevisions = new();
        public RemoteGameSave? chunkedCompleteResult;
        public Exception? chunkedCompleteThrows;
        public int chunkedCompleteCalls;
        public readonly List<string> chunkedCompleteResumeTokens = new();
        public readonly List<string> archivedSaves = new();
        public readonly List<string> archivedSnapshots = new();
        private GameSaveRecordPage deltaPage = new GameSaveRecordPage { isDone = true };
        private readonly Dictionary<string, GameSaveRecordState> recordStates = new();
        private readonly Dictionary<string, GameSaveRecordDescriptor>
            manifestDescriptors = new();

        public void SetValueManifest(string snapshotId, long revision, string valuesJson)
        {
            manifestDescriptors.Clear();
            recordStates.Clear();
            foreach (var property in JObject.Parse(valuesJson).Properties())
            {
                var stateId = $"{snapshotId}:{property.Name}:{revision}";
                var descriptor = new GameSaveRecordDescriptor
                {
                    recordKind = NeoGameSaveRecordKinds.Value,
                    recordId = property.Name,
                    mapKey = (property.Value as JObject)?["mapKey"]?.Value<string>(),
                    recordStateId = stateId,
                    recordRevisionToken = $"token:{revision}:{property.Name}",
                    contentHashAlgorithm = "sha256-canonical-json-v1",
                    contentHash = $"content:{revision}:{property.Name}",
                    lastChangedRevision = revision,
                };
                manifestDescriptors[descriptor.LogicalKey] = descriptor;

                JObject data;
                if (property.Value is JObject row)
                {
                    data = (JObject)row.DeepClone();
                    data.Remove("id");
                    data.Remove("mapKey");
                }
                else
                {
                    data = new JObject { ["value"] = property.Value.DeepClone() };
                }
                recordStates[stateId] = new GameSaveRecordState
                {
                    id = stateId,
                    recordKind = NeoGameSaveRecordKinds.Value,
                    recordId = property.Name,
                    dataSchemaVersion = 1,
                    dataJson = data.ToString(Newtonsoft.Json.Formatting.None),
                };
            }
        }

        public void SetValueDelta(string snapshotId, long revision, string valuesJson)
        {
            var descriptors = new List<GameSaveRecordDescriptor>();
            recordStates.Clear();
            foreach (var property in JObject.Parse(valuesJson).Properties())
            {
                var stateId = $"{snapshotId}:{property.Name}:{revision}";
                descriptors.Add(new GameSaveRecordDescriptor
                {
                    recordKind = NeoGameSaveRecordKinds.Value,
                    recordId = property.Name,
                    mapKey = (property.Value as JObject)?["mapKey"]?.Value<string>(),
                    recordStateId = stateId,
                    recordRevisionToken = $"token:{revision}:{property.Name}",
                    contentHashAlgorithm = "sha256-canonical-json-v1",
                    contentHash = $"content:{revision}:{property.Name}",
                    lastChangedRevision = revision,
                });

                JObject data;
                if (property.Value is JObject row)
                {
                    data = (JObject)row.DeepClone();
                    data.Remove("id");
                    data.Remove("mapKey");
                }
                else
                {
                    data = new JObject { ["value"] = property.Value.DeepClone() };
                }
                recordStates[stateId] = new GameSaveRecordState
                {
                    id = stateId,
                    recordKind = NeoGameSaveRecordKinds.Value,
                    recordId = property.Name,
                    dataSchemaVersion = 1,
                    dataJson = data.ToString(Newtonsoft.Json.Formatting.None),
                };
            }
            deltaPage = new GameSaveRecordPage
            {
                page = descriptors,
                isDone = true,
            };
        }

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
                getResult ?? NeoSaveTestSupport.Remote(customId, snapshotId));

        public Awaitable<GameSaveRecordPage> GetSaveRecordManifestPageAsync(
            string customId, string snapshotId, GameSaveRecordPageRequest request) =>
            NeoAwaitable.FromResult(new GameSaveRecordPage
            {
                page = new List<GameSaveRecordDescriptor>(manifestDescriptors.Values),
                isDone = true,
            });

        public Awaitable<GameSaveRecordPage> GetSaveRecordDeltaPageAsync(
            string customId, string snapshotId, GameSaveRecordDeltaPageRequest request) =>
            NeoAwaitable.FromResult(deltaPage);

        public Awaitable<IReadOnlyList<GameSaveRecordState>> GetSaveRecordStatesAsync(
            string customId, string snapshotId, IReadOnlyList<string> recordStateIds)
        {
            var states = new List<GameSaveRecordState>();
            foreach (var id in recordStateIds)
            {
                if (recordStates.TryGetValue(id, out var state)) states.Add(state);
            }
            return NeoAwaitable.FromResult<IReadOnlyList<GameSaveRecordState>>(states);
        }

        public Awaitable<NeoCommitResult> CommitAsync(NeoSaveCommitRequest request, bool replaceSnapshot)
        {
            commits.Add((request, replaceSnapshot));
            return NeoAwaitable.FromResult(commitResults.Dequeue());
        }

        public Awaitable<NeoCommitResult> CommitSparseSnapshotAsync(
            string customId,
            NeoSparseSnapshotCommitRequest request)
        {
            sparseCommits.Add((customId, request));
            return NeoAwaitable.FromResult(
                sparseCommitResults.Count != 0
                    ? sparseCommitResults.Dequeue()
                    : commitResults.Dequeue());
        }

        public Awaitable<NeoCommitResult> BeginStagedSnapshotAsync(
            string customId,
            NeoStagedSnapshotBeginRequest request)
        {
            stagedBegins.Add((customId, request));
            if (stagedBeginThrows != null) throw stagedBeginThrows;
            return NeoAwaitable.FromResult(stagedBeginResults.Dequeue());
        }

        public Awaitable<NeoChunkedCreateTarget> BeginChunkedCreateAsync(
            NeoChunkedCreateRequest request)
        {
            chunkedBeginCalls++;
            chunkedBeginFingerprints.Add(request.uploadFingerprint);
            if (chunkedBeginThrows != null) throw chunkedBeginThrows;
            return NeoAwaitable.FromResult(
                chunkedCreateTarget
                ?? throw new InvalidOperationException(
                    "No chunked create target configured."));
        }

        public Awaitable<NeoLivePatchResult> AppendChunkedCreateAsync(
            string customId,
            string resumeToken,
            long baseSnapshotRevision,
            IReadOnlyList<GameSaveRecordChange> changes,
            NeoTimestamp updatedAt)
        {
            chunkedAppends.Add(new List<GameSaveRecordChange>(changes));
            chunkedAppendResumeTokens.Add(resumeToken);
            chunkedAppendBaseRevisions.Add(baseSnapshotRevision);
            if (chunkedAppendFailures.Count != 0
                && chunkedAppendFailures.Dequeue() is { } failure)
            {
                throw failure;
            }

            var descriptors = new List<GameSaveRecordDescriptor>();
            foreach (var change in changes)
            {
                string recordKind;
                string recordId;
                switch (change)
                {
                    case GameSaveValueReplaceChange value:
                        recordKind = NeoGameSaveRecordKinds.Value;
                        recordId = value.valueId;
                        break;
                    case GameSaveValuePatchChange value:
                        recordKind = NeoGameSaveRecordKinds.Value;
                        recordId = value.valueId;
                        break;
                    case GameSaveValueRestoreToAuthoredChange value:
                        recordKind = NeoGameSaveRecordKinds.Value;
                        recordId = value.valueId;
                        break;
                    case GameSaveStaticBindingSetChange binding:
                        recordKind = NeoGameSaveRecordKinds.StaticBinding;
                        recordId = binding.memberId;
                        break;
                    case GameSaveStaticBindingRestoreToAuthoredChange binding:
                        recordKind = NeoGameSaveRecordKinds.StaticBinding;
                        recordId = binding.memberId;
                        break;
                    default:
                        throw new InvalidOperationException(
                            "Unexpected record change in fake API.");
                }
                var descriptor = new GameSaveRecordDescriptor
                {
                    recordKind = recordKind,
                    recordId = recordId,
                    recordStateId = $"state-{recordId}",
                    recordRevisionToken = $"token-{recordId}",
                    contentHash = $"hash-{recordId}",
                    lastChangedRevision = chunkedAppends.Count,
                };
                manifestDescriptors[descriptor.LogicalKey] = descriptor;
                descriptors.Add(descriptor);
            }
            return NeoAwaitable.FromResult(NeoLivePatchResult.Patched(
                chunkedCreateTarget?.snapshotId ?? resumeToken,
                baseSnapshotRevision + 1,
                updatedAt,
                descriptors));
        }

        public Awaitable<RemoteGameSave> CompleteChunkedCreateAsync(
            string customId,
            string resumeToken)
        {
            chunkedCompleteCalls++;
            chunkedCompleteResumeTokens.Add(resumeToken);
            if (chunkedCompleteThrows != null) throw chunkedCompleteThrows;
            return NeoAwaitable.FromResult(
                chunkedCompleteResult
                ?? throw new InvalidOperationException(
                    "No completed chunked save configured."));
        }

        public Awaitable<NeoCloneResult> CloneSaveAsync(
            string customId,
            NeoCloneRequest request)
        {
            cloneRequests.Add(customId);
            if (cloneResult == null)
            {
                throw new InvalidOperationException("No clone result configured.");
            }

            return NeoAwaitable.FromResult(cloneResult);
        }

        public Awaitable<NeoSaveTransitionStatus> GetSaveTransitionStatusAsync(
            string customId)
        {
            transitionStatusRequests.Add(customId);
            if (transitionStatuses.Count == 0)
            {
                throw new InvalidOperationException(
                    "No save transition status configured.");
            }
            return NeoAwaitable.FromResult(transitionStatuses.Dequeue());
        }

        public Awaitable RetrySaveTransitionAsync(string customId)
        {
            transitionRetryRequests.Add(customId);
            return NeoAwaitable.Completed();
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
            return NeoAwaitable.FromResult(
                getResult ?? NeoSaveTestSupport.Remote(customId, "s"));
        }
    }
}
