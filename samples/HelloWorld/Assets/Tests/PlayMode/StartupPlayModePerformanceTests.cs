// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.Collections;
using System.Diagnostics;
using System.Threading.Tasks;
using HelloWorld.Assets.Scripts.Neo;
using NeoCompose.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace HelloWorld.Assets.Tests.PlayMode
{
    public class StartupPlayModePerformanceTests
    {
        [UnityTest]
        public IEnumerator HelloWorldStartup_ProfilePlayerFrames()
        {
            Assert.That(Application.isPlaying, Is.True);
            Task profile = Measure();
            while (!profile.IsCompleted) yield return null;
            profile.GetAwaiter().GetResult();
        }

        [UnityTest]
        public IEnumerator RenderingHonorsExplicitBudgetsAcrossLayersWithoutTrailingFrames()
        {
            Task check = CheckRenderBudgets();
            while (!check.IsCompleted) yield return null;
            check.GetAwaiter().GetResult();
        }

        private static async Task CheckRenderBudgets()
        {
            using var store = new NeoProjectStore(
                dataSource: new NeoResourcesProjectDataSource("Neo/project"),
                localStore: new NeoInMemoryLocalSaveStore());
            await store.LoadAsync();
            using var client = await HelloWorldNeo.Load(store.CreateNew());
            var content = client.Assets.Worlds.OldConsoleLanding.Content;
            int tiles = 0;
            foreach (var tile in content.Background.GetTiles()) tiles++;
            foreach (var tile in content.Collisions.GetTiles()) tiles++;
            foreach (int limit in new[] { int.MaxValue, 100 })
            {
                var root = new GameObject("Render budget test");
                try
                {
                    var renderer = root.AddComponent<NeoTileGridRenderer>();
                    int frame = Time.frameCount;
                    await renderer.RenderAsync(content, new NeoTileGridRenderOptions
                    {
                        MaxTilesPerFrame = limit,
                        MaxMillisecondsPerFrame = double.PositiveInfinity,
                        YieldBeforeRender = false,
                        LiveSync = false,
                    });
                    Assert.That(Time.frameCount - frame, Is.EqualTo((tiles - 1) / limit));
                    Assert.That(root.GetComponentsInChildren<Tilemap>().Length, Is.EqualTo(2));
                }
                finally { Object.DestroyImmediate(root); }
            }
        }

        [UnityTest]
        public IEnumerator ResourceParsingResumesOnMainThreadAndSharesItsSchema()
        {
            Task check = CheckResourceSource();
            while (!check.IsCompleted) yield return null;
            check.GetAwaiter().GetResult();
        }

        private static async Task CheckResourceSource()
        {
            int mainThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            var source = new NeoResourcesProjectDataSource("Neo/project");
            using var first = new NeoProjectStore(dataSource: source,
                localStore: new NeoInMemoryLocalSaveStore());
            using var second = new NeoProjectStore(dataSource: source,
                localStore: new NeoInMemoryLocalSaveStore());
            await first.LoadAsync();
            Assert.That(System.Threading.Thread.CurrentThread.ManagedThreadId, Is.EqualTo(mainThread));
            await second.LoadAsync();
            Assert.That(System.Threading.Thread.CurrentThread.ManagedThreadId, Is.EqualTo(mainThread));
            Assert.That(second.Schema, Is.SameAs(first.Schema));
        }

        private static async Task Measure()
        {
            for (int sample = 0; sample < 6; sample++)
            {
                var timer = Stopwatch.StartNew();
                using var store = new NeoProjectStore(
                    dataSource: new NeoResourcesProjectDataSource("Neo/project"),
                    localStore: new NeoInMemoryLocalSaveStore());
                int storeFrame = Time.frameCount;
                await store.LoadAsync();
                int storeFrames = Time.frameCount - storeFrame;
                double storeMs = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                int frame = Time.frameCount;
                using var client = await HelloWorldNeo.Load(store.CreateNew());
                double clientMs = timer.Elapsed.TotalMilliseconds;
                int clientFrames = Time.frameCount - frame;
                timer.Restart();
                var content = client.Assets.Worlds.OldConsoleLanding.Content;
                double gridMs = timer.Elapsed.TotalMilliseconds;
                var root = new GameObject("Startup player profile");
                try
                {
                    var renderer = root.AddComponent<NeoTileGridRenderer>();
                    timer.Restart();
                    frame = Time.frameCount;
                    await renderer.RenderAsync(content);
                    double renderMs = timer.Elapsed.TotalMilliseconds;
                    int renderFrames = Time.frameCount - frame;
                    Assert.That(root.GetComponentsInChildren<Tilemap>().Length, Is.EqualTo(2));
                    TestContext.WriteLine($"STARTUP_PLAYER_PROFILE sample={sample} storeMs={storeMs:F3} storeFrames={storeFrames} clientMs={clientMs:F3} clientFrames={clientFrames} gridMs={gridMs:F3} renderMs={renderMs:F3} renderFrames={renderFrames}");
                }
                finally { Object.DestroyImmediate(root); }
            }
        }
    }
}
