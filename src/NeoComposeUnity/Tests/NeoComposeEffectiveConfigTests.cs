// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using NeoCompose.Runtime;
using NeoCompose.Unity.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace NeoCompose.Tests
{
    /// <summary>
    /// Covers the editor-only effective-configuration overlay (P53 §6): the
    /// committed asset is never mutated, endpoints always come from the rig, and
    /// developer-owned fields keep their latch.
    /// </summary>
    public class NeoComposeEffectiveConfigTests
    {
        private const string PackageRoot =
            "Packages/com.ryanbliss.neocompose/Tests";

        private NeoComposeConfig? committed;
        private NeoComposeConfig? overlay;

        [SetUp]
        public void SetUp()
        {
            NeoComposeEffectiveConfig.ResetForTests();
            committed = ScriptableObject.CreateInstance<NeoComposeConfig>();
            committed.name = "NeoComposeConfig";
            committed.apiBaseUrl = "https://neocompose.app";
            committed.convexUrl = "https://production-deployment.convex.cloud";
            committed.projectId = "committed-project";
            committed.projectName = "Committed Project";
            committed.versionId = "committed-version";
            committed.targetReleaseChannelId = "committed-channel";
            committed.runtimeOAuthClientId = "committed-client";
            committed.runtimeOAuthScopes = new[] { "project:committed-project:save:read" };
        }

        [TearDown]
        public void TearDown()
        {
            if (overlay != null) UnityEngine.Object.DestroyImmediate(overlay);
            if (committed != null) UnityEngine.Object.DestroyImmediate(committed);
            overlay = null;
            committed = null;
            NeoComposeEffectiveConfig.ResetForTests();
        }

        private const string TempRoot = "Assets/NeoComposeEffectiveConfigTestsTemp";

        private static void CleanupTempRoot()
        {
            if (AssetDatabase.IsValidFolder(TempRoot))
            {
                AssetDatabase.DeleteAsset(TempRoot);
            }
        }

        private static void EnsureTempRoot()
        {
            CleanupTempRoot();
            AssetDatabase.CreateFolder("Assets", "NeoComposeEffectiveConfigTestsTemp");
        }

        private static NeoComposeRigManifest LoadManifest(string fileName)
        {
            return NeoComposeRigManifestReader.Parse(
                File.ReadAllText(Path.Combine(PackageRoot, fileName)),
                fileName);
        }

        [Test]
        public void Overlay_AppliesEndpointsOnlyForAnUnseededRig()
        {
            var manifest = LoadManifest("rig-manifest-unseeded.json");

            overlay = NeoComposeEffectiveConfig.Apply(committed!, manifest);

            Assert.AreEqual("http://127.0.0.1:31100", overlay.apiBaseUrl);
            Assert.AreEqual("https://formal-toucan-689.convex.cloud", overlay.convexUrl);
            // No seeded sample: the committed selection stays in force.
            Assert.AreEqual("committed-project", overlay.projectId);
            Assert.AreEqual("Committed Project", overlay.projectName);
            Assert.AreEqual("committed-version", overlay.versionId);
            Assert.AreEqual("committed-channel", overlay.targetReleaseChannelId);
            Assert.AreEqual("committed-client", overlay.runtimeOAuthClientId);
        }

        [Test]
        public void Overlay_AppliesSampleIdentityForASeededRig()
        {
            var manifest = LoadManifest("rig-manifest-seeded.json");

            overlay = NeoComposeEffectiveConfig.Apply(committed!, manifest);

            Assert.AreEqual("http://127.0.0.1:31200", overlay.apiBaseUrl);
            Assert.AreEqual("https://brisk-ocelot-412.convex.cloud", overlay.convexUrl);
            Assert.AreEqual("k57c3n1q8w2m0v9xr4jb6d5haz", overlay.projectId);
            Assert.AreEqual("Hello World", overlay.projectName);
            Assert.AreEqual("j92f7t0p3s6y1k4nw8rc5b2mqe", overlay.versionId);
            Assert.AreEqual("h41d9x5v7z0g2l6ps3bt8n1krw", overlay.targetReleaseChannelId);
            Assert.AreEqual("neo-rig-brisk-ocelot-hello-world", overlay.runtimeOAuthClientId);
            CollectionAssert.AreEqual(
                new[]
                {
                    "project:k57c3n1q8w2m0v9xr4jb6d5haz:save:read",
                    "project:k57c3n1q8w2m0v9xr4jb6d5haz:save:write",
                },
                overlay.runtimeOAuthScopes);
        }

        [Test]
        public void Overlay_LeavesTheCommittedAssetUntouched()
        {
            var manifest = LoadManifest("rig-manifest-seeded.json");

            overlay = NeoComposeEffectiveConfig.Apply(committed!, manifest);

            Assert.AreNotSame(committed, overlay);
            Assert.AreEqual("https://neocompose.app", committed!.apiBaseUrl);
            Assert.AreEqual("https://production-deployment.convex.cloud", committed.convexUrl);
            Assert.AreEqual("committed-project", committed.projectId);
            Assert.AreEqual(HideFlags.DontSave, overlay.hideFlags);
        }

        [Test]
        public void Overlay_RespectsTheRuntimeOAuthOverrideLatch()
        {
            committed!.runtimeOAuthOverridden = true;
            var manifest = LoadManifest("rig-manifest-seeded.json");

            overlay = NeoComposeEffectiveConfig.Apply(committed, manifest);

            // Project identity still follows the rig; the hand-edited runtime OAuth
            // fields are developer-owned and stick.
            Assert.AreEqual("k57c3n1q8w2m0v9xr4jb6d5haz", overlay.projectId);
            Assert.AreEqual("committed-client", overlay.runtimeOAuthClientId);
            CollectionAssert.AreEqual(
                new[] { "project:committed-project:save:read" },
                overlay.runtimeOAuthScopes);
        }

        [Test]
        public void Overlay_IsRecognizedAsARigOverlay()
        {
            overlay = NeoComposeEffectiveConfig.Apply(
                committed!,
                LoadManifest("rig-manifest-unseeded.json"));

            Assert.IsTrue(NeoComposeEffectiveConfig.IsRigOverlay(overlay));
            Assert.IsFalse(NeoComposeEffectiveConfig.IsRigOverlay(committed!));
        }

        [Test]
        public void Save_RefusesToSerializeARigOverlay()
        {
            EnsureTempRoot();
            try
            {
                var assetPath = $"{TempRoot}/NeoComposeConfig.asset";
                var committedAsset = NeoComposeConfigProvider.LoadOrCreate(assetPath, new[] { TempRoot });
                committedAsset.projectName = "Committed Project";
                committedAsset.apiBaseUrl = "https://neocompose.app";
                NeoComposeConfigProvider.Save(committedAsset);
                var serializedBefore = File.ReadAllText(assetPath);

                overlay = NeoComposeEffectiveConfig.Apply(
                    committedAsset,
                    LoadManifest("rig-manifest-seeded.json"));
                overlay.projectName = "Edited in rig mode";
                NeoComposeConfigProvider.Save(overlay);
                AssetDatabase.SaveAssets();

                // The overlay's ephemeral values never reach the committed asset —
                // on disk or in memory.
                Assert.AreEqual(serializedBefore, File.ReadAllText(assetPath));
                Assert.AreEqual("Committed Project", committedAsset.projectName);
                Assert.AreEqual("https://neocompose.app", committedAsset.apiBaseUrl);
            }
            finally
            {
                CleanupTempRoot();
            }
        }

        [Test]
        public void LoadDefault_ReturnsTheEditorEffectiveConfig()
        {
            var installed = NeoComposeConfig.EditorEffectiveConfigResolver;
            var sentinel = ScriptableObject.CreateInstance<NeoComposeConfig>();
            sentinel.name = "SentinelOverlay";
            try
            {
                NeoComposeConfig.EditorEffectiveConfigResolver = _ => sentinel;

                var resolved = NeoComposeConfig.LoadDefault();
                if (resolved == null)
                {
                    Assert.Ignore(
                        "This Unity project has no committed NeoComposeConfig in Resources, " +
                        "so LoadDefault cannot be exercised here.");
                }

                Assert.AreSame(sentinel, resolved);
            }
            finally
            {
                NeoComposeConfig.EditorEffectiveConfigResolver = installed;
                UnityEngine.Object.DestroyImmediate(sentinel);
            }
        }

        [Test]
        public void LoadDefault_ReturnsTheCommittedAssetWithoutAResolver()
        {
            var installed = NeoComposeConfig.EditorEffectiveConfigResolver;
            try
            {
                NeoComposeConfig.EditorEffectiveConfigResolver = null;

                var resolved = NeoComposeConfig.LoadDefault();
                var committedAsset = Resources.Load<NeoComposeConfig>(
                    NeoComposeDefaults.ConfigResourcePath);

                Assert.AreSame(committedAsset, resolved);
            }
            finally
            {
                NeoComposeConfig.EditorEffectiveConfigResolver = installed;
            }
        }

        [Test]
        public void DescribeActiveRig_IsNullWhenNoRigIsBound()
        {
            // The package's own checkout carries no .neo-rig pointer and the test
            // runner sets no override, so the editor must stay in committed mode.
            if (!string.IsNullOrEmpty(
                    Environment.GetEnvironmentVariable(
                        NeoComposeRigManifestResolver.ManifestEnvironmentVariable)) ||
                NeoComposeRigManifestResolver.FindPointerFile(
                    NeoComposeRigManifestResolver.UnityProjectRoot()) != null)
            {
                Assert.Ignore("A rig is bound to this checkout; committed-mode assertion does not apply.");
            }

            Assert.IsNull(NeoComposeEffectiveConfig.ResolveActiveRig());
            Assert.IsNull(NeoComposeEffectiveConfig.DescribeActiveRig());
            Assert.AreSame(committed, NeoComposeEffectiveConfig.Resolve(committed!));
        }
    }
}
