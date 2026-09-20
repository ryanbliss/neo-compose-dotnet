// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.IO;
using System;
using System.Collections.Generic;
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
        public void TypedReplayComparisonPreservesRowSemantics()
        {
            MemberValue[] rows =
            {
                new ObjectMemberValue { value = new Dictionary<string, string> { ["a"] = "one", ["b"] = "two" } },
                new ObjectMemberValue { value = null },
                new ArrayMemberValue { value = new[] { "one", "two" } },
                new ArrayMemberValue { value = null },
                new NumberMemberValue { value = 3 },
                new NumberMemberValue { value = null },
                new BoolMemberValue { value = true },
                new StringMemberValue { value = "text" },
                new Vector2MemberValue { value = new NeoVector2Value { x = 1, y = 2 } },
                new Vector3MemberValue { value = new NeoVector3Value { x = 1, y = 2, z = 3 } },
                new ColorMemberValue { value = new NeoColorValue { r = 1, a = 1 } },
                new FileMemberValue { value = null },
                new FileMemberValue { value = new FileValue { fileId = "file" } },
                new SpriteMemberValue { value = new SpriteValue { fileId = "sprite", sliceIndex = 2 } },
                new DelegateMemberValue { value = new NeoDelegateValue { memberId = "method", valueId = "receiver" } },
                new ActionMemberValue { value = new NeoActionValue() },
                new NullMemberValue(),
            };
            foreach (MemberValue row in rows)
            {
                row.id = "row";
                var copy = (MemberValue)JObject.FromObject(row).ToObject(row.GetType())!;
                Check(row, copy);
                copy.updatedAt = new NeoTimestamp(123);
                copy.createdAt = new NeoTimestamp(456);
                Check(row, copy);
                foreach (Action<MemberValue> change in new Action<MemberValue>[]
                {
                    value => value.containerId = "container",
                    value => value.mapKey = "entry",
                    value => value.sourceValueId = "source",
                    value => value.mark = "removed",
                    value => value.genericBindings = new Dictionary<string, string> { ["T"] = "class" },
                })
                {
                    copy = (MemberValue)JObject.FromObject(row).ToObject(row.GetType())!;
                    change(copy);
                    Check(row, copy);
                }
            }
            var a = new ObjectMemberValue { id = "row", classId = "class", value = new(),
                instanceConstructorId = "ctor", constructorArgs = new() { ["one"] = JObject.Parse("{ 'x': 1, 'y': 2 }") } };
            var b = (ObjectMemberValue)JObject.FromObject(a).ToObject(typeof(ObjectMemberValue))!;
            b.constructorArgs!["one"] = JObject.Parse("{ 'y': 2, 'x': 1 }");
            Check(a, b);
            b.constructorArgs["one"]!["y"] = 3;
            Check(a, b);
            Check(new ArrayMemberValue { value = new[] { "one", "two" } },
                new ArrayMemberValue { value = new[] { "two", "one" } });

            static void Check(MemberValue left, MemberValue right) => Assert.AreEqual(
                NeoSemanticJson.ProjectRecordsEqual(JObject.FromObject(left), JObject.FromObject(right)),
                NeoSemanticJson.MemberRowsEqual(left, right), left.GetType().Name);
        }

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

            var cache = new GameSaveRecordCache();
            var descriptor = new GameSaveRecordDescriptor
            {
                recordKind = NeoGameSaveRecordKinds.Value,
                recordId = "row-1",
                recordStateId = "state-1",
                recordRevisionToken = "token-1",
                contentHash = "hash-1",
            };
            cache.descriptors[descriptor.LogicalKey] = descriptor;
            var noOp = NeoSaveSynchronizer.BuildLivePatch(
                baseline, metadataOnly, cache);

            Assert.That(noOp.IsEmpty, Is.True,
                "server-managed top-level metadata must not create a patch entry");

            ((JObject)metadataOnly["row-1"]!["value"]!)["updatedAt"] = 11;
            var nestedChange = NeoSaveSynchronizer.BuildLivePatch(
                baseline, metadataOnly, cache);

            Assert.That(nestedChange.changes, Has.Count.EqualTo(1));
            var change = nestedChange.changes[0] as GameSaveValuePatchChange;
            Assert.That(change, Is.Not.Null);
            Assert.That(change!.valueId, Is.EqualTo("row-1"));
            Assert.That(change.set.Keys, Is.EqualTo(new[] { "value" }),
                "nested updatedAt is authored domain data and remains semantic");
            Assert.That(change.baseRecordStateId, Is.EqualTo("state-1"));
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
