// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NeoCompose.Unity.Editor;
using NUnit.Framework;

using NeoCompose.Runtime;

namespace NeoCompose.Tests
{
    public class NeoComposeDeviceAuthFlowTests
    {
        private const string ApiBaseUrl = "https://example.test";

        // UAUTH-021
        [Test]
        public async Task Authorize_PersistsTokenScopesAndIdentityOnSuccess()
        {
            var transport = new FakeTransport
            {
                code = SampleCode(),
                pollResults = new Queue<NeoComposeDevicePollResult>(new[]
                {
                    NeoComposeDevicePollResult.Pending(),
                    NeoComposeDevicePollResult.Success(new NeoComposeDeviceTokenSuccess
                    {
                        accessToken = "the-access-token",
                        tokenType = "bearer",
                        expiresInSeconds = 604800,
                        scope = "openid profile:read unity:export",
                    }),
                }),
                profile = new NeoComposeUserProfile("Ada Lovelace", "ada@example.test"),
            };
            var store = new RecordingTokenStore();
            var flow = NewFlow(transport, store);

            NeoComposeDeviceCodeResponse? shown = null;
            var result = await flow.AuthorizeAsync(ApiBaseUrl, code => shown = code, CancellationToken.None);

            Assert.AreEqual(NeoComposeDeviceAuthOutcome.Success, result.outcome);
            Assert.IsNotNull(shown, "onCodeReady must fire with the user code.");
            Assert.AreEqual("WDJB-MJHT", shown!.userCode);
            Assert.AreEqual(1, transport.openedVerificationCount, "Verification URI must be opened once.");

            Assert.IsNotNull(store.saved);
            Assert.AreEqual("the-access-token", store.saved!.accessToken);
            Assert.AreEqual("Ada Lovelace", store.saved.displayName);
            Assert.AreEqual("ada@example.test", store.saved.displayEmail);
            CollectionAssert.AreEqual(
                new[] { "openid", "profile:read", "unity:export" },
                store.saved.scopes);
            Assert.AreEqual(ApiBaseUrl, store.saved.authBaseUrl);
        }

        // UAUTH-020
        [Test]
        public async Task Authorize_PollsThroughPendingAndSlowDownBeforeSuccess()
        {
            var transport = new FakeTransport
            {
                code = SampleCode(intervalSeconds: 5),
                pollResults = new Queue<NeoComposeDevicePollResult>(new[]
                {
                    NeoComposeDevicePollResult.Pending(),
                    NeoComposeDevicePollResult.SlowDown(),
                    NeoComposeDevicePollResult.Pending(),
                    NeoComposeDevicePollResult.Success(new NeoComposeDeviceTokenSuccess
                    {
                        accessToken = "t",
                        expiresInSeconds = 3600,
                        scope = "openid",
                    }),
                }),
            };
            var flow = NewFlow(transport, new RecordingTokenStore());

            var result = await flow.AuthorizeAsync(ApiBaseUrl, null, CancellationToken.None);

            Assert.AreEqual(NeoComposeDeviceAuthOutcome.Success, result.outcome);
            Assert.AreEqual(4, transport.pollCount);
            // First poll waits the base interval (5s); slow_down adds 5s for the
            // remaining polls.
            CollectionAssert.AreEqual(new[] { 5, 5, 10, 10 }, transport.requestedDelays);
        }

        [Test]
        public async Task Authorize_RetriesTransientPollFailureAndRespectsRetryAfter()
        {
            var transport = new FakeTransport
            {
                code = SampleCode(intervalSeconds: 5),
                pollResults = new Queue<NeoComposeDevicePollResult>(new[]
                {
                    NeoComposeDevicePollResult.Retry("temporarily unavailable", retryAfterSeconds: 12),
                    NeoComposeDevicePollResult.Success(new NeoComposeDeviceTokenSuccess
                    {
                        accessToken = "t",
                        expiresInSeconds = 3600,
                        scope = "openid",
                    }),
                }),
            };
            var flow = NewFlow(transport, new RecordingTokenStore());

            var result = await flow.AuthorizeAsync(ApiBaseUrl, null, CancellationToken.None);

            Assert.AreEqual(NeoComposeDeviceAuthOutcome.Success, result.outcome);
            CollectionAssert.AreEqual(new[] { 5, 12 }, transport.requestedDelays);
        }

