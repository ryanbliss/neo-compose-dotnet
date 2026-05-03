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
        Task<NeoComposeUnityExportResponse> ExportProjectAsync(string apiBaseUrl, string projectId);
    }

    public sealed class NeoComposeEditorApiClient : INeoComposeEditorApiClient
    {
        public async Task<NeoComposeProjectListResponse> ListProjectsAsync(string apiBaseUrl, string? query)
        {
            var url = BuildUrl(apiBaseUrl, "/api/projects");
            if (!string.IsNullOrWhiteSpace(query) && query.Trim().Length > 1)
            {
                url += "?query=" + UnityWebRequest.EscapeURL(query.Trim());
            }

            var json = await PostAsync(url);
            var response = JsonConvert.DeserializeObject<NeoComposeProjectListResponse>(json);
            if (response == null)
            {
                throw new InvalidOperationException("Neo Compose project list response was empty.");
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
            var json = await PostAsync(url);
            var response = JsonConvert.DeserializeObject<NeoComposeUnityExportResponse>(json);
            if (response == null)
            {
                throw new InvalidOperationException("Neo Compose project export response was empty.");
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

        private static async Task<string> PostAsync(string url)
        {
            using var request = new UnityWebRequest(url, "POST");
            request.downloadHandler = new DownloadHandlerBuffer();
            request.uploadHandler = new UploadHandlerRaw(Array.Empty<byte>());
            request.SetRequestHeader("Content-Type", "application/json");

            await SendAsync(request);
            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new InvalidOperationException(
                    $"Neo Compose request failed ({request.responseCode}) {url}: {request.error}");
            }

            return request.downloadHandler.text;
        }

        private static Task SendAsync(UnityWebRequest request)
        {
            var completion = new TaskCompletionSource<bool>();
            var operation = request.SendWebRequest();
            operation.completed += _ => completion.SetResult(true);
            return completion.Task;
        }
    }
}
