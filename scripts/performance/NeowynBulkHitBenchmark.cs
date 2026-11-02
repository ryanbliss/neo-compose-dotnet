// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#if NEO_COMPOSE_NEOWYN_TESTS
using System;
using System.Linq;
using System.Threading.Tasks;
using Assets.Scripts.Neo;
using NeoCompose.Runtime;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using QualityLevel = Assets.Scripts.Neo.QualityLevel;

public class NeowynBulkHitBenchmark
{
    private sealed class Effects : IPlacedItemFunctionHandler
    {
        public int Drops;
        public void Drop(IReadOnlyInventoryItemStack stack)
        {
            _ = stack.Clone();
            Drops++;
        }
        public void PlaySound(IReadOnlyAudioScenario sound, bool destructive)
        {
        }
    }
    [Test]
    public async Task BulkHits()
    {
        var config = Object.Instantiate(NeoComposeConfig.LoadDefault());
        config.enableOAuthCloudSync = false;
        try
        {
            using var store = new NeoProjectStore(dataSource: TestProjectSource.instance.Source, config: config, localStore: new NeoInMemoryLocalSaveStore());
            await store.LoadAsync();
            using var neo = await NeowynNeo.Load(store.CreateNew());
            var area = neo.Save.World.CurrentArea;
            var content = AreaTileGridContent.ResolveForSave(neo.Client, area.valueId);
            var item = neo.Assets.Items.Index.Slug["boulder.small"];
            var pattern = ToolPatterns.Get(QualityLevel.FiveStars).Items.Last().Horizontal.Cells(new Vector2Int(800, 800)).ToArray();
            TestContext.WriteLine("PICKAXE cells=" + pattern.Length);
            foreach (var cell in pattern)
                Assert.That(content.Background.TrySet<GrassTile>(cell).Ok, Is.True);
            foreach (string phase in new[] { "damage", "variant", "destroy" })
            {
                var rocks = pattern.Select(cell =>
                {
                    var rock = (DestructablePlacedItem)item.Placed.Initialize();
                    Assert.That(content.ForegroundPrimaryObjects.TrySpawn(cell - rock.Anchor, rock).Ok, Is.True);
                    return content.ForegroundPrimaryObjects.GetObject<DestructablePlacedItem>(cell);
                }).ToArray();
                var effects = new Effects();
                foreach (var rock in rocks)
                    rock.FunctionHandler = effects;
                int damage = phase == "destroy" ? 10000 : phase == "variant" ? 1 : 0;
                var timer = System.Diagnostics.Stopwatch.StartNew();
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                neo.RunTransaction(() =>
                {
                    foreach (var rock in rocks)
                        rock.Hit(ToolType.Pickaxe, QualityLevel.FiveStars, damage);
                });
                timer.Stop();
                TestContext.WriteLine($"PICKAXE phase={phase} count={rocks.Length} ms={timer.Elapsed.TotalMilliseconds:F3} bytes={GC.GetAllocatedBytesForCurrentThread()-allocated}");
                foreach (var cell in pattern)
                {
                    var rock = content.ForegroundPrimaryObjects.GetObject<DestructablePlacedItem>(cell);
                    if (phase == "destroy")
                        Assert.That(rock, Is.Null);
                    else
                    {
                        Assert.That(rock.Health, Is.LessThanOrEqualTo(((IReadOnlyDestructableStackData)item.DefaultData).Health));
                        content.ForegroundPrimaryObjects.Remove(rock);
                    }
                }
            }
        }
        finally
        {
            NeowynNeo.Instance?.Dispose();
            Object.DestroyImmediate(config);
        }
    }
}

#endif
