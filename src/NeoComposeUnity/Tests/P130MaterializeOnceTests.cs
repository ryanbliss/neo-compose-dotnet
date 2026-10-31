// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public partial class P75VirtualInstanceValueTests
    {
        private static ProjectData P130CaptureProject()
        {
            var data = BuildProjectData();
            data.classes["save-root-class"].schema["Input"] = "replay-input";
            data.members["replay-input"] = new IntMember
            {
                id = "replay-input",
                name = "Input",
                kind = MemberKind.Int,
                defaultValue = new NumberMemberValueBase { value = 3 },
            };
            data.classes["thing-class"].schema["Captured"] = "captured-count";
            data.members["captured-count"] = new IntMember
            {
                id = "captured-count",
                name = "Captured",
                kind = MemberKind.Int,
                Requirement = NeoMemberRequirementKind.Required,
                Storage = NeoMemberStorage.Save,
                defaultValue = new NumberMemberValueBase { init = Init(IntTypeInfo(), SavePointer("Input")) },
            };
            return data;
        }

        [Test]
        public void P130CapturedMutableDefaultIsSavedAndLiteralSiblingIsSparse()
        {
            var data = P130CaptureProject();
            string saved;
            using (var client = NeoTestSaveStack.ClientFromSchema(data))
            {
                var thing = client.save.Get<NeoMemberClassWritable>("Thing");
                var captured = thing.Get<NeoMemberIntWritable>("Captured");
                var literal = thing.Get<NeoMemberIntWritable>("Count");
                Assert.AreEqual(3d, captured.value!.value);
                string capturedId = captured.value.id;
                string literalId = literal.value!.id;
                int count = client.VirtualConstructionCount;
                client.save.Get<NeoMemberIntWritable>("Input").Set(9);
                Assert.AreEqual(count, client.VirtualConstructionCount);
                Assert.AreEqual(3d, captured.value!.value);
                saved = client.SerializeSaveData();
                var values = (JObject)JObject.Parse(saved)["values"]!;
                Assert.IsNotNull(values[capturedId]);
                Assert.IsNull(values[literalId]);
            }
            using var reloaded = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: saved);
            Assert.AreEqual(3d, reloaded.save.Get<NeoMemberClassWritable>("Thing").Get<NeoMemberIntWritable>("Captured").value!.value);
        }

        [Test]
        public void P130CaptureThroughLocalSavesItsLaterRow()
        {
            ProjectData data = P130CaptureProject();
            // Captured has no initializer here: the constructor body sets it
            // from a local, after a literal write commits in between.
            ((IntMember)data.members["captured-count"]).defaultValue = new NumberMemberValueBase { value = 0 };
            data.classes["thing-class"].constructorIds = new[] { "thing-ctor" };
            ((ObjectMemberValue)data.values["thing-instance"]).instanceConstructorId = "thing-ctor";
            var receiver = new VariablePointer { type = PointerKind.Variable, variableId = "__this__" };
            data.constructors["thing-ctor"] = new ConstructorRecord
            {
                id = "thing-ctor",
                classId = "thing-class",
                projectId = "p75-project",
                argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                action = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = new[] { ConstructorVariable("__this__", ClassType("thing-class")), ConstructorVariable("__root__", ClassType("__root__")) },
                    typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                    instructions = new Instruction[]
                    {
                        new VariableInstruction { variable = new Variable { id = "input", typeInfo = IntTypeInfo(), pointer = SavePointer("Input") } },
                        new AssignInstruction
                        {
                            type = InstructionKind.Assign, operatorValue = "=",
                            target = new WriteTarget { pointer = PointerKeyOf(receiver, "Count"), typeInfo = IntTypeInfo(), writability = WritabilityKind.Session },
                            pointer = new ValuePointer { type = PointerKind.Value, value = new Value { typeInfo = IntTypeInfo(), value = 1 } },
                        },
                        new AssignInstruction
                        {
                            type = InstructionKind.Assign, operatorValue = "=",
                            target = new WriteTarget { pointer = PointerKeyOf(receiver, "Captured"), typeInfo = IntTypeInfo(), writability = WritabilityKind.Session },
                            pointer = new VariablePointer { type = PointerKind.Variable, variableId = "input" },
                        },
                    },
                },
            };
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var thing = client.save.Get<NeoMemberClassWritable>("Thing");
            var captured = thing.Get<NeoMemberIntWritable>("Captured");
            Assert.AreEqual(3d, captured.value!.value);
            var values = (JObject)JObject.Parse(client.SerializeSaveData())["values"]!;
            Assert.IsNotNull(values[captured.value.id], "The capture carried through a local saves.");
            Assert.IsNull(values[thing.Get<NeoMemberIntWritable>("Count").value!.id], "A literal written after the read does not.");
        }

        [Test]
        public void P130CollectionCopiedFromMutableStateKeepsItsMembership()
        {
            ProjectData data = BuildProjectData();
            data.members["input-entry"] = new IntMember
            {
                id = "input-entry",
                projectId = "p75-project",
                name = "Entry",
                kind = MemberKind.Int,
                Requirement = NeoMemberRequirementKind.Required,
            };
            data.values["input-a"] = new NumberMemberValue { id = "input-a", value = 1 };
            data.values["input-b"] = new NumberMemberValue { id = "input-b", value = 2 };
            data.members["save-inputs"] = new ListMember
            {
                id = "save-inputs",
                projectId = "p75-project",
                name = "Inputs",
                kind = MemberKind.List,
                entryMemberId = "input-entry",
                ListKind = NeoListKind.Ordered,
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new ArrayMemberValueBase { value = new[] { "input-a", "input-b" } },
            };
            data.classes["save-root-class"].schema["Inputs"] = "save-inputs";
            var listType = new CollectionTypeInfo { type = MemberKind.List, required = true, entryTypeInfo = IntTypeInfo() };
            data.members["copied"] = new ListMember
            {
                id = "copied",
                projectId = "p75-project",
                name = "Copied",
                kind = MemberKind.List,
                entryMemberId = "input-entry",
                ListKind = NeoListKind.Ordered,
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new ArrayMemberValueBase { init = Init(listType, SavePointer("Inputs")) },
            };
            data.classes["thing-class"].schema["Copied"] = "copied";
            string saved;
            using (var client = NeoTestSaveStack.ClientFromSchema(data))
            {
                var copied = client.save.Get<NeoMemberClassWritable>("Thing").Get<NeoMemberList>("Copied");
                Assert.AreEqual(2, copied.Count());
                saved = client.SerializeSaveData();
            }
            // The input grows between sessions; the capture keeps its two entries.
            data.values["input-c"] = new NumberMemberValue { id = "input-c", value = 9 };
            ((ListMember)data.members["save-inputs"]).defaultValue!.value = new[] { "input-a", "input-b", "input-c" };
            using var reloaded = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: saved);
            Assert.AreEqual(3, reloaded.save.Get<NeoMemberList>("Inputs").Count());
            Assert.AreEqual(2, reloaded.save.Get<NeoMemberClassWritable>("Thing").Get<NeoMemberList>("Copied").Count());
        }

        [Test]
        public void P130LiteralDefaultsSaveNothing()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var thing = client.save.Get<NeoMemberClassWritable>("Thing");
            string rootId = thing.value!.id;
            string countId = thing.Get<NeoMemberIntWritable>("Count").value!.id;
            var values = (JObject)JObject.Parse(client.SerializeSaveData())["values"]!;
            Assert.IsNull(values[rootId]);
            Assert.IsNull(values[countId]);
        }

        [Test]
        public void P130RestoreAuthoredRevealsMaterializedCapture()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(P130CaptureProject());
            var thing = client.save.Get<NeoMemberClassWritable>("Thing");
            var captured = thing.Get<NeoMemberIntWritable>("Captured");
            string capturedId = captured.value!.id;
            int count = client.VirtualConstructionCount;
            client.save.Get<NeoMemberIntWritable>("Input").Set(9);
            captured.Set(12);
            var incoming = JObject.Parse(client.SerializeSaveData());
            ((JObject)incoming["values"]!).Remove(capturedId);
            client.ApplyExternalSaveContent(incoming.ToString());
            Assert.IsNull(JObject.Parse(client.SerializeSaveData())["values"]![capturedId]);
            Assert.AreEqual(3d, thing.Get<NeoMemberIntWritable>("Captured").value!.value);
            Assert.AreEqual(count, client.VirtualConstructionCount);
        }

        [Test]
        public void P130ReplacedChildDetachesUntilRestoreRelinksIt()
        {
            ProjectData data = BuildProjectData();
            ((ClassMember)data.members["thing-member"]).Storage = NeoMemberStorage.Inherit;
            data.classes["child-class"] = SchemaClass("child-class", "Child", NeoMemberStorage.Save);
            data.classes["thing-class"].schema["Child"] = "thing-child";
            data.members["thing-child"] = new ClassMember
            {
                id = "thing-child",
                projectId = "p75-project",
                name = "Child",
                kind = MemberKind.Class,
                DeclaredClassId = "child-class",
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new ObjectMemberValueBase { value = new Dictionary<string, string>() },
            };
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var owner = client.save.Get<NeoMemberClassWritable>("Thing");
            string oldId = owner.Get<NeoMemberClassWritable>("Child").value!.id;
            string parentId = owner.value!.id;
            int count = client.VirtualConstructionCount;
            var plan = new NeoWritePlan(client);
            var parent = (ObjectMemberValue)client.CloneRowForWrite(owner.value!);
            parent.value ??= new Dictionary<string, string>();
            parent.value["Child"] = "replacement-child";
            plan.Set(NeoValueOwnership.Save, parent);
            plan.Set(NeoValueOwnership.Save, new ObjectMemberValue
            {
                id = "replacement-child",
                classId = "child-class",
                value = new Dictionary<string, string>(),
            });
            plan.Commit();
            Assert.IsTrue(client.IsDetachedVirtualRow(oldId));
            Assert.AreEqual("replacement-child", owner.Get<NeoMemberClassWritable>("Child").value!.id);
            plan = new NeoWritePlan(client);
            plan.Remove(NeoValueOwnership.Save, parentId);
            plan.Remove(NeoValueOwnership.Save, "replacement-child");
            plan.Commit();
            Assert.IsFalse(client.IsDetachedVirtualRow(oldId));
            Assert.AreEqual(oldId, owner.Get<NeoMemberClassWritable>("Child").value!.id);
            Assert.AreEqual(count, client.VirtualConstructionCount);
        }

        [Test]
        public void P130ExternalArgumentChangeOnlyRematerializesItsRoot()
        {
            var data = BuildLiveConstructorArgumentProjectData();
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var thing = client.save.Get<NeoMemberClassWritable>("Thing");
            var other = client.save.Get<NeoMemberClassWritable>("Other");
            Assert.AreEqual("from constructor argument", PayloadName(thing));
            Assert.AreEqual("from constructor argument", PayloadName(other));
            int count = client.VirtualConstructionCount;
            var incoming = JObject.Parse(client.SerializeSaveData());
            ReplacePayload(incoming, "constructor-payload", "changed");
            client.ApplyExternalSaveContent(incoming.ToString());
            Assert.AreEqual(count, client.VirtualConstructionCount);
            incoming = JObject.Parse(client.SerializeSaveData());
            var retargeted = ObjectValue("thing-instance", "thing-class");
            retargeted.instanceConstructorId = "thing-ctor";
            retargeted.constructorArgs = new Dictionary<string, JToken?> { ["__arg_0__"] = "alternate-payload" };
            retargeted.genericBindings = new Dictionary<string, string>(((ObjectMemberValue)data.values["thing-instance"]).genericBindings!);
            ((JObject)incoming["values"]!)[retargeted.id] = JObject.FromObject(retargeted);
            client.ApplyExternalSaveContent(incoming.ToString());
            Assert.AreEqual(count + 1, client.VirtualConstructionCount);
            Assert.AreEqual("alternate", PayloadName(thing));
            Assert.AreEqual("from constructor argument", PayloadName(other));
        }

        [Test]
        public void P130VariantSwitchRestoresConstructionWithoutReconstructingReceiver()
        {
            var data = BuildConstructedUnorderedChildrenProjectData();
            AddThingVariant(data);
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var thing = client.save.Get<NeoMemberClassWritable>("Thing");
            var receiver = new SparseThingValue(client, thing);
            var baseline = NeoGeneratedTypesSupport.ResolveBaseVariant<SparseThingValue>(client, "thing-class");
            var other = client.GetOrCreateVariant<SparseThingValue>("thing-variant");
            var entry = Entry(thing);
            string label = entry.Get<NeoMemberStringWritable>("Label").value!.value!;
            int count = client.VirtualConstructionCount;
            NeoGeneratedTypesSupport.ApplyVariant(receiver, baseline);
            NeoGeneratedTypesSupport.ApplyVariant(receiver, other);
            NeoGeneratedTypesSupport.ApplyVariant(receiver, baseline);
            Assert.AreEqual(count, client.VirtualConstructionCount);
            Assert.AreEqual(label, Entry(thing).Get<NeoMemberStringWritable>("Label").value!.value);
        }

        [Test]
        public void P130NewInstanceRunsConstructor()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildConstructedUnorderedChildrenProjectData());
            using var instance = NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(client, "thing-class", "thing-ctor",
                Array.Empty<NeoDeclaredConstructorArgument>());
            // The constructor body builds the entry; no materialized graph supplies it.
            Assert.AreEqual("Sprite", Entry(instance).Get<NeoMemberStringWritable>("Name").value!.value);
        }

        [Test]
        public void P130ArgumentFromMutableStateSavesOnlyTheFieldsThatReadIt()
        {
            // Thing.Stats = new Stats(seed: Save.Input). The body writes a
            // literal Label, then Value from the parameter. Only Value read
            // save data, so only Value saves: not Label, not the Stats row.
            ProjectData data = BuildProjectData();
            data.classes["save-root-class"].schema["Input"] = "replay-input";
            data.members["replay-input"] = new IntMember
            {
                id = "replay-input",
                name = "Input",
                kind = MemberKind.Int,
                defaultValue = new NumberMemberValueBase { value = 3 },
            };
            var text = new PrimitiveTypeInfo { type = MemberKind.String, required = true };
            data.classes["stats-class"] = new NeoSchemaClass
            {
                id = "stats-class",
                name = "Stats",
                projectId = "p75-project",
                schema = new Dictionary<string, string> { ["Value"] = "stats-value", ["Label"] = "stats-label" },
                constructorIds = new[] { "stats-ctor" },
            };
            data.members["stats-value"] = new IntMember { id = "stats-value", name = "Value", kind = MemberKind.Int, defaultValue = new NumberMemberValueBase { value = 0 } };
            data.members["stats-label"] = new StringMember { id = "stats-label", name = "Label", kind = MemberKind.String, defaultValue = new StringMemberValueBase { value = "default" } };
            var receiver = new VariablePointer { type = PointerKind.Variable, variableId = "__this__" };
            data.constructors["stats-ctor"] = new ConstructorRecord
            {
                id = "stats-ctor",
                classId = "stats-class",
                projectId = "p75-project",
                argumentTypes = new[] { new FunctionArgumentTypeInfo { name = "seed", type = MemberKind.Int, required = true } },
                action = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = new[] { ConstructorVariable("__this__", ClassType("stats-class")), ConstructorVariable("__root__", ClassType("__root__")), ConstructorVariable("__arg_0__", IntTypeInfo()) },
                    typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                    instructions = new Instruction[]
                    {
                        new AssignInstruction
                        {
                            type = InstructionKind.Assign, operatorValue = "=",
                            target = new WriteTarget { pointer = PointerKeyOf(receiver, "Label"), typeInfo = text, writability = WritabilityKind.Session },
                            pointer = new ValuePointer { type = PointerKind.Value, value = new Value { typeInfo = text, value = "built" } },
                        },
                        new AssignInstruction
                        {
                            type = InstructionKind.Assign, operatorValue = "=",
                            target = new WriteTarget { pointer = PointerKeyOf(receiver, "Value"), typeInfo = IntTypeInfo(), writability = WritabilityKind.Session },
                            pointer = new VariablePointer { type = PointerKind.Variable, variableId = "__arg_0__" },
                        },
                    },
                },
            };
            data.classes["thing-class"].schema["Stats"] = "thing-stats";
            data.members["thing-stats"] = new ClassMember
            {
                id = "thing-stats",
                name = "Stats",
                kind = MemberKind.Class,
                DeclaredClassId = "stats-class",
                Storage = NeoMemberStorage.Save,
                defaultValue = new ObjectMemberValueBase
                {
                    init = Init(ClassType("stats-class"), new FunctionPointer
                    {
                        type = PointerKind.Function,
                        function = new DeclaredConstructorFunction
                        {
                            type = FunctionKind.DeclaredConstructor,
                            info = new DeclaredConstructorInfo
                            {
                                constructorId = "stats-ctor",
                                schemaClassInfo = ClassType("stats-class"),
                                args = new[] { new DeclaredConstructorArgument { name = "seed", valuePointer = SavePointer("Input") } },
                                fields = Array.Empty<FunctionClassConstructorField>(),
                            },
                        },
                    }),
                },
            };
            string saved;
            using (var client = NeoTestSaveStack.ClientFromSchema(data))
            {
                var stats = client.save.Get<NeoMemberClassWritable>("Thing").Get<NeoMemberClassWritable>("Stats");
                Assert.AreEqual(3d, stats.Get<NeoMemberIntWritable>("Value").value!.value);
                Assert.AreEqual("built", stats.Get<NeoMemberStringWritable>("Label").value!.value);
                client.save.Get<NeoMemberIntWritable>("Input").Set(9);
                Assert.AreEqual(3d, stats.Get<NeoMemberIntWritable>("Value").value!.value);
                saved = client.SerializeSaveData();
                var values = (JObject)JObject.Parse(saved)["values"]!;
                Assert.IsNotNull(values[stats.Get<NeoMemberIntWritable>("Value").value!.id], "A field computed from a save-backed argument saves.");
                Assert.IsNull(values[stats.Get<NeoMemberStringWritable>("Label").value!.id], "A literal beside it stays sparse.");
                Assert.IsNull(values[stats.value!.id], "The argument did not choose the object, so its row stays sparse.");
            }
            using var reloaded = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: saved);
            var reloadedStats = reloaded.save.Get<NeoMemberClassWritable>("Thing").Get<NeoMemberClassWritable>("Stats");
            Assert.AreEqual(9d, reloaded.save.Get<NeoMemberIntWritable>("Input").value!.value);
            Assert.AreEqual(3d, reloadedStats.Get<NeoMemberIntWritable>("Value").value!.value, "The saved value wins over a rebuild from the changed input.");
            Assert.AreEqual("built", reloadedStats.Get<NeoMemberStringWritable>("Label").value!.value);
        }
    }
}
