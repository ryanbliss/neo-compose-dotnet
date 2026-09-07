// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine.Networking;

using NeoCompose.Runtime;

namespace NeoCompose.Unity.Editor
{
    public interface INeoComposeEditorApiClient
    {
        Task<NeoComposeProjectListResponse> ListProjectsAsync(string apiBaseUrl, string? query);
        Task<NeoComposeProjectReleaseChannelListResponse> ListReleaseChannelsAsync(string apiBaseUrl, string projectId);
        Task<NeoComposeProjectVersionListResponse> ListVersionsAsync(string apiBaseUrl, string projectId);
        Task<NeoComposeProjectVersionStatusListResponse> ListVersionStatusesAsync(string apiBaseUrl, string projectId);
        Task<NeoComposeProjectVersionMetadataResponse> GetVersionMetadataAsync(
            string apiBaseUrl,
            string projectId,
            string versionId);
        Task<NeoComposeProjectEditResponse> UpdateProjectExportSettingsAsync(
            string apiBaseUrl,
            string projectId,
            string versionId,
            string namespaceForGeneratedTypes,
            bool singleton);

        Task<NeoComposeUnityExportResponse> ExportProjectAsync(string apiBaseUrl, string projectId, string versionId);
        Task<NeoComposeUnityExportDeltaManifestResponse> ExportProjectDeltaAsync(
            string apiBaseUrl,
            string projectId,
            string versionId,
            NeoComposeUnityExportCursor cursor);
        Task<NeoComposeUnityExportSnapshotResponse> ExportProjectSnapshotsAsync(
            string apiBaseUrl,
            string projectId,
            string versionId,
            string[] snapshotIds,
            NeoComposeProjectReadBase readBase);
        Task<NeoComposeUnityExportFileDownloadResponse> ExportProjectFileDownloadsAsync(
            string apiBaseUrl,
            string projectId,
            string versionId,
            string[] fileIds);

        Task<byte[]> DownloadFileAsync(string downloadUrl);
    }

    public sealed class NeoComposeEditorApiClient : INeoComposeEditorApiClient
    {
        // Full exports assemble paginated snapshots and generate C# before responding.
        private const int FullExportTimeoutSeconds = 300;

        private readonly INeoComposeAccessTokenProvider tokenProvider;
        private readonly INeoComposeHttpClient httpClient;
        private readonly INeoComposeSessionRefresher sessionRefresher;

        public NeoComposeEditorApiClient()
            : this(
                new NeoComposeTokenStoreAccessTokenProvider(apiBaseUrl => NeoComposeTokenStore.Create(apiBaseUrl)),
                new NeoComposeUnityHttpClient(),
                new NeoComposeSessionRefresher(apiBaseUrl => NeoComposeTokenStore.Create(apiBaseUrl)))
        {
        }

        public NeoComposeEditorApiClient(
            INeoComposeAccessTokenProvider tokenProvider,
            INeoComposeHttpClient httpClient,
            INeoComposeSessionRefresher? sessionRefresher = null)
        {
            this.tokenProvider = tokenProvider;
            this.httpClient = httpClient;
            this.sessionRefresher = sessionRefresher ?? new NeoComposeSessionRefresher(apiBaseUrl => NeoComposeTokenStore.Create(apiBaseUrl));
        }

        public async Task<NeoComposeProjectListResponse> ListProjectsAsync(string apiBaseUrl, string? query)
        {
            var url = BuildUrl(apiBaseUrl, "/api/projects");
            var trimmedQuery = query?.Trim();
            if (trimmedQuery != null && trimmedQuery.Length > 1)
            {
                url += "?query=" + UnityWebRequest.EscapeURL(trimmedQuery);
            }

            var operation = new NeoComposeApiOperation("list Neo Compose projects", requiredScope: "project:list");
            var json = await PostAuthorizedAsync(apiBaseUrl, url, operation);
            return Deserialize<NeoComposeProjectListResponse>(json, "project list");
        }

        public async Task<NeoComposeProjectReleaseChannelListResponse> ListReleaseChannelsAsync(
            string apiBaseUrl,
            string projectId)
        {
            RequireProjectId(projectId);
            var url = BuildUrl(apiBaseUrl, $"/api/projects/{UnityWebRequest.EscapeURL(projectId)}/release-channels");
            var operation = new NeoComposeApiOperation(
                "read this project's release channels", projectId, "project:release-channel:read");
            var json = await PostAuthorizedAsync(apiBaseUrl, url, operation);
            return Deserialize<NeoComposeProjectReleaseChannelListResponse>(json, "release channel");
        }

        public async Task<NeoComposeProjectVersionListResponse> ListVersionsAsync(
            string apiBaseUrl,
            string projectId)
        {
            RequireProjectId(projectId);
            var url = BuildUrl(apiBaseUrl, $"/api/projects/{UnityWebRequest.EscapeURL(projectId)}/versions");
            var operation = new NeoComposeApiOperation(
                "read this project's versions", projectId, "project:version:read");
            var json = await PostAuthorizedAsync(apiBaseUrl, url, operation);
            return Deserialize<NeoComposeProjectVersionListResponse>(json, "project versions");
        }

