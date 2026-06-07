// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime;
using NeoCompose.Unity.Editor;
using NUnit.Framework;
using UnityEngine;

namespace NeoCompose.Tests
{
    /// <summary>
    /// Covers the Phase 9 runtime-OAuth config surface: building auth options from
    /// synced fields, the pre-ship cloud-sync warning, and the editor sync's
    /// seed-but-never-clobber handling of the developer-owned toggle.
    /// </summary>
    public class NeoComposeConfigCloudSyncTests
    {
        private static NeoComposeConfig MakeConfig()
        {
            var config = ScriptableObject.CreateInstance<NeoComposeConfig>();
            config.apiBaseUrl = "https://neo.test";
            config.projectId = "project-1";
            return config;
        }

        [Test]
        public void TryBuildAuthenticationOptions_JoinsScopesWithSpace_WhenClientPresent()
        {
            var config = MakeConfig();
            config.runtimeOAuthClientId = "neo-compose-runtime-project-1";
            config.runtimeOAuthScopes = new[] { "project:project-1:save:read", "project:project-1:save:write" };

            Assert.IsTrue(config.TryBuildAuthenticationOptions(out var options));
            Assert.IsNotNull(options);
            Assert.AreEqual("neo-compose-runtime-project-1", options!.clientId);
            Assert.AreEqual("project:project-1:save:read project:project-1:save:write", options.scopes);
            Assert.AreEqual("project-1", options.projectId);
        }

        [Test]
        public void TryBuildAuthenticationOptions_False_WhenNoClientId()
        {
            var config = MakeConfig();
            config.runtimeOAuthScopes = new[] { "project:project-1:save:read" };

            Assert.IsFalse(config.TryBuildAuthenticationOptions(out var options));
            Assert.IsNull(options);
        }

        [Test]
        public void TryBuildAuthenticationOptions_False_WhenNoScopes()
        {
            var config = MakeConfig();
            config.runtimeOAuthClientId = "neo-compose-runtime-project-1";
            config.runtimeOAuthScopes = new[] { "", "   " };

            Assert.IsFalse(config.TryBuildAuthenticationOptions(out _));
        }

        [Test]
        public void IsCloudSaveSyncConfigured_RequiresToggleAndClient()
        {
            var config = MakeConfig();
            config.runtimeOAuthClientId = "client-1";

            config.enableOAuthCloudSync = false;
            Assert.IsFalse(config.IsCloudSaveSyncConfigured, "Off toggle stays local-only even with a client.");

            config.enableOAuthCloudSync = true;
            Assert.IsTrue(config.IsCloudSaveSyncConfigured);

            config.runtimeOAuthClientId = "";
            Assert.IsFalse(config.IsCloudSaveSyncConfigured, "On toggle without a client is not configured.");
        }

        [Test]
        public void TryGetCloudSaveSyncWarning_None_WhenSyncOff()
        {
            var config = MakeConfig();
            config.enableOAuthCloudSync = false;

            Assert.IsFalse(config.TryGetCloudSaveSyncWarning(out var warning));
            Assert.IsNull(warning);
        }

        [Test]
        public void TryGetCloudSaveSyncWarning_Warns_WhenSaveScopeButNoClient()
        {
            var config = MakeConfig();
            config.enableOAuthCloudSync = true;
            config.runtimeOAuthScopes = new[] { "project:project-1:save:read" };

            Assert.IsTrue(config.TryGetCloudSaveSyncWarning(out var warning));
            StringAssert.Contains("client id", warning);
        }

        [Test]
        public void TryGetCloudSaveSyncWarning_None_WhenSaveScopeAndClientPresent()
        {
            var config = MakeConfig();
            config.enableOAuthCloudSync = true;
            config.runtimeOAuthClientId = "client-1";
            config.runtimeOAuthScopes = new[] { "project:project-1:save:read" };

            Assert.IsFalse(config.TryGetCloudSaveSyncWarning(out _));
        }

        [Test]
        public void TryGetCloudSaveSyncWarning_Warns_WhenRuntimeScopeButNoApiKey()
        {
            var config = MakeConfig();
            config.enableOAuthCloudSync = true;
            config.runtimeOAuthClientId = "client-1";
            config.runtimeOAuthScopes = new[] { "project:project-1:save:read", "project:project-1:runtime:read" };

            Assert.IsTrue(config.TryGetCloudSaveSyncWarning(out var warning));
            StringAssert.Contains("runtime API key", warning);
        }

