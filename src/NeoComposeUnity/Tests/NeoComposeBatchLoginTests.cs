// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using NeoCompose.Runtime;
using NeoCompose.Unity.Editor;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// The one decision headless sign-in makes on its own: whether the
    /// credential already stored for the rig origin can stand in for a fresh
    /// device authorization. Everything else the batch entry point does is the
    /// shared device flow, covered by
    /// <see cref="NeoComposeDeviceAuthFlowTests"/>.
    /// </summary>
    public class NeoComposeBatchLoginTests
    {
        private static readonly DateTimeOffset Now =
            DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

        private static NeoComposeStoredToken TokenExpiringIn(TimeSpan life, string accessToken = "token")
        {
            return new NeoComposeStoredToken(
                accessToken,
                (Now + life).ToUnixTimeSeconds(),
                new[] { "openid" },
                "http://127.0.0.1:31104",
                "Agent",
                "agent@studiobliss.io");
        }

        [Test]
        public void BatchLogin_ReusesACredentialWithLifeLeft()
        {
            var stored = TokenExpiringIn(NeoComposeBatchLogin.ReuseMargin + TimeSpan.FromMinutes(1));

            Assert.IsTrue(NeoComposeBatchLogin.CanReuseStoredSignIn(stored, Now, force: false));
        }

        [Test]
        public void BatchLogin_SignsInWhenNothingIsStored()
        {
            Assert.IsFalse(NeoComposeBatchLogin.CanReuseStoredSignIn(null, Now, force: false));
        }

        [Test]
        public void BatchLogin_SignsInWhenTheStoredCredentialCarriesNoAccessToken()
        {
            var stored = TokenExpiringIn(TimeSpan.FromHours(1), accessToken: "");

            Assert.IsFalse(NeoComposeBatchLogin.CanReuseStoredSignIn(stored, Now, force: false));
        }

        [Test]
        public void BatchLogin_SignsInWhenTheStoredCredentialExpiresWithinTheMargin()
        {
            // A token that dies mid-synchronize is no better than no token.
            var stored = TokenExpiringIn(NeoComposeBatchLogin.ReuseMargin - TimeSpan.FromMinutes(1));

            Assert.IsFalse(NeoComposeBatchLogin.CanReuseStoredSignIn(stored, Now, force: false));
        }

        [Test]
        public void BatchLogin_SignsInWhenForced()
        {
            var stored = TokenExpiringIn(TimeSpan.FromHours(8));

            Assert.IsFalse(NeoComposeBatchLogin.CanReuseStoredSignIn(stored, Now, force: true));
        }
    }
}
