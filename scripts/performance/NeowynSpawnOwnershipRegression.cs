// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#if NEO_COMPOSE_NEOWYN_TESTS
using System;
using System.Threading.Tasks;
using System.Linq;
using Assets.Scripts.Neo;
using NeoCompose.Runtime;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
public class NeowynSpawnOwnershipRegression
{
    [Test]
    public async Task FreshSaveSleeps()
    {
        var config = Object.Instantiate(NeoComposeConfig.LoadDefault());
        config.enableOAuthCloudSync = false;
        try
        {
            using var store = new NeoProjectStore(dataSource: TestProjectSource.instance.Source, config: config, localStore: new NeoInMemoryLocalSaveStore());
            await store.LoadAsync();
            using var sync = store.CreateNew("spawn-regression");
            using var neo = await NeowynNeo.Load(sync);
            neo.Client.Random.Seed = 314159;
            for (int night = 0; night < 3; night++)
            {
                neo.Save.World.ProcessSleep();
                neo.Save.World.Time.SetToNextMorning();
                TestContext.WriteLine("SLEEP passed=" + night);
            }
            var counters = neo.Assets.Areas.Where(area => area.Spawner != null)
                .SelectMany(area => area.Spawner.Occurrences)
                .ToDictionary(occurrence => occurrence.valueId, occurrence => occurrence.CurrentCount);
            Assert.That(counters.Values.Sum(), Is.GreaterThan(0));
            await neo.Client.CommitAsync();
            neo.Dispose();
            using var reopened = store.Open("spawn-regression");
            using var loaded = await NeowynNeo.Load(reopened);
            var restored = loaded.Assets.Areas.Where(area => area.Spawner != null)
                .SelectMany(area => area.Spawner.Occurrences)
                .ToDictionary(occurrence => occurrence.valueId, occurrence => occurrence.CurrentCount);
            CollectionAssert.AreEquivalent(counters, restored);
            loaded.Save.World.ProcessSleep();
            TestContext.WriteLine("SLEEP counters=" + counters.Values.Sum() + " persisted; reload slept successfully");
        }
        finally
        {
            NeowynNeo.Instance?.Dispose();
            Object.DestroyImmediate(config);
        }
    }
}

#endif
