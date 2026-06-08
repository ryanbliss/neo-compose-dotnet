// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NeoCompose.Unity.Editor;
using NUnit.Framework;

using NeoCompose.Runtime;

namespace NeoCompose.Tests
{
    public class NeoComposeEditorApiClientTests
    {
        private const string ApiBaseUrl = "https://example.test";
        private const string ProjectId = "project-1";
        private const string VersionId = "version-1";

        // UAUTH-030
        [Test]
        public async Task EveryAuthorizedRequest_AttachesBearerToken()
        {
            var http = new FakeHttpClient();
            var client = NewClient(new FakeProvider("the-token"), http);

            await client.ListProjectsAsync(ApiBaseUrl, null);
            await client.ListReleaseChannelsAsync(ApiBaseUrl, ProjectId);
            await client.ListVersionsAsync(ApiBaseUrl, ProjectId);
            await client.ListVersionStatusesAsync(ApiBaseUrl, ProjectId);
            await client.GetVersionMetadataAsync(ApiBaseUrl, ProjectId, VersionId);
            await client.UpdateProjectExportSettingsAsync(ApiBaseUrl, ProjectId, VersionId, "Ns", true);
            await client.ExportProjectAsync(ApiBaseUrl, ProjectId, VersionId);
            await client.ExportProjectFileDownloadsAsync(ApiBaseUrl, ProjectId, VersionId, new[] { "file-1" });

            Assert.AreEqual(8, http.sends.Count);
            foreach (var send in http.sends)
            {
                Assert.AreEqual("the-token", send.bearer, $"Request to {send.url} must carry the bearer token.");
            }
        }

        [Test]
        public async Task AuthorizedRequest_AsksSessionRefresherBeforeSendingApiRequest()
        {
            var http = new FakeHttpClient();
            var refresher = new RecordingRefresher();
            var client = NewClient(new FakeProvider("the-token"), http, refresher);

            await client.ExportProjectAsync(ApiBaseUrl, ProjectId, VersionId);

            Assert.AreEqual(1, refresher.refreshCalls);
            Assert.AreEqual(ApiBaseUrl, refresher.lastApiBaseUrl);
            Assert.AreEqual(1, http.sends.Count);
        }

        // UAUTH-031
        [Test]
        public void AuthorizedRequest_FailsFastWhenSignedOut_WithoutSending()
        {
            var http = new FakeHttpClient();
            var client = NewClient(new FakeProvider(null), http);

            Assert.ThrowsAsync<NeoComposeNotSignedInException>(
                async () => await client.ExportProjectAsync(ApiBaseUrl, ProjectId, VersionId));
            Assert.AreEqual(0, http.sends.Count, "No request may be issued when signed out.");
        }

        // UAUTH-032
        [Test]
        public async Task DownloadFile_DoesNotAttachBearerAndUsesDownloadPath()
        {
            var http = new FakeHttpClient();
            var client = NewClient(new FakeProvider("the-token"), http);

            var bytes = await client.DownloadFileAsync("https://files.example.test/signed");

            Assert.AreEqual(0, http.sends.Count, "File downloads must not go through the bearer-authorized path.");
            CollectionAssert.AreEqual(new[] { "https://files.example.test/signed" }, http.downloads);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, bytes);
        }

        // UAUTH-037 / UAUTH-042
        [Test]
        public void Unauthorized401_ThrowsNotSignedInWithoutRetry()
        {
            var http = new FakeHttpClient
            {
                status = 401,
                body = "{\"error\":\"Bearer token is invalid or expired.\"}",
            };
            var client = NewClient(new FakeProvider("the-token"), http);

            Assert.ThrowsAsync<NeoComposeNotSignedInException>(
                async () => await client.ExportProjectAsync(ApiBaseUrl, ProjectId, VersionId));
            Assert.AreEqual(1, http.sends.Count, "401 must not be retried.");
        }

        // UAUTH-038 / UAUTH-039 / UAUTH-043
        [Test]
        public void Forbidden403OnSettingsEdit_ThrowsCapabilitySpecificAuthorizationError()
        {
            var http = new FakeHttpClient
            {
                status = 403,
                body = "{\"error\":\"Bearer token is missing required scope \\\"unity:settings:write\\\".\"}",
            };
            var client = NewClient(new FakeProvider("the-token"), http);

            var ex = Assert.ThrowsAsync<NeoComposeApiAuthorizationException>(
                async () => await client.UpdateProjectExportSettingsAsync(ApiBaseUrl, ProjectId, VersionId, "Ns", true));

            StringAssert.Contains("edit this project's Unity settings", ex!.Message);
            Assert.AreEqual("unity:settings:write", ex.RequiredScope);
            Assert.AreEqual(ProjectId, ex.ProjectId);
            Assert.AreEqual(1, http.sends.Count, "403 must not be retried or re-authenticated.");
        }

