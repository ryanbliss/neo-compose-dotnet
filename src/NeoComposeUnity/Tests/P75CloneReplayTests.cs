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

            static InitializerBody Init(TypeInfo type, Pointer pointer) => new()
            {
                code = "fixture",
                compiled = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = new[] { ConstructorVariable("__root__", ClassType("__root__")) },
                    typeInfo = type,
                    instructions = new Instruction[] { new ReturnInstruction { type = InstructionKind.Return, pointer = pointer } }
                }
            };
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
        }
    }
}