        [Test]
        public void TryGetCloudSaveSyncWarning_None_WhenRuntimeScopeAndApiKeyPresent()
        {
            var config = MakeConfig();
            config.enableOAuthCloudSync = true;
            config.runtimeOAuthClientId = "client-1";
            config.projectRuntimeApiKey = "rk_live_123";
            config.runtimeOAuthScopes = new[] { "project:project-1:runtime:read" };

            Assert.IsFalse(config.TryGetCloudSaveSyncWarning(out _));
        }

        [Test]
        public void ApplyRuntimeOAuthConfig_Clears_WhenNotConfiguredForVersion()
        {
            var config = MakeConfig();
            config.runtimeOAuthClientId = "stale-client";
            config.runtimeOAuthScopes = new[] { "project:project-1:save:read" };

            NeoComposeSynchronizer.ApplyRuntimeOAuthConfig(
                config,
                new NeoComposeUnityRuntimeOAuthConfig { configuredForVersion = false });

            Assert.AreEqual("", config.runtimeOAuthClientId);
            Assert.IsEmpty(config.runtimeOAuthScopes);
        }

        [Test]
        public void ApplyRuntimeOAuthConfig_SeedsToggle_OnFirstClientAvailability()
        {
            var config = MakeConfig();
            Assert.IsFalse(config.enableOAuthCloudSync);

            NeoComposeSynchronizer.ApplyRuntimeOAuthConfig(
                config,
                new NeoComposeUnityRuntimeOAuthConfig
                {
                    configuredForVersion = true,
                    runtimeOAuthClientId = "client-1",
                    scopes = new[] { "project:project-1:save:read" },
                });

            Assert.AreEqual("client-1", config.runtimeOAuthClientId);
            Assert.AreEqual(new[] { "project:project-1:save:read" }, config.runtimeOAuthScopes);
            Assert.IsTrue(config.enableOAuthCloudSync, "Toggle seeds true the first time a client becomes available.");
        }

        [Test]
        public void ApplyRuntimeOAuthConfig_NeverClobbers_DeveloperToggleOff()
        {
            var config = MakeConfig();
            // Developer already has a client and deliberately turned sync OFF.
            config.runtimeOAuthClientId = "client-1";
            config.enableOAuthCloudSync = false;

            NeoComposeSynchronizer.ApplyRuntimeOAuthConfig(
                config,
                new NeoComposeUnityRuntimeOAuthConfig
                {
                    configuredForVersion = true,
                    runtimeOAuthClientId = "client-1",
                    scopes = new[] { "project:project-1:save:read" },
                });

            Assert.IsFalse(config.enableOAuthCloudSync, "Sync must not re-enable a toggle the developer turned off.");
        }

        [Test]
        public void ApplyRuntimeOAuthConfig_RespectsOverride_DoesNotClobberManualValues()
        {
            var config = MakeConfig();
            config.runtimeOAuthClientId = "manual-client";
            config.runtimeOAuthScopes = new[] { "project:project-1:save:read" };
            config.runtimeOAuthOverridden = true;

            NeoComposeSynchronizer.ApplyRuntimeOAuthConfig(
                config,
                new NeoComposeUnityRuntimeOAuthConfig
                {
                    configuredForVersion = true,
                    runtimeOAuthClientId = "synced-client",
                    scopes = new[] { "project:project-1:save:write" },
                });

            Assert.AreEqual("manual-client", config.runtimeOAuthClientId, "An override must not be clobbered by sync.");
            Assert.AreEqual(new[] { "project:project-1:save:read" }, config.runtimeOAuthScopes);
        }

        [Test]
        public void ApplyRuntimeOAuthConfig_RespectsOverride_EvenWhenNotConfiguredForVersion()
        {
            var config = MakeConfig();
            config.runtimeOAuthClientId = "manual-client";
            config.runtimeOAuthOverridden = true;

            NeoComposeSynchronizer.ApplyRuntimeOAuthConfig(
                config,
                new NeoComposeUnityRuntimeOAuthConfig { configuredForVersion = false });

            Assert.AreEqual("manual-client", config.runtimeOAuthClientId, "An override is not cleared by sync.");
        }

        [Test]
        public void ApplyRuntimeOAuthConfig_PreservesToggleOn_AcrossSync()
        {
            var config = MakeConfig();
            config.runtimeOAuthClientId = "client-1";
            config.enableOAuthCloudSync = true;

            NeoComposeSynchronizer.ApplyRuntimeOAuthConfig(
                config,
                new NeoComposeUnityRuntimeOAuthConfig
                {
                    configuredForVersion = true,
                    runtimeOAuthClientId = "client-1",
                    scopes = new[] { "project:project-1:save:write" },
                });

            Assert.IsTrue(config.enableOAuthCloudSync);
            Assert.AreEqual(new[] { "project:project-1:save:write" }, config.runtimeOAuthScopes);
        }
    }
}
