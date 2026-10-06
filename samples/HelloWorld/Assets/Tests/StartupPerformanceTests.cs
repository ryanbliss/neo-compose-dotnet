// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using HelloWorld.Assets.Scripts.Neo;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace HelloWorld.Assets.Tests
{
    public class StartupPerformanceTests
    {
        [Test]
        public async Task LargeProjectJson_Profile()
        {
            Dictionary<string, string> files = SampleProjectFixture.PartitionFiles();
            var main = JObject.Parse(files[NeoProjectExportContract.MainPartitionFile]);
            string projectId = (string)JObject.Parse(SampleProjectFixture.Json)["project"]["id"];
            int originalCount = main.Count;
            int added = 0;
            foreach (int rowCount in new[] { 1_000, 10_000 })
            {
                for (; added < rowCount; added++)
                {
                    string id = "startup-profile-" + added;
                    main.Add(id, new JObject
                    {
                        ["id"] = id,
                        ["projectId"] = projectId,
                        ["value"] = added,
                    });
                }
                files[NeoProjectExportContract.MainPartitionFile] = main.ToString(Formatting.None);
                using var store = new NeoProjectStore(
                    dataSource: new NeoJsonProjectDataSource(SampleProjectFixture.Json, files),
                    localStore: new NeoInMemoryLocalSaveStore());
                var timer = Stopwatch.StartNew();
                await store.LoadAsync();
                double loadMs = timer.Elapsed.TotalMilliseconds;
                Assert.That(store.Schema.values.Count, Is.EqualTo(originalCount + rowCount));
                TestContext.WriteLine($"LARGE_JSON_PROFILE addedRows={rowCount} mainPartitionChars={files[NeoProjectExportContract.MainPartitionFile].Length} loadMs={loadMs:F3}");
            }
        }

        [Test]
        public async Task HelloWorldStartup_Profile()
        {
            // Retain cold and warm initialization, each with its own save/client.
            for (int sample = 0; sample < 2; sample++)
            {
                using var store = new NeoProjectStore(
                    dataSource: NeoJsonProjectDataSource.FromFile(SampleProjectFixture.ProjectJsonPath),
                    localStore: new NeoInMemoryLocalSaveStore());
                var timer = Stopwatch.StartNew();
                await store.LoadAsync();
                double storeMs = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                using var client = await HelloWorldNeo.Load(store.CreateNew());
                double clientMs = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                var content = client.Assets.Worlds.OldConsoleLanding.Content;
                int tiles = 0;
                foreach (var tile in content.Background.GetTiles())
                    tiles++;
                foreach (var tile in content.Collisions.GetTiles())
                    tiles++;
                double gridMs = timer.Elapsed.TotalMilliseconds;
                var root = new GameObject("Startup profile");
                try
                {
                    var renderer = root.AddComponent<NeoTileGridRenderer>();
                    timer.Restart();
                    await renderer.RenderAsync(content);
                    double renderMs = timer.Elapsed.TotalMilliseconds;
                    Assert.That(root.GetComponentsInChildren<Tilemap>().Length, Is.EqualTo(2));
                    Assert.That(tiles, Is.GreaterThan(0));
                    TestContext.WriteLine($"STARTUP_PROFILE sample={sample} storeMs={storeMs:F3} clientMs={clientMs:F3} gridMs={gridMs:F3} renderMs={renderMs:F3} tiles={tiles}");
                }
                finally { Object.DestroyImmediate(root); }
            }
        }
    }
}
