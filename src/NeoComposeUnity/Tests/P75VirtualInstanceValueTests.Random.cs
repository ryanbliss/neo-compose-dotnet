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
            NeoScriptRandomIrFixture.Install(data);
            var member = (IntMember)data.members["thing-count"];
            if (constant)
                member.Storage = NeoMemberStorage.Immutable;
            member.defaultValue = new NumberMemberValueBase
            {
                init = new InitializerBody
                {
                    code = "NeoRandom.Range(-2147483648, 2147483647)",
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
            client.Random.Seed = 17;
            var baseline = new NeoRandom(17);
            double expected = IdentityRange(thing.id);
            for (int round = 0; round < 2; round++)
            {
                client.LoadValuePartition(partition);
                Assert.IsTrue(client.TryGetVirtualClassChildValueId(thing.id, "Count", out string? childId));
                Assert.IsTrue(client.TryGetValue(childId!, out NumberMemberValue? value));
                Assert.AreEqual(expected, value!.value);
                typeof(NeoClient).GetMethod("InitializeVirtualInstanceValues", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(client, new object[] { true });
                Assert.IsTrue(client.TryGetVirtualClassChildValueId(thing.id, "Count", out string? rebuiltId));
                Assert.AreEqual(childId, rebuiltId);
                Assert.IsTrue(client.TryGetValue(rebuiltId!, out NumberMemberValue? rebuilt));
                Assert.AreEqual(expected, rebuilt!.value);
                Assert.AreEqual(baseline.Value(), client.Random.Value());
                client.UnloadValuePartition(partition);
            }
        }

        [Test]
        public void VirtualGeneratorsRestartAfterPartitionReloadAndFullReconstruction()
        {
            ProjectData data = BuildProjectData();
            NeoScriptRandomIrFixture.Install(data);
            data.members["thing-rng"] = new ClassMember
            {
                id = "thing-rng",
                name = "Rng",
                kind = MemberKind.Class,
                DeclaredClassId = NeoRandomContract.Class,
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new ObjectMemberValueBase
                {
                    init = new InitializerBody
                    {
                        code = "new NeoRandom(17)",
                        compiled = JObject.Parse(NeoScriptRandomIrFixture.Json)["freshGenerator"]!.ToObject<FunctionWithReturnType>(),
                    },
                },
            };
            data.classes["thing-class"].schema["Rng"] = "thing-rng";
            var thing = data.values["thing-instance"];
            data.values.Remove(thing.id);
            const string partition = "world:thing-class";
            data.valuePartitions = NeoTestExport.Partitions(new Dictionary<string, JToken>
            {
                [partition] = new JObject { [thing.id] = JObject.FromObject(thing) },
            });
            data.members["p123-draw"] = new NSPropertyMember
            {
                id = "p123-draw",
                name = "Draw",
                kind = MemberKind.NSProperty,
                returnTypeInfo = new PrimitiveTypeInfo { type = MemberKind.Float, required = true },
                getter = JObject.Parse(NeoScriptRandomIrFixture.Json)["instanceDraw"]!.ToObject<FunctionWithReturnType>()!,
            };
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            for (int round = 0; round < 2; round++)
            {
                client.LoadValuePartition(partition);
                Assert.IsTrue(client.TryGetVirtualClassChildValueId(thing.id, "Rng", out string? id));
                Assert.IsTrue(client.TryGetValueOwnership(id!, out var ownership));
                var seed = new NeoMemberNSProperty(client, (NSPropertyMember)data.members[NeoRandomContract.Seed], null, ownership);
                var draw = new NeoMemberNSProperty(client, (NSPropertyMember)data.members["p123-draw"], null, ownership);
                Assert.AreEqual((double)new NeoRandom(17).Value(), draw.Compute(id!).value);
                var read = seed.Compute(id!);
                Assert.IsTrue(read.ok, read.error);
                Assert.AreEqual(17d, read.value);
                Assert.IsTrue(seed.Set(id!, 42d).ok);
                Assert.AreEqual(42d, seed.Compute(id!).value);
                typeof(NeoClient).GetMethod("InitializeVirtualInstanceValues", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(client, new object[] { true });
                Assert.IsTrue(client.TryGetVirtualClassChildValueId(thing.id, "Rng", out string? rebuilt));
                Assert.AreEqual(id, rebuilt);
                Assert.AreEqual(17d, seed.Compute(rebuilt!).value);
                Assert.AreEqual((double)new NeoRandom(17).Value(), draw.Compute(rebuilt!).value);
                Assert.IsTrue(seed.Set(rebuilt!, 99d).ok);
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
            client.Random.Seed = 17;
            var baseline = new NeoRandom(17);
            double expected = IdentityRange("thing-count");
            for (int round = 0; round < 2; round++)
            {
                var count = client.save.Get<NeoMemberClassWritable>("Thing").Get<NeoMemberInt>("Count");
                Assert.AreEqual(expected, count.value!.value);
                Assert.AreEqual(baseline.Value(), client.Random.Value());
                client.InvalidateSchemaResolutionCaches();
            }
        }
    }
}
