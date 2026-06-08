// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.IO;
using System.Text.RegularExpressions;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeoCompose.Tests
{
    /// <summary>
    /// Phase 9 runtime cloud wiring: the <see cref="NeoProjectStore"/> config master
    /// switch (<c>enableOAuthCloudSync</c>), the <see cref="NeoClient.CommitAsync"/>
    /// local mapping, and the generated client's auth/api accessors.
    /// </summary>
    public class NeoRuntimeCloudConfigTests
    {
        private const string PackageRoot = "Packages/com.ryanbliss.neocompose/Tests";

        private static string LoadFixture(string fileName) =>
            File.ReadAllText(Path.Combine(PackageRoot, fileName));

        private sealed class InMemoryTokenStore : INeoComposeTokenStore
        {
            private NeoComposeStoredToken? token;
            public NeoComposeStoredToken? Load() => token;
            public void Save(NeoComposeStoredToken value) => token = value;
            public void Clear() => token = null;
            public NeoComposeTokenHint? PeekHint() => token?.ToHint();
        }

        private static NeoComposeConfig CloudReadyConfig()
        {
            var config = ScriptableObject.CreateInstance<NeoComposeConfig>();
            config.apiBaseUrl = "https://neo.test";
            config.projectId = "project-1";
            config.targetReleaseChannelId = "channel-1";
            config.runtimeOAuthClientId = "neo-compose-runtime-project-1";
            config.runtimeOAuthScopes = new[] { "project:project-1:save:read", "project:project-1:save:write" };
            return config;
        }

        [Test]
        public void FromConfig_OffStaysLocal_EvenWhenEveryFieldPopulated()
        {
            var config = CloudReadyConfig();
            config.enableOAuthCloudSync = false;

            var store = new NeoProjectStore(config: config, localStore: new NeoInMemoryLocalSaveStore());

            Assert.IsNull(store.Authentication, "Cloud sync off must never wire authentication.");
        }

        [Test]
        public void FromConfig_MissingCredential_WarnsAndFallsBackToLocal()
        {
            var config = CloudReadyConfig();
            config.enableOAuthCloudSync = true;
            config.runtimeOAuthClientId = ""; // credential the save scopes require is missing.

            LogAssert.Expect(LogType.Warning, new Regex("cloud save sync is enabled but"));
            var store = new NeoProjectStore(config: config, localStore: new NeoInMemoryLocalSaveStore());

            Assert.IsNull(store.Authentication, "A missing credential falls back to local-only, not a throw.");
        }

        [Test]
        public void FromConfig_AutoConstructsAuthentication_WhenConfiguredAndNonePassed()
        {
            var config = CloudReadyConfig();
            config.enableOAuthCloudSync = true;

            // Building the default authentication touches the platform token store,
            // which logs a one-time not-hardware-backed warning; that is not under test.
            LogAssert.ignoreFailingMessages = true;
            try
            {
                var store = new NeoProjectStore(config: config, localStore: new NeoInMemoryLocalSaveStore());
                Assert.IsNotNull(store.Authentication, "Cloud sync on with a configured client auto-wires auth.");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        [Test]
        public void FromConfig_UsesExplicitAuthentication_WhenProvided()
        {
            var config = CloudReadyConfig();
            config.enableOAuthCloudSync = true;
            var explicitAuth = new NeoAuthentication(
                new NeoAuthenticationOptions(
                    config.apiBaseUrl,
                    config.projectId,
                    config.runtimeOAuthClientId,
                    "project:project-1:save:read"),
                new InMemoryTokenStore());

            var store = new NeoProjectStore(config: config, localStore: new NeoInMemoryLocalSaveStore(), authentication: explicitAuth);

            Assert.AreSame(explicitAuth, store.Authentication);
        }

        [Test]
        public void CommitAsync_PersistsThroughLoaderToLocalStore()
        {
            var stack = NeoTestSaveStack.Create(LoadFixture("synth-example.json"));
            var client = stack.Load();
            client.SetSaveValue(new StringAttributeValue
            {
                id = "committed-value",
                createdAt = "now",
                updatedAt = "now",
                value = "persisted",
            });

            Assert.IsNull(stack.PersistedContent(), "Nothing is persisted until the first commit.");

            client.CommitAsync().GetAwaiter().GetResult();

            var persisted = stack.PersistedContent();
            Assert.IsNotNull(persisted);
            StringAssert.Contains("committed-value", persisted);
        }

        [Test]
        public void LocalOnlyClient_ExposesSynchronizerButNoCloudAccessors()
        {
            var stack = NeoTestSaveStack.Create(LoadFixture("synth-example.json"));
            var client = stack.Load();

            Assert.AreSame(stack.Synchronizer, client.Synchronizer);
            Assert.IsNull(client.ApiClient, "A local-only loader exposes no cloud transport.");
            Assert.IsNull(client.Authentication, "A local-only loader exposes no authentication.");
        }
    }
}
