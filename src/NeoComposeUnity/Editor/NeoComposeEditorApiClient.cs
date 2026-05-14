// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine.Networking;

namespace NeoCompose.Unity.Editor
{
    public interface INeoComposeEditorApiClient
    {
        Task<NeoComposeProjectListResponse> ListProjectsAsync(string apiBaseUrl, string? query);
        Task<NeoComposeProjectEditResponse> UpdateProjectExportSettingsAsync(
            string apiBaseUrl,
            string projectId,
            string namespaceForGeneratedTypes,
            bool singleton);

        Task<NeoComposeUnityExportResponse> ExportProjectAsync(string apiBaseUrl, string projectId);
        Task<NeoComposeUnityExportFileDownloadResponse> ExportProjectFileDownloadsAsync(
            string apiBaseUrl,
            string projectId,
            string[] fileIds);

        Task<byte[]> DownloadFileAsync(string downloadUrl);
    }

    public sealed class NeoComposeEditorApiClient : INeoComposeEditorApiClient
    {
        private const int RequestTimeoutSeconds = 30;
        private const int FileDownloadTimeoutSeconds = 120;

        public async Task<NeoComposeProjectListResponse> ListProjectsAsync(string apiBaseUrl, string? query)
        {
            var url = BuildUrl(apiBaseUrl, "/api/projects");
            var trimmedQuery = query?.Trim();
            if (trimmedQuery != null && trimmedQuery.Length > 1)
            {
                url += "?query=" + UnityWebRequest.EscapeURL(trimmedQuery);
            }

            var json = await PostAsync(url);
            var response = JsonConvert.DeserializeObject<NeoComposeProjectListResponse>(json);
            if (response == null)
            {
                throw new InvalidOperationException("Neo Compose project list response was empty.");
            }

            return response;
        }

        public async Task<NeoComposeProjectEditResponse> UpdateProjectExportSettingsAsync(
            string apiBaseUrl,
            string projectId,
            string namespaceForGeneratedTypes,
            bool singleton)
        {
            if (string.IsNullOrWhiteSpace(projectId))
            {
                throw new ArgumentException("Project id cannot be empty.", nameof(projectId));
            }

            var url = BuildUrl(apiBaseUrl, $"/api/projects/{UnityWebRequest.EscapeURL(projectId)}/edit");
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
            var json = await PostAsync(url, JsonConvert.SerializeObject(request));
            var response = JsonConvert.DeserializeObject<NeoComposeProjectEditResponse>(json);
            if (response == null)
            {
                throw new InvalidOperationException("Neo Compose project edit response was empty.");
            }

            return response;
        }

        public async Task<NeoComposeUnityExportResponse> ExportProjectAsync(string apiBaseUrl, string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId))
            {
                throw new ArgumentException("Project id cannot be empty.", nameof(projectId));
            }

            var url = BuildUrl(apiBaseUrl, $"/api/projects/{UnityWebRequest.EscapeURL(projectId)}/export");
            var json = await PostAsync(url, "");
            var response = JsonConvert.DeserializeObject<NeoComposeUnityExportResponse>(json);
            if (response == null)
            {
                throw new InvalidOperationException("Neo Compose project export response was empty.");
            }

            return response;
        }

        public async Task<NeoComposeUnityExportFileDownloadResponse> ExportProjectFileDownloadsAsync(
            string apiBaseUrl,
            string projectId,
            string[] fileIds)
        {
            if (string.IsNullOrWhiteSpace(projectId))
            {
                throw new ArgumentException("Project id cannot be empty.", nameof(projectId));
            }

            var url = BuildUrl(apiBaseUrl, $"/api/projects/{UnityWebRequest.EscapeURL(projectId)}/export/files");
            var request = new NeoComposeUnityExportFileDownloadRequest
            {
                fileIds = fileIds,
            };
            var json = await PostAsync(url, JsonConvert.SerializeObject(request));
            var response = JsonConvert.DeserializeObject<NeoComposeUnityExportFileDownloadResponse>(json);
            if (response == null)
            {
                throw new InvalidOperationException("Neo Compose file export response was empty.");
            }

            return response;
        }

        public async Task<byte[]> DownloadFileAsync(string downloadUrl)
        {
            using var request = UnityWebRequest.Get(downloadUrl);
            request.timeout = FileDownloadTimeoutSeconds;
            await SendAsync(request, FileDownloadTimeoutSeconds, "Neo Compose file download");
            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new InvalidOperationException(
                    $"Neo Compose file download failed ({request.responseCode}) {downloadUrl}: {request.error}");
            }

            return request.downloadHandler.data;
        }

        private static string BuildUrl(string apiBaseUrl, string path)
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                throw new ArgumentException("API base URL cannot be empty.", nameof(apiBaseUrl));
            }

            return apiBaseUrl.Trim().TrimEnd('/') + path;
        }

        private static async Task<string> PostAsync(string url, string body = "")
        {
            using var request = new UnityWebRequest(url, "POST");
            request.timeout = RequestTimeoutSeconds;
            request.downloadHandler = new DownloadHandlerBuffer();
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
            request.SetRequestHeader("Content-Type", "application/json");

            await SendAsync(request, RequestTimeoutSeconds, "Neo Compose API request");
            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new InvalidOperationException(
                    $"Neo Compose request failed ({request.responseCode}) {url}: {request.error}");
            }

            return request.downloadHandler.text;
        }

        private static async Task SendAsync(
            UnityWebRequest request,
            int timeoutSeconds,
            string operationName)
        {
            var completion = new TaskCompletionSource<bool>();
            var operation = request.SendWebRequest();
            if (operation.isDone)
            {
                return;
            }

            operation.completed += _ =>
            {
                if (!completion.Task.IsCompleted)
                {
                    completion.SetResult(true);
                }
            };
            var timeout = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
            if (await Task.WhenAny(completion.Task, timeout) == completion.Task)
            {
                return;
            }

            request.Abort();
            throw new TimeoutException($"{operationName} timed out after {timeoutSeconds} seconds.");
        }
    }
}
