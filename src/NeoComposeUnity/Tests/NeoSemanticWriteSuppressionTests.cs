// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.IO;
using System.Threading.Tasks;
using Assets.Scripts.Neo;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace NeoCompose.Tests
{
    public class NeoSemanticWriteSuppressionTests
    {
        private const string ProjectFixture =
            "Packages/com.ryanbliss.neocompose/Tests/synth-example.json";

        [Test]
        public void LivePatchBatch_IgnoresOnlyTopLevelServerMetadata()
        {
            var baseline = JObject.Parse(@"{
  'row-1': {
    '_id': 'mongo-before',
    'id': 'row-1',
    'projectId': 'project-before',
    'createdAt': 1,
    'updatedAt': 2,
    'value': { 'updatedAt': 10, 'score': 7 }
  }
}");
            var metadataOnly = JObject.Parse(@"{
  'row-1': {
    '_id': 'mongo-after',
    'id': 'row-1',
    'projectId': 'project-after',
    'createdAt': 100,
    'updatedAt': 200,
    'value': { 'score': 7, 'updatedAt': 10 }
  }
}");

            var noOp = NeoSaveSynchronizer.BuildLivePatch(baseline, metadataOnly);

            Assert.That(noOp.IsEmpty, Is.True,
                "server-managed top-level metadata must not create a patch entry");

            ((JObject)metadataOnly["row-1"]!["value"]!)["updatedAt"] = 11;
            var nestedChange = NeoSaveSynchronizer.BuildLivePatch(baseline, metadataOnly);

            Assert.That(nestedChange.entries.Keys, Is.EquivalentTo(new[] { "row-1" }),
                "nested updatedAt is authored domain data and remains semantic");
        }

        [Test]
        public async Task Commit_DoesNotStampOrCallLoaderWithoutASemanticChange()
        {
            var schema = JsonConvert.DeserializeObject<ProjectData>(
                File.ReadAllText(ProjectFixture))!;
            const string loaded = @"{
  'name': 'Loaded',
  'projectId': 'test-project',
  'version': { 'id': 'version-1', 'label': '0.1.0' },
  'createdAt': 100,
  'updatedAt': 123,
  'values': {},
  'staticBindings': {}
}";
            var loader = new CountingSaveLoader(schema, loaded);
            var app = await TestProjectNeo.Load(loader);

            await app.CommitAsync();

            Assert.That(loader.CommitCalls, Is.Zero);
            Assert.That(
                JObject.Parse(app.SerializeSaveData())["updatedAt"]!.Value<double>(),
                Is.EqualTo(123));

            app.Save.Score = 41;
            await app.CommitAsync();
            Assert.That(loader.CommitCalls, Is.EqualTo(1));
            double realWriteTimestamp =
                JObject.Parse(app.SerializeSaveData())["updatedAt"]!.Value<double>();
            Assert.That(realWriteTimestamp, Is.GreaterThan(123));

            app.Save.Score = 41;
            await app.CommitAsync();

            Assert.That(loader.CommitCalls, Is.EqualTo(1),
                "a same-value setter must not reach the persistence boundary");
            Assert.That(
                JObject.Parse(app.SerializeSaveData())["updatedAt"]!.Value<double>(),
                Is.EqualTo(realWriteTimestamp),
                "a suppressed commit must not manufacture a fresh updatedAt");
            app.Dispose();
        }

        private sealed class CountingSaveLoader : INeoSaveLoader
        {
            private string content;

            public CountingSaveLoader(ProjectData schema, string content)
            {
                Schema = schema;
                this.content = content;
            }

            public ProjectData Schema { get; }
            public string CustomId => "save-1";
            public int CommitCalls { get; private set; }

            public Awaitable<string?> LoadSaveContentAsync() =>
                NeoAwaitable.FromResult<string?>(content);

            public Awaitable CommitSaveContentAsync(
                string nextContent,
                bool replaceSnapshot)
            {
                CommitCalls++;
                content = nextContent;
                return NeoAwaitable.Completed();
            }
        }
    }
}
