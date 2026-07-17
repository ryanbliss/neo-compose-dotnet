// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using UnityEngine.Networking;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Runtime save operations against the Neo Compose REST API. Every request
    /// attaches the player's <c>Authorization: Bearer</c> token, fails fast when
    /// the player is signed out (before any network call), and maps HTTP failures
    /// to typed outcomes: <c>401</c> → re-authentication required
    /// (<see cref="NeoComposeNotSignedInException"/>), <c>403</c> → authorized-but-
    /// denied (<see cref="NeoComposeApiAuthorizationException"/>), and a commit
    /// <c>409</c> → a non-error optimistic-concurrency conflict surfaced through
    /// <see cref="NeoCommitResult"/>.
    /// </summary>
    public interface INeoApiClient
    {
        Awaitable<NeoSaveFileList> ListSavesAsync(string? targetReleaseChannelId);
        Awaitable<RemoteGameSave> GetSaveAsync(string customId);
        Awaitable<IReadOnlyList<RemoteGameSaveSummary>> GetSaveSnapshotsAsync(string customId);
        Awaitable<RemoteGameSave> GetSaveSnapshotAsync(string customId, string snapshotId);
        Awaitable<GameSaveRecordPage> GetSaveRecordManifestPageAsync(
            string customId,
            string snapshotId,
            GameSaveRecordPageRequest request);
        Awaitable<GameSaveRecordPage> GetSaveRecordDeltaPageAsync(
            string customId,
            string snapshotId,
            GameSaveRecordDeltaPageRequest request);
        Awaitable<IReadOnlyList<GameSaveRecordState>> GetSaveRecordStatesAsync(
            string customId,
            string snapshotId,
            IReadOnlyList<string> recordStateIds);
        Awaitable<NeoCommitResult> CommitAsync(NeoSaveCommitRequest request, bool replaceSnapshot);
        Awaitable<NeoChunkedCreateTarget> BeginChunkedCreateAsync(
            NeoChunkedCreateRequest request);
        Awaitable<NeoLivePatchResult> AppendChunkedCreateAsync(
            string customId,
            string snapshotId,
            IReadOnlyList<GameSaveRecordChange> changes,
            NeoTimestamp updatedAt);
        Awaitable<RemoteGameSave> CompleteChunkedCreateAsync(
            string customId,
            string snapshotId);
        Awaitable<NeoCloneResult> CloneSaveAsync(string customId, NeoCloneRequest request);
        Awaitable<NeoSaveTransitionStatus> GetSaveTransitionStatusAsync(string customId);
        Awaitable ArchiveSaveAsync(string customId);
        Awaitable<RemoteGameSave> ArchiveSnapshotAsync(string customId, string snapshotId);
    }

    /// <inheritdoc cref="INeoApiClient"/>
    public sealed class NeoApiClient : INeoApiClient
    {
        private readonly string apiBaseUrl;
        private readonly string projectId;
        private readonly INeoComposeAccessTokenProvider tokenProvider;
        private readonly INeoComposeHttpClient httpClient;
        private readonly INeoComposeSessionRefresher sessionRefresher;

        public NeoApiClient(
            string apiBaseUrl,
            string projectId,
            INeoComposeAccessTokenProvider tokenProvider,
            INeoComposeHttpClient httpClient,
            INeoComposeSessionRefresher sessionRefresher)
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                throw new ArgumentException("API base URL cannot be empty.", nameof(apiBaseUrl));
            }
            if (string.IsNullOrWhiteSpace(projectId))
            {
                throw new ArgumentException("Project id cannot be empty.", nameof(projectId));
            }

            this.apiBaseUrl = apiBaseUrl.Trim().TrimEnd('/');
            this.projectId = projectId.Trim();
            this.tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            this.sessionRefresher =
                sessionRefresher ?? throw new ArgumentNullException(nameof(sessionRefresher));
        }

        public async Awaitable<NeoSaveFileList> ListSavesAsync(string? targetReleaseChannelId)
        {
            var url = SavesUrl("/query");
            var body = JsonConvert.SerializeObject(new { targetReleaseChannelId });
            var operation = new NeoComposeApiOperation(
                "list this project's save files", projectId, ReadScope);
            var json = await PostAuthorizedAsync(url, operation, body);
            return Deserialize<NeoSaveFileList>(json, "save file list");
        }

        public async Awaitable<RemoteGameSave> GetSaveAsync(string customId)
        {
            RequireCustomId(customId);
            var url = SavesUrl($"/{UnityWebRequest.EscapeURL(customId)}/query");
            var operation = new NeoComposeApiOperation(
                "read this save file", projectId, ReadScope);
            var json = await PostAuthorizedAsync(url, operation);
            var save = Deserialize<RemoteGameSave>(json, "save file");
            await NeoGameSaveRecordSync.LoadManifestAsync(this, customId, save);
            return save;
        }

        public async Awaitable<IReadOnlyList<RemoteGameSaveSummary>> GetSaveSnapshotsAsync(
            string customId)
        {
            RequireCustomId(customId);
            var url = SavesUrl($"/{UnityWebRequest.EscapeURL(customId)}/snapshots/query");
            var operation = new NeoComposeApiOperation(
                "read this save file's snapshots", projectId, ReadScope);
            var json = await PostAuthorizedAsync(url, operation);
            var wrapper = Deserialize<SnapshotListWire>(json, "save snapshots");
            return wrapper.snapshots;
        }

        public async Awaitable<RemoteGameSave> GetSaveSnapshotAsync(
            string customId,
            string snapshotId)
        {
            RequireCustomId(customId);
            if (string.IsNullOrWhiteSpace(snapshotId))
            {
                throw new ArgumentException("Snapshot id cannot be empty.", nameof(snapshotId));
            }
            var url = SavesUrl(
                $"/{UnityWebRequest.EscapeURL(customId)}/snapshots/" +
                $"{UnityWebRequest.EscapeURL(snapshotId)}/query");
            var operation = new NeoComposeApiOperation(
                "read this save file snapshot", projectId, ReadScope);
            var json = await PostAuthorizedAsync(url, operation);
            var save = Deserialize<RemoteGameSave>(json, "save snapshot");
            await NeoGameSaveRecordSync.LoadManifestAsync(this, customId, save);
            return save;
        }

        public async Awaitable<GameSaveRecordPage> GetSaveRecordManifestPageAsync(
            string customId,
            string snapshotId,
            GameSaveRecordPageRequest request)
        {
            RequireRecordReadArgs(customId, snapshotId, request);
            var url = SnapshotRecordsUrl(customId, snapshotId, "/manifest/query");
            var operation = new NeoComposeApiOperation(
                "read this save snapshot's record manifest", projectId, ReadScope);
            var json = await PostAuthorizedAsync(
                url, operation, JsonConvert.SerializeObject(request));
            return Deserialize<GameSaveRecordPage>(json, "save record manifest page");
        }

        public async Awaitable<GameSaveRecordPage> GetSaveRecordDeltaPageAsync(
            string customId,
            string snapshotId,
            GameSaveRecordDeltaPageRequest request)
        {
            RequireRecordReadArgs(customId, snapshotId, request);
            if (request.throughRevision < request.afterRevision)
            {
                throw new ArgumentException(
                    "Delta throughRevision cannot precede afterRevision.", nameof(request));
            }
            var url = SnapshotRecordsUrl(customId, snapshotId, "/delta/query");
            var operation = new NeoComposeApiOperation(
                "read this save snapshot's record delta", projectId, ReadScope);
            var json = await PostAuthorizedAsync(
                url, operation, JsonConvert.SerializeObject(request));
            return Deserialize<GameSaveRecordPage>(json, "save record delta page");
        }

        public async Awaitable<IReadOnlyList<GameSaveRecordState>> GetSaveRecordStatesAsync(
            string customId,
            string snapshotId,
            IReadOnlyList<string> recordStateIds)
        {
            RequireCustomId(customId);
            RequireSnapshotId(snapshotId);
            if (recordStateIds == null)
            {
                throw new ArgumentNullException(nameof(recordStateIds));
            }
            var request = new GameSaveRecordStatesRequest();
            request.recordStateIds.AddRange(recordStateIds);
            var url = SnapshotRecordsUrl(customId, snapshotId, "/states/query");
            var operation = new NeoComposeApiOperation(
                "read this save snapshot's record states", projectId, ReadScope);
            var json = await PostAuthorizedAsync(
                url, operation, JsonConvert.SerializeObject(request));
            var response = Deserialize<GameSaveRecordStatesResponse>(
                json, "save record states");
            return response.states;
        }

        public async Awaitable<NeoCommitResult> CommitAsync(
            NeoSaveCommitRequest request,
            bool replaceSnapshot)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            RequireCustomId(request.customId);

            var url = SavesUrl("/commit");
            var body = JsonConvert.SerializeObject(new { save = request, replaceSnapshot });
            var operation = new NeoComposeApiOperation(
                "commit this save file", projectId, WriteScope);

            var response = await SendAuthorizedAsync(url, body);

            // 409 is not an error: the head moved, surface the server head so the
            // caller can resolve. Handle it before the generic error mapping.
            if (response.StatusCode == 409)
            {
                var conflict = Deserialize<CommitResponseWire>(response.Text, "save commit conflict");
                if (conflict.serverHead == null)
                {
                    throw new InvalidOperationException(
                        "Neo Compose save commit conflict response did not include the server head.");
                }

                await NeoGameSaveRecordSync.LoadManifestAsync(
                    this, request.customId, conflict.serverHead);

                return NeoCommitResult.Conflict(conflict.serverHead);
            }

            var json = ReadBody(url, operation, response);
            var committed = Deserialize<CommitResponseWire>(json, "save commit");
            if (committed.save == null)
            {
                throw new InvalidOperationException(
                    "Neo Compose save commit response did not include the committed save.");
            }

            await NeoGameSaveRecordSync.LoadManifestAsync(
                this, request.customId, committed.save);

            return NeoCommitResult.Committed(committed.save);
        }

        public async Awaitable<NeoCloneResult> CloneSaveAsync(
            string customId,
            NeoCloneRequest request)
        {
            RequireCustomId(customId);
            var url = SavesUrl($"/{UnityWebRequest.EscapeURL(customId)}/clone");
            var body = JsonConvert.SerializeObject(request ?? new NeoCloneRequest());
            var operation = new NeoComposeApiOperation(
                "clone this save file", projectId, WriteScope);
            var json = await PostAuthorizedAsync(url, operation, body);
            var response = Deserialize<CloneResponseWire>(json, "clone result");
            if (response.kind == "cloned" && response.save != null)
            {
                await NeoGameSaveRecordSync.LoadManifestAsync(
                    this, response.save.id, response.save);
                return NeoCloneResult.Cloned(response.save);
            }
            if (response.kind == "transitioning")
            {
                RequireTransitionIdentity(
                    response.customId, response.targetSnapshotId, "clone");
                return NeoCloneResult.Transitioning(
                    response.customId, response.targetSnapshotId);
            }
            throw new InvalidOperationException(
                $"Neo Compose clone response had unsupported kind '{response.kind}'.");
        }

        public async Awaitable<NeoChunkedCreateTarget> BeginChunkedCreateAsync(
            NeoChunkedCreateRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            RequireCustomId(request.customId);
            var url = SavesUrl("/chunked-create/begin");
            var operation = new NeoComposeApiOperation(
                "begin this large save file", projectId, WriteScope);
            var json = await PostAuthorizedAsync(
                url, operation, JsonConvert.SerializeObject(new { save = request }));
            var target = Deserialize<NeoChunkedCreateTarget>(
                json, "chunked save create target");
            RequireTransitionIdentity(
                target.customId, target.snapshotId, "chunked save create");
            return target;
        }

        public async Awaitable<NeoLivePatchResult> AppendChunkedCreateAsync(
            string customId,
            string snapshotId,
            IReadOnlyList<GameSaveRecordChange> changes,
            NeoTimestamp updatedAt)
        {
            RequireCustomId(customId);
            RequireSnapshotId(snapshotId);
            if (changes == null) throw new ArgumentNullException(nameof(changes));
            if (changes.Count == 0 || changes.Count > 64)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(changes),
                    "A chunked save append must contain between 1 and 64 changes.");
            }
            var url = SavesUrl(
                $"/{UnityWebRequest.EscapeURL(customId)}/chunked-create/append");
            var operation = new NeoComposeApiOperation(
                "append records to this large save file", projectId, WriteScope);
            var body = JsonConvert.SerializeObject(new
            {
                snapshotId,
                changes,
                updatedAt = updatedAt.EpochMilliseconds,
            });
            var json = await PostAuthorizedAsync(url, operation, body);
            return ParseLivePatchResult(json, "chunked save append");
        }

        public async Awaitable<RemoteGameSave> CompleteChunkedCreateAsync(
            string customId,
            string snapshotId)
        {
            RequireCustomId(customId);
            RequireSnapshotId(snapshotId);
            var url = SavesUrl(
                $"/{UnityWebRequest.EscapeURL(customId)}/chunked-create/complete");
            var operation = new NeoComposeApiOperation(
                "complete this large save file", projectId, WriteScope);
            var json = await PostAuthorizedAsync(
                url, operation, JsonConvert.SerializeObject(new { snapshotId }));
            var save = Deserialize<RemoteGameSave>(
                json, "completed chunked save file");
            await NeoGameSaveRecordSync.LoadManifestAsync(
                this, customId, save);
            return save;
        }

        public async Awaitable<NeoSaveTransitionStatus> GetSaveTransitionStatusAsync(
            string customId)
        {
            RequireCustomId(customId);
            var url = SavesUrl(
                $"/{UnityWebRequest.EscapeURL(customId)}/status/query");
            var operation = new NeoComposeApiOperation(
                "read this save file's copy status", projectId, ReadScope);
            var json = await PostAuthorizedAsync(url, operation);
            var response = Deserialize<TransitionStatusWire>(
                json, "save transition status");
            if (response.kind == "ready" && response.save != null)
            {
                await NeoGameSaveRecordSync.LoadManifestAsync(
                    this, response.save.id, response.save);
                return NeoSaveTransitionStatus.Ready(response.save);
            }
            if (response.kind == "copying")
            {
                RequireTransitionIdentity(
                    response.customId, response.targetSnapshotId, "copy status");
                return NeoSaveTransitionStatus.Copying(
                    response.customId, response.targetSnapshotId);
            }
            if (response.kind == "failed")
            {
                RequireTransitionIdentity(
                    response.customId, response.targetSnapshotId, "copy status");
                return NeoSaveTransitionStatus.Failed(
                    response.customId, response.targetSnapshotId, response.error);
            }
            throw new InvalidOperationException(
                $"Neo Compose save transition response had unsupported kind " +
                $"'{response.kind}'.");
        }

        public async Awaitable ArchiveSaveAsync(string customId)
        {
            RequireCustomId(customId);
            var url = SavesUrl($"/{UnityWebRequest.EscapeURL(customId)}/archive");
            var operation = new NeoComposeApiOperation(
                "archive this save file", projectId, ArchiveScope);
            await PostAuthorizedAsync(url, operation);
        }

        public async Awaitable<RemoteGameSave> ArchiveSnapshotAsync(string customId, string snapshotId)
        {
            RequireCustomId(customId);
            if (string.IsNullOrWhiteSpace(snapshotId))
            {
                throw new ArgumentException("Snapshot id cannot be empty.", nameof(snapshotId));
            }

            var url = SavesUrl(
                $"/{UnityWebRequest.EscapeURL(customId)}/snapshots/{UnityWebRequest.EscapeURL(snapshotId)}/archive");
            var operation = new NeoComposeApiOperation(
                "archive this save snapshot", projectId, ArchiveScope);
            var json = await PostAuthorizedAsync(url, operation);
            var save = Deserialize<RemoteGameSave>(json, "archived save file");
            await NeoGameSaveRecordSync.LoadManifestAsync(this, customId, save);
            return save;
        }

        private string ReadScope => $"project:{projectId}:save:read";
        private string WriteScope => $"project:{projectId}:save:write";
        private string ArchiveScope => $"project:{projectId}:save:archive";

        private string SavesUrl(string suffix) =>
            $"{apiBaseUrl}/api/projects/{UnityWebRequest.EscapeURL(projectId)}/saves{suffix}";

        private string SnapshotRecordsUrl(
            string customId,
            string snapshotId,
            string suffix) =>
            SavesUrl(
                $"/{UnityWebRequest.EscapeURL(customId)}/snapshots/" +
                $"{UnityWebRequest.EscapeURL(snapshotId)}/records{suffix}");

        private async Awaitable<string> PostAuthorizedAsync(
            string url,
            NeoComposeApiOperation operation,
            string body = "")
        {
            var response = await SendAuthorizedAsync(url, body);
            return ReadBody(url, operation, response);
        }

        private async Awaitable<NeoComposeWebResponse> SendAuthorizedAsync(string url, string body)
        {
            // Refresh proactively, then resolve the token. GetAccessToken throws
            // NeoComposeNotSignedInException when signed out or expired, so we fail
            // fast before issuing an unauthenticated request the server rejects.
            await sessionRefresher.RefreshIfDueAsync(apiBaseUrl);
            var token = tokenProvider.GetAccessToken(apiBaseUrl);
            return await httpClient.SendAsync(url, "POST", body, token);
        }

        private static string ReadBody(
            string url,
            NeoComposeApiOperation operation,
            NeoComposeWebResponse response)
        {
            if (response.IsConnectionError)
            {
                throw new InvalidOperationException(
                    $"Neo Compose request failed (connection) {url}: {response.Error}");
            }

            if (response.IsSuccessStatus) return response.Text;

            if (response.StatusCode == 401)
            {
                throw new NeoComposeNotSignedInException(
                    "Your Neo Compose session is no longer valid. Sign in again to continue.");
            }

            if (response.StatusCode == 403)
            {
                throw new NeoComposeApiAuthorizationException(
                    BuildForbiddenMessage(operation, response),
                    operation.ProjectId,
                    operation.RequiredScope);
            }

            if (response.StatusCode == 404)
            {
                throw new NeoComposeNotFoundException(
                    $"Neo Compose could not find the resource for '{operation.Description}' ({url}): {response.Text}");
            }

            throw new InvalidOperationException(
                $"Neo Compose request failed ({response.StatusCode}) {url}: {response.Text}");
        }

        private static string BuildForbiddenMessage(
            NeoComposeApiOperation operation,
            NeoComposeWebResponse response)
        {
            var message = $"You don't have permission to {operation.Description}.";
            if (!string.IsNullOrEmpty(operation.ProjectId))
            {
                message += $" (project {operation.ProjectId})";
            }

            var serverDetail = TryReadServerError(response.Text);
            if (serverDetail != null) message += $" {serverDetail}";
            return message;
        }

        private static string? TryReadServerError(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try
            {
                var error = JsonConvert.DeserializeObject<ApiErrorWire>(body);
                if (error != null && !string.IsNullOrEmpty(error.error)) return error.error;
            }
            catch (JsonException)
            {
                // Non-JSON body; ignore.
            }

            return null;
        }

        private static NeoLivePatchResult ParseLivePatchResult(
            string json,
            string description)
        {
            var response = Deserialize<LivePatchResponseWire>(json, description);
            if (response.kind == "patched")
            {
                if (string.IsNullOrWhiteSpace(response.snapshotId))
                {
                    throw new InvalidOperationException(
                        $"Neo Compose {description} response omitted snapshotId.");
                }
                return NeoLivePatchResult.Patched(
                    response.snapshotId,
                    response.snapshotRevision,
                    response.synchronizedAt,
                    response.changedDescriptors);
            }
            if (response.kind == "staleTarget" && response.serverHead != null)
            {
                return NeoLivePatchResult.StaleTarget(response.serverHead);
            }
            if (response.kind == "conflict" && response.conflict != null)
            {
                return NeoLivePatchResult.RecordConflict(response.conflict);
            }
            throw new InvalidOperationException(
                $"Neo Compose {description} response had unsupported kind " +
                $"'{response.kind}'.");
        }

        private static T Deserialize<T>(string json, string description)
            where T : class
        {
            var response = JsonConvert.DeserializeObject<T>(
                json,
                NeoSaveJson.ContentSettings);
            if (response == null)
            {
                throw new InvalidOperationException($"Neo Compose {description} response was empty.");
            }

            return response;
        }

        private static void RequireCustomId(string customId)
        {
            if (string.IsNullOrWhiteSpace(customId))
            {
                throw new ArgumentException("Save customId cannot be empty.", nameof(customId));
            }
        }

        private static void RequireSnapshotId(string snapshotId)
        {
            if (string.IsNullOrWhiteSpace(snapshotId))
            {
                throw new ArgumentException("Snapshot id cannot be empty.", nameof(snapshotId));
            }
        }

        private static void RequireTransitionIdentity(
            string customId,
            string targetSnapshotId,
            string description)
        {
            if (string.IsNullOrWhiteSpace(customId)
                || string.IsNullOrWhiteSpace(targetSnapshotId))
            {
                throw new InvalidOperationException(
                    $"Neo Compose {description} response did not include its " +
                    "customId and targetSnapshotId.");
            }
        }

        private static void RequireRecordReadArgs(
            string customId,
            string snapshotId,
            GameSaveRecordPageRequest request)
        {
            RequireCustomId(customId);
            RequireSnapshotId(snapshotId);
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.numItems <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request), "Record page size must be positive.");
            }
        }

        private sealed class CommitResponseWire
        {
            public string kind = "";
            public RemoteGameSave? save;
            public RemoteGameSave? serverHead;
        }

        private sealed class CloneResponseWire
        {
            public string kind = "";
            public RemoteGameSave? save;
            public string customId = "";
            public string targetSnapshotId = "";
        }

        private sealed class TransitionStatusWire
        {
            public string kind = "";
            public RemoteGameSave? save;
            public string customId = "";
            public string targetSnapshotId = "";
            public string? error;
        }

        private sealed class LivePatchResponseWire
        {
            public string kind = "";
            public string snapshotId = "";
            public long snapshotRevision;
            public NeoTimestamp synchronizedAt;
            public List<GameSaveRecordDescriptor> changedDescriptors = new();
            public GameSaveSnapshotRevisionSignal? serverHead;
            public GameSaveRecordConflict? conflict;
        }

        private sealed class SnapshotListWire
        {
            public List<RemoteGameSaveSummary> snapshots = new();
        }

        private sealed class ApiErrorWire
        {
            public string error = "";
        }
    }
}
