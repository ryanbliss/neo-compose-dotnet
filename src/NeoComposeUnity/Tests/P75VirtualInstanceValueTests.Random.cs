// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public partial class P75VirtualInstanceValueTests
    {
        private static ProjectData RandomDefaultProject(bool constant = false)
        {
            ProjectData data = BuildProjectData();
            var member = (IntMember)data.members["thing-count"];
            if (constant)
                member.Storage = NeoMemberStorage.Immutable;
            member.defaultValue = new NumberMemberValueBase
            {
                init = new InitializerBody
                {
                    code = "Random.Range(-2147483648, 2147483647)",
                    compiled = JObject.Parse(NeoScriptRandomIrFixture.Json)["rangeInt"]!.ToObject<FunctionWithReturnType>(),
                },
            };
            return data;
        }

        private static double IdentityRange(string identity)
        {
            var random = new NeoRandom(0);
            using var frame = random.EnterIdentity(identity);
            return random.RangeInt(int.MinValue, int.MaxValue);
        }

        [Test]
        public void RandomStoredDefaultsSurvivePartitionReloadAndFullRebuildWithoutGameplayDraws()
        {
            ProjectData data = RandomDefaultProject();
            var thing = (ObjectMemberValue)data.values["thing-instance"];
            data.values.Remove(thing.id);
            const string partition = "world:thing-class";
            data.valuePartitions = NeoTestExport.Partitions(new Dictionary<string, JToken>
            {
                [partition] = new JObject { [thing.id] = JObject.FromObject(thing) },
            });
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            client.Random.InitState(17);
            var baseline = new NeoRandom(17);
            double expected = IdentityRange(thing.id);
            for (int round = 0; round < 2; round++)
            {
                client.LoadValuePartition(partition);
                Assert.IsTrue(client.TryGetVirtualClassChildValueId(thing.id, "Count", out string? childId));
                Assert.IsTrue(client.TryGetValue(childId!, out NumberMemberValue? value));
                Assert.AreEqual(expected, value!.value);
                client.ApplyExternalSaveContent(client.SerializeSaveData());
                Assert.IsTrue(client.TryGetVirtualClassChildValueId(thing.id, "Count", out string? rebuiltId));
                Assert.AreEqual(childId, rebuiltId);
                Assert.IsTrue(client.TryGetValue(rebuiltId!, out NumberMemberValue? rebuilt));
                Assert.AreEqual(expected, rebuilt!.value);
                Assert.AreEqual(baseline.value, client.Random.value);
                client.UnloadValuePartition(partition);
            }
        }

        [Test]
        public void NestedRandomDefaultsFollowAuthoredSchemaOrderAfterJsonKeySorting()
        {
            ProjectData data = RandomDefaultProject();
            data.classes["child-class"] = SchemaClass("child-class", "Child", NeoMemberStorage.Save);
            data.classes["child-class"].schema["Count"] = "thing-count";
            data.members["thing-child"] = new ClassMember
            {
                id = "thing-child",
                name = "Child",
                kind = MemberKind.Class,
                DeclaredClassId = "child-class",
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new ObjectMemberValueBase { value = new Dictionary<string, string>() },
            };
            data.classes["thing-class"].schema = new Dictionary<string, string>
            {
                ["Child"] = "thing-child",
                ["Count"] = "thing-count",
            };
            data.classes["thing-class"].schemaKeyOrder = new[] { "Count", "Child" };
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var expected = new NeoRandom(0);
            using var identity = expected.EnterIdentity("thing-instance");
            var owner = client.save.Get<NeoMemberClassWritable>("Thing");
            Assert.AreEqual(expected.RangeInt(int.MinValue, int.MaxValue), owner.Get<NeoMemberInt>("Count").value!.value);
            Assert.AreEqual(expected.RangeInt(int.MinValue, int.MaxValue), owner.Get<NeoMemberClass>("Child").Get<NeoMemberInt>("Count").value!.value);
        }

        [Test]
        public void RandomConstantEvictionUsesDeclarationIdentityWithoutGameplayDraws()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(RandomDefaultProject(constant: true));
            client.Random.InitState(17);
            var baseline = new NeoRandom(17);
            double expected = IdentityRange("thing-count");
            for (int round = 0; round < 2; round++)
            {
                var count = client.save.Get<NeoMemberClassWritable>("Thing").Get<NeoMemberInt>("Count");
                Assert.AreEqual(expected, count.value!.value);
                Assert.AreEqual(baseline.value, client.Random.value);
                client.InvalidateSchemaResolutionCaches();
            }
        }
    }
}
