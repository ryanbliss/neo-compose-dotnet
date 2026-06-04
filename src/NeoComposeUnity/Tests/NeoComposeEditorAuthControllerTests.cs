// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NeoCompose.Unity.Editor;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class NeoComposeEditorAuthControllerTests
    {
        private const string ApiBaseUrl = "https://example.test";

        private NeoComposeTokenStore store = null!;
        private MemSecretBackend secret = null!;
        private DateTimeOffset clockNow;

        [SetUp]
        public void SetUp()
        {
            secret = new MemSecretBackend();
            store = new NeoComposeTokenStore(ApiBaseUrl, secret, new MemHintStore());
            clockNow = DateTimeOffset.UtcNow;
        }

        private NeoComposeEditorAuthController NewController(
            Func<string, NeoComposeTokenStore, NeoComposeDeviceAuthorizationFlow>? flowFactory = null)
        {
            return new NeoComposeEditorAuthController(
                _ => store,
                flowFactory,
                () => clockNow);
        }

        private void SaveValidToken(string name = "Ada", string email = "ada@example.test")
        {
            store.Save(new NeoComposeStoredToken(
                "access",
                clockNow.AddDays(7).ToUnixTimeSeconds(),
                new[] { "openid" },
                ApiBaseUrl,
                name,
                email));
        }

        // UAUTH-025 / UAUTH-027
        [Test]
        public void RefreshState_SignedInWithIdentityWhenValidTokenStored()
        {
            SaveValidToken();
            var controller = NewController();

            controller.RefreshState(ApiBaseUrl);

            Assert.AreEqual(NeoComposeAuthState.SignedIn, controller.State);
            Assert.AreEqual("Ada", controller.DisplayName);
            Assert.AreEqual("ada@example.test", controller.DisplayEmail);
            Assert.IsTrue(controller.AreAuthSensitiveControlsEnabled);
        }

        // UAUTH-026 / UAUTH-027
        [Test]
        public void RefreshState_ExpiredWhenStoredTokenExpired()
        {
            store.Save(new NeoComposeStoredToken(
                "access",
                clockNow.AddMinutes(-1).ToUnixTimeSeconds(),
                new[] { "openid" },
                ApiBaseUrl,
                "Ada",
                "ada@example.test"));
            var controller = NewController();

            controller.RefreshState(ApiBaseUrl);

            Assert.AreEqual(NeoComposeAuthState.Expired, controller.State);
            Assert.AreEqual("Ada", controller.DisplayName, "Identity is preserved for the expired message.");
            Assert.IsFalse(controller.AreAuthSensitiveControlsEnabled);
        }

        // UAUTH-024 / UAUTH-027
        [Test]
        public void RefreshState_SignedOutWhenNoToken()
        {
            var controller = NewController();

            controller.RefreshState(ApiBaseUrl);

            Assert.AreEqual(NeoComposeAuthState.SignedOut, controller.State);
            Assert.IsFalse(controller.AreAuthSensitiveControlsEnabled);
        }

        // UAUTH-028
        [Test]
        public async Task SignInAsync_SuccessTransitionsToSignedIn()
        {
            var transport = new FakeTransport
            {
                token = new NeoComposeDeviceTokenSuccess
                {
                    accessToken = "fresh-token",
                    expiresInSeconds = 604800,
                    scope = "openid profile:read",
                },
                profile = new NeoComposeUserProfile("Grace", "grace@example.test"),
            };
            var controller = NewController((apiBaseUrl, s) => NewFlow(transport, s));

            NeoComposeDeviceCodeResponse? shown = null;
            var result = await controller.SignInAsync(ApiBaseUrl, code => shown = code);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(shown);
            Assert.AreEqual(NeoComposeAuthState.SignedIn, controller.State);
            Assert.AreEqual("Grace", controller.DisplayName);
            Assert.IsFalse(controller.IsBusy);
            Assert.IsTrue(controller.AreAuthSensitiveControlsEnabled);
        }

        // UAUTH-037 / UAUTH-042
        [Test]
        public void HandleApiException_401_ClearsTokenAndExpiresWithKnownIdentity()
        {
            SaveValidToken();
            var controller = NewController();
            controller.RefreshState(ApiBaseUrl);
            Assert.AreEqual(NeoComposeAuthState.SignedIn, controller.State);

            controller.HandleApiException(ApiBaseUrl, new NeoComposeNotSignedInException("401"));

            Assert.AreEqual(NeoComposeAuthState.Expired, controller.State);
            Assert.IsNull(store.Load(), "The dead token must be cleared on 401.");
            Assert.IsFalse(controller.AreAuthSensitiveControlsEnabled);
        }

        // UAUTH-037
        [Test]
        public void HandleApiException_401_SignedOutWhenNoIdentity()
        {
            var controller = NewController();

            controller.HandleApiException(ApiBaseUrl, new NeoComposeNotSignedInException("401"));

            Assert.AreEqual(NeoComposeAuthState.SignedOut, controller.State);
        }

        // UAUTH-038 / UAUTH-040
        [Test]
        public void HandleApiException_403_KeepsSignedInAndRecordsMessage()
        {
            SaveValidToken();
            var controller = NewController();
            controller.RefreshState(ApiBaseUrl);

            controller.HandleApiException(
                ApiBaseUrl,
                new NeoComposeApiAuthorizationException(
                    "You don't have permission to edit this project's Unity settings.",
                    "project-1",
                    "unity:settings:write"));

            Assert.AreEqual(NeoComposeAuthState.SignedIn, controller.State, "403 must not sign the user out.");
            StringAssert.Contains("Unity settings", controller.AuthorizationMessage);
            Assert.IsNotNull(store.Load(), "403 must not clear the token.");
        }

        // UAUTH-025
        [Test]
        public void ClearLocal_ReturnsToSignedOut()
        {
            SaveValidToken();
            var controller = NewController();
            controller.RefreshState(ApiBaseUrl);

            controller.ClearLocal(ApiBaseUrl);

            Assert.AreEqual(NeoComposeAuthState.SignedOut, controller.State);
            Assert.IsNull(store.Load());
            Assert.AreEqual("", controller.DisplayName);
        }

        private NeoComposeDeviceAuthorizationFlow NewFlow(FakeTransport transport, NeoComposeTokenStore tokenStore)
        {
            return new NeoComposeDeviceAuthorizationFlow(
                transport,
                tokenStore,
                () => clockNow,
                (_, _) => Task.CompletedTask,
                _ => { });
        }

        private sealed class FakeTransport : INeoComposeDeviceAuthTransport
        {
            public NeoComposeDeviceTokenSuccess token = new();
            public NeoComposeUserProfile profile = NeoComposeUserProfile.Empty;

            public Task<NeoComposeDeviceCodeResponse> RequestDeviceCodeAsync(
                string apiBaseUrl,
                string clientId,
                string scope,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(new NeoComposeDeviceCodeResponse
                {
                    deviceCode = "device",
                    userCode = "WDJB-MJHT",
                    verificationUriComplete = "https://example.test/auth/device?user_code=WDJB-MJHT",
                    expiresInSeconds = 600,
                    intervalSeconds = 1,
                });
            }

            public Task<NeoComposeDevicePollResult> PollDeviceTokenAsync(
                string apiBaseUrl,
                string clientId,
                string deviceCode,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(NeoComposeDevicePollResult.Success(token));
            }

            public Task<NeoComposeUserProfile> GetProfileAsync(
                string apiBaseUrl,
                string accessToken,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(profile);
            }
        }

        private sealed class MemSecretBackend : INeoComposeTokenSecretBackend
        {
            private readonly Dictionary<string, string> values = new();

            public bool IsAvailable => true;
            public string Name => "Memory";

            public string? Read(string service, string account) =>
                values.TryGetValue(Key(service, account), out var value) ? value : null;

            public void Write(string service, string account, string secret) =>
                values[Key(service, account)] = secret;

            public void Delete(string service, string account) => values.Remove(Key(service, account));

            private static string Key(string service, string account) => service + "::" + account;
        }

        private sealed class MemHintStore : INeoComposeTokenHintStore
        {
            private readonly Dictionary<string, string> values = new();

            public string? Read(string key) => values.TryGetValue(key, out var value) ? value : null;

            public void Write(string key, string value) => values[key] = value;

            public void Delete(string key) => values.Remove(key);
        }
    }
}
