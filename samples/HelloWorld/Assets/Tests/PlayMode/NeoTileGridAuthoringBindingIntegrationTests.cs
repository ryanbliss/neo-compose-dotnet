// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace HelloWorld.Assets.Tests
{
    public sealed class NeoTileGridAuthoringBindingIntegrationTests
    {
        private const string LandingGridValueId =
            "8f96912d-5bbb-428c-84eb-8932ef588161";

        private GameObject? root;

        [TearDown]
        public void TearDown()
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
        }

        [Test]
        public async Task RefreshPreviewAsync_PaintsAndPublishesTheReturnedResult()
        {
            var binding = CreateBinding();
            NeoTileGridPreviewResult? published = null;
            binding.PreviewRendered += result => published = result;

            var result = await binding.RefreshPreviewAsync();

            Assert.AreEqual(LandingGridValueId, result.ValueId);
            Assert.AreSame(binding.renderer, result.Renderer);
            Assert.AreSame(result, published);
            Assert.AreSame(result.Content, result.Renderer.CurrentContent);
            Assert.IsTrue(result.Renderer.IsLiveSynced);
        }

        [Test]
        public async Task NewRefreshSupersedesThePriorRefreshAndPublishesOnlyNewest()
        {
            var binding = CreateBinding();
            var published = new List<NeoTileGridPreviewResult>();
            binding.PreviewRendered += published.Add;

            var first = binding.RefreshPreviewAsync();
            var second = binding.RefreshPreviewAsync();

            await AssertCanceled(first);
            var newest = await second;
            Assert.That(published, Is.EqualTo(new[] { newest }));
            Assert.AreSame(newest.Content, newest.Renderer.CurrentContent);
        }

        [Test]
        public async Task CallerCancellationPreventsStaleCompletion()
        {
            var binding = CreateBinding();
            var published = new List<NeoTileGridPreviewResult>();
            binding.PreviewRendered += published.Add;
            using var cancellation = new CancellationTokenSource();

            var refresh = binding.RefreshPreviewAsync(cancellation.Token);
            cancellation.Cancel();

            await AssertCanceled(refresh);
            Assert.IsEmpty(published);
            Assert.IsFalse(binding.renderer?.IsLiveSynced ?? false);
            Assert.IsNull(binding.renderer?.CurrentContent);
        }

        [Test]
        public async Task DisableDuringLoadPreventsStaleRender()
        {
            var binding = CreateBinding(active: true);
            var published = new List<NeoTileGridPreviewResult>();
            binding.PreviewRendered += published.Add;

            var refresh = binding.RefreshPreviewAsync();
            root!.SetActive(false);

            await AssertCanceled(refresh);
            Assert.IsEmpty(published);
            Assert.IsFalse(binding.renderer?.IsLiveSynced ?? false);
            Assert.IsNull(binding.renderer?.CurrentContent);
        }

        private NeoTileGridAuthoringBinding CreateBinding(bool active = false)
        {
            root = new GameObject("TileGrid authoring binding integration test");
            root.SetActive(false);
            var binding = root.AddComponent<NeoTileGridAuthoringBinding>();
            binding.refreshOnEnable = false;
            binding.valueId = LandingGridValueId;
            if (active) root.SetActive(true);
            return binding;
        }

        private static async Task AssertCanceled(
            Awaitable<NeoTileGridPreviewResult> refresh)
        {
            try
            {
                await refresh;
                Assert.Fail("Expected the authoring preview refresh to be canceled.");
            }
            catch (OperationCanceledException)
            {
                // Expected: a newer refresh, caller cancellation, or disable won.
            }
        }
    }
}
