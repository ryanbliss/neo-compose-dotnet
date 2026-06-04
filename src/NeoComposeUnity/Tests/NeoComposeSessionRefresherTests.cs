// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NeoCompose.Unity.Editor;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class NeoComposeSessionRefresherTests
    {
        private const string ApiBaseUrl = "https://example.test";
        private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(2_000);

        [Test]
        public async Task RefreshIfDue_UsesGetSessionAndPersistsReplacementTokenHeader()
        {
            var store = StoreWith(Token("old-token", expiresAt: 200_000, updatedAt: 1_000));
            var http = new FakeHttpClient
            {
                body = "{\"session\":{\"expiresAt\":\"1970-01-03T07:33:20Z\",\"updatedAt\":\"1970-01-02T01:00:00Z\"},\"user\":{\"name\":\"Grace\",\"email\":\"grace@example.test\"}}",
                headers = new Dictionary<string, string> { ["set-auth-token"] = "new-token" },
            };
            var refresher = NewRefresher(store, http, DateTimeOffset.FromUnixTimeSeconds(100_000));

            var refreshed = await refresher.RefreshIfDueAsync(ApiBaseUrl);

            Assert.IsTrue(refreshed);
            Assert.AreEqual(1, http.sends.Count);
            Assert.AreEqual(NeoComposeAuthEndpoints.GetSessionUrl(ApiBaseUrl), http.sends[0].url);
            Assert.AreEqual("GET", http.sends[0].method);
            Assert.AreEqual("old-token", http.sends[0].bearer);
            Assert.AreEqual("new-token", store.saved!.accessToken);
            Assert.AreEqual(200_000, store.saved.expiresAtUnixSeconds);
            Assert.AreEqual(90_000, store.saved.updatedAtUnixSeconds);
            Assert.AreEqual(100_000, store.saved.sessionCheckedAtUnixSeconds);
            Assert.AreEqual("Grace", store.saved.displayName);
            Assert.AreEqual("grace@example.test", store.saved.displayEmail);
        }

        [Test]
        public async Task RefreshIfDue_SkipsWhenTokenIsExpiredBeforeUpdateAgeOrCheckedWithinHour()
        {
            var expiredStore = StoreWith(Token("expired", expiresAt: 1_999, updatedAt: 1_000));
            var beforeUpdateAgeStore = StoreWith(Token("not-due", expiresAt: 200_000, updatedAt: 90_000));
            var checkedRecentlyStore = StoreWith(Token(
                "checked-recently",
                expiresAt: 200_000,
                updatedAt: 1_000,
                sessionCheckedAt: 99_000));
            var http = new FakeHttpClient();

            Assert.IsFalse(await NewRefresher(expiredStore, http).RefreshIfDueAsync(ApiBaseUrl));
            Assert.IsFalse(await NewRefresher(beforeUpdateAgeStore, http, DateTimeOffset.FromUnixTimeSeconds(100_000)).RefreshIfDueAsync(ApiBaseUrl));
            Assert.IsFalse(await NewRefresher(checkedRecentlyStore, http, DateTimeOffset.FromUnixTimeSeconds(100_000)).RefreshIfDueAsync(ApiBaseUrl));
            Assert.AreEqual(0, http.sends.Count);
        }

        [Test]
        public async Task RefreshIfDue_RecordsCheckWhenSessionDoesNotRotate()
        {
            var store = StoreWith(Token("old-token", expiresAt: 200_000, updatedAt: 1_000));
            var http = new FakeHttpClient
            {
                body = "{\"session\":{\"expiresAt\":\"200000\",\"updatedAt\":\"1000\"}}",
            };
            var refresher = NewRefresher(store, http, DateTimeOffset.FromUnixTimeSeconds(100_000));

            var refreshed = await refresher.RefreshIfDueAsync(ApiBaseUrl);

            Assert.IsFalse(refreshed);
            Assert.AreEqual(1, http.sends.Count);
            Assert.AreEqual("old-token", store.saved!.accessToken);
            Assert.AreEqual(100_000, store.saved.sessionCheckedAtUnixSeconds);
        }

        [Test]
        public async Task RefreshSessionIfDue_401ClearsStoredTokenThroughAuthController()
        {
            var store = StoreWith(Token("dead", expiresAt: 200_000, updatedAt: 1_000));
            var http = new FakeHttpClient { status = 401 };
            var refresher = NewRefresher(store, http, DateTimeOffset.FromUnixTimeSeconds(100_000));
            var controller = new NeoComposeEditorAuthController(
                _ => store,
                now: () => Now,
                sessionRefresher: refresher);
            controller.RefreshState(ApiBaseUrl);

            var refreshed = await controller.RefreshSessionIfDueAsync(ApiBaseUrl);

            Assert.IsFalse(refreshed);
            Assert.IsNull(store.saved);
            Assert.AreEqual(NeoComposeAuthState.Expired, controller.State);
        }

        private static NeoComposeSessionRefresher NewRefresher(
            InMemoryTokenStore store,
            FakeHttpClient http,
            DateTimeOffset? now = null) =>
            new NeoComposeSessionRefresher(_ => store, http, () => now ?? Now);

        private static NeoComposeStoredToken Token(
            string accessToken,
            long expiresAt,
            long updatedAt,
            long sessionCheckedAt = 0) =>
            new NeoComposeStoredToken(
                accessToken,
                expiresAt,
                updatedAt,
                sessionCheckedAt,
                new[] { "openid" },
                ApiBaseUrl,
                "Ada",
                "ada@example.test");

        private static InMemoryTokenStore StoreWith(NeoComposeStoredToken token) =>
            new InMemoryTokenStore { saved = token };

        private sealed class InMemoryTokenStore : INeoComposeTokenStore
        {
            public NeoComposeStoredToken? saved;

            public NeoComposeStoredToken? Load() => saved;

            public void Save(NeoComposeStoredToken token) => saved = token;

            public void Clear() => saved = null;

            public NeoComposeTokenHint? PeekHint() => saved?.ToHint();
        }

        private sealed class FakeHttpClient : INeoComposeHttpClient
        {
            public readonly List<(string url, string method, string? body, string? bearer)> sends = new();
            public long status = 200;
            public string body = "{\"session\":{\"expiresAt\":\"10000\",\"updatedAt\":\"2000\"}}";
            public Dictionary<string, string> headers = new();

            public Task<NeoComposeWebResponse> SendAsync(
                string url,
                string method,
                string? jsonBody,
                string? bearerToken)
            {
                sends.Add((url, method, jsonBody, bearerToken));
                return Task.FromResult(new NeoComposeWebResponse(status, false, body, "", headers));
            }

            public Task<byte[]> DownloadAsync(string url) => Task.FromResult(Array.Empty<byte>());
        }
    }
}