        [Test]
        public void PollError_429AndServerErrorsAreRetryable()
        {
            var now = DateTimeOffset.Parse("2026-07-29T12:00:00Z");
            var rateLimited = new NeoComposeWebResponse(
                429,
                false,
                "",
                "",
                new Dictionary<string, string> { ["Retry-After"] = "17" });
            var unavailable = new NeoComposeWebResponse(503, false, "", "");

            var rateLimitResult = NeoComposeDeviceAuthTransport.MapPollError(null, rateLimited, now);
            var unavailableResult = NeoComposeDeviceAuthTransport.MapPollError(null, unavailable, now);

            Assert.AreEqual(NeoComposeDevicePollStatus.Retry, rateLimitResult.status);
            Assert.AreEqual(17, rateLimitResult.retryAfterSeconds);
            Assert.AreEqual(NeoComposeDevicePollStatus.Retry, unavailableResult.status);
        }

        [Test]
        public void PollError_PermanentOAuthErrorTerminatesFlow()
        {
            var response = new NeoComposeWebResponse(400, false, "", "");
            var error = new NeoComposeDeviceErrorResponse
            {
                error = "invalid_grant",
                errorDescription = "The device grant is invalid.",
            };

            var result = NeoComposeDeviceAuthTransport.MapPollError(
                error,
                response,
                DateTimeOffset.UtcNow);

            Assert.AreEqual(NeoComposeDevicePollStatus.Error, result.status);
            Assert.AreEqual("The device grant is invalid.", result.message);
        }

        // UAUTH-020
        [Test]
        public async Task Authorize_ReturnsDeniedWhenApprovalDenied()
        {
            var transport = new FakeTransport
            {
                code = SampleCode(),
                pollResults = new Queue<NeoComposeDevicePollResult>(new[]
                {
                    NeoComposeDevicePollResult.Denied(),
                }),
            };
            var store = new RecordingTokenStore();
            var flow = NewFlow(transport, store);

            var result = await flow.AuthorizeAsync(ApiBaseUrl, null, CancellationToken.None);

            Assert.AreEqual(NeoComposeDeviceAuthOutcome.Denied, result.outcome);
            Assert.IsNull(store.saved, "No token must be persisted on denial.");
        }

        // UAUTH-020
        [Test]
        public async Task Authorize_ReturnsExpiredWhenTokenExpires()
        {
            var transport = new FakeTransport
            {
                code = SampleCode(),
                pollResults = new Queue<NeoComposeDevicePollResult>(new[]
                {
                    NeoComposeDevicePollResult.Expired(),
                }),
            };
            var flow = NewFlow(transport, new RecordingTokenStore());

            var result = await flow.AuthorizeAsync(ApiBaseUrl, null, CancellationToken.None);

            Assert.AreEqual(NeoComposeDeviceAuthOutcome.Expired, result.outcome);
        }

        // UAUTH-017 / UAUTH-020
        [Test]
        public async Task Authorize_TimesOutWhenDeadlinePassesWhilePending()
        {
            var clock = new FakeClock(DateTimeOffset.UnixEpoch);
            var transport = new FakeTransport
            {
                code = SampleCode(expiresInSeconds: 10, intervalSeconds: 5),
                // Always pending; the deadline should end the loop.
                defaultPoll = NeoComposeDevicePollResult.Pending(),
            };
            // Each simulated delay advances the clock so the 10s deadline passes.
            var flow = new NeoComposeDeviceAuthorizationFlow(
                transport,
                new RecordingTokenStore(),
                () => clock.Now,
                (seconds, _) =>
                {
                    transport.requestedDelays.Add(seconds);
                    clock.Advance(TimeSpan.FromSeconds(seconds));
                    return Task.CompletedTask;
                },
                _ => transport.openedVerificationCount++,
                "neo-compose-unity",
                "openid profile:read");

            var result = await flow.AuthorizeAsync(ApiBaseUrl, null, CancellationToken.None);

            Assert.AreEqual(NeoComposeDeviceAuthOutcome.TimedOut, result.outcome);
        }

        // UAUTH-018
        [Test]
        public async Task Authorize_ReturnsCanceledWhenTokenCancellationRequested()
        {
            using var cts = new CancellationTokenSource();
            var transport = new FakeTransport
            {
                code = SampleCode(),
                delayCallback = () => cts.Cancel(),
                defaultPoll = NeoComposeDevicePollResult.Pending(),
            };
            var flow = NewFlow(transport, new RecordingTokenStore(), cts);

            var result = await flow.AuthorizeAsync(ApiBaseUrl, null, cts.Token);

            Assert.AreEqual(NeoComposeDeviceAuthOutcome.Canceled, result.outcome);
        }

