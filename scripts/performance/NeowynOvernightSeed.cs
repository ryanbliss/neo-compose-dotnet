// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#if NEO_COMPOSE_NEOWYN_TESTS
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using Assets.Scripts.Neo;
using NeoCompose.Runtime;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public class NeowynOvernightSeed
{
    [Test, Timeout(1800000)]
    public async Task GrowTwoSeasons()
    {
        string dir = Environment.GetEnvironmentVariable("NEO_OVERNIGHT_SEED");
        Assert.That(string.IsNullOrWhiteSpace(dir), Is.False, "Set NEO_OVERNIGHT_SEED to a new directory.");
        Assert.That(Directory.Exists(dir), Is.False, "Preserve the benchmark seed.");
        UnityEngine.Random.InitState(271828);
        var config = Object.Instantiate(NeoComposeConfig.LoadDefault());
        config.enableOAuthCloudSync = false;
        try
        {
            using var store = new NeoProjectStore(dataSource: TestProjectSource.instance.Source, config: config, localStore: new NeoFileLocalSaveStore(dir));
            await store.LoadAsync();
            var clock = Stopwatch.StartNew();
            using var neo = await NeowynNeo.Load(store.CreateNew(customId: "crowded"));
            neo.Client.Random.Seed = 271828;
            Report("load", 0, neo, clock.Elapsed.TotalMilliseconds);
            for (int day = 1; day <= 56; day++)
            {
                clock.Restart();
                neo.Save.World.ProcessSleep();
                neo.Save.World.Time.SetToNextMorning();
                Report("sleep", day, neo, clock.Elapsed.TotalMilliseconds);
                if (day % 7 == 0) await neo.CommitAsync();
                await Task.Yield();
            }
            await neo.CommitAsync();
        }
        finally { Object.DestroyImmediate(config); }
    }
    private static void Report(string phase, int day, NeowynNeo neo, double ms)
    {
        int count = neo.Save.World.CurrentArea.Children.OfType<ForegroundPrimaryObjectsLayerLink>().Single().Count();
        string line = $"{phase},day={day},objects={count},ms={ms:F3}";
        TestContext.WriteLine(line);
    }
}

#endif