        // UAUTH-040 / UAUTH-043
        [Test]
        public void Forbidden403OnExport_KeepsUserSignedInWithExportMessage()
        {
            var http = new FakeHttpClient { status = 403, body = "{}" };
            var provider = new FakeProvider("the-token");
            var client = NewClient(provider, http);

            var ex = Assert.ThrowsAsync<NeoComposeApiAuthorizationException>(
                async () => await client.ExportProjectAsync(ApiBaseUrl, ProjectId, VersionId));

            StringAssert.Contains("export this project", ex!.Message);
            // A 403 is not an auth failure: the provider is never asked to drop
            // the token, and the request is not retried.
            Assert.IsTrue(provider.TryGetAccessToken(ApiBaseUrl, out _), "User must remain signed in after a 403.");
            Assert.AreEqual(1, http.sends.Count);
        }

        // UAUTH-031
        [Test]
        public void TokenProvider_ReturnsTokenWhenValid()
        {
            var provider = new NeoComposeTokenStoreAccessTokenProvider(
                _ => StoreWith(ValidToken()),
                () => DateTimeOffset.UtcNow);

            Assert.AreEqual("access", provider.GetAccessToken(ApiBaseUrl));
            Assert.IsTrue(provider.TryGetAccessToken(ApiBaseUrl, out var token));
            Assert.AreEqual("access", token);
        }

        // UAUTH-031
        [Test]
        public void TokenProvider_ThrowsWhenSignedOut()
        {
            var provider = new NeoComposeTokenStoreAccessTokenProvider(
                _ => StoreWith(null),
                () => DateTimeOffset.UtcNow);

            Assert.IsFalse(provider.TryGetAccessToken(ApiBaseUrl, out _));
            Assert.Throws<NeoComposeNotSignedInException>(() => provider.GetAccessToken(ApiBaseUrl));
        }

        // UAUTH-031
        [Test]
        public void TokenProvider_TreatsExpiredTokenAsSignedOut()
        {
            var now = DateTimeOffset.UtcNow;
            var expired = new NeoComposeStoredToken(
                "access", now.AddMinutes(-1).ToUnixTimeSeconds(), new[] { "openid" }, ApiBaseUrl, "", "");
            var provider = new NeoComposeTokenStoreAccessTokenProvider(_ => StoreWith(expired), () => now);

            Assert.IsFalse(provider.TryGetAccessToken(ApiBaseUrl, out _));
            Assert.Throws<NeoComposeNotSignedInException>(() => provider.GetAccessToken(ApiBaseUrl));
        }

        private static NeoComposeStoredToken ValidToken() =>
            new NeoComposeStoredToken(
                "access",
                DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds(),
                new[] { "openid" },
                ApiBaseUrl,
                "",
                "");

        private static INeoComposeTokenStore StoreWith(NeoComposeStoredToken? token) =>
            new InMemoryTokenStore { saved = token };

        private static NeoComposeEditorApiClient NewClient(
            INeoComposeAccessTokenProvider provider,
            INeoComposeHttpClient http,
            INeoComposeSessionRefresher? refresher = null) =>
            new NeoComposeEditorApiClient(provider, http, refresher ?? new NoopRefresher());

        private sealed class InMemoryTokenStore : INeoComposeTokenStore
        {
            public NeoComposeStoredToken? saved;

            public NeoComposeStoredToken? Load() => saved;

            public void Save(NeoComposeStoredToken token) => saved = token;

            public void Clear() => saved = null;

            public NeoComposeTokenHint? PeekHint() => saved?.ToHint();
        }

        private sealed class FakeProvider : INeoComposeAccessTokenProvider
        {
            private readonly string? token;

            public FakeProvider(string? token) => this.token = token;

            public string GetAccessToken(string apiBaseUrl)
            {
                if (token == null)
                {
                    throw new NeoComposeNotSignedInException("Not signed in.");
                }

                return token;
            }

            public bool TryGetAccessToken(string apiBaseUrl, out string value)
            {
                value = token ?? "";
                return token != null;
            }
        }

        private sealed class NoopRefresher : INeoComposeSessionRefresher
        {
            public Task<bool> RefreshIfDueAsync(string apiBaseUrl) => Task.FromResult(false);
        }

        private sealed class RecordingRefresher : INeoComposeSessionRefresher
        {
            public int refreshCalls;
            public string lastApiBaseUrl = "";

            public Task<bool> RefreshIfDueAsync(string apiBaseUrl)
            {
                refreshCalls++;
                lastApiBaseUrl = apiBaseUrl;
                return Task.FromResult(false);
            }
        }

        private sealed class FakeHttpClient : INeoComposeHttpClient
        {
            public readonly List<(string url, string method, string? body, string? bearer)> sends = new();
            public readonly List<string> downloads = new();
            public long status = 200;
            public string body = "{}";

            public Task<NeoComposeWebResponse> SendAsync(
                string url,
                string method,
                string? jsonBody,
                string? bearerToken)
            {
                sends.Add((url, method, jsonBody, bearerToken));
                return Task.FromResult(new NeoComposeWebResponse(status, false, body, ""));
            }

            public Task<byte[]> DownloadAsync(string url)
            {
                downloads.Add(url);
                return Task.FromResult(new byte[] { 1, 2, 3 });
            }
        }
    }
}
