// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public partial class P75VirtualInstanceValueTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void MaterializedDefaultsRetainValuesThroughStoredAncestorEdges(bool storedSpine)
        {
            static InitializerBody Init(TypeInfo type, Pointer pointer) => new()
            {
                code = "root.Save.Thing.Nested.Deep.Count",
                compiled = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = new[] { ConstructorVariable("__root__", ClassType("__root__")) },
                    typeInfo = type,
                    instructions = new Instruction[] { new ReturnInstruction { type = InstructionKind.Return, pointer = pointer } },
                },
            };
            ProjectData Build()
            {
                var data = BuildNestedProjectData();
                data.classes["thing-class"].schema["ReadCount"] = "read-count";
                data.members["read-count"] = new IntMember
                {
                    id = "read-count",
                    name = "ReadCount",
                    kind = MemberKind.Int,
                    defaultValue = new NumberMemberValueBase
                    {
                        init = Init(IntTypeInfo(), PointerKeyOf(PointerKeyOf(PointerKeyOf(PointerKeyOf(PointerKeyOf(RootPointer(), "Save"), "Thing"), "Nested"), "Deep"), "Count")),
                    },
                };
                return data;
            }
            using var client = NeoTestSaveStack.ClientFromSchema(Build());
            if (storedSpine)
            {
                var owner = client.save.Get<NeoMemberClassWritable>("Thing");
                var nested = owner.Get<NeoMemberClassWritable>("Nested");
                var deep = nested.Get<NeoMemberClassWritable>("Deep");
                var ownerSpine = (ObjectMemberValue)client.CloneRowForWrite(owner.value!);
                ownerSpine.value = new Dictionary<string, string> { ["Nested"] = nested.value!.id };
                var nestedSpine = (ObjectMemberValue)client.CloneRowForWrite(nested.value!);
                nestedSpine.value = new Dictionary<string, string> { ["Deep"] = deep.value!.id };
                var plan = new NeoWritePlan(client);
                plan.Set(NeoValueOwnership.Save, ownerSpine);
                plan.Set(NeoValueOwnership.Save, nestedSpine);
                plan.Commit();
            }
            var thing = client.save.Get<NeoMemberClassWritable>("Thing");
            var count = thing.Get<NeoMemberClassWritable>("Nested").Get<NeoMemberClassWritable>("Deep").Get<NeoMemberIntWritable>("Count");
            Assert.AreEqual(5, thing.Get<NeoMemberInt>("ReadCount").value!.value);
            int constructions = client.VirtualConstructionCount;
            count.Set(17);
            Assert.AreEqual(constructions, client.VirtualConstructionCount);
            Assert.AreEqual(5, thing.Get<NeoMemberInt>("ReadCount").value!.value);
            Assert.AreEqual(17, count.value!.value);
            string thingId = thing.value!.id;
            string nestedId = thing.Get<NeoMemberClass>("Nested").value!.id;
            string deepId = thing.Get<NeoMemberClass>("Nested").Get<NeoMemberClass>("Deep").value!.id;
            string countId = count.value!.id;
            string serialized = client.SerializeSaveData();
            using var reopened = NeoTestSaveStack.ClientFromSchema(Build(), loadedSaveContent: serialized);
            var loadedThing = reopened.save.Get<NeoMemberClassWritable>("Thing");
            var loadedNested = loadedThing.Get<NeoMemberClassWritable>("Nested");
            var loadedDeep = loadedNested.Get<NeoMemberClassWritable>("Deep");
            var loadedCount = loadedDeep.Get<NeoMemberIntWritable>("Count");
            Assert.AreEqual(thingId, loadedThing.value!.id);
            Assert.AreEqual(nestedId, loadedNested.value!.id);
            Assert.AreEqual(deepId, loadedDeep.value!.id);
            Assert.AreEqual(countId, loadedCount.value!.id);
            Assert.AreEqual(17, loadedCount.value!.value);
            Assert.AreEqual(5, loadedThing.Get<NeoMemberInt>("ReadCount").value!.value);
            loadedCount.Set(29);
            Assert.AreEqual(29, loadedCount.value!.value);
            Assert.AreEqual(5, loadedThing.Get<NeoMemberInt>("ReadCount").value!.value);
        }
    }
}
