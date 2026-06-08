// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class NeoAuthenticationTests
    {
        private const string ApiBaseUrl = "https://example.test";
        private const string ProjectId = "project-1";
        private const string ClientId = "neo-compose-runtime-project-1";
        private const string Scopes = "openid profile:read project:project-1:save:write";

        private static NeoAuthenticationOptions Options() =>
            new NeoAuthenticationOptions(ApiBaseUrl, ProjectId, ClientId, Scopes);

        private sealed class TestTokenStore : INeoComposeTokenStore
        {
            public NeoComposeStoredToken? token;
            public int clearCount;
            public NeoComposeStoredToken? Load() => token;
            public void Save(NeoComposeStoredToken t) => token = t;
            public void Clear()
            {
                token = null;
                clearCount++;
            }
            public NeoComposeTokenHint? PeekHint() => token?.ToHint();
        }

        private sealed class FakeTransport : INeoComposeDeviceAuthTransport
        {
            public NeoComposeDeviceCodeResponse code = new()
            {
                deviceCode = "device-code",
                userCode = "USER-CODE",
                verificationUri = "https://example.test/auth/device",
                expiresInSeconds = 600,
                intervalSeconds = 1,
            };
            public NeoComposeDevicePollResult poll =
                NeoComposeDevicePollResult.Success(new NeoComposeDeviceTokenSuccess
                {
                    accessToken = "access-token-xyz",
                    tokenType = "Bearer",
                    expiresInSeconds = 3600,
                    scope = Scopes,
                });
            public NeoComposeUserProfile profile = new("Ada Lovelace", "ada@example.test");
            public int requestedCodes;

            public Task<NeoComposeDeviceCodeResponse> RequestDeviceCodeAsync(
                string apiBaseUrl, string clientId, string scope, CancellationToken ct)
            {
                requestedCodes++;
                return Task.FromResult(code);
            }

            public Task<NeoComposeDevicePollResult> PollDeviceTokenAsync(
                string apiBaseUrl, string clientId, string deviceCode, CancellationToken ct) =>
                Task.FromResult(poll);

            public Task<NeoComposeUserProfile> GetProfileAsync(
                string apiBaseUrl, string accessToken, CancellationToken ct) =>
                Task.FromResult(profile);
        }

        private sealed class FakeRefresher : INeoComposeSessionRefresher
        {
            public bool called;
            public bool result;
            public Exception? toThrow;
            public Task<bool> RefreshIfDueAsync(string apiBaseUrl)
            {
                called = true;
                if (toThrow != null) throw toThrow;
                return Task.FromResult(result);
            }
        }

        private sealed class FakeRevoker : INeoComposeTokenRevoker
        {
            public bool called;
            public Exception? toThrow;
            public Task RevokeAsync(string apiBaseUrl, string accessToken)
            {
                called = true;
                if (toThrow != null) throw toThrow;
                return Task.CompletedTask;
            }
        }

        private static Func<NeoComposeDeviceAuthorizationFlow> FlowFactory(
            FakeTransport transport,
            INeoComposeTokenStore store) =>
            () => new NeoComposeDeviceAuthorizationFlow(
                transport,
                store,
                () => DateTimeOffset.FromUnixTimeSeconds(0),
                (_, _) => Task.CompletedTask,
                _ => { },
                ClientId,
                Scopes);

        private static NeoComposeStoredToken StoredToken(long expiresAtUnixSeconds) =>
            new NeoComposeStoredToken(
                "stored-access-token",
                expiresAtUnixSeconds,
                new[] { "openid" },
                ApiBaseUrl,
                "Ada Lovelace",
                "ada@example.test");

        [Test]
        public async Task SignIn_Success_StoresTokenAndSignsIn()
        {
            var store = new TestTokenStore();
            var transport = new FakeTransport();
            var prompts = new List<NeoComposeDeviceCodeResponse>();
            var auth = new NeoAuthentication(
                Options(),
                store,
                FlowFactory(transport, store),
                now: () => DateTimeOffset.FromUnixTimeSeconds(0));
            auth.OnDeviceAuthorizationPrompt += prompts.Add;

            var result = await auth.SignInAsync();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(auth.State, Is.EqualTo(NeoAuthenticationState.SignedIn));
            Assert.That(auth.CurrentAccessToken, Is.EqualTo("access-token-xyz"));
            Assert.That(auth.DisplayEmail, Is.EqualTo("ada@example.test"));
            Assert.That(prompts, Has.Count.EqualTo(1));
            Assert.That(prompts[0].userCode, Is.EqualTo("USER-CODE"));
        }

        [Test]
        public async Task SignIn_SurfacesAwaitingState_WhenCodeReady()
        {
            var store = new TestTokenStore();
            var transport = new FakeTransport();
            var auth = new NeoAuthentication(
                Options(),
                store,
                FlowFactory(transport, store),
                now: () => DateTimeOffset.FromUnixTimeSeconds(0));
            NeoAuthenticationState observedDuringPrompt = NeoAuthenticationState.SignedOut;
            auth.OnDeviceAuthorizationPrompt += _ => observedDuringPrompt = auth.State;

            await auth.SignInAsync();

            Assert.That(
                observedDuringPrompt,
                Is.EqualTo(NeoAuthenticationState.AwaitingUserAuthorization));
        }

        [Test]
        public async Task SignIn_Denied_RemainsSignedOut()
        {
            var store = new TestTokenStore();
            var transport = new FakeTransport { poll = NeoComposeDevicePollResult.Denied() };
            var auth = new NeoAuthentication(
                Options(),
                store,
                FlowFactory(transport, store),
                now: () => DateTimeOffset.FromUnixTimeSeconds(0));

            var result = await auth.SignInAsync();

            Assert.That(result.outcome, Is.EqualTo(NeoComposeDeviceAuthOutcome.Denied));
            Assert.That(auth.State, Is.EqualTo(NeoAuthenticationState.SignedOut));
            Assert.That(auth.CurrentAccessToken, Is.Null);
        }

        [Test]
        public async Task SignOut_RevokesAndClears()
        {
            var store = new TestTokenStore { token = StoredToken(long.MaxValue) };
            var revoker = new FakeRevoker();
            var auth = new NeoAuthentication(
                Options(),
                store,
                revoker: revoker,
                now: () => DateTimeOffset.FromUnixTimeSeconds(0));
            Assert.That(auth.State, Is.EqualTo(NeoAuthenticationState.SignedIn));

            await auth.SignOutAsync();

            Assert.That(revoker.called, Is.True);
            Assert.That(store.token, Is.Null);
            Assert.That(auth.State, Is.EqualTo(NeoAuthenticationState.SignedOut));
        }

        [Test]
        public async Task SignOut_ClearsEvenWhenRevokeThrows()
        {
            var store = new TestTokenStore { token = StoredToken(long.MaxValue) };
            var revoker = new FakeRevoker { toThrow = new Exception("network down") };
            var auth = new NeoAuthentication(
                Options(), store, revoker: revoker, now: () => DateTimeOffset.FromUnixTimeSeconds(0));

            await auth.SignOutAsync();

            Assert.That(store.token, Is.Null);
            Assert.That(auth.State, Is.EqualTo(NeoAuthenticationState.SignedOut));
        }

        [Test]
        public void RefreshState_FromExpiredHint_SetsExpired()
        {
            var now = DateTimeOffset.FromUnixTimeSeconds(10_000);
            var store = new TestTokenStore { token = StoredToken(5_000) };
            var auth = new NeoAuthentication(Options(), store, now: () => now);

            Assert.That(auth.State, Is.EqualTo(NeoAuthenticationState.Expired));
            Assert.That(auth.TryGetAccessToken(out _), Is.False);
        }

        [Test]
        public async Task RefreshSessionIfDue_DelegatesToRefresher()
        {
            var store = new TestTokenStore { token = StoredToken(long.MaxValue) };
            var refresher = new FakeRefresher { result = true };
            var auth = new NeoAuthentication(
                Options(), store, sessionRefresher: refresher, now: () => DateTimeOffset.FromUnixTimeSeconds(0));

            var refreshed = await auth.RefreshSessionIfDueAsync();

            Assert.That(refresher.called, Is.True);
            Assert.That(refreshed, Is.True);
        }

        [Test]
        public async Task RefreshSession_NotSignedIn_ClearsToken()
        {
            var store = new TestTokenStore { token = StoredToken(long.MaxValue) };
            var refresher = new FakeRefresher
            {
                toThrow = new NeoComposeNotSignedInException("session invalid"),
            };
            var auth = new NeoAuthentication(
                Options(), store, sessionRefresher: refresher, now: () => DateTimeOffset.FromUnixTimeSeconds(0));

            var refreshed = await auth.RefreshSessionIfDueAsync();

            Assert.That(refreshed, Is.False);
            Assert.That(store.token, Is.Null);
            Assert.That(auth.State, Is.EqualTo(NeoAuthenticationState.Expired));
        }
    }
}
