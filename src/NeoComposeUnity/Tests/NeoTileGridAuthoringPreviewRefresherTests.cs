// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using NeoCompose.Unity.Editor;
using NUnit.Framework;
using UnityEngine;

namespace NeoCompose.Tests
{
    public sealed class NeoTileGridAuthoringPreviewRefresherTests
    {
        private readonly List<GameObject> createdObjects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var createdObject in createdObjects)
            {
                if (createdObject != null) UnityEngine.Object.DestroyImmediate(createdObject);
            }
            createdObjects.Clear();
        }

        [Test]
        public void FindMatchingBindings_UsesExactProjectAndVersion()
        {
            string suffix = Guid.NewGuid().ToString("N");
            string projectId = "project-" + suffix;
            string versionId = "version-" + suffix;
            var expected = CreateBinding(projectId, versionId, "grid-expected");
            CreateBinding(projectId, "other-" + suffix, "grid-other-version");
            CreateBinding("other-" + suffix, versionId, "grid-other-project");

            var matches = NeoTileGridAuthoringPreviewRefresher.FindMatchingBindings(
                projectId,
                versionId);

            Assert.That(matches, Is.EqualTo(new[] { expected }));
        }

        [Test]
        public async Task RefreshBindingsAsync_NoMatchingBinding_IsSuccessfulNoOp()
        {
            string suffix = Guid.NewGuid().ToString("N");
            CreateBinding("other-project-" + suffix, "other-version-" + suffix, "grid");

            await NeoTileGridAuthoringPreviewRefresher.RefreshBindingsAsync(
                "project-" + suffix,
                "version-" + suffix,
                CancellationToken.None);
        }

        [Test]
        public void FindMatchingBindings_ValidatesProjectAndVersionIndependently()
        {
            var projectError = Assert.Throws<ArgumentException>(() =>
                NeoTileGridAuthoringPreviewRefresher.FindMatchingBindings(
                    "",
                    "version"));
            Assert.That(projectError!.ParamName, Is.EqualTo("projectId"));

            var versionError = Assert.Throws<ArgumentException>(() =>
                NeoTileGridAuthoringPreviewRefresher.FindMatchingBindings(
                    "project",
                    ""));
            Assert.That(versionError!.ParamName, Is.EqualTo("versionId"));
        }

        [Test]
        public async Task RefreshBindingsAsync_IdentifiesTheFailingBinding()
        {
            string suffix = Guid.NewGuid().ToString("N");
            string projectId = "project-" + suffix;
            string versionId = "version-" + suffix;
            CreateBinding(projectId, versionId, "");

            try
            {
                await NeoTileGridAuthoringPreviewRefresher.RefreshBindingsAsync(
                    projectId,
                    versionId,
                    CancellationToken.None);
                Assert.Fail("Expected the invalid authoring binding to fail.");
            }
            catch (InvalidOperationException exception)
            {
                StringAssert.Contains("<missing>", exception.Message);
                StringAssert.Contains(projectId, exception.Message);
                StringAssert.Contains(versionId, exception.Message);
            }
        }

        private NeoTileGridAuthoringBinding CreateBinding(
            string projectId,
            string versionId,
            string valueId)
        {
            var gameObject = new GameObject("Neo preview refresher test");
            gameObject.SetActive(false);
            createdObjects.Add(gameObject);
            var binding = gameObject.AddComponent<NeoTileGridAuthoringBinding>();
            binding.refreshOnEnable = false;
            binding.projectId = projectId;
            binding.versionId = versionId;
            binding.valueId = valueId;
            return binding;
        }
    }
}
