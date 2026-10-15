// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public partial class P75VirtualInstanceValueTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void CandidateReplayReadsOmittedChildrenOfAnUnchangedDependency(bool reload)
        {
            ProjectData data = BuildNestedProjectData();
            var save = (ObjectMemberValue)data.values["value-save"];
            foreach (string key in new[] { "InputA", "InputB" })
            {
                data.classes["save-root-class"].schema[key] = key;
                data.members[key] = new IntMember { id = key, name = key, kind = MemberKind.Int, Requirement = NeoMemberRequirementKind.Required };
                data.values[key] = new NumberMemberValue { id = key, value = 5 };
                save.value![key] = key;
            }
            ((IntMember)data.members["deep-count"]).defaultValue = new NumberMemberValueBase
            {
                init = RetainedInitializer(PointerKeyOf(PointerKeyOf(RootPointer(), "Save"), "InputA")),
            };
            data.classes["reader-class"] = SchemaClass("reader-class", "Reader", NeoMemberStorage.Save);
            data.classes["reader-class"].schema["Observed"] = "reader-observed";
            data.classes["reader-class"].schema["Trigger"] = "reader-trigger";
            data.classes["save-root-class"].schema["Reader"] = "reader-member";
            data.members["reader-member"] = ClassPlacement("reader-member", "Reader", "reader-class", NeoMemberStorage.Save);
            var reader = ObjectValue("zzz-reader", "reader-class");
            reader.constructorArgs = new();
            reader.instanceConstructorId = null;
            data.values[reader.id] = reader;
            save.value!["Reader"] = reader.id;
            data.members["reader-observed"] = new IntMember
            {
                id = "reader-observed",
                name = "Observed",
                kind = MemberKind.Int,
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new NumberMemberValueBase
                {
                    init = RetainedInitializer(PointerKeyOf(PointerKeyOf(PointerKeyOf(PointerKeyOf(PointerKeyOf(RootPointer(), "Save"), "Thing"), "Nested"), "Deep"), "Count")),
                },
            };
            data.members["reader-trigger"] = new IntMember
            {
                id = "reader-trigger",
                name = "Trigger",
                kind = MemberKind.Int,
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new NumberMemberValueBase
                {
                    init = RetainedInitializer(PointerKeyOf(PointerKeyOf(RootPointer(), "Save"), "InputB")),
                },
            };
            using var first = NeoTestSaveStack.ClientFromSchema(data);
            using var reopened = reload ? NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: first.SerializeSaveData()) : null;
            NeoClient client = reopened ?? first;
            var observed = client.save.Get<NeoMemberClassWritable>("Reader").Get<NeoMemberInt>("Observed");
            Assert.That(observed.value!.value, Is.EqualTo(5));
            var source = client.save.Get<NeoMemberClassWritable>("Thing").Get<NeoMemberClass>("Nested").Get<NeoMemberClass>("Deep").Get<NeoMemberInt>("Count");
            var retained = source.value;
            // One batch invalidates both constructions, but only InputB changes.
            // Reader must see Thing's retained children during replay.
            client.SetWritableValues(NeoValueOwnership.Save, new MemberValue[]
            {
                new NumberMemberValue { id = "InputA", value = 5 },
                new NumberMemberValue { id = "InputB", value = 7 },
            });
            Assert.That(observed.value!.value, Is.EqualTo(5));
            Assert.That(client.save.Get<NeoMemberClassWritable>("Reader").Get<NeoMemberInt>("Trigger").value!.value, Is.EqualTo(7));
            Assert.That(source.value, Is.SameAs(retained));
            // A changed dependency must still rebuild its defaults.
            client.SetWritableValues(NeoValueOwnership.Save, new MemberValue[]
            {
                new NumberMemberValue { id = "InputA", value = 9 },
                new NumberMemberValue { id = "InputB", value = 11 },
            });
            Assert.That(observed.value!.value, Is.EqualTo(9));
            Assert.That(client.save.Get<NeoMemberClassWritable>("Reader").Get<NeoMemberInt>("Trigger").value!.value, Is.EqualTo(11));
            Assert.That(source.value!.value, Is.EqualTo(9));
        }

        private static InitializerBody RetainedInitializer(Pointer pointer) => new()
        {
            code = "test initializer",
            compiled = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = new[] { ConstructorVariable("__root__", ClassType("__root__")) },
                typeInfo = IntTypeInfo(),
                instructions = new Instruction[] { new ReturnInstruction { type = InstructionKind.Return, pointer = pointer } },
            },
        };
    }
}
