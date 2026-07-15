// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class NeoApiClientTests
    {
        private const string ApiBaseUrl = "https://example.test";
        private const string ProjectId = "project-1";

        private const string RemoteJson =
            "{" +
            "\"serverId\":\"server-1\",\"id\":\"save-1\",\"snapshotId\":\"snap-1\"," +
            "\"snapshotHash\":\"hash-1\",\"releaseChannelId\":\"channel-dev\"," +
            "\"name\":\"My Save\",\"projectId\":\"project-1\"," +
            "\"version\":{\"id\":\"v1\",\"label\":\"1.0\"}," +
            "\"author\":{\"kind\":\"user\",\"id\":\"user-1\"}," +
            "\"actor\":{\"kind\":\"user\",\"id\":\"user-1\"}," +
            "\"values\":{},\"staticBindings\":{}," +
            "\"createdAt\":1,\"updatedAt\":2,\"synchronizedAt\":3,\"archivedAt\":null" +
            "}";

        // Parses cleanly for every endpoint's response shape at once.
        private static readonly string CombinedBody =
            "{\"save\":" + RemoteJson + ",\"saves\":[],\"cloneRequired\":{},\"snapshots\":[]}";

        [Test]
        public async Task EveryRequest_AttachesBearerToken()
        {
            var http = new FakeHttpClient { body = CombinedBody };
            var client = NewClient(new FakeProvider("the-token"), http);

            await client.ListSavesAsync(null);
            await client.GetSaveAsync("save-1");
            await client.GetSaveSnapshotsAsync("save-1");
            await client.CommitAsync(NewCommit(), replaceSnapshot: false);
            await client.CloneSaveAsync("save-1", new NeoCloneRequest());
            await client.ArchiveSaveAsync("save-1");
            await client.ArchiveSnapshotAsync("save-1", "snap-1");

            Assert.That(http.sends, Has.Count.EqualTo(7));
            foreach (var send in http.sends)
            {
                Assert.That(send.bearer, Is.EqualTo("the-token"), $"Request to {send.url} must carry the bearer.");
                Assert.That(send.method, Is.EqualTo("POST"));
            }
        }

        [Test]
        public void Request_FailsFastWhenSignedOut_WithoutSending()
        {
            var http = new FakeHttpClient();
            var client = NewClient(new FakeProvider(null), http);

            Assert.ThrowsAsync<NeoComposeNotSignedInException>(
                async () => await client.ListSavesAsync(null));
            Assert.That(http.sends, Is.Empty, "No request may be issued when signed out.");
        }

        [Test]
        public async Task Request_RefreshesSessionBeforeSending()
        {
            var http = new FakeHttpClient { body = CombinedBody };
            var refresher = new RecordingRefresher();
            var client = NewClient(new FakeProvider("the-token"), http, refresher);

            await client.ListSavesAsync(null);

            Assert.That(refresher.refreshCalls, Is.EqualTo(1));
            Assert.That(refresher.lastApiBaseUrl, Is.EqualTo(ApiBaseUrl));
            Assert.That(http.sends, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task ListSaves_ParsesSavesAndCloneRequired()
        {
            var http = new FakeHttpClient
            {
                body = "{\"saves\":[" + RemoteJson + "],\"cloneRequired\":{\"save-1\":true}}",
            };
            var client = NewClient(new FakeProvider("the-token"), http);

            var list = await client.ListSavesAsync("channel-other");

            Assert.That(list.saves, Has.Count.EqualTo(1));
            Assert.That(list.saves[0].id, Is.EqualTo("save-1"));
            Assert.That(list.RequiresClone("save-1"), Is.True);
            Assert.That(list.RequiresClone("save-2"), Is.False);
            // The target channel must be forwarded in the request body.
            StringAssert.Contains("channel-other", http.sends[0].body);
        }

        [Test]
        public async Task Commit_Success_ReturnsCommittedSave()
        {
            var http = new FakeHttpClient
            {
                status = 200,
                body = "{\"kind\":\"committed\",\"save\":" + RemoteJson + "}",
            };
            var client = NewClient(new FakeProvider("the-token"), http);

            var result = await client.CommitAsync(NewCommit(), replaceSnapshot: true);

            Assert.That(result.IsConflict, Is.False);
            Assert.That(result.Outcome, Is.EqualTo(NeoCommitOutcome.Committed));
            Assert.That(result.CommittedSave!.id, Is.EqualTo("save-1"));
            // Envelope carries the save and the replaceSnapshot flag.
            StringAssert.Contains("\"replaceSnapshot\":true", http.sends[0].body);
            StringAssert.Contains("\"save\":", http.sends[0].body);
            StringAssert.Contains("\"staticBindings\":{\"member-current\":\"v-runtime\"}", http.sends[0].body);
            StringAssert.DoesNotContain("\"tileGridDeltas\"", http.sends[0].body);
        }

        [Test]
        public async Task Commit_Conflict409_SurfacesServerHead()
        {
            var http = new FakeHttpClient
            {
                status = 409,
                body = "{\"kind\":\"conflict\",\"serverHead\":" + RemoteJson + "}",
            };
            var client = NewClient(new FakeProvider("the-token"), http);

            var result = await client.CommitAsync(NewCommit(), replaceSnapshot: false);

            Assert.That(result.IsConflict, Is.True);
            Assert.That(result.Outcome, Is.EqualTo(NeoCommitOutcome.Conflict));
            Assert.That(result.CommittedSave, Is.Null);
            Assert.That(result.ServerHead!.snapshotHash, Is.EqualTo("hash-1"));
        }

        [Test]
        public void Unauthorized401_ThrowsNotSignedIn()
        {
            var http = new FakeHttpClient
            {
                status = 401,
                body = "{\"error\":\"Bearer token is invalid or expired.\"}",
            };
            var client = NewClient(new FakeProvider("the-token"), http);

            Assert.ThrowsAsync<NeoComposeNotSignedInException>(
                async () => await client.ListSavesAsync(null));
            Assert.That(http.sends, Has.Count.EqualTo(1), "401 must not be retried.");
        }

        [Test]
        public void Forbidden403_ThrowsAuthorizationWithWriteScope()
        {
            var http = new FakeHttpClient
            {
                status = 403,
                body = "{\"error\":\"Bearer token is missing required scope.\"}",
            };
            var client = NewClient(new FakeProvider("the-token"), http);

            var ex = Assert.ThrowsAsync<NeoComposeApiAuthorizationException>(
                async () => await client.CommitAsync(NewCommit(), replaceSnapshot: false));

            StringAssert.Contains("commit this save file", ex!.Message);
            Assert.That(ex.RequiredScope, Is.EqualTo("project:project-1:save:write"));
            Assert.That(ex.ProjectId, Is.EqualTo(ProjectId));
            Assert.That(http.sends, Has.Count.EqualTo(1), "403 must not be retried.");
        }

        private static NeoSaveCommitRequest NewCommit() =>
            new NeoSaveCommitRequest
            {
                customId = "save-1",
                name = "My Save",
                version = new VersionData { id = "v1", label = "1.0" },
                targetReleaseChannelId = "channel-dev",
                values = NeoSaveValues.Empty,
                staticBindings = new Dictionary<string, string?>
                {
                    ["member-current"] = "v-runtime",
                },
            };

        private static NeoApiClient NewClient(
            INeoComposeAccessTokenProvider provider,
            INeoComposeHttpClient http,
            INeoComposeSessionRefresher? refresher = null) =>
            new NeoApiClient(
                ApiBaseUrl,
                ProjectId,
                provider,
                http,
                refresher ?? new NoopRefresher());

        private sealed class FakeProvider : INeoComposeAccessTokenProvider
        {
            private readonly string? token;

            public FakeProvider(string? token) => this.token = token;

            public string GetAccessToken(string apiBaseUrl)
            {
                if (token == null) throw new NeoComposeNotSignedInException("Not signed in.");
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

            public Task<byte[]> DownloadAsync(string url) =>
                Task.FromResult(System.Array.Empty<byte>());
        }
    }
}