        public async Task<NeoComposeProjectVersionStatusListResponse> ListVersionStatusesAsync(
            string apiBaseUrl,
            string projectId)
        {
            RequireProjectId(projectId);
            var url = BuildUrl(apiBaseUrl, $"/api/projects/{UnityWebRequest.EscapeURL(projectId)}/version-statuses");
            var operation = new NeoComposeApiOperation(
                "read this project's version statuses", projectId, "project:version:status:read");
            var json = await PostAuthorizedAsync(apiBaseUrl, url, operation);
            return Deserialize<NeoComposeProjectVersionStatusListResponse>(json, "project version statuses");
        }

        public async Task<NeoComposeProjectVersionMetadataResponse> GetVersionMetadataAsync(
            string apiBaseUrl,
            string projectId,
            string versionId)
        {
            RequireProjectId(projectId);
            RequireVersionId(versionId);
            var url = BuildUrl(
                apiBaseUrl,
                $"/api/projects/{UnityWebRequest.EscapeURL(projectId)}/versions/{UnityWebRequest.EscapeURL(versionId)}");
            var operation = new NeoComposeApiOperation(
                "read this project version", projectId, "project:version:read");
            var json = await PostAuthorizedAsync(apiBaseUrl, url, operation);
            return Deserialize<NeoComposeProjectVersionMetadataResponse>(json, "project version metadata");
        }

        public async Task<NeoComposeProjectEditResponse> UpdateProjectExportSettingsAsync(
            string apiBaseUrl,
            string projectId,
            string versionId,
            string namespaceForGeneratedTypes,
            bool singleton)
        {
            RequireProjectId(projectId);
            RequireVersionId(versionId);
            var url = BuildUrl(
                apiBaseUrl,
                $"/api/projects/{UnityWebRequest.EscapeURL(projectId)}/versions/{UnityWebRequest.EscapeURL(versionId)}/edit");
            var request = new NeoComposeProjectEditRequest
            {
                exportSettings = new NeoComposeProjectExportSettings
                {
                    unity = new NeoComposeUnityExportSettings
                    {
                        namespaceForGeneratedTypes = namespaceForGeneratedTypes,
                        singleton = singleton,
                    },
                },
            };
            var operation = new NeoComposeApiOperation(
                "edit this project's Unity settings", projectId, "unity:settings:write");
            var json = await PostAuthorizedAsync(apiBaseUrl, url, operation, JsonConvert.SerializeObject(request));
            return Deserialize<NeoComposeProjectEditResponse>(json, "project edit");
        }

        public async Task<NeoComposeUnityExportResponse> ExportProjectAsync(
            string apiBaseUrl,
            string projectId,
            string versionId)
        {
            RequireProjectId(projectId);
            RequireVersionId(versionId);
            var url = BuildUrl(apiBaseUrl, $"/api/projects/{UnityWebRequest.EscapeURL(projectId)}/export");
            var operation = new NeoComposeApiOperation("export this project", projectId, "unity:export");
            var json = await PostAuthorizedAsync(
                apiBaseUrl, url, operation, JsonConvert.SerializeObject(new { versionId }),
                timeoutSeconds: FullExportTimeoutSeconds);
            return Deserialize<NeoComposeUnityExportResponse>(json, "project export");
        }

        public async Task<NeoComposeUnityExportDeltaManifestResponse> ExportProjectDeltaAsync(
            string apiBaseUrl,
            string projectId,
            string versionId,
            NeoComposeUnityExportCursor cursor)
        {
            RequireProjectId(projectId);
            RequireVersionId(versionId);
            if (cursor == null) throw new ArgumentNullException(nameof(cursor));
            var url = BuildUrl(apiBaseUrl, $"/api/projects/{UnityWebRequest.EscapeURL(projectId)}/export");
            var operation = new NeoComposeApiOperation("incrementally export this project", projectId, "unity:export");
            var json = await PostAuthorizedAsync(
                apiBaseUrl,
                url,
                operation,
                JsonConvert.SerializeObject(new { versionId, cursor }));
            return Deserialize<NeoComposeUnityExportDeltaManifestResponse>(json, "project export delta");
        }

