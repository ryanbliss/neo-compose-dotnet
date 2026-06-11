// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using NUnit.Framework;

namespace NeoCompose.Convex.Tests
{
    public sealed class ConvexJwtTokenProviderTests
    {
        private FakeAccessTokenProvider tokens = null!;
        private FakeHttpClient http = null!;
        private DateTimeOffset now;

        [SetUp]
        public void SetUp()
        {
            tokens = new FakeAccessTokenProvider();
            http = new FakeHttpClient();
            now = new DateTimeOffset(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);
        }

        private ConvexJwtTokenProvider CreateProvider(string apiBaseUrl = "https://api.example/") =>
            new ConvexJwtTokenProvider(apiBaseUrl, tokens, http, () => now);

        private static NeoComposeWebResponse Ok(string body) =>
            new NeoComposeWebResponse(200, false, body, "");

        [Test]
        public async Task MintsViaConvexTokenEndpointWithSessionBearer()
        {
            var jwt = TestJwt.WithExpiry(now.AddMinutes(15));
            http.Responses.Enqueue(Ok(TestJwt.MintResponseJson(jwt)));

            var minted = await CreateProvider().GetTokenAsync();

            Assert.That(minted, Is.EqualTo(jwt));
            Assert.That(http.Requests, Has.Count.EqualTo(1));
            var request = http.Requests[0];
            Assert.That(request.url, Is.EqualTo("https://api.example/api/auth/convex/token"));
            Assert.That(request.method, Is.EqualTo("GET"));
            Assert.That(request.body, Is.Null);
            Assert.That(request.bearer, Is.EqualTo("session-token"));
        }

        [Test]
        public async Task CachesUntilSlackBeforeExpiryThenRemints()
        {
            var first = TestJwt.WithExpiry(now.AddMinutes(15));
            var second = TestJwt.WithExpiry(now.AddMinutes(45));
            http.Responses.Enqueue(Ok(TestJwt.MintResponseJson(first)));
            http.Responses.Enqueue(Ok(TestJwt.MintResponseJson(second)));
            var provider = CreateProvider();

            Assert.That(await provider.GetTokenAsync(), Is.EqualTo(first));
            Assert.That(await provider.GetTokenAsync(), Is.EqualTo(first));
            Assert.That(http.Requests, Has.Count.EqualTo(1), "second call must hit the cache");

            // Inside the 60s slack window the cache no longer satisfies.
            now = now.AddMinutes(15).AddSeconds(-30);
            Assert.That(await provider.GetTokenAsync(), Is.EqualTo(second));
            Assert.That(http.Requests, Has.Count.EqualTo(2));
        }

        [Test]
        public void Maps401ToNotSignedIn()
        {
            http.Responses.Enqueue(new NeoComposeWebResponse(401, false, "", ""));
            var provider = CreateProvider();

            Assert.ThrowsAsync<NeoComposeNotSignedInException>(() => provider.GetTokenAsync());
            Assert.That(provider.LastFailureWasAuthRejection, Is.True);
        }

        [Test]
        public void ConnectionErrorThrowsDistinctTransportError()
        {
            http.Responses.Enqueue(new NeoComposeWebResponse(0, true, "", "timeout"));
            var provider = CreateProvider();

            var exception = Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.GetTokenAsync());
            Assert.That(exception!.Message, Does.Contain("(connection)"));
            Assert.That(provider.LastFailureWasAuthRejection, Is.False);
        }

        [Test]
        public void MissingTokenFieldThrows()
        {
            http.Responses.Enqueue(Ok("{}"));

            var exception = Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateProvider().GetTokenAsync());
            Assert.That(exception!.Message, Does.Contain("\"token\" field"));
        }

        [Test]
        public void SignedOutFailsFastWithoutNetwork()
        {
            tokens.Token = null;
            var provider = CreateProvider();

            Assert.ThrowsAsync<NeoComposeNotSignedInException>(() => provider.GetTokenAsync());
            Assert.That(http.Requests, Is.Empty);
            Assert.That(provider.LastFailureWasAuthRejection, Is.True);
        }

        [Test]
        public void MalformedJwtThrows()
        {
            http.Responses.Enqueue(Ok(TestJwt.MintResponseJson("not-a-jwt")));

            var exception = Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateProvider().GetTokenAsync());
            Assert.That(exception!.Message, Does.Contain("3 dot-separated"));
        }

        [Test]
        public void JwtWithoutExpiryClaimThrows()
        {
            http.Responses.Enqueue(Ok(TestJwt.MintResponseJson("h.e30.s"))); // payload {}

            var exception = Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateProvider().GetTokenAsync());
            Assert.That(exception!.Message, Does.Contain("\"exp\" claim"));
        }

        [Test]
        public async Task InvalidateDropsTheCachedJwt()
        {
            var jwt = TestJwt.WithExpiry(now.AddMinutes(15));
            http.Responses.Enqueue(Ok(TestJwt.MintResponseJson(jwt)));
            http.Responses.Enqueue(Ok(TestJwt.MintResponseJson(jwt)));
            var provider = CreateProvider();

            await provider.GetTokenAsync();
            provider.Invalidate();
            await provider.GetTokenAsync();

            Assert.That(http.Requests, Has.Count.EqualTo(2));
        }
    }
}
