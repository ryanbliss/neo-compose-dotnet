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
            "\"snapshotRevision\":1,\"releaseChannelId\":\"channel-dev\"," +
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
            var http = new FakeHttpClient
            {
                bodyForUrl = url =>
                {
                    if (url.EndsWith("/saves/query"))
                        return "{\"saves\":[],\"cloneRequired\":{}}";
                    if (url.EndsWith("/saves/save-1/query")) return RemoteJson;
                    if (url.EndsWith("/snapshots/query")) return "{\"snapshots\":[]}";
                    if (url.EndsWith("/snapshots/snap-1/query")) return RemoteJson;
                    if (url.EndsWith("/saves/commit"))
                        return "{\"kind\":\"committed\",\"save\":" + RemoteJson + "}";
                    if (url.EndsWith("/saves/chunked-create/begin"))
                        return "{\"customId\":\"save-1\",\"snapshotId\":\"snap-1\"," +
                            "\"snapshotRevision\":0,\"resumeToken\":\"resume-1\"}";
                    if (url.EndsWith("/chunked-create/append"))
                        return "{\"kind\":\"patched\",\"snapshotId\":\"snap-1\"," +
                            "\"snapshotRevision\":1,\"synchronizedAt\":3," +
                            "\"changedDescriptors\":[]}";
                    if (url.EndsWith("/chunked-create/complete")) return RemoteJson;
                    if (url.EndsWith("/saves/save-1/clone"))
                        return "{\"kind\":\"cloned\",\"save\":" + RemoteJson + "}";
                    if (url.EndsWith("/saves/save-1/status/query"))
                        return "{\"kind\":\"ready\",\"save\":" + RemoteJson + "}";
                    if (url.EndsWith("/snapshots/snap-1/archive")) return RemoteJson;
                    return "{}";
                },
            };
            var client = NewClient(new FakeProvider("the-token"), http);

            await client.ListSavesAsync(null);
            await client.GetSaveAsync("save-1");
            await client.GetSaveSnapshotsAsync("save-1");
            await client.GetSaveSnapshotAsync("save-1", "snap-1");
            await client.CommitAsync(NewCommit(), replaceSnapshot: false);
            await client.BeginChunkedCreateAsync(new NeoChunkedCreateRequest
            {
                customId = "save-1",
                uploadFingerprint = "sha256:upload-1",
            });
            await client.AppendChunkedCreateAsync(
                "save-1",
                "resume-1",
                0,
                new List<GameSaveRecordChange>
                {
                    new GameSaveValueReplaceChange
                    {
                        valueId = "value-1",
                        value = Newtonsoft.Json.Linq.JObject.Parse("{\"value\":1}"),
                    },
                },
                3);
            await client.CompleteChunkedCreateAsync("save-1", "resume-1");
            await client.CloneSaveAsync("save-1", new NeoCloneRequest());
            await client.GetSaveTransitionStatusAsync("save-1");
            await client.ArchiveSaveAsync("save-1");
            await client.ArchiveSnapshotAsync("save-1", "snap-1");

            Assert.That(http.sends, Has.Count.EqualTo(19));
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
        public async Task SnapshotHistory_ListsSummariesAndFetchesSelectedDetail()
        {
            var http = new FakeHttpClient
            {
                body = "{\"snapshots\":[" + RemoteJson + "]}",
            };
            var client = NewClient(new FakeProvider("the-token"), http);

            var summaries = await client.GetSaveSnapshotsAsync("save-1");

            Assert.That(summaries, Has.Count.EqualTo(1));
            Assert.That(summaries[0].snapshotId, Is.EqualTo("snap-1"));
            StringAssert.EndsWith(
                "/saves/save-1/snapshots/query",
                http.sends[0].url);

            http.body = RemoteJson;
            var detail = await client.GetSaveSnapshotAsync("save-1", "snap-1");

            Assert.That(detail.snapshotRevision, Is.EqualTo(1));
            Assert.That(detail.values, Is.Not.Null);
            StringAssert.EndsWith(
                "/saves/save-1/snapshots/snap-1/query",
                http.sends[1].url);
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
            Assert.That(result.ServerHead!.snapshotRevision, Is.EqualTo(1));
        }

        [Test]
        public async Task Clone_TransitioningReturnsPollIdentityWithoutReadingPartialRecords()
        {
            var http = new FakeHttpClient
            {
                body = "{\"kind\":\"transitioning\",\"customId\":\"save-2\"," +
                    "\"targetSnapshotId\":\"snap-2\"}",
            };
            var client = NewClient(new FakeProvider("the-token"), http);

            var result = await client.CloneSaveAsync(
                "save-1", new NeoCloneRequest());

            Assert.That(result.Outcome, Is.EqualTo(NeoCloneOutcome.Transitioning));
            Assert.That(result.CustomId, Is.EqualTo("save-2"));
            Assert.That(result.TargetSnapshotId, Is.EqualTo("snap-2"));
            Assert.That(result.ClonedSave, Is.Null);
            Assert.That(http.sends, Has.Count.EqualTo(1),
                "an accepted copy must not expose or request partial manifest records");
        }

        [Test]
        public async Task ChunkedCreate_UsesMetadataThenBoundedRecordAppendThenComplete()
        {
            var http = new FakeHttpClient
            {
                bodyForUrl = url =>
                {
                    if (url.EndsWith("/chunked-create/begin"))
                        return "{\"customId\":\"save-1\",\"snapshotId\":\"snap-new\"," +
                            "\"snapshotRevision\":0,\"resumeToken\":\"resume-new\"}";
                    if (url.EndsWith("/chunked-create/append"))
                        return "{\"kind\":\"patched\",\"snapshotId\":\"snap-new\"," +
                            "\"snapshotRevision\":1,\"synchronizedAt\":4," +
                            "\"changedDescriptors\":[]}";
                    if (url.EndsWith("/chunked-create/complete")) return RemoteJson;
                    return "{}";
                },
            };
            var client = NewClient(new FakeProvider("the-token"), http);
            var target = await client.BeginChunkedCreateAsync(
                new NeoChunkedCreateRequest
                {
                    customId = "save-1",
                    name = "Large",
                    targetReleaseChannelId = "channel-dev",
                    createdAt = 1,
                    updatedAt = 2,
                    uploadFingerprint = "sha256:large-save-v1",
                });
            var change = new GameSaveValueReplaceChange
            {
                valueId = "value-1",
                value = Newtonsoft.Json.Linq.JObject.Parse("{\"value\":1}"),
            };
            var appended = await client.AppendChunkedCreateAsync(
                target.customId,
                target.resumeToken,
                target.snapshotRevision,
                new[] { change },
                2);
            await client.CompleteChunkedCreateAsync(
                target.customId, target.resumeToken);

            Assert.That(appended.SnapshotRevision, Is.EqualTo(1));
            StringAssert.EndsWith("/saves/chunked-create/begin", http.sends[0].url);
            StringAssert.DoesNotContain("values", http.sends[0].body);
            StringAssert.EndsWith(
                "/saves/save-1/chunked-create/append", http.sends[1].url);
            StringAssert.Contains("\"resumeToken\":\"resume-new\"", http.sends[1].body);
            StringAssert.Contains("\"baseSnapshotRevision\":0", http.sends[1].body);
            StringAssert.DoesNotContain("snapshotId", http.sends[1].body);
            StringAssert.Contains("\"kind\":\"value.replace\"", http.sends[1].body);
            StringAssert.EndsWith(
                "/saves/save-1/chunked-create/complete", http.sends[2].url);
            StringAssert.Contains("\"resumeToken\":\"resume-new\"", http.sends[2].body);
        }

        [Test]
        public async Task TransitionStatus_ParsesCopyingAndFailedOutcomes()
        {
            var http = new FakeHttpClient
            {
                body = "{\"kind\":\"copying\",\"customId\":\"save-2\"," +
                    "\"targetSnapshotId\":\"snap-2\"}",
            };
            var client = NewClient(new FakeProvider("the-token"), http);

            var copying = await client.GetSaveTransitionStatusAsync("save-2");
            Assert.That(copying.Outcome, Is.EqualTo(NeoSaveTransitionOutcome.Copying));
            StringAssert.EndsWith(
                "/saves/save-2/status/query", http.sends[0].url);

            http.body = "{\"kind\":\"failed\",\"customId\":\"save-2\"," +
                "\"targetSnapshotId\":\"snap-2\",\"error\":\"copy failed\"}";
            var failed = await client.GetSaveTransitionStatusAsync("save-2");

            Assert.That(failed.Outcome, Is.EqualTo(NeoSaveTransitionOutcome.Failed));
            Assert.That(failed.Error, Is.EqualTo("copy failed"));
        }

        [Test]
        public async Task RecordReads_UsePagedAllPartitionRoutes()
        {
            var http = new FakeHttpClient();
            var client = NewClient(new FakeProvider("the-token"), http);

            await client.GetSaveRecordManifestPageAsync(
                "save-1", "snap-1", new GameSaveRecordPageRequest
                {
                    cursor = "cursor-1",
                    numItems = 64,
                });
            await client.GetSaveRecordDeltaPageAsync(
                "save-1", "snap-1", new GameSaveRecordDeltaPageRequest
                {
                    afterRevision = 4,
                    throughRevision = 7,
                    numItems = 64,
                });
            await client.GetSaveRecordStatesAsync(
                "save-1", "snap-1", new[] { "state-1" });

            StringAssert.EndsWith("/records/manifest/query", http.sends[0].url);
            StringAssert.Contains("\"cursor\":\"cursor-1\"", http.sends[0].body);
            StringAssert.DoesNotContain("mapKey", http.sends[0].body);
            StringAssert.EndsWith("/records/delta/query", http.sends[1].url);
            StringAssert.Contains("\"afterRevision\":4", http.sends[1].body);
            StringAssert.Contains("\"throughRevision\":7", http.sends[1].body);
            StringAssert.EndsWith("/records/states/query", http.sends[2].url);
            StringAssert.Contains("\"recordStateIds\":[\"state-1\"]", http.sends[2].body);
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
            public System.Func<string, string>? bodyForUrl;

            public Task<NeoComposeWebResponse> SendAsync(
                string url,
                string method,
                string? jsonBody,
                string? bearerToken)
            {
                sends.Add((url, method, jsonBody, bearerToken));
                if (url.Contains("/records/manifest/query"))
                {
                    return Task.FromResult(new NeoComposeWebResponse(
                        200, false,
                        "{\"page\":[],\"isDone\":true,\"continueCursor\":null}", ""));
                }
                if (url.Contains("/records/delta/query"))
                {
                    return Task.FromResult(new NeoComposeWebResponse(
                        200, false,
                        "{\"page\":[],\"isDone\":true,\"continueCursor\":null}", ""));
                }
                if (url.Contains("/records/states/query"))
                {
                    return Task.FromResult(new NeoComposeWebResponse(
                        200, false, "{\"states\":[]}", ""));
                }
                return Task.FromResult(new NeoComposeWebResponse(
                    status, false, bodyForUrl?.Invoke(url) ?? body, ""));
            }

            public Task<byte[]> DownloadAsync(string url) =>
                Task.FromResult(System.Array.Empty<byte>());
        }
    }
}