        public async Task<NeoComposeUnityExportSnapshotResponse> ExportProjectSnapshotsAsync(
            string apiBaseUrl,
            string projectId,
            string versionId,
            string[] snapshotIds,
            NeoComposeProjectReadBase readBase)
        {
            RequireProjectId(projectId);
            RequireVersionId(versionId);
            if (snapshotIds == null) throw new ArgumentNullException(nameof(snapshotIds));
            if (readBase == null) throw new ArgumentNullException(nameof(readBase));
            readBase.Validate();
            var url = BuildUrl(
                apiBaseUrl,
                $"/api/projects/{UnityWebRequest.EscapeURL(projectId)}/export/snapshots");
            var operation = new NeoComposeApiOperation("read changed project export snapshots", projectId, "unity:export");
            var json = await PostAuthorizedAsync(
                apiBaseUrl,
                url,
                operation,
                JsonConvert.SerializeObject(new { versionId, snapshotIds, readBase }));
            return Deserialize<NeoComposeUnityExportSnapshotResponse>(json, "project export snapshots");
        }

        public async Task<NeoComposeUnityExportFileDownloadResponse> ExportProjectFileDownloadsAsync(
            string apiBaseUrl,
            string projectId,
            string versionId,
            string[] fileIds)
        {
            RequireProjectId(projectId);
            RequireVersionId(versionId);
            var url = BuildUrl(apiBaseUrl, $"/api/projects/{UnityWebRequest.EscapeURL(projectId)}/export/files");
            var request = new NeoComposeUnityExportFileDownloadRequest
            {
                versionId = versionId,
                fileIds = fileIds,
            };
            var operation = new NeoComposeApiOperation("download this project's files", projectId, "unity:export");
            var json = await PostAuthorizedAsync(apiBaseUrl, url, operation, JsonConvert.SerializeObject(request));
            return Deserialize<NeoComposeUnityExportFileDownloadResponse>(json, "file export");
        }

        public Task<byte[]> DownloadFileAsync(string downloadUrl)
        {
            // Pre-signed storage URL: no bearer token, the URL is self-authorizing.
            return httpClient.DownloadAsync(downloadUrl);
        }

        private async Task<string> PostAuthorizedAsync(
            string apiBaseUrl,
            string url,
            NeoComposeApiOperation operation,
            string body = "",
            int timeoutSeconds = NeoComposeWebRequests.DefaultTimeoutSeconds)
        {
            // Fail fast before issuing the request when the user is signed out or
            // the token has expired.
            await sessionRefresher.RefreshIfDueAsync(apiBaseUrl);
            var token = tokenProvider.GetAccessToken(apiBaseUrl);
            var response = await httpClient.SendAsync(url, "POST", body, token, timeoutSeconds);
            return ReadResponse(url, operation, response);
        }

        private static string ReadResponse(string url, NeoComposeApiOperation operation, NeoComposeWebResponse response)
        {
            if (response.IsConnectionError)
            {
                throw new InvalidOperationException(
                    $"Neo Compose request failed (connection) {url}: {response.Error}");
            }

            if (response.IsSuccessStatus) return response.Text;
            if (response.StatusCode == 409 && TryReadServerError(response.Text) == "project-read-restart")
                throw new NeoComposeProjectReadRestartException();

            // 401: authentication failure. The session is gone or invalid; the
            // caller routes to re-sign-in. The device flow has no refresh token,
            // so this always means full re-authentication.
            if (response.StatusCode == 401)
            {
                throw new NeoComposeNotSignedInException(
                    "Your Neo Compose session is no longer valid. Sign in again to continue.");
            }

            // 403: authorization failure. The user stays signed in; surface a
            // specific, non-destructive message. Never retry or re-auth.
            if (response.StatusCode == 403)
            {
                throw new NeoComposeApiAuthorizationException(
                    BuildForbiddenMessage(operation, response),
                    operation.ProjectId,
                    operation.RequiredScope);
            }

            throw new InvalidOperationException(
                $"Neo Compose request failed ({response.StatusCode}) {url}: {response.Text}");
        }

        private static string BuildForbiddenMessage(NeoComposeApiOperation operation, NeoComposeWebResponse response)
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
                var error = JsonConvert.DeserializeObject<NeoComposeDeviceErrorResponse>(body);
                if (error != null && error.error.Length > 0) return error.error;
            }
            catch (JsonException)
            {
                // Non-JSON body; ignore.
            }

            return null;
        }

        private static T Deserialize<T>(string json, string description)
            where T : class
        {
            var response = JsonConvert.DeserializeObject<T>(json);
            if (response == null)
            {
                throw new InvalidOperationException($"Neo Compose {description} response was empty.");
            }

            return response;
        }

        private static string BuildUrl(string apiBaseUrl, string path)
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                throw new ArgumentException("API base URL cannot be empty.", nameof(apiBaseUrl));
            }

            return apiBaseUrl.Trim().TrimEnd('/') + path;
        }

        private static void RequireProjectId(string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId))
            {
                throw new ArgumentException("Project id cannot be empty.", nameof(projectId));
            }
        }

        private static void RequireVersionId(string versionId)
        {
            if (string.IsNullOrWhiteSpace(versionId))
            {
                throw new ArgumentException("Version id cannot be empty.", nameof(versionId));
            }
        }
    }
}