        // UAUTH-020
        [Test]
        public async Task Authorize_FailsWhenDeviceCodeRequestFails()
        {
            var transport = new FakeTransport
            {
                requestException = new NeoComposeDeviceAuthException("origin unreachable"),
            };
            var flow = NewFlow(transport, new RecordingTokenStore());

            var result = await flow.AuthorizeAsync(ApiBaseUrl, null, CancellationToken.None);

            Assert.AreEqual(NeoComposeDeviceAuthOutcome.Failed, result.outcome);
            StringAssert.Contains("origin unreachable", result.message);
        }

        [Test]
        public async Task Authorize_ReturnsPersistenceFailureWhenFreshStoreCannotReloadToken()
        {
            var transport = new FakeTransport
            {
                code = SampleCode(),
                pollResults = new Queue<NeoComposeDevicePollResult>(new[]
                {
                    NeoComposeDevicePollResult.Success(new NeoComposeDeviceTokenSuccess
                    {
                        accessToken = "issued-token",
                        expiresInSeconds = 3600,
                        scope = "openid",
                    }),
                }),
            };
            var writtenStore = new RecordingTokenStore();
            var freshStore = new RecordingTokenStore();
            var flow = NewFlow(transport, writtenStore, verificationStoreFactory: () => freshStore);

            var result = await flow.AuthorizeAsync(ApiBaseUrl, null, CancellationToken.None);

            Assert.AreEqual(NeoComposeDeviceAuthOutcome.PersistenceFailed, result.outcome);
            StringAssert.Contains("could not securely persist", result.message);
            Assert.IsNull(writtenStore.saved, "A partially persisted sign-in must be cleared.");
        }

        private static NeoComposeDeviceCodeResponse SampleCode(
            int expiresInSeconds = 600,
            int intervalSeconds = 5) =>
            new NeoComposeDeviceCodeResponse
            {
                deviceCode = "device-code-123",
                userCode = "WDJB-MJHT",
                verificationUri = "/auth/device",
                verificationUriComplete = "https://example.test/auth/device?user_code=WDJB-MJHT",
                expiresInSeconds = expiresInSeconds,
                intervalSeconds = intervalSeconds,
            };

        private static NeoComposeDeviceAuthorizationFlow NewFlow(
            FakeTransport transport,
            INeoComposeTokenStore store,
            CancellationTokenSource? cts = null,
            Func<INeoComposeTokenStore>? verificationStoreFactory = null)
        {
            return new NeoComposeDeviceAuthorizationFlow(
                transport,
                store,
                () => DateTimeOffset.UtcNow,
                (seconds, token) =>
                {
                    transport.requestedDelays.Add(seconds);
                    transport.delayCallback?.Invoke();
                    token.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                },
                _ => transport.openedVerificationCount++,
                "neo-compose-unity",
                "openid profile:read",
                verificationStoreFactory);
        }

        private sealed class FakeClock
        {
            public FakeClock(DateTimeOffset start) => Now = start;

            public DateTimeOffset Now { get; private set; }

            public void Advance(TimeSpan delta) => Now += delta;
        }

        private sealed class FakeTransport : INeoComposeDeviceAuthTransport
        {
            public NeoComposeDeviceCodeResponse code = new();
            public Queue<NeoComposeDevicePollResult> pollResults = new();
            public NeoComposeDevicePollResult? defaultPoll;
            public NeoComposeUserProfile profile = NeoComposeUserProfile.Empty;
            public NeoComposeDeviceAuthException? requestException;
            public Action? delayCallback;

            public readonly List<int> requestedDelays = new();
            public int pollCount;
            public int openedVerificationCount;

            public Task<NeoComposeDeviceCodeResponse> RequestDeviceCodeAsync(
                string apiBaseUrl,
                string clientId,
                string scope,
                CancellationToken cancellationToken)
            {
                if (requestException != null) throw requestException;
                return Task.FromResult(code);
            }

            public Task<NeoComposeDevicePollResult> PollDeviceTokenAsync(
                string apiBaseUrl,
                string clientId,
                string deviceCode,
                CancellationToken cancellationToken)
            {
                pollCount++;
                if (pollResults.Count > 0) return Task.FromResult(pollResults.Dequeue());
                if (defaultPoll != null) return Task.FromResult(defaultPoll);
                return Task.FromResult(NeoComposeDevicePollResult.Pending());
            }

            public Task<NeoComposeUserProfile> GetProfileAsync(
                string apiBaseUrl,
                string accessToken,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(profile);
            }
        }

        private sealed class RecordingTokenStore : INeoComposeTokenStore
        {
            public NeoComposeStoredToken? saved;

            public NeoComposeStoredToken? Load() => saved;

            public void Save(NeoComposeStoredToken token) => saved = token;

            public void Clear() => saved = null;

            public NeoComposeTokenHint? PeekHint() => saved?.ToHint();
        }
    }
}
