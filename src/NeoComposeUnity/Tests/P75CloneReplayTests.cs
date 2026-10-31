// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Tests
{
    public partial class P75VirtualInstanceValueTests
    {
        private static InitializerBody Init(TypeInfo type, Pointer pointer) => new()
        {
            code = "fixture",
            compiled = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = new[] { ConstructorVariable("__this__", ClassType("thing-class")), ConstructorVariable("__root__", ClassType("__root__")) },
                typeInfo = type,
                instructions = new Instruction[] { new ReturnInstruction { type = InstructionKind.Return, pointer = pointer } }
            }
        };

        [TestCase(false)]
        [TestCase(true)]
        public void ReplacingStoredOwnedFieldPreservesSiblingDefaults(bool script)
        {
            var data = BuildReboundFieldProjectData();
            ((ClassMember)data.members["thing-nested"]).Requirement = NeoMemberRequirementKind.Optional;
            ((ClassMember)data.members["thing-nested"]).defaultValue = new ObjectMemberValueBase { value = null };
            foreach (string preset in new[] { "asset-preset", "asset-other-preset" })
            {
                ((ObjectMemberValue)data.values[preset]).value!["Deep"] = preset + "-deep";
                data.values[preset + "-deep"] = ObjectValue(preset + "-deep", "deep-class",
                    new Dictionary<string, string> { ["Count"] = preset + "-count" });
                data.values[preset + "-count"] = new NumberMemberValue { id = preset + "-count", value = 5 };
            }
            data.classes["save-root-class"].schema["Input"] = "replay-input";
            data.members["replay-input"] = new IntMember
            {
                id = "replay-input",
                name = "Input",
                kind = MemberKind.Int,
                defaultValue = new NumberMemberValueBase { value = 3 }
            };
            data.classes["thing-class"].schema["Unchanged"] = "unchanged-count";
            data.members["unchanged-count"] = new IntMember
            {
                id = "unchanged-count",
                name = "Unchanged",
                kind = MemberKind.Int,
                Requirement = NeoMemberRequirementKind.Required,
                Storage = NeoMemberStorage.Save,
                defaultValue = new NumberMemberValueBase { init = Init(IntTypeInfo(), SavePointer("Input")) }
            };
            data.classes["thing-class"].schema["Sibling"] = "thing-other";
            data.members["thing-other"] = new ClassMember
            {
                id = "thing-other",
                name = "Sibling",
                kind = MemberKind.Class,
                DeclaredClassId = "nested-class",
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new ObjectMemberValueBase { value = new Dictionary<string, string>() }
            };
            string saved;
            using (var client = NeoTestSaveStack.ClientFromSchema(data))
            {
                var thing = client.save.Get<NeoMemberClassWritable>("Thing");
                bool other = false;
                void Assign()
                {
                    other = !other;
                    if (script)
                        AssignPreset(client, "Nested", other ? "OtherPreset" : "Preset");
                    else
                        thing.SetSerializedValue("Nested", NeoValueWritePayload.FromValueReference(other ? "asset-other-preset" : "asset-preset"));
                }
                Assign();
                var sibling = thing.Get<NeoMemberClassWritable>("Sibling").Get<NeoMemberClassWritable>("Deep");
                sibling.Get<NeoMemberIntWritable>("Count").Set(17);
                string siblingId = sibling.value!.id;
                Assert.That(client.TryGetVirtualClassChildValueId(thing.value!.id, "Unchanged", out _), Is.True, "Fixture must retain a virtual sibling default.");
                for (int i = 0; i < 3; i++)
                {
                    string oldId = thing.Get<NeoMemberClassWritable>("Nested").value!.id;
                    int replays = client.VirtualReplayCount;
                    Assign();
                    Assert.That(client.VirtualReplayCount, Is.EqualTo(replays), "Replacing a stored child must not reconstruct unrelated defaults.");
                    Assert.That(thing.Get<NeoMemberClassWritable>("Nested").value!.id, Is.Not.EqualTo(oldId));
                    AssertReleased(client, oldId);
                    Assert.That(thing.Get<NeoMemberClassWritable>("Sibling").Get<NeoMemberClassWritable>("Deep").value!.id, Is.EqualTo(siblingId));
                    Assert.That(sibling.Get<NeoMemberIntWritable>("Count").value!.value, Is.EqualTo(17));
                    Assert.That(thing.Get<NeoMemberClassWritable>("Nested").Get<NeoMemberClassWritable>("Deep").Get<NeoMemberIntWritable>("Count").value!.value, Is.EqualTo(5));
                }
                int beforeDependency = client.VirtualConstructionCount;
                client.save.Get<NeoMemberIntWritable>("Input").Set(9);
                Assert.That(client.VirtualConstructionCount, Is.EqualTo(beforeDependency));
                Assert.That(thing.Get<NeoMemberIntWritable>("Unchanged").value!.value, Is.EqualTo(3));
                thing.Remove("Nested");
                Assert.That(BoundRowId(thing, "Nested"), Is.Null, "Removing the override must restore its null default.");
                saved = client.SerializeSaveData();
            }
            using var reloaded = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: saved);
            Assert.That(reloaded.save.Get<NeoMemberClassWritable>("Thing").Get<NeoMemberClassWritable>("Sibling")
                .Get<NeoMemberClassWritable>("Deep").Get<NeoMemberIntWritable>("Count").value!.value, Is.EqualTo(17));
        }

        [Test]
        public void ClonePreservesSaveStateInsideImmutableObject()
        {
            var data = BuildNestedProjectData();
            data.members["thing-nested"].Storage = NeoMemberStorage.Immutable;
            data.classes["nested-class"].allowedStorage = NeoMemberStorage.Inherit;
            data.members["nested-deep"].Storage = NeoMemberStorage.Save;
            string saved;
            using (var client = NeoTestSaveStack.ClientFromSchema(data))
            {
                var source = client.save.Get<NeoMemberClassWritable>("Thing");
                var deep = source.Get<NeoMemberClass>("Nested").Get<NeoMemberClassWritable>("Deep");
                deep.Get<NeoMemberIntWritable>("Count").Set(42);
                string id = client.CloneValueReference(source.value!.id, source.ownership);
                var clone = new NeoMemberClassWritable(client, (ClassMember)data.members["thing-member"], id, NeoValueOwnership.Session);
                var clonedDeep = clone.Get<NeoMemberClass>("Nested").Get<NeoMemberClassWritable>("Deep");
                Assert.AreEqual(42, clonedDeep.Get<NeoMemberInt>("Count").value!.value);
                clonedDeep.Get<NeoMemberIntWritable>("Count").Set(99);
                Assert.AreEqual(42, deep.Get<NeoMemberInt>("Count").value!.value);
                client.save.SetSerializedValue("Thing", NeoGeneratedTypesSupport.ValueReference(new ImmutableProbeReference { valueId = id }));
                saved = client.SerializeSaveData();
            }
            using var reloaded = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: saved);
            Assert.AreEqual(99, reloaded.save.Get<NeoMemberClass>("Thing").Get<NeoMemberClass>("Nested")
                .Get<NeoMemberClass>("Deep").Get<NeoMemberInt>("Count").value!.value);
        }

        [Test]
        public void ClonePreservesListenersOnSaveFieldsOfImmutableOwners()
        {
            var data = BuildNestedProjectData();
            data.members["thing-nested"].Storage = NeoMemberStorage.Immutable;
            data.classes["nested-class"].allowedStorage = NeoMemberStorage.Inherit;
            data.members["nested-deep"].Storage = NeoMemberStorage.Save;
            data.classes["nested-class"].schema["Count"] = "deep-count";
            data.members["deep-count"].Storage = NeoMemberStorage.Save;
            data.classes["nested-class"].schema["Changed"] = "clone-handler";
            data.members["clone-handler"] = new FunctionMember
            {
                id = "clone-handler",
                name = "Changed",
                kind = MemberKind.Function,
                returnTypeInfo = new VoidTypeInfo { type = MemberKind.Void, required = true },
                argumentTypes = new[] { new FunctionArgumentTypeInfo { name = "next", type = MemberKind.Int, required = true } }
            };
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var heard = new List<double>();
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["clone-handler"] = (_, _, args, _) => { heard.Add(System.Convert.ToDouble(args[0])); return null; }
            });
            var source = client.save.Get<NeoMemberClassWritable>("Thing");
            var nested = source.Get<NeoMemberClass>("Nested");
            client.EditMemberChangeListener(nested.value!.id, NeoValueOwnership.Asset, "deep-count", IntTypeInfo(),
                new NeoDelegateValue { memberId = "clone-handler", valueId = nested.value.id }, true);
            nested.Get<NeoMemberIntWritable>("Count").Set(7);
            CollectionAssert.AreEqual(new[] { 7d }, heard);
            heard.Clear();
            string id = client.CloneValueReference(source.value!.id, source.ownership);
            var clone = new NeoMemberClassWritable(client, (ClassMember)data.members["thing-member"], id, NeoValueOwnership.Session);
            var copiedOwner = clone.Get<NeoMemberClass>("Nested");
            Assert.That(clone.value!.copiedChangeListeners![copiedOwner.value!.id]["deep-count"][0].valueId,
                Is.EqualTo(copiedOwner.value.id));
            copiedOwner.Get<NeoMemberIntWritable>("Count").Set(9);
            CollectionAssert.AreEqual(new[] { 9d }, heard);
            Assert.AreEqual(7, nested.Get<NeoMemberInt>("Count").value!.value);
            id = client.CloneValueReference(id, NeoValueOwnership.Session);
            var repeated = new NeoMemberClassWritable(client, (ClassMember)data.members["thing-member"], id, NeoValueOwnership.Session);
            repeated.Get<NeoMemberClass>("Nested").Get<NeoMemberIntWritable>("Count").Set(10);
            CollectionAssert.AreEqual(new[] { 9d, 10d }, heard);
            client.save.SetSerializedValue("Thing", NeoGeneratedTypesSupport.ValueReference(new ImmutableProbeReference { valueId = id }));
            using var reopened = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: client.SerializeSaveData());
            reopened.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["clone-handler"] = (_, _, args, _) => { heard.Add(System.Convert.ToDouble(args[0])); return null; }
            });
            reopened.save.Get<NeoMemberClass>("Thing").Get<NeoMemberClass>("Nested").Get<NeoMemberIntWritable>("Count").Set(11);
            CollectionAssert.AreEqual(new[] { 9d, 10d, 11d }, heard);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CopiedImmutableDefaultsKeepTheirCapturedValues(bool samePlan)
        {
            var data = BuildProjectData();
            data.classes["save-root-class"].schema["Input"] = "clone-input";
            data.members["clone-input"] = new IntMember
            {
                id = "clone-input",
                name = "Input",
                kind = MemberKind.Int,
                defaultValue = new NumberMemberValueBase { value = 3 }
            };
            data.classes["thing-class"].schema["Frozen"] = "clone-frozen";
            data.members["clone-frozen"] = new IntMember
            {
                id = "clone-frozen",
                name = "Frozen",
                kind = MemberKind.Int,
                Storage = NeoMemberStorage.Immutable,
                defaultValue = new NumberMemberValueBase { init = Init(IntTypeInfo(), SavePointer("Input")) }
            };
            var initializer = ((IntMember)data.members["clone-frozen"]).defaultValue!.init!.compiled!;
            initializer.instructions = new Instruction[]
            {
                new VariableInstruction
                {
                    type = InstructionKind.Variable,
                    variable = new Variable { id = "self", typeInfo = ClassType("thing-class"), pointer = new VariablePointer { type = PointerKind.Variable, variableId = "__this__" } }
                },
                initializer.instructions[0]
            };
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var source = client.save.Get<NeoMemberClassWritable>("Thing");
            int replays = client.VirtualReplayCount;
            string copied;
            if (samePlan)
            {
                var plan = new NeoWritePlan(client);
                using (client.ReadCandidate(plan))
                    copied = client.CloneValueReference(source.value!.id, source.ownership);
                var input = (NumberMemberValue)client.CloneRowForWrite(client.save.Get<NeoMemberInt>("Input").value!);
                input.value = 9;
                plan.Set(NeoValueOwnership.Save, input);
                plan.Commit();
            }
            else
                copied = client.CloneValueReference(source.value!.id, source.ownership);
            var clone = new NeoMemberClassWritable(client, (ClassMember)data.members["thing-member"], copied, NeoValueOwnership.Session);
            Assert.AreEqual(3, clone.Get<NeoMemberInt>("Frozen").value!.value);
            if (!samePlan)
                Assert.AreEqual(replays, client.VirtualReplayCount);
            int constructions = client.VirtualConstructionCount;
            client.save.Get<NeoMemberIntWritable>("Input").Set(9);
            Assert.AreEqual(3, source.Get<NeoMemberInt>("Frozen").value!.value);
            Assert.AreEqual(3, clone.Get<NeoMemberInt>("Frozen").value!.value);
            Assert.AreEqual(constructions, client.VirtualConstructionCount);
        }

        [Test]
        public void RemovingSparseRootReclaimsMaterializedNestedReplayPartitions()
        {
            var data = BuildProjectData();
            data.classes["save-root-class"].schema["Input"] = "input";
            data.members["input"] = new IntMember
            {
                id = "input",
                name = "Input",
                kind = MemberKind.Int,
                defaultValue = new NumberMemberValueBase { value = 1 }
            };
            data.classes["leaf-class"] = SchemaClass("leaf-class", "Leaf", NeoMemberStorage.Save);
            data.classes["leaf-class"].schema["Count"] = "leaf-count";
            data.members["leaf-count"] = new IntMember
            {
                id = "leaf-count",
                name = "Count",
                kind = MemberKind.Int,
                defaultValue = new NumberMemberValueBase { init = Init(IntTypeInfo(), SavePointer("Input")) }
            };
            data.classes["thing-class"].schema["Leaf"] = "thing-leaf";
            data.members["thing-leaf"] = new ClassMember
            {
                id = "thing-leaf",
                name = "Leaf",
                kind = MemberKind.Class,
                DeclaredClassId = "leaf-class",
                defaultValue = new ObjectMemberValueBase
                {
                    init = Init(ClassType("leaf-class"), new FunctionPointer
                    {
                        type = PointerKind.Function,
                        function = new DeclaredConstructorFunction
                        {
                            type = FunctionKind.DeclaredConstructor,
                            info = new DeclaredConstructorInfo { schemaClassInfo = ClassType("leaf-class"), args = System.Array.Empty<DeclaredConstructorArgument>(), fields = System.Array.Empty<FunctionClassConstructorField>() }
                        }
                    })
                }
            };
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var thing = client.save.Get<NeoMemberClassWritable>("Thing");
            var leaf = thing.Get<NeoMemberClassWritable>("Leaf");
            var count = leaf.Get<NeoMemberIntWritable>("Count");
            count.Set(42);
            string leafId = leaf.value!.id, countId = count.value!.id;
            var pin = new NeoWritePlan(client);
            pin.Set(NeoValueOwnership.Save, client.CloneRowForWrite(leaf.value));
            pin.Commit();
            var removal = new NeoWritePlan(client);
            client.StageOwnedRemoval(removal, NeoValueOwnership.Save, thing.value!.id, thing.member, tombstone: true);
            removal.Commit();
            Assert.IsFalse(client.saveValues.ContainsKey(leafId));
            Assert.IsFalse(client.saveValues.ContainsKey(countId));
            using var reloaded = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: client.SerializeSaveData());
            Assert.IsFalse(reloaded.saveValues.ContainsKey(leafId));


        }

        [Test]
        public void CloneKeepsNestedConstructorRecipeWhenParentConstructsDifferentArguments()
        {
            var data = BuildNestedAggregateConstructorProjectData();
            data.classes["nested-class"].schema["Frozen"] = "nested-frozen";
            data.members["nested-frozen"] = new StringMember
            {
                id = "nested-frozen",
                name = "Frozen",
                kind = MemberKind.String,
                Storage = NeoMemberStorage.Immutable,
                defaultValue = new StringMemberValueBase
                {
                    init = new InitializerBody
                    {
                        code = "Literal",
                        compiled = new FunctionWithReturnType
                        {
                            compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                            parameters = data.constructors["nested-ctor"].action!.parameters,
                            typeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true },
                            instructions = new Instruction[] { new ReturnInstruction
                            {
                                type = InstructionKind.Return,
                                pointer = new VariablePointer { type = PointerKind.Variable, variableId = "__arg_2__" }
                            } }
                        }
                    }
                }
            };
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var source = client.save.Get<NeoMemberClassWritable>("Thing");
            var child = source.Get<NeoMemberClassWritable>("Holder").Get<NeoMemberClassWritable>("Nested");
            string expected = child.Get<NeoMemberString>("Frozen").value!.value!;
            // The next enclosing construction supplies a different argument.
            // Clone still copies the existing child's construction recipe.
            data.values["nested-template"].constructorArgs!["__arg_2__"] = "new parent argument";
            string id = client.CloneValueReference(source.value!.id, source.ownership);
            var copy = new NeoMemberClassWritable(client, (ClassMember)data.members["thing-member"], id, NeoValueOwnership.Session);
            var copiedChild = copy.Get<NeoMemberClassWritable>("Holder").Get<NeoMemberClassWritable>("Nested");
            Assert.AreEqual(expected, copiedChild.value!.constructorArgs!["__arg_2__"]!.Value<string>());
            Assert.AreEqual(expected, copiedChild.Get<NeoMemberString>("Frozen").value!.value);
        }

        [Test]
        public void ScriptRemovalOfVirtualUnorderedEntryStaysRemovedAfterReload()
        {
            var data = BuildUnorderedListProjectData();
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var list = client.save.Get<NeoMemberClassWritable>("Thing").Get<NeoMemberListWritable>("Items");
            string id = list[0].value!.id;
            Assert.IsFalse(client.values.ContainsKey(id));
            Assert.IsFalse(client.saveValues.ContainsKey(id));
            ExecuteSaveInstruction(client, new CollectionCallInstruction
            {
                type = InstructionKind.CollectionCall,
                mutation = CollectionMutationKind.RemoveAt,
                target = new WriteTarget
                {
                    pointer = PointerKeyOf(SavePointer("Thing"), "Items"),
                    typeInfo = new CollectionTypeInfo
                    {
                        type = MemberKind.List,
                        required = true,
                        entryTypeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true }
                    },
                    writability = WritabilityKind.Save
                },
                args = new Pointer[] { IntLiteral(0) }
            });
            Assert.AreEqual(1, list.Count);
            Assert.IsTrue(client.saveValues[id].IsRemoved);
            using var reloaded = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: client.SerializeSaveData());
            Assert.AreEqual(1, reloaded.save.Get<NeoMemberClassWritable>("Thing").Get<NeoMemberList>("Items").Count);
        }

        [Test]
        public void RepeatedClonesPreserveNestedDefaultsAndMutableOverrides()
        {
            var data = BuildNestedProjectData();
            data.classes["deep-class"].schema["Frozen"] = "deep-frozen";
            data.members["deep-frozen"] = new IntMember
            {
                id = "deep-frozen",
                name = "Frozen",
                kind = MemberKind.Int,
                Storage = NeoMemberStorage.Immutable,
                defaultValue = new NumberMemberValueBase { value = 7 }
            };
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var member = (ClassMember)data.members["thing-member"];
            var source = client.save.Get<NeoMemberClassWritable>("Thing");
            source.Get<NeoMemberClassWritable>("Nested").Get<NeoMemberClassWritable>("Deep").Get<NeoMemberIntWritable>("Count").Set(42);
            var ids = new HashSet<string>();
            int replays = client.VirtualReplayCount;
            for (int i = 0; i < 4; i++)
            {
                string id = client.CloneValueReference(source.value!.id, source.ownership);
                var copy = new NeoMemberClassWritable(client, member, id, NeoValueOwnership.Session);
                var deep = copy.Get<NeoMemberClassWritable>("Nested").Get<NeoMemberClassWritable>("Deep");
                Assert.IsTrue(ids.Add(id));
                Assert.IsTrue(ids.Add(deep.value!.id));
                Assert.AreEqual(42, deep.Get<NeoMemberInt>("Count").value!.value);
                Assert.AreEqual(7, deep.Get<NeoMemberInt>("Frozen").value!.value);
                source = copy;
            }
            Assert.AreEqual(replays, client.VirtualReplayCount, "repeated copies reuse resolved defaults");
        }
    }
}
