// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using NeoCompose.Unity.Editor;
using NUnit.Framework;
using UnityEditor;

using NeoCompose.Runtime;

namespace NeoCompose.Tests
{
    public class NeoComposeTokenStoreTests
    {
        private const string AuthBaseUrl = "https://example.test";

        private string tempRoot = "";
        private readonly List<string> editorPrefsKeysToClear = new();

        [SetUp]
        public void SetUp()
        {
            tempRoot = Path.Combine(
                Path.GetTempPath(),
                "neo-compose-token-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
            foreach (var key in editorPrefsKeysToClear)
            {
                EditorPrefs.DeleteKey(key);
            }

            editorPrefsKeysToClear.Clear();
        }

        private NeoComposeStoredToken SampleToken() =>
            new NeoComposeStoredToken(
                accessToken: "secret-access-token-value-123",
                expiresAtUnixSeconds: DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds(),
                scopes: new[] { "openid", "profile:read", "project:list", "unity:export" },
                authBaseUrl: AuthBaseUrl,
                displayName: "Ada Lovelace",
                displayEmail: "ada@example.test");

        // UAUTH-010
        [Test]
        public void Store_RoundTripsAndClearsToken()
        {
            var store = new NeoComposeTokenStore(
                AuthBaseUrl,
                new NeoComposeFileSecretBackend(tempRoot),
                new InMemoryHintStore());

            Assert.IsNull(store.Load(), "Expected no token before save.");

            var token = SampleToken();
            store.Save(token);

            var loaded = store.Load();
            Assert.IsNotNull(loaded);
            Assert.AreEqual(token.accessToken, loaded!.accessToken);
            Assert.AreEqual(token.expiresAtUnixSeconds, loaded.expiresAtUnixSeconds);
            Assert.AreEqual(token.displayName, loaded.displayName);
            Assert.AreEqual(token.displayEmail, loaded.displayEmail);
            CollectionAssert.AreEqual(token.scopes, loaded.scopes);

            store.Clear();
            Assert.IsNull(store.Load(), "Expected no token after clear.");
        }

        // UAUTH-010
        [Test]
        public void Store_PeekHintReturnsIdentityWithoutSecret()
        {
            var hintStore = new InMemoryHintStore();
            var store = new NeoComposeTokenStore(
                AuthBaseUrl,
                new NeoComposeFileSecretBackend(tempRoot),
                hintStore);
            store.Save(SampleToken());

            var hint = store.PeekHint();
            Assert.IsNotNull(hint);
            Assert.AreEqual("Ada Lovelace", hint!.displayName);
            Assert.AreEqual("ada@example.test", hint.displayEmail);

            foreach (var value in hintStore.Values)
            {
                StringAssert.DoesNotContain(
                    "secret-access-token-value-123",
                    value,
                    "Hint store must never contain the access token.");
            }
        }

        // UAUTH-011
        [Test]
        public void DefaultStore_WritesSecretOutsideAssetsAndNotInEditorPrefs()
        {
            var hintStore = new NeoComposeEditorPrefsTokenHintStore();
            var fileBackend = new NeoComposeFileSecretBackend(tempRoot);
            var store = new NeoComposeTokenStore(AuthBaseUrl, fileBackend, hintStore);
            var hintKey = "NeoCompose.Unity.TokenHint." + NeoComposeSecretKey.Sanitize(AuthBaseUrl);
            editorPrefsKeysToClear.Add(hintKey);

            store.Save(SampleToken());

            // Secret file lives outside the Unity project tree.
            StringAssert.DoesNotContain(
                "/Assets/",
                fileBackend.Root.Replace('\\', '/') + "/",
                "Secret backend must not write inside Assets/.");
            Assert.IsTrue(
                Directory.GetFiles(tempRoot).Length > 0,
                "Expected the secret to be written to the restricted file root.");

            // EditorPrefs holds only the non-secret hint, never the token.
            Assert.IsTrue(EditorPrefs.HasKey(hintKey));
            StringAssert.DoesNotContain(
                "secret-access-token-value-123",
                EditorPrefs.GetString(hintKey),
                "EditorPrefs must never contain the access token.");

            store.Clear();
            Assert.IsFalse(EditorPrefs.HasKey(hintKey), "Clear must remove the EditorPrefs hint.");
            Assert.AreEqual(0, Directory.GetFiles(tempRoot).Length, "Clear must remove the secret file.");
        }

        // UAUTH-009 / UAUTH-011
        [Test]
        public void FileSecretBackend_RoundTripsAndDeletes()
        {
            var backend = new NeoComposeFileSecretBackend(tempRoot);
            Assert.IsNull(backend.Read("svc", "acct"));

            backend.Write("svc", "acct", "value-1");
            Assert.AreEqual("value-1", backend.Read("svc", "acct"));

            backend.Write("svc", "acct", "value-2");
            Assert.AreEqual("value-2", backend.Read("svc", "acct"), "Write must overwrite in place.");

            backend.Delete("svc", "acct");
            Assert.IsNull(backend.Read("svc", "acct"));
        }

        [Test]
        public void StoredToken_IsExpiredReflectsAbsoluteExpiry()
        {
            var now = DateTimeOffset.UtcNow;
            var expired = new NeoComposeStoredToken(
                "t", now.AddMinutes(-1).ToUnixTimeSeconds(), Array.Empty<string>(), AuthBaseUrl, "", "");
            var valid = new NeoComposeStoredToken(
                "t", now.AddMinutes(5).ToUnixTimeSeconds(), Array.Empty<string>(), AuthBaseUrl, "", "");

            Assert.IsTrue(expired.IsExpired(now));
            Assert.IsFalse(valid.IsExpired(now));
        }

        [Test]
        public void SecretKey_SanitizesUnsafeCharacters()
        {
            Assert.AreEqual(
                "https___example.test_3000",
                NeoComposeSecretKey.Sanitize("https://example.test:3000"));
            Assert.AreEqual("default", NeoComposeSecretKey.Sanitize(""));
        }

        private sealed class InMemoryHintStore : INeoComposeTokenHintStore
        {
            private readonly Dictionary<string, string> values = new();

            public IEnumerable<string> Values => values.Values;

            public string? Read(string key) => values.TryGetValue(key, out var value) ? value : null;

            public void Write(string key, string value) => values[key] = value;

            public void Delete(string key) => values.Remove(key);
        }
    }
}
