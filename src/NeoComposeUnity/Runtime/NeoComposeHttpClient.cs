// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Threading.Tasks;


namespace NeoCompose.Runtime
{
    /// <summary>
    /// HTTP seam for the editor API client. Abstracted so request authorization
    /// (bearer header attachment, fail-fast, status mapping) can be unit-tested
    /// without real network access.
    /// </summary>
    public interface INeoComposeHttpClient
    {
        Task<NeoComposeWebResponse> SendAsync(
            string url,
            string method,
            string? jsonBody,
            string? bearerToken);

        /// <summary>
        /// Downloads raw bytes from a pre-signed storage URL. Never carries a
        /// bearer token.
        /// </summary>
        Task<byte[]> DownloadAsync(string url);
    }

    public sealed class NeoComposeUnityHttpClient : INeoComposeHttpClient
    {
        public Task<NeoComposeWebResponse> SendAsync(
            string url,
            string method,
            string? jsonBody,
            string? bearerToken) =>
            NeoComposeWebRequests.SendAsync(url, method, jsonBody, bearerToken);

        public Task<byte[]> DownloadAsync(string url) => NeoComposeWebRequests.DownloadBytesAsync(url);
    }
}
