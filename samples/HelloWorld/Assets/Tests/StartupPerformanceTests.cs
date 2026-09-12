// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
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
        public void LargeProjectJson_Profile()
        {
            var export = JObject.Parse(File.ReadAllText("Assets/Resources/Neo/project.json"));
            var values = (JObject)export["values"];
            int originalCount = values.Count;
            int added = 0;
            foreach (int rowCount in new[] { 10_000, 100_000 })
            {
                for (; added < rowCount; added++)
                {
                    string id = "startup-profile-" + added;
                    values.Add(id, new JObject
                    {
                        ["id"] = id,
                        ["projectId"] = (string)export["project"]["id"],
                        ["value"] = added,
                    });
                }
                string json = export.ToString(Formatting.None);
                for (int sample = 0; sample < 4; sample++)
                {
                    var timer = Stopwatch.StartNew();
                    var schema = JsonConvert.DeserializeObject<ProjectData>(json);
                    double parseMs = timer.Elapsed.TotalMilliseconds;
                    Assert.That(schema.values.Count, Is.EqualTo(originalCount + rowCount));
                    TestContext.WriteLine($"LARGE_JSON_PROFILE sample={sample} addedRows={rowCount} jsonChars={json.Length} parseMs={parseMs:F3}");
                }
            }
        }

        [Test]
        public async Task HelloWorldStartup_Profile()
        {
            string json = File.ReadAllText("Assets/Resources/Neo/project.json");
            for (int sample = 0; sample < 6; sample++)
            {
                var timer = Stopwatch.StartNew();
                var schema = JsonConvert.DeserializeObject<ProjectData>(json);
                double parseMs = timer.Elapsed.TotalMilliseconds;
                using var store = new NeoProjectStore(
                    dataSource: new NeoJsonProjectDataSource(json),
                    localStore: new NeoInMemoryLocalSaveStore());
                GC.KeepAlive(schema);
                timer.Restart();
                await store.LoadAsync();
                double storeMs = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                using var client = await HelloWorldNeo.Load(store.CreateNew());
                double clientMs = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                var content = client.Assets.Worlds.OldConsoleLanding.Content;
                int tiles = 0;
                foreach (var tile in content.Background.GetTiles()) tiles++;
                foreach (var tile in content.Collisions.GetTiles()) tiles++;
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
                    TestContext.WriteLine($"STARTUP_PROFILE sample={sample} jsonChars={json.Length} parseMs={parseMs:F3} storeMs={storeMs:F3} clientMs={clientMs:F3} gridMs={gridMs:F3} renderMs={renderMs:F3} tiles={tiles}");
                }
                finally { Object.DestroyImmediate(root); }
            }
        }
    }
}
