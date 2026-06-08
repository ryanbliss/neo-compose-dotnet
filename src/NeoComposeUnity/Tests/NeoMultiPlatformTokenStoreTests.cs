// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NeoCompose.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeoCompose.Tests
{
    public class NeoMultiPlatformTokenStoreTests
    {
        private const string AuthBaseUrl = "https://example.test";
        private const string ProjectId = "project-1";

        private sealed class FakeSecretStore : INeoAuthSecretStore
        {
            public readonly Dictionary<string, string> values = new();
            public string? Read(string key) => values.TryGetValue(key, out var v) ? v : null;
            public void Write(string key, string secret) => values[key] = secret;
            public void Delete(string key) => values.Remove(key);
        }

        private sealed class FakeHintStore : INeoComposeTokenHintStore
        {
            public readonly Dictionary<string, string> values = new();
            public string? Read(string key) => values.TryGetValue(key, out var v) ? v : null;
            public void Write(string key, string value) => values[key] = value;
            public void Delete(string key) => values.Remove(key);
        }

        private static NeoComposeStoredToken SampleToken() =>
            new NeoComposeStoredToken(
                accessToken: "secret-access-token-123",
                expiresAtUnixSeconds: DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds(),
                scopes: new[] { "openid", "profile:read", "project:project-1:save:write" },
                authBaseUrl: AuthBaseUrl,
                displayName: "Ada Lovelace",
                displayEmail: "ada@example.test");

        [Test]
        public void Store_RoundTripsSaveLoadAndClear()
        {
            var store = new NeoMultiPlatformTokenStore(
                "account-a", new FakeSecretStore(), new FakeHintStore());
            Assert.That(store.Load(), Is.Null);

            var token = SampleToken();
            store.Save(token);

            var loaded = store.Load();
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.accessToken, Is.EqualTo(token.accessToken));
            Assert.That(loaded.displayEmail, Is.EqualTo("ada@example.test"));
            Assert.That(loaded.scopes, Is.EquivalentTo(token.scopes));

            var hint = store.PeekHint();
            Assert.That(hint, Is.Not.Null);
            Assert.That(hint!.displayName, Is.EqualTo("Ada Lovelace"));

            store.Clear();
            Assert.That(store.Load(), Is.Null);
            Assert.That(store.PeekHint(), Is.Null);
        }

        [Test]
        public void Store_LoadReturnsNull_WhenSecretMissingDespiteHint()
        {
            var secret = new FakeSecretStore();
            var hint = new FakeHintStore();
            var store = new NeoMultiPlatformTokenStore("account-a", secret, hint);
            store.Save(SampleToken());

            // Secret store wiped (e.g. keychain entry lost) but hint remains.
            secret.values.Clear();

            Assert.That(store.Load(), Is.Null);
            Assert.That(store.PeekHint(), Is.Not.Null);
        }

        [Test]
        public void Store_IsolatesCredentialsAcrossAccounts()
        {
            // Two games on one device share the backing store but use different
            // namespaced account keys, so neither can read the other's token.
            var secret = new FakeSecretStore();
            var hint = new FakeHintStore();
            var gameA = new NeoMultiPlatformTokenStore("account-a", secret, hint);
            var gameB = new NeoMultiPlatformTokenStore("account-b", secret, hint);

            gameA.Save(SampleToken());

            Assert.That(gameA.Load(), Is.Not.Null);
            Assert.That(gameB.Load(), Is.Null);
        }

        [Test]
        public void Namespace_DistinctPerApplicationAuthHostAndProject()
        {
            var gameA = new NeoAuthCredentialNamespace("com.studio.game-a");
            var gameB = new NeoAuthCredentialNamespace("com.studio.game-b");

            var keyA = gameA.BuildAccountKey(AuthBaseUrl, ProjectId);
            var keyB = gameB.BuildAccountKey(AuthBaseUrl, ProjectId);
            Assert.That(keyA, Is.Not.EqualTo(keyB), "different apps must not collide");

            Assert.That(
                gameA.BuildAccountKey("https://other.test", ProjectId),
                Is.Not.EqualTo(keyA),
                "different auth hosts must not collide");
            Assert.That(
                gameA.BuildAccountKey(AuthBaseUrl, "project-2"),
                Is.Not.EqualTo(keyA),
                "different projects must not collide");

            // Stable + opaque (hex SHA-256).
            Assert.That(gameA.BuildAccountKey(AuthBaseUrl, ProjectId), Is.EqualTo(keyA));
            Assert.That(keyA, Does.Match("^[0-9a-f]{64}$"));
        }

        [Test]
        public void Namespace_AuthHostIgnoresPathAndTrailingSlash()
        {
            var ns = new NeoAuthCredentialNamespace("com.studio.game-a");
            var bare = ns.BuildAccountKey("https://example.test", ProjectId);
            Assert.That(ns.BuildAccountKey("https://example.test/", ProjectId), Is.EqualTo(bare));
            Assert.That(ns.BuildAccountKey("https://example.test/api/auth", ProjectId), Is.EqualTo(bare));
            // Port is significant.
            Assert.That(
                ns.BuildAccountKey("http://localhost:3000", ProjectId),
                Is.Not.EqualTo(ns.BuildAccountKey("http://localhost:4000", ProjectId)));
        }

        [Test]
        public void Namespace_SharedNamespaceOverrideEnablesIntentionalSharing()
        {
            // Same app + same overridden shared namespace → same key (sharing).
            var a = new NeoAuthCredentialNamespace("com.studio.game-a", "studio-suite");
            var b = new NeoAuthCredentialNamespace("com.studio.game-a", "studio-suite");
            Assert.That(
                a.BuildAccountKey(AuthBaseUrl, ProjectId),
                Is.EqualTo(b.BuildAccountKey(AuthBaseUrl, ProjectId)));

            // Different shared namespace → different key (default vs override).
            var def = new NeoAuthCredentialNamespace("com.studio.game-a");
            Assert.That(
                a.BuildAccountKey(AuthBaseUrl, ProjectId),
                Is.Not.EqualTo(def.BuildAccountKey(AuthBaseUrl, ProjectId)));
        }

        [Test]
        public void Create_WarnsAndRoundTripsThroughObfuscatedPlayerPrefsFallback()
        {
            LogAssert.Expect(LogType.Warning, new Regex("hardware-backed credential store"));
            var ns = new NeoAuthCredentialNamespace("com.studio.fallback-test");
            var store = NeoMultiPlatformTokenStore.Create(AuthBaseUrl, ProjectId, ns);

            try
            {
                store.Save(SampleToken());
                var loaded = store.Load();
                Assert.That(loaded, Is.Not.Null);
                Assert.That(loaded!.accessToken, Is.EqualTo("secret-access-token-123"));
            }
            finally
            {
                store.Clear();
            }
        }

        [Test]
        public void ObfuscatedSecretStore_EncryptsAtRestAndRoundTrips()
        {
            var key = "NeoCompose.Test.Secret." + Guid.NewGuid().ToString("N");
            var secretStore = new NeoObfuscatedPlayerPrefsSecretStore();
            try
            {
                secretStore.Write(key, "plaintext-token");
                Assert.That(secretStore.Read(key), Is.EqualTo("plaintext-token"));
                // The persisted value is ciphertext, not the plaintext token.
                Assert.That(PlayerPrefs.GetString(key), Is.Not.EqualTo("plaintext-token"));
                Assert.That(PlayerPrefs.GetString(key), Is.Not.Empty);

                secretStore.Delete(key);
                Assert.That(secretStore.Read(key), Is.Null);
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
            }
        }
    }
}
