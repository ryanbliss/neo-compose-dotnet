// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    public class P75VirtualInstanceValueTests
    {
        private sealed class ResolvedThing : NeoGeneratedClassValue
        {
            internal ResolvedThing(NeoClient client, NeoMemberClass node, bool readOnly)
                : base(client, node, "thing-class", readOnly, node.ownership) { }
        }

        [Test]
        public void ReadOnlyComputedClassProjectionRetainsRuntimeOwnership()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var thing = client.save.Get<NeoMemberClassWritable>("Thing");
            thing.Get<NeoMemberIntWritable>("Count").Set(73);
            var runtime = NSGetterEvaluator.UnwrapRow(thing.value!,
                new NSGetterEvaluator.Context(client, null, null), NeoValueOwnership.Save);
            using var projected = NeoGeneratedTypesSupport.ReadRequiredNSPropertyClass<NeoMemberClass>(
                client, runtime, false, (_, node) => node, (_, node) => node);
            Assert.That(projected.ownership, Is.EqualTo(NeoValueOwnership.Save));
            Assert.That(projected.Get<NeoMemberInt>("Count").value!.value, Is.EqualTo(73));
        }

        [TestCase(0)]
        [TestCase(2000)]
        public void RepeatedClassResolutionKeepsTheGeneratedViewsRegisteredNode(int unrelatedValues)
        {
            var data = BuildProjectData();
            for (int i = 0; i < unrelatedValues; i++)
                data.values["unrelated-" + i] = ObjectValue("unrelated-" + i, "thing-class");
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                ["thing-class"] = (c, n) => NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                    c, n, (owner, node) => new ResolvedThing(owner, node, true)),
            };
            var writable = new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>
            {
                ["thing-class"] = (c, n) => NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                    c, n, (owner, node) => new ResolvedThing(owner, node, false)),
            };
            var first = (ResolvedThing)NeoGeneratedTypesSupport.ResolveClassValue(
                client, "thing-instance", factories, writable)!;
            Assert.That(first.BackingNode.member.id, Is.EqualTo("thing-member"));
            Assert.That(first.BackingNode.Get<NeoMemberInt>("Count").value!.value, Is.EqualTo(5));
            Assert.That(client.InferMemberParents("thing-instance").Select(p => p.Key), Is.EqualTo(new[] { "value-save" }));
            for (int i = 0; i < 3; i++)
            {
                var next = NeoGeneratedTypesSupport.ResolveClassValue(client, "thing-instance", factories, writable);
                Assert.AreSame(first, next);
                Assert.IsTrue(client.TryGetNode(first.BackingNode.member.RuntimeDeclarationIdentity,
                    first.BackingNode.overrideValueId, first.BackingNode.ownership, out var registered));
                Assert.AreSame(first.BackingNode, registered,
                    "Repeated renderer/native resolution must not replace the node that refreshes this cached view.");
            }
        }

        [Test]
        public void LookupFindsCollectionInsideSparseInstance()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildUnorderedListProjectData());
            var items = client.save.Get<NeoMemberClassWritable>("Thing").Get<NeoMemberList>("Items");
            Assert.IsTrue(client.TryResolveLookupCollectionValueId("thing-items", null, out string? target));
            Assert.AreEqual(items.value!.id, target);
            Assert.IsFalse(client.saveValues.ContainsKey(target!));
        }

        [Test]
        public void NullSavedOverlayPreservesAuthoredConstructorDefaults()
        {
            var data = BuildProjectData();
            data.classes["thing-class"].allowedStorage = NeoMemberStorage.Inherit;
            var member = (ClassMember)data.members["thing-member"];
            member.Storage = NeoMemberStorage.Inherit;
            member.Requirement = NeoMemberRequirementKind.Optional;
            data.classes["assets-root-class"].schema["Thing"] = member.id;
            ((ObjectMemberValue)data.values["value-assets"]).value!["Thing"] = "thing-instance";
            ((IntMember)data.members["thing-count"]).defaultValue = new NumberMemberValueBase
            {
                init = new InitializerBody
                {
                    code = "5",
                    compiled = new FunctionWithReturnType
                    {
                        compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                        parameters = new[] { ConstructorVariable("__root__", ClassType("__root__")) },
                        typeInfo = IntTypeInfo(),
                        instructions = new Instruction[] { new ReturnInstruction { type = InstructionKind.Return, pointer = IntLiteral(5) } },
                    },
                },
            };
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var asset = client.assets.Get<NeoMemberClass>("Thing");
            var saved = client.save.Get<NeoMemberClassWritable>("Thing");
            var original = asset.Get<NeoMemberInt>("Count").value!;
            client.ImportValueReference(NeoValueOwnership.Save, "thing-instance", out _, "thing-instance");
            client.SetSaveValue(new ObjectMemberValue { id = "thing-instance", classId = "thing-class", value = null });
            Assert.IsNull(saved.value!.value);
            Assert.AreEqual(5, asset.Get<NeoMemberInt>("Count").value!.value);
            Assert.AreEqual(original.id, asset.Get<NeoMemberInt>("Count").value!.id);
            var reset = new NeoWritePlan(client);
            reset.Remove(NeoValueOwnership.Save, "thing-instance");
            reset.Commit();
            Assert.AreEqual(5, saved.Get<NeoMemberIntWritable>("Count").value!.value);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void NestedLeafDependencyReplaysOnlyItsOwnGraph(bool parentReadsResult, bool parentWritesResult)
        {
            var data = BuildProjectData();
            data.classes["save-root-class"].schema["Input"] = "input-member";
            data.members["input-member"] = new IntMember
            {
                id = "input-member", name = "Input", kind = MemberKind.Int,
                Storage = NeoMemberStorage.Save, defaultValue = new NumberMemberValueBase { value = 1 },
            };
            data.values["input-value"] = new NumberMemberValue { id = "input-value", value = 1 };
            ((ObjectMemberValue)data.values["value-save"]).value!["Input"] = "input-value";
            data.classes["save-root-class"].schema["Unrelated"] = "unrelated-member";
            data.members["unrelated-member"] = new IntMember
            {
                id = "unrelated-member", name = "Unrelated", kind = MemberKind.Int,
                Storage = NeoMemberStorage.Save, defaultValue = new NumberMemberValueBase { value = 0 },
            };
            data.values["unrelated-value"] = new NumberMemberValue { id = "unrelated-value", value = 0 };
            ((ObjectMemberValue)data.values["value-save"]).value!["Unrelated"] = "unrelated-value";
            data.classes["leaf-class"] = new NeoSchemaClass
            {
                id = "leaf-class", name = "Leaf", projectId = "p75-project",
                schema = new Dictionary<string, string> { ["Count"] = "leaf-count", ["Name"] = "leaf-name" },
            };
            data.members["leaf-name"] = new StringMember { id = "leaf-name", name = "Name", kind = MemberKind.String, defaultValue = new StringMemberValueBase { value = "default" } };
            data.members["leaf-count"] = new IntMember
            {
                id = "leaf-count", name = "Count", kind = MemberKind.Int,
                defaultValue = new NumberMemberValueBase { init = Init(IntTypeInfo(),
                    PointerKeyOf(new ReferencePointer { type = PointerKind.Reference, valueId = "value-save" }, "Input")) },
            };
            data.classes["thing-class"].schema["Leaf"] = "thing-leaf";
            data.members["thing-leaf"] = new ClassMember
            {
                id = "thing-leaf", name = "Leaf", kind = MemberKind.Class, classId = "leaf-class",
                defaultValue = new ObjectMemberValueBase { init = Init(ClassType("leaf-class"), new FunctionPointer
                {
                    type = PointerKind.Function,
                    function = new DeclaredConstructorFunction
                    {
                        type = FunctionKind.DeclaredConstructor,
                        info = new DeclaredConstructorInfo
                        {
                            schemaClassInfo = ClassType("leaf-class"),
                            args = Array.Empty<DeclaredConstructorArgument>(),
                            fields = new[] { new FunctionClassConstructorField
                            {
                                schemaKey = "Name", memberId = "leaf-name",
                                valuePointer = new ValuePointer { type = PointerKind.Value,
                                    value = new Value { typeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true }, value = "call-site" } },
                            } },
                        },
                    },
                }) },
            };
            if (parentReadsResult || parentWritesResult)
            {
                data.classes["thing-class"].constructorIds = new[] { "thing-ctor" };
                ((ObjectMemberValue)data.values["thing-instance"]).instanceConstructorId = "thing-ctor";
                var receiver = new VariablePointer { type = PointerKind.Variable, variableId = "__this__" };
                data.constructors["thing-ctor"] = new ConstructorRecord
                {
                    id = "thing-ctor", classId = "thing-class", projectId = "p75-project",
                    argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                    action = new FunctionWithReturnType
                    {
                        compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                        parameters = new[] { ConstructorVariable("__this__", ClassType("thing-class")), ConstructorVariable("__root__", ClassType("__root__")) },
                        typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                        instructions = new Instruction[]
                        {
                            new AssignInstruction
                            {
                                type = InstructionKind.Assign, operatorValue = "=",
                                target = new WriteTarget { pointer = parentWritesResult ? PointerKeyOf(PointerKeyOf(receiver, "Leaf"), "Count") : PointerKeyOf(receiver, "Count"), typeInfo = IntTypeInfo(), writability = WritabilityKind.Session },
                                pointer = parentWritesResult ? new ValuePointer { type = PointerKind.Value, value = new Value { typeInfo = IntTypeInfo(), value = 99 } } : PointerKeyOf(PointerKeyOf(receiver, "Leaf"), "Count"),
                            },
                        },
                    },
                };
            }
            data.classes["leaf-class"].constructorIds = new[] { "replacement-leaf-ctor" };
            data.constructors["replacement-leaf-ctor"] = new ConstructorRecord
            {
                id = "replacement-leaf-ctor", classId = "leaf-class", projectId = "p75-project",
                argumentTypes = new[] { new FunctionArgumentTypeInfo { name = "config", type = MemberKind.Class, classId = "save-root-class", required = true } },
                action = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = new[] { ConstructorVariable("__this__", ClassType("leaf-class")), ConstructorVariable("__root__", ClassType("__root__")), ConstructorVariable("__arg_0__", ClassType("save-root-class")) },
                    typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                    instructions = new Instruction[] { new AssignInstruction
                    {
                        type = InstructionKind.Assign, operatorValue = "=",
                        target = new WriteTarget
                        {
                            pointer = PointerKeyOf(new VariablePointer { type = PointerKind.Variable, variableId = "__this__" }, "Name"),
                            typeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true }, writability = WritabilityKind.Session,
                        },
                        pointer = new ValuePointer { type = PointerKind.Value, value = new Value
                            { typeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true }, value = "replacement" } },
                    } },
                },
            };
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var thing = client.save.Get<NeoMemberClassWritable>("Thing");
            var sibling = thing.Get<NeoMemberIntWritable>("Count");
            var leaf = thing.Get<NeoMemberClassWritable>("Leaf");
            string leafId = leaf.value!.id;
            string valueId = leaf.Get<NeoMemberIntWritable>("Count").value!.id;
            client.TryGetValue(sibling.value!.id, out MemberValue? originalSibling);
            Assert.AreEqual(parentWritesResult ? 99 : 1, leaf.Get<NeoMemberIntWritable>("Count").value!.value);
            client.save.Get<NeoMemberIntWritable>("Input").Set(7);
            Assert.AreEqual(parentWritesResult ? 99 : 7, leaf.Get<NeoMemberIntWritable>("Count").value!.value);
            Assert.AreEqual("call-site", leaf.Get<NeoMemberStringWritable>("Name").value!.value);
            Assert.AreEqual(leafId, leaf.value!.id);
            Assert.AreEqual(valueId, leaf.Get<NeoMemberIntWritable>("Count").value!.id);
            client.TryGetValue(sibling.value!.id, out MemberValue? nextSibling);
            if (parentReadsResult)
                Assert.AreEqual(7, thing.Get<NeoMemberIntWritable>("Count").value!.value, "A parent that consumes the result must replay too.");
            else if (!parentWritesResult)
                Assert.AreSame(originalSibling, nextSibling, "Changing a leaf input must not reconstruct its enclosing graph.");
            client.TryGetValue(valueId, out MemberValue? beforeUnrelatedWrite);
            client.save.Get<NeoMemberIntWritable>("Unrelated").Set(99);
            client.TryGetValue(valueId, out MemberValue? afterUnrelatedWrite);
            Assert.AreSame(beforeUnrelatedWrite, afterUnrelatedWrite,
                "A class reference must depend on the fields read, not every write to its row.");
            var replacement = new NeoWritePlan(client);
            var saveRoot = (ObjectMemberValue)client.CloneRowForWrite(client.save.value!);
            saveRoot.value!["Input"] = "replacement-input";
            replacement.Set(NeoValueOwnership.Save, new NumberMemberValue { id = "replacement-input", value = 11 });
            replacement.Set(NeoValueOwnership.Save, saveRoot);
            replacement.Commit();
            Assert.AreEqual(parentWritesResult ? 99 : 11, leaf.Get<NeoMemberIntWritable>("Count").value!.value,
                "Replacing a field link must invalidate readers even when the class identity is unchanged.");
            if (parentReadsResult) Assert.AreEqual(11, thing.Get<NeoMemberIntWritable>("Count").value!.value);
            leaf.Get<NeoMemberIntWritable>("Count").Set(42);
            if (parentReadsResult)
                Assert.AreEqual(11, thing.Get<NeoMemberIntWritable>("Count").value!.value,
                    "The parent constructor reads the fresh child before saved overrides are applied.");
            client.save.Get<NeoMemberIntWritable>("Input").Set(9);
            Assert.AreEqual(42, leaf.Get<NeoMemberIntWritable>("Count").value!.value, "Stored field overrides must survive independent replay.");

            if (!parentReadsResult && !parentWritesResult)
            {
                var replaced = (ObjectMemberValue)client.CloneRowForWrite(leaf.value!);
                replaced.instanceConstructorId = "replacement-leaf-ctor";
                replaced.constructorArgs = new() { ["__arg_0__"] = new JValue("value-save") };
                replaced.value = new();
                var replacePlan = new NeoWritePlan(client);
                replacePlan.Remove(NeoValueOwnership.Save, valueId);
                replacePlan.Set(NeoValueOwnership.Save, replaced);
                replacePlan.Commit();
                var currentLeaf = thing.Get<NeoMemberClassWritable>("Leaf");
                Assert.AreEqual("replacement", currentLeaf.Get<NeoMemberStringWritable>("Name").value!.value,
                    "Replacing a nested constructor must not restore the old call-site initializer.");
                client.save.Get<NeoMemberIntWritable>("Input").Set(12);
                Assert.AreEqual("replacement", currentLeaf.Get<NeoMemberStringWritable>("Name").value!.value);
                Assert.AreEqual(12, currentLeaf.Get<NeoMemberIntWritable>("Count").value!.value);
                string currentCountId = currentLeaf.Get<NeoMemberIntWritable>("Count").value!.id;
                client.TryGetValue(currentCountId, out MemberValue? beforeMetadataOnlyChange);
                var unrelatedPlan = new NeoWritePlan(client);
                var unrelatedRoot = (ObjectMemberValue)client.CloneRowForWrite(client.save.value!);
                unrelatedRoot.value!["Unrelated"] = "other-unrelated-value";
                unrelatedPlan.Set(NeoValueOwnership.Save, new NumberMemberValue { id = "other-unrelated-value", value = 100 });
                unrelatedPlan.Set(NeoValueOwnership.Save, unrelatedRoot);
                unrelatedPlan.Commit();
                client.TryGetValue(currentCountId, out MemberValue? afterMetadataOnlyChange);
                Assert.AreSame(beforeMetadataOnlyChange, afterMetadataOnlyChange,
                    "Stored constructor argument metadata must not read an entire class payload.");
            }

            static InitializerBody Init(TypeInfo type, Pointer pointer) => new()
            {
                code = "fixture",
                compiled = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = new[] { ConstructorVariable("__root__", ClassType("__root__")) },
                    typeInfo = type,
                    instructions = new Instruction[] { new ReturnInstruction { type = InstructionKind.Return, pointer = pointer } },
                },
            };
        }

        [Test]
        public void RemovingAListEntryRetainsUnchangedSiblingConstruction()
        {
            var data = BuildNestedProjectData();
            var nested = ObjectValue("nested-instance", "nested-class");
            nested.instanceConstructorId = null;
            nested.constructorArgs = new();
            data.values[nested.id] = nested;
            ((ObjectMemberValue)data.values["thing-instance"]).value!["Nested"] = nested.id;
            data.classes["thing-class"].schema["Items"] = "items";
            data.members["items"] = new ListMember
            {
                id = "items", name = "Items", kind = MemberKind.List, entryMemberId = "entry",
                defaultValue = new ArrayMemberValueBase { value = new[] { "first", "second" } },
            };
            data.members["entry"] = new StringMember { id = "entry", name = "Entry", kind = MemberKind.String };
            data.values["first"] = new StringMemberValue { id = "first", value = "one" };
            data.values["second"] = new StringMemberValue { id = "second", value = "two" };
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var thing = client.save.Get<NeoMemberClassWritable>("Thing");
            var count = thing.Get<NeoMemberClassWritable>("Nested")
                .Get<NeoMemberClassWritable>("Deep").Get<NeoMemberIntWritable>("Count");
            var before = count.value;
            thing.Get<NeoMemberListWritable>("Items").RemoveAt(0);
            Assert.AreSame(before, count.value, "An unchanged sibling must retain its constructed rows.");
            Assert.AreEqual(1, thing.Get<NeoMemberListWritable>("Items").value!.value!.Length);
            Assert.AreEqual(5, count.value!.value);
        }

        [Test]
        public void BindingScriptRootOnlyCapturesTheRootActuallyRead()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var reads = new HashSet<string>();
            using (client.CaptureValueReads(reads))
            {
                var ctx = new NSGetterEvaluator.Context(client, null, null);
                var root = NeoScriptValueMarshaller.ResolveRoot(client, ctx);
                Assert.IsEmpty(reads, "Binding names must not subscribe constructors to unused roots.");
                ctx = ctx.WithRoot(root);
                var getter = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = Array.Empty<Variable>(), typeInfo = IntTypeInfo(),
                    instructions = new Instruction[]
                    {
                        new ReturnInstruction
                        {
                            type = InstructionKind.Return,
                            pointer = PointerKeyOf(PointerKeyOf(PointerKeyOf(RootPointer(), "Save"), "Thing"), "Count"),
                        },
                    },
                };
                Assert.AreEqual(5d, NSGetterEvaluator.Evaluate(getter, ctx));
                Assert.Contains("value-save", reads.ToArray());
                Assert.IsFalse(reads.Contains("value-assets"));
                Assert.IsFalse(reads.Contains("value-session"));
                Assert.Contains(client.save.Get<NeoMemberClassWritable>("Thing")
                    .Get<NeoMemberIntWritable>("Count").value!.id, reads.ToArray());
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnstampedStoredClassReadsItsDeclarationDefault(bool overridden)
        {
            ProjectData data = BuildProjectData(defaultCount: 1);
            var thing = ObjectValue("thing-instance", "thing-class");
            if (overridden)
            {
                data.values["stored-count"] = new NumberMemberValue { id = "stored-count", value = 3 };
                thing.value!["Count"] = "stored-count";
            }
            data.values[thing.id] = thing;
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            Assert.AreEqual(overridden ? 3d : 1d, client.save.Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberIntWritable>("Count").value!.value, "C# wrapper");
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            ctx = ctx.WithRoot(NeoScriptRuntimeRoot(client, ctx));
            var getter = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = Array.Empty<Variable>(),
                typeInfo = IntTypeInfo(),
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = PointerKeyOf(PointerKeyOf(PointerKeyOf(RootPointer(), "Save"), "Thing"), "Count"),
                    },
                },
            };
            Assert.AreEqual(overridden ? 3d : 1d,
                NSGetterEvaluator.Evaluate(getter, ctx), "NeoScript read");
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void NeoScriptReadsOmittedNullableClassDefaults(bool storedOverride, bool partial)
        {
            ProjectData data = BuildProjectData();
            if (partial) ((ClassMember)data.members["thing-member"]).Payload = NeoMemberPayloadKind.Partial;
            data.classes["thing-class"].schema["Optional"] = "optional-class";
            data.members["optional-class"] = new ClassMember
            {
                id = "optional-class", name = "Optional", kind = MemberKind.Class,
                classId = "thing-class", Requirement = NeoMemberRequirementKind.Optional,
                defaultValue = new ObjectMemberValueBase { value = null },
            };
            if (storedOverride)
            {
                data.values["optional-value"] = ObjectValue("optional-value", "thing-class");
                ((ObjectMemberValue)data.values["thing-instance"]).value!["Optional"] = "optional-value";
            }
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            ctx = ctx.WithRoot(NeoScriptRuntimeRoot(client, ctx));
            var type = ClassType("thing-class"); type.required = false;
            var getter = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = Array.Empty<Variable>(), typeInfo = type,
                instructions = new Instruction[] { new ReturnInstruction
                {
                    type = InstructionKind.Return,
                    pointer = PointerKeyOf(PointerKeyOf(PointerKeyOf(RootPointer(), "Save"), "Thing"), "Optional"),
                } },
            };
            // Partial objects preserve absent keys rather than inheriting defaults.
            if (partial && !storedOverride)
            {
                Assert.Throws<NSGetterRuntimeError>(() => NSGetterEvaluator.Evaluate(getter, ctx));
                return;
            }
            // Read through NeoScript before constructing a C# child wrapper.
            object? value = NSGetterEvaluator.Evaluate(getter, ctx);
            if (storedOverride) Assert.IsNotNull(value);
            else Assert.IsNull(value);
        }

        [Test]
        public void UnstampedDefaultsKeepWritesIsolatedAndSurviveSaveReload()
        {
            ProjectData data = BuildProjectData(1);
            UnstampThing(data);
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoMemberIntWritable count = client.save.Get<NeoMemberClassWritable>("Thing").Get<NeoMemberIntWritable>("Count");
            string id = count.value!.id;
            count.Set(9);
            Assert.AreEqual(9, count.value!.value);
            Assert.AreEqual(1, ((IntMember)data.members["thing-count"]).defaultValue!.value);
            CollectionAssert.IsEmpty(client.FindUnlinkedSaveValueIds());
            string saved = client.SerializeSaveData();
            client.ApplyExternalSaveContent(saved);
            count = client.save.Get<NeoMemberClassWritable>("Thing").Get<NeoMemberIntWritable>("Count");
            Assert.AreEqual(id, count.value!.id);
            Assert.AreEqual(9, count.value.value);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnstampedEnumDefaultUsesSharedDeclaration(bool overridden)
        {
            ProjectData data = BuildProjectData();
            var thing = UnstampThing(data);
            data.classes["thing-class"].schema["Quality"] = "quality";
            data.enums["quality-level"] = new NeoCompose.Runtime.Json.Enum
            {
                id = "quality-level", name = "QualityLevel", projectId = "p75-project",
                options = new Dictionary<string, EnumOption>
                {
                    ["one-star"] = new EnumOption { text = "OneStar" },
                    ["three-star"] = new EnumOption { text = "ThreeStar" },
                },
            };
            data.members["quality"] = new EnumMember
            {
                id = "quality", name = "Quality", projectId = "p75-project", kind = MemberKind.Enum,
                enumId = "quality-level", defaultValue = new ArrayMemberValueBase { value = new[] { "one-star" } },
            };
            if (overridden)
            {
                thing.value!["Quality"] = "quality-override";
                data.values["quality-override"] = new ArrayMemberValue { id = "quality-override", value = new[] { "three-star" } };
            }
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            ctx = ctx.WithRoot(NeoScriptRuntimeRoot(client, ctx));
            var getter = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = Array.Empty<Variable>(),
                typeInfo = new EnumTypeInfo { type = MemberKind.Enum, enumId = "quality-level", required = true },
                instructions = new Instruction[] { new ReturnInstruction
                {
                    type = InstructionKind.Return,
                    pointer = PointerKeyOf(PointerKeyOf(PointerKeyOf(RootPointer(), "Save"), "Thing"), "Quality"),
                } },
            };
            CollectionAssert.AreEqual(new[] { overridden ? "three-star" : "one-star" },
                (System.Collections.IEnumerable)NSGetterEvaluator.Evaluate(getter, ctx)!);
        }

        [Test]
        public void UnstampedGenericSlotKeepsItsDeclarationDefault()
        {
            ProjectData data = BuildGenericConstructorProjectData();
            var thing = UnstampThing(data);
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            Assert.IsTrue(client.TryGetVirtualClassChildValueId(thing.id, "Payload", out string? payloadId));
            Assert.IsNotNull(payloadId);
            Assert.AreEqual("from generic default", PayloadName(client.save.Get<NeoMemberClassWritable>("Thing")));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void UnstampedNullAndPartialClassesDoNotProjectDefaults(bool partial)
        {
            ProjectData data = BuildProjectData();
            var thing = UnstampThing(data);
            if (partial) ((ClassMember)data.members["thing-member"]).Payload = NeoMemberPayloadKind.Partial;
            else thing.value = null;
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            Assert.IsFalse(client.TryGetVirtualClassChildValueId(thing.id, "Count", out _));
        }

        [TestCase(NeoListKind.Ordered)]
        [TestCase(NeoListKind.Unordered)]
        public void VirtualCollectionEntriesRetainClosedGenericPlacements(NeoListKind kind)
        {
            ProjectData data = BuildUnorderedListProjectData("entry");
            ((ListMember)data.members["thing-items"]).ListKind = kind;
            var itemClass = SchemaClass("item-class", "Item", NeoMemberStorage.Save);
            itemClass.genericParams = new List<GenericParamDeclaration> { new() { id = "item-t", name = "T" } };
            itemClass.schema["Count"] = "item-count";
            data.classes[itemClass.id] = itemClass;
            data.members["int-binding"] = new IntMember { id = "int-binding", name = "IntBinding", kind = MemberKind.Int,
                Requirement = NeoMemberRequirementKind.Required, defaultValue = new NumberMemberValueBase { value = 5 } };
            data.members["item-count"] = new GenericMember { id = "item-count", name = "Count", kind = MemberKind.Generic,
                genericParamId = "item-t", defaultValue = new NullMemberValueBase { value = 5d } };
            data.members["thing-item"] = new ClassMember { id = "thing-item", name = "Item", kind = MemberKind.Class,
                classId = itemClass.id, Requirement = NeoMemberRequirementKind.Required,
                classArguments = new Dictionary<string, GenericBinding> { ["item-t"] = new() { kind = NeoGenericBindingKind.Member, memberId = "int-binding" } } };
            var entry = ObjectValue("entry", itemClass.id);
            data.values[entry.id] = entry;
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            var list = client.save.Get<NeoMemberClassWritable>("Thing").Get<NeoMemberList>("Items");
            var child = list.Cast<NeoMemberClassWritable>().Single();
            Assert.IsTrue(client.TryInferMemberForValueId(child.value!.id, out Member? placement));
            Assert.That(((ClassMember)placement!).classArguments!["item-t"].memberId, Is.EqualTo("int-binding"));
            Assert.That(child.Get<NeoMemberIntWritable>("Count").value!.value, Is.EqualTo(5d));
        }

        [TestCase(NeoListKind.Ordered)]
        [TestCase(NeoListKind.Unordered)]
        public void UnstampedDefaultsRetainCollectionEntries(NeoListKind listKind)
        {
            ProjectData data = BuildUnorderedListProjectData();
            UnstampThing(data);
            ((ListMember)data.members["thing-items"]).ListKind = listKind;
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            Assert.IsTrue(client.TryGetVirtualClassChildValueId("thing-instance", "Items", out string? listId));
            if (listKind == NeoListKind.Unordered)
                Assert.AreEqual(2, client.GetUnorderedListEntryIds(listId!).Count);
            NeoMemberList items = client.save.Get<NeoMemberClassWritable>("Thing").Get<NeoMemberList>("Items");
            CollectionAssert.AreEquivalent(new[] { "A", "B" }, items.Cast<NeoMemberStringWritable>().Select(item => item.value!.value));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnstampedDefaultsResolveNestedClasses(bool computed)
        {
            ProjectData data = BuildNestedProjectData();
            UnstampThing(data);
            if (computed) ((IntMember)data.members["deep-count"]).defaultValue = ComputedIntInitializer(5);
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            Assert.AreEqual(5, client.save.Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberClassWritable>("Nested").Get<NeoMemberClassWritable>("Deep")
                .Get<NeoMemberIntWritable>("Count").value!.value);
        }

        private static ObjectMemberValue UnstampThing(ProjectData data)
        {
            var previous = (ObjectMemberValue)data.values["thing-instance"];
            var thing = ObjectValue(previous.id, previous.classId!, previous.value);
            thing.genericBindings = previous.genericBindings;
            data.values[thing.id] = thing;
            return thing;
        }

        [TestCase(200)]
        [TestCase(1000)]
        [Explicit("Measures indexed declaration projection and cached reads; run serially.")]
        public void UnstampedDeclarationDefaultScaling(int count)
        {
            ProjectData data = BuildUnorderedListProjectData();
            ObjectMemberValue thing = UnstampThing(data);
            thing.value!["Items"] = "many-items";
            data.values["many-items"] = new ArrayMemberValue { id = "many-items", value = Array.Empty<string>() };
            ((ListMember)data.members["thing-items"]).defaultValue = new ArrayMemberValueBase { value = Array.Empty<string>() };
            data.members["thing-item"] = new ClassMember
            {
                id = "thing-item", name = "Item", kind = MemberKind.Class, classId = "item-class",
            };
            data.classes["item-class"] = SchemaClass("item-class", "Item", NeoMemberStorage.Save);
            data.classes["item-class"].schema["Count"] = "item-count";
            data.members["item-count"] = new IntMember
            {
                id = "item-count", name = "Count", kind = MemberKind.Int,
                defaultValue = new NumberMemberValueBase { value = 1 },
            };
            for (int i = 0; i < count; i++)
            {
                ObjectMemberValue row = ObjectValue($"many-item-{i}", "item-class");
                row.containerId = "many-items";
                data.values[row.id] = row;
            }
            var timer = System.Diagnostics.Stopwatch.StartNew();
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            TestContext.WriteLine($"{count} unstamped roots load: {timer.Elapsed.TotalMilliseconds:F2} ms");
            Assert.IsTrue(client.TryGetVirtualClassChildValueId("many-item-0", "Count", out string? valueId));
            timer.Restart();
            for (int i = 0; i < 100000; i++)
            {
                client.TryGetVirtualClassChildValueId("many-item-0", "Count", out string? repeatedId);
                if (repeatedId != valueId) Assert.Fail("Read changed the default id.");
            }
            TestContext.WriteLine($"100000 cached reads: {timer.Elapsed.TotalMilliseconds:F2} ms");
            Assert.IsTrue(client.TryGetValue(valueId!, out NumberMemberValue? value));
            Assert.AreEqual(1, value!.value);
        }

        [Test]
        public void UnstampedPartitionDefaultsAreRemovedOnUnload()
        {
            ProjectData data = BuildProjectData(1);
            ObjectMemberValue thing = UnstampThing(data);
            data.values.Remove(thing.id);
            const string partition = "world:thing-class";
            data.valuePartitions = new Dictionary<string, JToken>
            {
                [partition] = new JObject { [thing.id] = JObject.FromObject(thing) },
            };
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            client.LoadValuePartition(partition);
            Assert.IsTrue(client.TryGetVirtualClassChildValueId(thing.id, "Count", out string? childId));
            Assert.IsTrue(client.TryGetValue<MemberValue>(childId!, out _));
            client.UnloadValuePartition(partition);
            Assert.IsFalse(client.TryGetVirtualClassChildValueId(thing.id, "Count", out _));
            Assert.IsFalse(client.TryGetValue<MemberValue>(childId!, out _));
            client.LoadValuePartition(partition);
            Assert.IsTrue(client.TryGetVirtualClassChildValueId(thing.id, "Count", out string? reloadedId));
            Assert.AreEqual(childId, reloadedId);
        }

        [Test]
        public void PlainDeclarationRootKeepsItsDirectBindingAndLiveUpdates()
        {
            ProjectData data = BuildProjectData();
            data.classes["save-root-class"].schema["Bound"] = "bound-member";
            data.members["bound-member"] = new DictionaryMember
            {
                id = "bound-member", name = "Bound", projectId = "p75-project", kind = MemberKind.Dictionary,
                entryMemberId = "thing-count", valueId = "bound-value", Storage = NeoMemberStorage.Save,
                defaultValue = new ObjectMemberValueBase { value = new Dictionary<string, string>() },
            };
            data.values["bound-value"] = new ObjectMemberValue
            {
                id = "bound-value", value = new Dictionary<string, string> { ["entry"] = "bound-entry" },
            };
            data.values["bound-entry"] = new NumberMemberValue { id = "bound-entry", value = 3 };
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            var dictionary = client.save.Get<NeoMemberDictionary>("Bound");
            Assert.AreEqual("bound-value", dictionary.value!.id);
            Assert.IsFalse(client.TryGetVirtualClassChildValueId("value-save", "Bound", out _));
            var incoming = JObject.Parse(client.SerializeSaveData());
            incoming["values"]!["bound-entry"] = JObject.FromObject(new NumberMemberValue { id = "bound-entry", value = 8 });
            client.ApplyExternalSaveContent(incoming.ToString());
            dictionary = client.save.Get<NeoMemberDictionary>("Bound");
            Assert.AreEqual("bound-value", dictionary.value!.id);
            Assert.AreEqual(8, ((NeoMemberIntWritable)dictionary["entry"]).value!.value);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SparseConstructorReplaysALaterSiblingBeforeUnwrappingItsArgument(bool unstamped)
        {
            ProjectData data = BuildGenericConstructorProjectData();
            AddSparsePayloadSibling(data);
            if (unstamped) data.values["zz-payload"] = ObjectValue("zz-payload", "payload-class");
            ConstructorRecord constructor = data.constructors["thing-ctor"];
            data.members["thing-payload"] = new ClassMember
            {
                id = "thing-payload", projectId = "p75-project", name = "Payload", kind = MemberKind.Class,
                classId = "payload-class", Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new ObjectMemberValueBase
                {
                    init = ReturnVariableInitializer("Payload", ClassType("payload-class"), constructor.action!.parameters, "__arg_0__"),
                },
            };
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            Assert.AreEqual("default", client.save.Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberClassWritable>("Payload").Get<NeoMemberStringWritable>("Name").value!.value);
            Assert.AreEqual("default", client.save.Get<NeoMemberClassWritable>("ZPayload")
                .Get<NeoMemberStringWritable>("Name").value!.value);
            client.save.Get<NeoMemberClassWritable>("ZPayload")
                .Get<NeoMemberStringWritable>("Name").Set("updated argument");
            Assert.AreEqual("updated argument", client.save.Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberClassWritable>("Payload").Get<NeoMemberStringWritable>("Name").value!.value,
                "A virtual argument descendant must invalidate constructors that read it.");
        }

        [Test]
        public void VirtualTileDefaultsStayReadableWhileOnlyCellCanBeWritten()
        {
            ProjectData data = BuildProjectData();
            data.classes["thing-class"].system = new JObject { ["worldKind"] = "tile" };
            data.classes["thing-class"].schema["Cell"] = "tile-cell";
            data.members["tile-cell"] = new Vector2IntMember
            {
                id = "tile-cell", name = "Cell", kind = MemberKind.Vector2Int,
                defaultValue = new Vector2MemberValueBase { value = new NeoVector2Value { x = 0, y = 0 } },
            };
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            var tile = client.save.Get<NeoMemberClassWritable>("Thing");
            var cell = tile.Get<NeoMemberVector2IntWritable>("Cell");
            cell.Set(new UnityEngine.Vector2Int(2, 3));
            Assert.AreEqual(2, cell.value!.value!.x);
            var count = tile.Get<NeoMemberIntWritable>("Count");
            string before = client.SerializeSaveData();
            Assert.Throws<NeoPlacementValidationException>(() => count.Set(9));
            Assert.AreEqual(before, client.SerializeSaveData());
            Assert.AreEqual(5, count.value!.value);
        }

        [Test]
        public void ResettingVirtualTileShadowRestoresItsClassDefaults()
        {
            ProjectData data = BuildNestedProjectData();
            data.classes["nested-class"].system = new JObject { ["worldKind"] = "tile" };
            data.classes["nested-class"].schema["Cell"] = "tile-cell";
            data.members["tile-cell"] = new Vector2IntMember
            {
                id = "tile-cell", name = "Cell", kind = MemberKind.Vector2Int,
                defaultValue = new Vector2MemberValueBase { value = new NeoVector2Value { x = 0, y = 0 } },
            };
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            var tile = client.save.Get<NeoMemberClassWritable>("Thing").Get<NeoMemberClassWritable>("Nested");
            string id = tile.value!.id;
            string cellId = tile.Get<NeoMemberVector2IntWritable>("Cell").value!.id;
            Assert.IsFalse(client.values.ContainsKey(id));
            client.SetSaveValue(new ObjectMemberValue
            {
                id = id, classId = "nested-class", value = new Dictionary<string, string> { ["Cell"] = cellId },
            });
            Assert.IsTrue(client.saveValues.ContainsKey(id));
            var reset = new NeoWritePlan(client);
            reset.Remove(NeoValueOwnership.Save, id);
            Assert.DoesNotThrow(() => reset.Commit());
            Assert.IsFalse(client.saveValues.ContainsKey(id));
            tile = client.save.Get<NeoMemberClassWritable>("Thing").Get<NeoMemberClassWritable>("Nested");
            Assert.AreEqual(5, tile.Get<NeoMemberClassWritable>("Deep").Get<NeoMemberIntWritable>("Count").value!.value);
        }

        [Test]
        public void ReplayDefersComputedChildrenUnderAnAuthoredPlacementDefault()
        {
            // Implicit replay delegates content to the placement declaration,
            // whose rows are authored assets rather than the temporary Session
            // graph a constructor publishes. The deferral has to cover the
            // whole replay, not only rows replay just minted, or those authored
            // rows' computed leaves are materialized as literals and fail closed.
            ProjectData data = BuildProjectData(defaultCount: 1);
            var nestedClass = SchemaClass(
                "placement-nested-class", "PlacementNested", NeoMemberStorage.Save);
            nestedClass.schema["Computed"] = "placement-computed";
            data.classes[nestedClass.id] = nestedClass;
            data.members["placement-computed"] = new IntMember
            {
                id = "placement-computed", projectId = "p75-project", name = "Computed",
                kind = MemberKind.Int, Requirement = NeoMemberRequirementKind.Required,
                defaultValue = ComputedIntInitializer(7),
            };
            data.classes["thing-class"].schema["Nested"] = "thing-nested";
            data.members["thing-nested"] = ClassPlacement(
                "thing-nested", "Nested", nestedClass.id, NeoMemberStorage.Save);

            var placement = (ClassMember)data.members["thing-member"];
            placement.defaultValue = new ObjectMemberValueBase
            {
                classId = "thing-class",
                value = new Dictionary<string, string>
                {
                    ["Count"] = "placement-count",
                    ["Nested"] = "placement-nested",
                },
            };
            data.values["placement-count"] = new NumberMemberValue
            {
                id = "placement-count", value = 5,
            };
            // Computed is deliberately absent: replay mints it.
            data.values["placement-nested"] = ObjectValue(
                "placement-nested", nestedClass.id);

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            Assert.AreEqual(7, client.save
                .Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberClassWritable>("Nested")
                .Get<NeoMemberIntWritable>("Computed").value!.value);
        }

        [Test]
        public void NestedReplayKeepsOuterTemporarySessionRowsAwaitingInitializers()
        {
            ProjectData data = BuildProjectData();
            var nestedClass = SchemaClass(
                "outer-nested-class", "OuterNested", NeoMemberStorage.Immutable | NeoMemberStorage.Session);
            nestedClass.schema["Computed"] = "outer-computed";
            var outerClass = SchemaClass(
                "outer-class", "Outer", NeoMemberStorage.Immutable | NeoMemberStorage.Save | NeoMemberStorage.Session);
            outerClass.schema["Nested"] = "outer-nested";
            outerClass.schema["Observed"] = "outer-observed";
            outerClass.constructorIds = new[] { "outer-ctor" };
            var innerClass = SchemaClass(
                "inner-class", "Inner", NeoMemberStorage.Save);
            innerClass.schema["Result"] = "inner-result";
            innerClass.constructorIds = new[] { "inner-ctor" };
            data.classes[nestedClass.id] = nestedClass;
            data.classes[outerClass.id] = outerClass;
            data.classes[innerClass.id] = innerClass;

            data.classes["save-root-class"].schema["Outer"] = "save-outer";
            data.classes["save-root-class"].schema["Inner"] = "save-inner";
            data.classes["session-root-class"].schema["Outer"] = "session-outer";
            data.classes["assets-root-class"].schema["Source"] = "assets-source";
            data.members["assets-source"] = ClassPlacement(
                "assets-source", "Source", outerClass.id, NeoMemberStorage.Immutable);
            data.members["save-outer"] = ClassPlacement(
                "save-outer", "Outer", outerClass.id, NeoMemberStorage.Save);
            data.members["save-inner"] = ClassPlacement(
                "save-inner", "Inner", innerClass.id, NeoMemberStorage.Save);
            data.members["session-outer"] = ClassPlacement(
                "session-outer",
                "Outer",
                outerClass.id,
                NeoMemberStorage.Session,
                NeoMemberRequirementKind.Optional);
            data.members["outer-nested"] = new ClassMember
            {
                id = "outer-nested", projectId = "p75-project", name = "Nested", kind = MemberKind.Class,
                classId = nestedClass.id, Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new ObjectMemberValueBase
                {
                    classId = nestedClass.id,
                    value = new Dictionary<string, string>(),
                },
            };
            data.members["outer-computed"] = new IntMember
            {
                id = "outer-computed", projectId = "p75-project", name = "Computed", kind = MemberKind.Int,
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = ComputedIntInitializer(7),
            };
            data.members["outer-observed"] = new IntMember
            {
                id = "outer-observed", projectId = "p75-project", name = "Observed", kind = MemberKind.Int,
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new NumberMemberValueBase { value = 0 },
            };
            data.members["inner-result"] = new IntMember
            {
                id = "inner-result", projectId = "p75-project", name = "Result", kind = MemberKind.Int,
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new NumberMemberValueBase { value = 1 },
            };

            data.constructors["inner-ctor"] = new ConstructorRecord
            {
                id = "inner-ctor",
                projectId = "p75-project",
                classId = innerClass.id,
                argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                action = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = new[]
                    {
                        ConstructorVariable("__this__", ClassType(innerClass.id)),
                        ConstructorVariable("__root__", ClassType("__root__")),
                    },
                    typeInfo = new PrimitiveTypeInfo
                    {
                        type = MemberKind.Null,
                        required = true,
                    },
                    instructions = new Instruction[]
                    {
                        new FunctionCallInstruction
                        {
                            type = InstructionKind.FunctionCall,
                            call = ApplyBaseVariant(
                                PointerKeyOf(
                                    PointerKeyOf(
                                        PointerKeyOf(RootPointer(), "Session"),
                                        "Outer"),
                                    "Nested"),
                                nestedClass.id),
                        },
                    },
                },
            };

            var initialOuterArgument = new FunctionArgumentTypeInfo
            {
                name = "InitialOuter",
                type = MemberKind.Class,
                required = true,
                classId = outerClass.id,
            };
            data.constructors["outer-ctor"] = new ConstructorRecord
            {
                id = "outer-ctor",
                projectId = "p75-project",
                classId = outerClass.id,
                argumentTypes = new[] { initialOuterArgument },
                action = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = new[]
                    {
                        ConstructorVariable("__this__", ClassType(outerClass.id)),
                        ConstructorVariable("__root__", ClassType("__root__")),
                        ConstructorVariable("__arg_0__", initialOuterArgument),
                    },
                    typeInfo = new PrimitiveTypeInfo
                    {
                        type = MemberKind.Null,
                        required = true,
                    },
                    instructions = new Instruction[]
                    {
                        AssignClass(
                            PointerKeyOf(
                                PointerKeyOf(RootPointer(), "Session"),
                                "Outer"),
                            CloneClass(
                                PointerKeyOf(PointerKeyOf(RootPointer(), "Assets"), "Source"),
                                outerClass.id),
                            outerClass.id,
                            WritabilityKind.Session),
                        new AssignInstruction
                        {
                            type = InstructionKind.Assign,
                            target = new WriteTarget
                            {
                                pointer = PointerKeyOf(
                                    new VariablePointer
                                    {
                                        type = PointerKind.Variable,
                                        variableId = "__this__",
                                    },
                                    "Observed"),
                                typeInfo = IntTypeInfo(),
                                writability = WritabilityKind.Session,
                            },
                            operatorValue = "=",
                            pointer = PointerKeyOf(
                                PointerKeyOf(
                                    PointerKeyOf(RootPointer(), "Save"),
                                    "Inner"),
                                "Result"),
                        },
                        AssignClass(
                            PointerKeyOf(
                                PointerKeyOf(RootPointer(), "Session"),
                                "Outer"),
                            new VariablePointer
                            {
                                type = PointerKind.Variable,
                                variableId = "__arg_0__",
                            },
                            outerClass.id,
                            WritabilityKind.Session),
                    },
                },
            };

            // Replay Outer first. Its constructor publishes a sparse source clone,
            // then reads Inner and restores the durable Session reference.
            // Applying Base inside Inner constructs a wrapper for that clone's
            // Nested row; Base is already selected, so the application is a no-op.
            var outer = ObjectValue("aaa-outer", outerClass.id);
            outer.instanceConstructorId = "outer-ctor";
            outer.constructorArgs = new Dictionary<string, JToken?>
            {
                ["__arg_0__"] = "session-outer-initial",
            };
            var inner = ObjectValue("zzz-inner", innerClass.id);
            inner.instanceConstructorId = "inner-ctor";
            inner.constructorArgs = new Dictionary<string, JToken?>();
            data.values[outer.id] = outer;
            data.values[inner.id] = inner;
            // The stored source has a constructor recipe and a durable Nested
            // spine. Computed is supplied virtually by its declaration.
            var source = ObjectValue(
                "bbb-source",
                outerClass.id,
                new Dictionary<string, string> { ["Nested"] = "source-nested" });
            source.instanceConstructorId = "outer-ctor";
            source.constructorArgs = new Dictionary<string, JToken?>
            {
                ["__arg_0__"] = "session-outer-initial",
            };
            data.values[source.id] = source;
            data.values["source-nested"] = ObjectValue("source-nested", nestedClass.id);
            ((ObjectMemberValue)data.values["value-assets"]).value!["Source"] = source.id;
            data.values["session-outer-initial"] = ObjectValue(
                "session-outer-initial",
                outerClass.id,
                new Dictionary<string, string>
                {
                    ["Nested"] = "session-outer-initial-nested",
                    ["Observed"] = "session-outer-initial-observed",
                });
            data.values["session-outer-initial-nested"] = ObjectValue(
                "session-outer-initial-nested",
                nestedClass.id,
                new Dictionary<string, string>
                {
                    ["Computed"] = "session-outer-initial-computed",
                });
            data.values["session-outer-initial-computed"] =
                new NumberMemberValue
                {
                    id = "session-outer-initial-computed",
                    value = 0,
                };
            data.values["session-outer-initial-observed"] =
                new NumberMemberValue
                {
                    id = "session-outer-initial-observed",
                    value = 0,
                };
            var saveRoot = (ObjectMemberValue)data.values["value-save"];
            saveRoot.value!["Outer"] = outer.id;
            saveRoot.value["Inner"] = inner.id;
            ((ObjectMemberValue)data.values["value-session"]).value!["Outer"] =
                "session-outer-initial";

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);

            Assert.AreEqual(
                1d,
                client.save.Get<NeoMemberClassWritable>("Outer")
                    .Get<NeoMemberIntWritable>("Observed").value!.value);
            Assert.AreEqual(
                7d,
                client.save.Get<NeoMemberClassWritable>("Outer")
                    .Get<NeoMemberClassWritable>("Nested")
                    .Get<NeoMemberIntWritable>("Computed").value!.value);
            Assert.AreEqual(
                1d,
                client.save.Get<NeoMemberClassWritable>("Inner")
                    .Get<NeoMemberIntWritable>("Result").value!.value);
            Assert.AreEqual(
                "session-outer-initial",
                client.session.Get<NeoMemberClassWritable>("Outer").value!.id);
        }

        [Test]
        public void SparseInitializerReadsALaterSharedCatalogOverrideOfAnAbstractGetter()
        {
            ProjectData data = BuildProjectData();
            var baseClass = SchemaClass("catalog-base", "CatalogBase", NeoMemberStorage.Immutable);
            baseClass.schema["Down"] = "catalog-down-getter";
            var concrete = SchemaClass("catalog-concrete", "CatalogConcrete", NeoMemberStorage.Immutable);
            concrete.extendsClassId = baseClass.id;
            concrete.schema["Down"] = "catalog-down-value";
            data.classes[baseClass.id] = baseClass;
            data.classes[concrete.id] = concrete;
            data.members["catalog-down-getter"] = new NSPropertyMember
            {
                id = "catalog-down-getter", projectId = "p75-project", name = "Down", kind = MemberKind.NSProperty,
                Modifier = NeoMemberModifierKind.Abstract, returnTypeInfo = IntTypeInfo(),
            };
            data.members["catalog-down-value"] = new IntMember
            {
                id = "catalog-down-value", projectId = "p75-project", name = "Down", kind = MemberKind.Int,
                extendsMemberId = "catalog-down-getter", Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new NumberMemberValueBase { value = 42 },
            };
            data.members["catalog-member"] = new ClassMember
            {
                id = "catalog-member", projectId = "p75-project", name = "Catalog", kind = MemberKind.Class,
                classId = baseClass.id, Requirement = NeoMemberRequirementKind.Required, Storage = NeoMemberStorage.Immutable,
            };
            data.classes["assets-root-class"].schema["Catalog"] = "catalog-member";
            var catalog = ObjectValue("zz-catalog", concrete.id);
            catalog.instanceConstructorId = null;
            catalog.constructorArgs = new Dictionary<string, JToken?>();
            data.values[catalog.id] = catalog;
            ((ObjectMemberValue)data.values["value-assets"]).value!["Catalog"] = catalog.id;
            ((IntMember)data.members["thing-count"]).defaultValue = new NumberMemberValueBase
            {
                init = new InitializerBody
                {
                    code = "root.Assets.Catalog.Down",
                    compiled = new FunctionWithReturnType
                    {
                        compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                        parameters = new[] { ConstructorVariable("__this__", ClassType("thing-class")), ConstructorVariable("__root__", ClassType("__root__")) },
                        typeInfo = IntTypeInfo(),
                        instructions = new Instruction[]
                        {
                            new ReturnInstruction
                            {
                                type = InstructionKind.Return,
                                pointer = new CallGetterPointer
                                {
                                    type = PointerKind.CallGetter, memberId = "catalog-down-getter",
                                    receiver = CallReceiver.Instance(PointerKeyOf(PointerKeyOf(RootPointer(), "Assets"), "Catalog")),
                                },
                            },
                        },
                    },
                },
            };
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            Assert.AreEqual(42d, client.save.Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberIntWritable>("Count").value!.value);
        }

        [Test]
        public void ConstructingWithARowBackedArgumentRecordsItsIdNotItsContents()
        {
            // P75 §4 — creation data is a recipe, so a row-backed argument is
            // recorded as the row it names. Every argument reaches the stamp
            // already marshalled into the evaluator's own record shape, which
            // carries no id of its own; serializing that shape stores the
            // row's CONTENTS instead, and replay then rebuilds the instance
            // from a payload map rather than from the row the caller passed.
            ProjectData data = BuildProjectData();
            var holderClass = SchemaClass(
                "holder-class", "Holder", NeoMemberStorage.Session);
            holderClass.constructorIds = new[] { "holder-ctor" };
            data.classes[holderClass.id] = holderClass;
            var argument = new FunctionArgumentTypeInfo
            {
                name = "held",
                type = MemberKind.Class,
                classId = "thing-class",
                required = true,
            };
            data.constructors["holder-ctor"] = new ConstructorRecord
            {
                id = "holder-ctor",
                projectId = "p75-project",
                classId = holderClass.id,
                argumentTypes = new[] { argument },
                action = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = new[]
                    {
                        ConstructorVariable("__this__", ClassType(holderClass.id)),
                        ConstructorVariable("__root__", ClassType("save-root-class")),
                        ConstructorVariable("__arg_0__", argument),
                    },
                    typeInfo = new PrimitiveTypeInfo
                    {
                        type = MemberKind.Null,
                        required = true,
                    },
                    instructions = Array.Empty<Instruction>(),
                },
            };

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            using var held = new HeldThingValue(
                client, client.save.Get<NeoMemberClassWritable>("Thing"));
            NeoMemberClassWritable holder =
                NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
                    client,
                    holderClass.id,
                    "holder-ctor",
                    new[] { new NeoDeclaredConstructorArgument("held", held) });

            JToken? recorded = holder.value!.constructorArgs!["__arg_0__"];
            Assert.AreEqual(JTokenType.String, recorded!.Type);
            Assert.AreEqual("thing-instance", recorded.Value<string>());
        }

        [TestCase(false)]
        [TestCase(true)]
        [TestCase(false, true)]
        [TestCase(false, false, MemberKind.List)]
        [TestCase(false, false, MemberKind.Dictionary)]
        [TestCase(false, false, MemberKind.Class, true)]
        public void SavingConstructorOnlyArgumentsPreservesSharedDependencies(
            bool clone, bool trackedPatch = false, MemberKind argumentKind = MemberKind.Class, bool detachClone = false)
        {
            var data = BuildProjectData();
            data.classes["thing-class"].allowedStorage = NeoMemberStorage.Inherit;
            data.members["transient-count"] = new IntMember
            {
                id = "transient-count", name = "Transient", kind = MemberKind.Int,
                Storage = NeoMemberStorage.Session,
                defaultValue = new NumberMemberValueBase { value = 0 },
            };
            data.classes["thing-class"].schema["Transient"] = "transient-count";
            data.members["config-item"] = new StringMember { id = "config-item", name = "Item", kind = MemberKind.String };
            data.members["config-items"] = new ListMember
            {
                id = "config-items", name = "Items", kind = MemberKind.List, ListKind = NeoListKind.Unordered,
                entryMemberId = "config-item", defaultValue = new ArrayMemberValueBase { value = Array.Empty<string>() },
            };
            data.classes["thing-class"].schema["Items"] = "config-items";
            var holderClass = new NeoSchemaClass
            {
                id = "recipe-holder", name = "RecipeHolder", projectId = "p75-project",
                schema = new Dictionary<string, string>(), constructorIds = new[] { "recipe-ctor" },
            };
            data.classes[holderClass.id] = holderClass;
            var argument = new FunctionArgumentTypeInfo
            {
                name = "config", type = argumentKind, classId = "thing-class", required = true,
                entryTypeInfo = argumentKind == MemberKind.Class ? null : ClassType("thing-class"),
            };
            var literal = new FunctionArgumentTypeInfo { name = "text", type = MemberKind.String, required = true };
            data.constructors["recipe-ctor"] = new ConstructorRecord
            {
                id = "recipe-ctor", projectId = "p75-project", classId = holderClass.id,
                argumentTypes = new[] { argument, literal },
                action = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = new[]
                    {
                        ConstructorVariable("__this__", ClassType(holderClass.id)),
                        ConstructorVariable("__root__", ClassType("save-root-class")),
                        ConstructorVariable("__arg_0__", argument),
                        ConstructorVariable("__arg_1__", literal),
                    },
                    typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                    instructions = Array.Empty<Instruction>(),
                },
            };
            foreach (var key in new[] { "First", "Second" })
            {
                data.members[key] = new ClassMember { id = key, kind = MemberKind.Class, classId = holderClass.id, name = key };
                data.classes["save-root-class"].schema[key] = key;
            }
            string saved, configId, argumentId;
            data.metadata = new ProjectExportMetadata
            {
                schemaVersion = NeoProjectExportContract.CurrentSchemaVersion,
                projectId = data.project.id, versionId = "unit-test-version",
            };
            data.internalRecordRelations = new Dictionary<string, InternalRecordRelation>();
            data.variantFolders = new Dictionary<string, VariantFolderRecord>();
            var stack = trackedPatch ? NeoTestSaveStack.Create(JObject.FromObject(data).ToString()) : null;
            using (var client = stack?.Load() ?? NeoTestSaveStack.ClientFromSchema(data))
            {
                var baseline = (JObject)JObject.Parse(client.SerializeSaveData())["values"]!;
                using var config = NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
                    client, "thing-class", null, Array.Empty<NeoDeclaredConstructorArgument>());
                configId = config.value!.id;
                argumentId = argumentKind == MemberKind.Class ? configId : "config-collection";
                config.Get<NeoMemberIntWritable>("Count").Set(42);
                config.Get<NeoMemberIntWritable>("Transient").Set(9);
                client.SetWritableValue(NeoValueOwnership.Session, new StringMemberValue
                {
                    id = "retained-list-entry", value = "unordered input",
                    containerId = config.Get<NeoMemberListWritable>("Items").value!.id,
                });
                using var reference = new HeldThingValue(client, config);
                if (argumentKind == MemberKind.List)
                    client.SetWritableValue(NeoValueOwnership.Session,
                        new ArrayMemberValue { id = argumentId, value = new[] { configId } });
                else if (argumentKind == MemberKind.Dictionary)
                    client.SetWritableValue(NeoValueOwnership.Session,
                        new ObjectMemberValue { id = argumentId, value = new Dictionary<string, string> { ["entry"] = configId } });
                client.SetWritableValue(NeoValueOwnership.Session, new StringMemberValue { id = "literal-row", value = "not a dependency" });
                var root = ObjectValue("value-save", "save-root-class", new Dictionary<string, string> { ["Thing"] = "thing-instance" });
                foreach (var key in new[] { "First", "Second" })
                {
                    using var holder = argumentKind == MemberKind.Class
                        ? NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(client, holderClass.id, "recipe-ctor",
                            new[] { new NeoDeclaredConstructorArgument("config", reference), new NeoDeclaredConstructorArgument("text", "literal-row") })
                        : null;
                    string holderId = holder?.value!.id ?? key + "-holder";
                    if (holder is null)
                    {
                        var recipe = ObjectValue(holderId, holderClass.id, new Dictionary<string, string>());
                        recipe.instanceConstructorId = "recipe-ctor";
                        recipe.constructorArgs = new Dictionary<string, JToken?>
                        {
                            ["__arg_0__"] = argumentId, ["__arg_1__"] = "literal-row",
                        };
                        client.SetWritableValue(NeoValueOwnership.Session, recipe);
                    }
                    string id = clone
                        ? client.CloneOwnedValueReferenceForNewParent(NeoValueOwnership.Save, NeoValueOwnership.Session, holderId, data.members[key])
                        : client.ImportValueReference(NeoValueOwnership.Save, holderId, out _);
                    root.value![key] = id;
                    Assert.AreEqual(argumentId, client.saveValues[id].constructorArgs!["__arg_0__"]!.Value<string>());
                }
                client.SetSaveValue(root);
                if (detachClone)
                {
                    using var storedClient = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: client.SerializeSaveData());
                    var storedRoot = (ObjectMemberValue)storedClient.CloneRowForWrite(storedClient.save.value!);
                    string detached = storedClient.CloneValueReference(storedRoot.value!["First"], NeoValueOwnership.Save);
                    storedRoot.value.Remove("First"); storedRoot.value.Remove("Second");
                    storedClient.SetSaveValue(storedRoot);
                    storedClient.RunGarbageCollector();
                    Assert.IsFalse(storedClient.saveValues.ContainsKey(configId), "Detached Session dependencies must not keep unrelated Save rows alive.");
                    Assert.IsTrue(storedClient.TryGetValue(NeoValueOwnership.Session, configId, out ObjectMemberValue? retained));
                    Assert.IsTrue(storedClient.TryGetValue(NeoValueOwnership.Session, retained!.value!["Count"], out NumberMemberValue? retainedCount));
                    Assert.AreEqual(42, retainedCount!.value);
                    Assert.DoesNotThrow(() => storedClient.CloneValueReference(detached, NeoValueOwnership.Session));
                    return;
                }
                client.RunGarbageCollector();
                Assert.IsTrue(client.saveValues.ContainsKey(configId), "Constructor-only inputs must survive without an owning field.");
                Assert.IsTrue(client.saveValues.ContainsKey("retained-list-entry"), "Unordered inputs need their container memberships retained.");
                Assert.IsFalse(client.saveValues.ContainsKey("literal-row"), "String arguments remain literals.");
                saved = client.SerializeSaveData();
                Assert.IsNull(JObject.Parse(saved)["values"]![configId]!["value"]!["Transient"]);
                Assert.AreEqual(9, config.Get<NeoMemberIntWritable>("Transient").value!.value,
                    "Serialization must not reset live Session state.");
                if (stack is not null)
                {
                    var build = typeof(NeoSaveSynchronizer).GetMethod("BuildPendingPatch",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                    var patch = (NeoSavePatch)build.Invoke(stack.Synchronizer, new object?[]
                    {
                        baseline, JObject.Parse(saved)["values"], null,
                        new Dictionary<string, string?>(), new Dictionary<string, string?>(), true,
                    })!;
                    Assert.IsTrue(patch.changes.OfType<GameSaveValueReplaceChange>().Any(change => change.valueId == configId),
                        "Tracked live patches must include retained constructor inputs.");
                    string countId = ((ObjectMemberValue)client.saveValues[configId]).value!["Count"];
                    Assert.IsTrue(patch.changes.OfType<GameSaveValueReplaceChange>().Any(change => change.valueId == countId));
                }
                root.value!.Remove("First");
                client.SetSaveValue(root);
                client.RunGarbageCollector();
                Assert.IsTrue(client.saveValues.ContainsKey(configId), "Removing one recipe must not delete another recipe's shared input.");
            }
            using var reopened = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: saved);
            foreach (var key in new[] { "First", "Second" })
                Assert.AreEqual(argumentId, reopened.save.Get<NeoMemberClassWritable>(key).value!.constructorArgs!["__arg_0__"]!.Value<string>());
            Assert.IsTrue(reopened.TryGetValue(configId, out ObjectMemberValue? storedConfig));
            Assert.IsTrue(reopened.TryGetValue(storedConfig!.value!["Count"], out NumberMemberValue? count));
            Assert.AreEqual(42, count!.value);
            using var restored = new NeoMemberClassWritable(reopened,
                new ClassMember { id = "restored-config", kind = MemberKind.Class, classId = "thing-class" }, configId, NeoValueOwnership.Save);
            Assert.AreEqual(0, restored.Get<NeoMemberIntWritable>("Transient").value!.value);
            Assert.IsTrue(reopened.TryGetValue("retained-list-entry", out StringMemberValue? listEntry));
            Assert.AreEqual("unordered input", listEntry!.value);
        }

        [Test]
        public void SavingGenericConstructorArgumentRetainsItsResolvedClassInput()
        {
            var data = BuildGenericConstructorProjectData();
            string saved;
            using (var client = NeoTestSaveStack.ClientFromSchema(data))
            {
                client.SetWritableValue(NeoValueOwnership.Session, new StringMemberValue { id = "runtime-name", value = "retained" });
                client.SetWritableValue(NeoValueOwnership.Session,
                    ObjectValue("runtime-payload", "payload-class", new Dictionary<string, string> { ["Name"] = "runtime-name" }));
                var root = (ObjectMemberValue)client.CloneRowForWrite(data.values["thing-instance"]);
                root.constructorArgs!["__arg_0__"] = "runtime-payload";
                client.SetSaveValue(root);
                client.RunGarbageCollector();
                saved = client.SerializeSaveData();
            }
            using var reopened = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: saved);
            Assert.AreEqual("runtime-payload", reopened.save.Get<NeoMemberClassWritable>("Thing")
                .value!.constructorArgs!["__arg_0__"]!.Value<string>());
            Assert.IsTrue(reopened.TryGetValue("runtime-name", out StringMemberValue? name));
            Assert.AreEqual("retained", name!.value);
        }

        private sealed class HeldThingValue : NeoGeneratedClassValue
        {
            internal HeldThingValue(NeoClient client, NeoMemberClassWritable node)
                : base(
                    client,
                    node,
                    "thing-class",
                    isReadOnly: false,
                    inheritedStorageOwnership: NeoValueOwnership.Save)
            {
            }
        }

        [Test]
        public void SparseConstructorDependencyCycleStillFailsClosed()
        {
            ProjectData data = BuildGenericConstructorProjectData();
            AddSparsePayloadSibling(data);
            var sibling = (ObjectMemberValue)data.values["zz-payload"];
            sibling.instanceConstructorId = "payload-ctor";
            sibling.constructorArgs = new Dictionary<string, JToken?> { ["__arg_0__"] = "thing-instance" };
            var argument = new FunctionArgumentTypeInfo { name = "Other", type = MemberKind.Class, classId = "thing-class", required = true };
            data.classes["payload-class"].constructorIds = new[] { "payload-ctor" };
            data.constructors["payload-ctor"] = new ConstructorRecord
            {
                id = "payload-ctor", projectId = "p75-project", classId = "payload-class", argumentTypes = new[] { argument },
                action = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = new[] { ConstructorVariable("__this__", ClassType("payload-class")), ConstructorVariable("__root__", ClassType("save-root-class")), ConstructorVariable("__arg_0__", argument) },
                    typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true }, instructions = Array.Empty<Instruction>(),
                },
            };
            Exception error = Assert.Throws<InvalidOperationException>(() => NeoTestSaveStack.ClientFromSchema(data))!;
            StringAssert.Contains("Sparse constructor dependency cycle", error.ToString());
        }

        [Test]
        public void ArgumentReadOfTheCurrentlyReplayingRootUsesItsPartialThis()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(NeoClient).GetField("isReplayingVirtualInstance", flags)!.SetValue(client, true);
            typeof(NeoClient).GetField("replayingVirtualInstanceRootId", flags)!.SetValue(client, "thing-instance");
            ((HashSet<string>)typeof(NeoClient).GetField("replayingVirtualRootIds", flags)!.GetValue(client)!).Add("thing-instance");
            ((Dictionary<string, HashSet<string>>)typeof(NeoClient).GetField("virtualValueIdsByRoot", flags)!.GetValue(client)!).Remove("thing-instance");
            Assert.DoesNotThrow(() => typeof(NeoClient).GetMethod("EnsureVirtualReplayArgumentReady", flags)!.Invoke(client, new object[] { "thing-instance" }));
        }

        [Test]
        public void NestedSparseRootWhoseIdSortsFirstRetainsItsOwnPlacementIndex()
        {
            ProjectData data = BuildNestedProjectData();
            var nested = ObjectValue("aaa-nested", "nested-class");
            nested.instanceConstructorId = null;
            nested.constructorArgs = new Dictionary<string, JToken?>();
            data.values[nested.id] = nested;
            ((ObjectMemberValue)data.values["thing-instance"]).value!["Nested"] = nested.id;
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            var count = client.save.Get<NeoMemberClassWritable>("Thing").Get<NeoMemberClassWritable>("Nested")
                .Get<NeoMemberClassWritable>("Deep").Get<NeoMemberIntWritable>("Count");
            Assert.AreEqual(5d, count.value!.value);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var placements = (System.Collections.IDictionary)typeof(NeoClient).GetField("virtualClassPlacementByChildId", flags)!.GetValue(client)!;
            object placement = placements[count.value.id]!;
            Assert.AreEqual(nested.id, placement.GetType().GetField("rootId", flags | System.Reflection.BindingFlags.Public)!.GetValue(placement));
        }

        private static void AddSparsePayloadSibling(ProjectData data)
        {
            data.classes["save-root-class"].schema["ZPayload"] = "sibling-payload";
            data.members["sibling-payload"] = new ClassMember
            {
                id = "sibling-payload", projectId = "p75-project", name = "ZPayload", kind = MemberKind.Class,
                classId = "payload-class", Requirement = NeoMemberRequirementKind.Required, Storage = NeoMemberStorage.Save,
            };
            var sibling = ObjectValue("zz-payload", "payload-class");
            sibling.instanceConstructorId = null;
            sibling.constructorArgs = new Dictionary<string, JToken?>();
            data.values[sibling.id] = sibling;
            ((ObjectMemberValue)data.values["value-save"]).value!["ZPayload"] = sibling.id;
            ((ObjectMemberValue)data.values["thing-instance"]).constructorArgs!["__arg_0__"] = sibling.id;
        }

        [TestCase(NeoValueOwnership.Save, false)]
        [TestCase(NeoValueOwnership.Save, true)]
        [TestCase(NeoValueOwnership.Session, false)]
        [TestCase(NeoValueOwnership.Session, true)]
        public void FirstLeafOverrideDoesNotReplayUnrelatedDefaults(NeoValueOwnership storage, bool selection)
        {
            ProjectData data = BuildProjectData();
            data.classes["save-root-class"].schema.Clear();
            ((ObjectMemberValue)data.values["value-save"]).value!.Clear();
            data.classes["assets-root-class"].schema["Thing"] = "thing-member";
            ((ObjectMemberValue)data.values["value-assets"]).value!["Thing"] = "thing-instance";
            data.members["thing-member"].Storage = NeoMemberStorage.Immutable;
            data.classes["thing-class"].allowedStorage = NeoMemberStorage.Immutable;
            data.classes["thing-class"].schema["Other"] = "other-member";
            data.members["other-member"] = new IntMember
            {
                id = "other-member", name = "Other", kind = MemberKind.Int,
                defaultValue = new NumberMemberValueBase { value = 7 },
            };
            if (selection)
            {
                data.enums["choices"] = new NeoCompose.Runtime.Json.Enum
                {
                    id = "choices", name = "Choices", projectId = "p75-project",
                    options = new Dictionary<string, EnumOption>
                    {
                        ["one"] = new EnumOption { text = "One" },
                        ["two"] = new EnumOption { text = "Two" },
                    },
                };
                data.members["thing-count"] = new EnumMember
                {
                    id = "thing-count", name = "Count", kind = MemberKind.Enum, enumId = "choices",
                    defaultValue = new ArrayMemberValueBase { value = new[] { "one" } },
                };
            }
            if (!selection)
            {
                var observer = SchemaClass("observer-class", "Observer", NeoMemberStorage.Save);
                observer.schema["Copied"] = "copied-member";
                data.classes[observer.id] = observer;
                data.classes["save-root-class"].schema["Observer"] = "observer-member";
                data.members["observer-member"] = new ClassMember
                {
                    id = "observer-member", name = "Observer", kind = MemberKind.Class,
                    classId = observer.id, Storage = NeoMemberStorage.Save,
                };
                data.members["copied-member"] = new IntMember
                {
                    id = "copied-member", name = "Copied", kind = MemberKind.Int,
                    defaultValue = new NumberMemberValueBase
                    {
                        init = new InitializerBody
                        {
                            // Exact-row references follow the effective overlay.
                            // An Assets-root read intentionally stays Asset-scoped.
                            code = "",
                            compiled = new FunctionWithReturnType
                            {
                                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                                parameters = new[]
                                {
                                    ConstructorVariable("__this__", ClassType(observer.id)),
                                    ConstructorVariable("__root__", ClassType("__root__")),
                                },
                                typeInfo = IntTypeInfo(),
                                instructions = new Instruction[]
                                {
                                    new ReturnInstruction
                                    {
                                        type = InstructionKind.Return,
                                        pointer = new ReferencePointer
                                        {
                                            type = PointerKind.Reference, valueId = "authored-count",
                                        },
                                    },
                                },
                            },
                        },
                    },
                };
                var observerValue = ObjectValue("observer-instance", observer.id);
                observerValue.instanceConstructorId = null;
                observerValue.constructorArgs = new Dictionary<string, JToken?>();
                data.values[observerValue.id] = observerValue;
                ((ObjectMemberValue)data.values["value-save"]).value!["Observer"] = observerValue.id;
            }
            // The writable view inherits its destination store while the
            // authored leaf remains Asset-owned until its first overlay.
            data.values["authored-count"] = selection
                ? new ArrayMemberValue { id = "authored-count", value = new[] { "one" } }
                : new NumberMemberValue { id = "authored-count", value = 5 };
            ((ObjectMemberValue)data.values["thing-instance"]).value!["Count"] = "authored-count";
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            var thing = client.assets.Get<NeoMemberClass>("Thing");
            string changedId = thing.Get<NeoMember>("Count").value!.id;
            string otherId = thing.Get<NeoMember>("Other").value!.id;
            Assert.IsTrue(client.TryGetValueOwnership(changedId, out var beforeOwnership));
            Assert.AreEqual(NeoValueOwnership.Asset, beforeOwnership, "The first write must cross the ownership boundary.");
            Assert.IsTrue(client.TryGetValue(otherId, out MemberValue? otherBefore));
            NeoMemberIntWritable? copied = selection ? null : client.save
                .Get<NeoMemberClassWritable>("Observer").Get<NeoMemberIntWritable>("Copied");
            if (copied is not null) Assert.AreEqual(5, copied.value!.value);
            var writable = thing.AsWritableView(storage);
            if (selection) writable.Get<NeoMemberEnumWritable>("Count").Set(new[] { "two" });
            else writable.Get<NeoMemberIntWritable>("Count").Set(9);
            Assert.IsTrue(client.TryGetValueOwnership(changedId, out var afterOwnership));
            Assert.AreEqual(storage, afterOwnership);
            Assert.IsTrue(client.TryGetValue(otherId, out MemberValue? otherAfter));
            Assert.AreSame(otherBefore, otherAfter, "A leaf override must reuse the unrelated constructed default.");
            if (copied is not null) Assert.AreEqual(9, copied.value!.value, "A constructor that copies the changed leaf must replay.");
            if (selection) CollectionAssert.AreEqual(new[] { "two" }, writable.Get<NeoMemberEnumWritable>("Count").value!.value);
            else Assert.AreEqual(9, writable.Get<NeoMemberIntWritable>("Count").value!.value);
        }

        [Test]
        public void SparseInstanceTracksDefaultAndWritesAtStableVirtualId()
        {
            using NeoClient first = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            NeoMemberClassWritable firstThing =
                first.save.Get<NeoMemberClassWritable>("Thing");
            NeoMemberIntWritable firstCount =
                firstThing.Get<NeoMemberIntWritable>("Count");

            Assert.AreEqual(5d, firstCount.value!.value);
            string virtualId = firstCount.value.id;
            Assert.AreEqual("35f55577-5ef0-5bf5-861b-070aa19817f5", virtualId);

            firstCount.Set(9);

            Assert.AreEqual(virtualId, firstCount.value!.id);
            Assert.AreEqual(9d, firstCount.value.value);
            Assert.AreEqual(virtualId, first.saveValues[virtualId].id);
            // The authored root stays sparse: runtime writes shadow the leaf
            // directly and do not materialize a parent spine into the save.
            Assert.AreEqual(1, first.saveValues.Count);

            using NeoClient second = NeoTestSaveStack.ClientFromSchema(BuildProjectData(7));
            NeoMemberIntWritable secondCount = second.save
                .Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberIntWritable>("Count");
            Assert.AreEqual(7d, secondCount.value!.value);
            Assert.AreEqual(virtualId, secondCount.value.id);
        }

        [Test]
        public void SparseImplicitInstanceReplaysItsPlacementMembersAuthoredDefault()
        {
            ProjectData data = BuildProjectData(defaultCount: 1);
            var placement = (ClassMember)data.members["thing-member"];
            placement.defaultValue = new ObjectMemberValueBase
            {
                classId = "thing-class",
                value = new Dictionary<string, string>
                {
                    ["Count"] = "placement-count",
                },
            };
            data.values["placement-count"] = new NumberMemberValue
            {
                id = "placement-count",
                value = 5,
            };

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);

            Assert.AreEqual(
                5d,
                client.save
                    .Get<NeoMemberClassWritable>("Thing")
                    .Get<NeoMemberIntWritable>("Count")
                    .value!.value,
                "An implicit construction pair delegates content to the placement declaration, not bare new C().");
        }

        [Test]
        public void OptionalLiteralDefaultIsMaterializedDuringConstruction()
        {
            ProjectData data = BuildProjectData();
            data.members["thing-count"].Requirement = NeoMemberRequirementKind.Optional;
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            Assert.AreEqual(5d, client.save.Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberIntWritable>("Count").value!.value);
        }

        [Test]
        public void SparseGenericInstanceUsesItsStoredStampForAggregateArguments()
        {
            ProjectData data = BuildGenericConstructorProjectData();

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoMemberClassWritable payload = client.save
                .Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberClassWritable>("Payload");

            Assert.AreEqual(
                "from generic default",
                payload.Get<NeoMemberStringWritable>("Name").value!.value);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReadOnlyValidationResolvesGenericConstructorChildrenBeforeRuntimeStoresExist(bool placementOnly)
        {
            ProjectData data = BuildGenericConstructorProjectData();
            if (placementOnly)
            {
                UsePlacementGenericBinding(data);
                data.values["constructor-payload"].classId = null;
            }
            ConstructorRecord constructor = data.constructors["thing-ctor"];
            ((GenericMember)data.members["thing-payload"]).defaultValue = new NullMemberValueBase
            {
                init = ReturnVariableInitializer("Payload", constructor.argumentTypes[0],
                    constructor.action!.parameters, "__arg_0__"),
            };
            data.classes["thing-class"].schema["ReadOnly"] = "read-only";
            data.members["read-only"] = new IntMember
            {
                id = "read-only",
                projectId = "p75-project",
                name = "ReadOnly",
                kind = MemberKind.Int,
                Storage = NeoMemberStorage.Immutable,
                Mutability = NeoMemberMutabilityKind.ReadOnly,
                defaultValue = new NumberMemberValueBase { value = 1 },
            };

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);

            Assert.AreEqual("from constructor argument", client.save
                .Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberClassWritable>("Payload")
                .Get<NeoMemberStringWritable>("Name").value!.value);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GenericLookupInitializerKeepsTheSelectionWrapperAndCatalogOwnership(bool placementOnly)
        {
            ProjectData data = BuildGenericConstructorProjectData();
            if (placementOnly) UsePlacementGenericBinding(data);
            data.classes["payload-class"].allowedStorage = NeoMemberStorage.Immutable;
            data.members["catalog-entry"] = new ClassMember
            {
                id = "catalog-entry", projectId = "p75-project", name = "CatalogEntry",
                kind = MemberKind.Class, classId = "payload-class",
                Requirement = NeoMemberRequirementKind.Required,
                Storage = NeoMemberStorage.Immutable,
            };
            data.members["catalog"] = new ListMember
            {
                id = "catalog", projectId = "p75-project", name = "Catalog",
                kind = MemberKind.List, entryMemberId = "catalog-entry",
                ListKind = NeoListKind.Ordered, Requirement = NeoMemberRequirementKind.Required,
                Storage = NeoMemberStorage.Immutable, valueId = "catalog-values",
            };
            data.values["catalog-values"] = new ArrayMemberValue
            {
                id = "catalog-values", value = new[] { "constructor-payload" },
            };
            data.classes["assets-root-class"].schema["Catalog"] = "catalog";
            ((ObjectMemberValue)data.values["value-assets"]).value!["Catalog"] = "catalog-values";
            data.members["payload-binding"] = new LookupMember
            {
                id = "payload-binding", projectId = "p75-project", name = "PayloadBinding",
                kind = MemberKind.Lookup, collectionMemberId = "catalog",
                collectionValueId = "catalog-values", Selection = NeoMemberSelectionKind.Single,
                Requirement = NeoMemberRequirementKind.Required,
            };
            ConstructorRecord constructor = data.constructors["thing-ctor"];
            var argument = new FunctionArgumentTypeInfo
            {
                name = "InitialItem", type = MemberKind.Class,
                classId = "payload-class", required = true,
            };
            constructor.argumentTypes = new[] { argument };
            constructor.action!.parameters[2].typeInfo = argument;
            ((GenericMember)data.members["thing-payload"]).defaultValue = new NullMemberValueBase
            {
                init = ReturnVariableInitializer("InitialItem", argument,
                    constructor.action.parameters, "__arg_0__"),
            };

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            var root = (ObjectMemberValue)data.values["thing-instance"];
            Assert.IsFalse(root.value!.ContainsKey("Payload"));
            MemberValue? resolved = client.ResolveClassChildRow(root, "Payload");
            Assert.IsInstanceOf<ArrayMemberValue>(resolved);
            CollectionAssert.AreEqual(new[] { "constructor-payload" }, ((ArrayMemberValue)resolved!).value);
            Assert.AreNotEqual("constructor-payload", resolved.id);
            CollectionAssert.AreEqual(new[] { "constructor-payload" }, client.save
                .Get<NeoMemberClassWritable>("Thing").Get<NeoMemberLookup>("Payload").Selected());
            Assert.IsTrue(client.TryGetValueOwnership("constructor-payload", out NeoValueOwnership ownership));
            Assert.AreEqual(NeoValueOwnership.Asset, ownership);
            Assert.IsFalse(client.TryFindOwnedParent(NeoValueOwnership.Save, "constructor-payload", out _));
        }

        [Test]
        public void OmittedNestedClassWaitsForItsComputedDefaultDuringRootReplay()
        {
            ProjectData data = BuildNestedProjectData();
            var type = new PrimitiveTypeInfo { type = MemberKind.Int, required = true };
            ((IntMember)data.members["deep-count"]).defaultValue = new NumberMemberValueBase
            {
                init = new InitializerBody
                {
                    code = "5",
                    compiled = new FunctionWithReturnType
                    {
                        compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                        parameters = Array.Empty<Variable>(), typeInfo = type,
                        instructions = new Instruction[] { new ReturnInstruction
                        {
                            type = InstructionKind.Return,
                            pointer = new ValuePointer { type = PointerKind.Value,
                                value = new Value { typeInfo = type, value = JToken.FromObject(5) } },
                        } },
                    },
                },
            };
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            Assert.AreEqual(5, client.save.Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberClassWritable>("Nested").Get<NeoMemberClassWritable>("Deep")
                .Get<NeoMemberIntWritable>("Count").value!.value);
        }

        [Test]
        public void NonVirtualSparseRowDoesNotDeferAComputedInitializer()
        {
            ProjectData data = BuildProjectData();
            var plainClass = SchemaClass(
                "plain-class",
                "Plain",
                NeoMemberStorage.Save);
            plainClass.schema["Count"] = "plain-count";
            data.classes[plainClass.id] = plainClass;
            data.members["plain-member"] = new ClassMember
            {
                id = "plain-member",
                projectId = "p75-project",
                name = "Plain",
                kind = MemberKind.Class,
                classId = plainClass.id,
                Requirement = NeoMemberRequirementKind.Required,
            };
            var countType = new PrimitiveTypeInfo
            {
                type = MemberKind.Int,
                required = true,
            };
            data.members["plain-count"] = new IntMember
            {
                id = "plain-count",
                projectId = "p75-project",
                name = "Count",
                kind = MemberKind.Int,
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new NumberMemberValueBase
                {
                    init = new InitializerBody
                    {
                        code = "5",
                        compiled = new FunctionWithReturnType
                        {
                            compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                            parameters = Array.Empty<Variable>(),
                            typeInfo = countType,
                            instructions = new Instruction[]
                            {
                                new ReturnInstruction
                                {
                                    type = InstructionKind.Return,
                                    pointer = new ValuePointer
                                    {
                                        type = PointerKind.Value,
                                        value = new Value
                                        {
                                            typeInfo = countType,
                                            value = JToken.FromObject(5),
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            };
            data.classes["save-root-class"].schema["Plain"] = "plain-member";
            data.values["plain-value"] = ObjectValue("plain-value", plainClass.id);
            ((ObjectMemberValue)data.values["value-save"]).value!["Plain"] = "plain-value";

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => NeoTestSaveStack.ClientFromSchema(data))!;
            StringAssert.Contains(
                "has a computed default and cannot be materialized as a literal",
                error.ToString());
        }

        [Test]
        public void ParentIndexInfersOnlyConflictingArraysAndKeepsLookupNonOwnership()
        {
            ProjectData data = BuildProjectData();
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            const int arrayCount = 2048;
            for (int index = 0; index < arrayCount; index++)
            {
                string id = $"unplaced-array-{index}";
                data.values[id] = new ArrayMemberValue
                {
                    id = id,
                    value = new[] { $"unplaced-child-{index}" },
                };
            }

            var flags = System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic;
            var build = typeof(NeoClient).GetMethod(
                "BuildParentByValueId",
                flags)!;
            var stopwatch = Stopwatch.StartNew();
            var parents = (Dictionary<string, string>)build.Invoke(
                client,
                new object[] { data.values.Values })!;
            stopwatch.Stop();

            Assert.AreEqual(
                arrayCount,
                parents.Count(pair => pair.Key.StartsWith(
                    "unplaced-child-",
                    StringComparison.Ordinal)));
            TestContext.WriteLine(
                $"Indexed {arrayCount} uncontested arrays in {stopwatch.Elapsed.TotalMilliseconds:F3} ms.");

            data.members["proof-entry"] = new StringMember
            {
                id = "proof-entry",
                projectId = "p75-project",
                name = "ProofEntry",
                kind = MemberKind.String,
                Requirement = NeoMemberRequirementKind.Required,
            };

            void AddList(string memberId, string valueId)
            {
                data.members[memberId] = new ListMember
                {
                    id = memberId,
                    projectId = "p75-project",
                    name = memberId,
                    kind = MemberKind.List,
                    valueId = valueId,
                    entryMemberId = "proof-entry",
                    ListKind = NeoListKind.Ordered,
                    Requirement = NeoMemberRequirementKind.Required,
                };
            }
            void AddLookup(string memberId, string valueId, string listMemberId)
            {
                data.members[memberId] = new LookupMember
                {
                    id = memberId,
                    projectId = "p75-project",
                    name = memberId,
                    kind = MemberKind.Lookup,
                    valueId = valueId,
                    collectionMemberId = listMemberId,
                    Selection = NeoMemberSelectionKind.Multi,
                    Requirement = NeoMemberRequirementKind.Required,
                };
            }
            void AddArray(string valueId, string childId) =>
                data.values[valueId] = new ArrayMemberValue
                {
                    id = valueId,
                    value = new[] { childId },
                };

            AddList("list-after", "list-after-values");
            AddLookup("lookup-before", "lookup-before-values", "list-after");
            AddArray("lookup-before-values", "lookup-first-child");
            AddArray("list-after-values", "lookup-first-child");
            AddList("list-before", "list-before-values");
            AddLookup("lookup-after", "lookup-after-values", "list-before");
            AddArray("list-before-values", "list-first-child");
            AddArray("lookup-after-values", "list-first-child");
            client.InvalidateSchemaResolutionCaches();

            stopwatch.Restart();
            parents = (Dictionary<string, string>)build.Invoke(
                client,
                new object[] { data.values.Values })!;
            stopwatch.Stop();
            Assert.AreEqual("list-after-values", parents["lookup-first-child"]);
            Assert.AreEqual("list-before-values", parents["list-first-child"]);
            TestContext.WriteLine(
                $"Indexed {arrayCount} arrays plus both Lookup/List orderings in {stopwatch.Elapsed.TotalMilliseconds:F3} ms.");
        }

        [TestCase(42d, false)]
        [TestCase(null, false)]
        [TestCase(42d, true)]
        [TestCase(null, true)]
        public void SparseComputedLeafWaitsForConstructorReplay(double? expected, bool immutable)
        {
            ProjectData data = BuildProjectData();
            var argument = new FunctionArgumentTypeInfo
            {
                name = "InitialCount",
                type = MemberKind.Int,
                required = expected.HasValue,
            };
            var parameters = new[]
            {
                ConstructorVariable("__this__", ClassType("thing-class")),
                ConstructorVariable("__root__", ClassType("save-root-class")),
                ConstructorVariable("__arg_0__", argument),
            };
            data.classes["thing-class"].constructorIds = new[] { "thing-ctor" };
            data.constructors["thing-ctor"] = new ConstructorRecord
            {
                id = "thing-ctor",
                projectId = "p75-project",
                classId = "thing-class",
                argumentTypes = new[] { argument },
                action = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = parameters,
                    typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                    instructions = Array.Empty<Instruction>(),
                },
            };
            data.members["thing-count"].Requirement = expected.HasValue
                ? NeoMemberRequirementKind.Required : NeoMemberRequirementKind.Optional;
            ((IntMember)data.members["thing-count"]).defaultValue = new NumberMemberValueBase
            {
                init = ReturnVariableInitializer("InitialCount", argument, parameters, "__arg_0__"),
            };
            var root = (ObjectMemberValue)data.values["thing-instance"];
            root.instanceConstructorId = "thing-ctor";
            root.constructorArgs = new Dictionary<string, JToken?> { ["__arg_0__"] = expected.HasValue ? JToken.FromObject(expected.Value) : JValue.CreateNull() };

            if (immutable) data.members["thing-count"].Storage = NeoMemberStorage.Immutable;
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);

            var count = client.save.Get<NeoMemberClassWritable>("Thing").Get<NeoMemberInt>("Count");
            Assert.AreEqual(expected, count.value!.value);
            if (immutable) Assert.IsNotInstanceOf<NeoMemberIntWritable>(count);
        }

        [Test]
        public void NullReferencePayloadDoesNotBecomeAContainmentEdge()
        {
            ProjectData data = BuildProjectData();
            data.values["member-reference"] = new ObjectMemberValue
            {
                id = "member-reference",
                value = new Dictionary<string, string>
                {
                    ["memberId"] = "thing-count",
                    ["valueId"] = null!,
                },
            };

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);

            Assert.AreEqual(5d, client.save.Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberIntWritable>("Count").value!.value);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void GenericInitializerRetainsItsPlacementWhenConstructedAndImported(bool computed)
        {
            ProjectData data = BuildGenericConstructorProjectData();
            var literalDefault = ((GenericMember)data.members["thing-payload"]).defaultValue;
            const string parameterId = "payload-param";
            data.classes["payload-class"].genericParams = new List<GenericParamDeclaration>
            {
                new() { id = parameterId, name = "T" },
            };
            data.members["payload-name"] = new GenericMember
            {
                id = "payload-name",
                name = "Name",
                kind = MemberKind.Generic,
                genericParamId = parameterId,
            };
            var binding = (ClassMember)data.members["payload-binding"];
            binding.classArguments = new Dictionary<string, GenericBinding>
            {
                [parameterId] = new()
                {
                    kind = NeoGenericBindingKind.Member,
                    memberId = "placement-string-binding",
                },
            };
            ((ObjectMemberValue)data.values["constructor-payload"]).genericBindings =
                new Dictionary<string, string> { [parameterId] = "placement-string-binding" };
            var stringType = new PrimitiveTypeInfo { type = MemberKind.String, required = true };
            var payloadType = ClassType("payload-class");
            payloadType.typeArguments = new Dictionary<string, TypeInfo> { [parameterId] = stringType };
            ((GenericMember)data.members["thing-payload"]).defaultValue = new NullMemberValueBase
            {
                init = new InitializerBody
                {
                    code = "new Payload<string>() { Name = \"initialized\" }",
                    compiled = new FunctionWithReturnType
                    {
                        compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                        parameters = Array.Empty<Variable>(),
                        typeInfo = payloadType,
                        instructions = new Instruction[]
                        {
                            new ReturnInstruction
                            {
                                type = InstructionKind.Return,
                                pointer = new FunctionPointer
                                {
                                    type = PointerKind.Function,
                                    function = new DeclaredConstructorFunction
                                    {
                                        type = FunctionKind.DeclaredConstructor,
                                        info = new DeclaredConstructorInfo
                                        {
                                            schemaClassInfo = payloadType,
                                            constructorId = null,
                                            args = Array.Empty<DeclaredConstructorArgument>(),
                                            fields = new[]
                                            {
                                                new FunctionClassConstructorField
                                                {
                                                    schemaKey = "Name", memberId = "payload-name",
                                                    valuePointer = new ValuePointer
                                                    {
                                                        type = PointerKind.Value,
                                                        value = new Value { typeInfo = stringType, value = "initialized" },
                                                    },
                                                },
                                            },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            };

            if (!computed) ((GenericMember)data.members["thing-payload"]).defaultValue = literalDefault;

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);

            Assert.AreEqual(computed ? "initialized" : "from generic default", client.save.Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberClassWritable>("Payload").Get<NeoMemberStringWritable>("Name").value!.value);
            var payload = client.save.Get<NeoMemberClassWritable>("Thing").Get<NeoMemberClassWritable>("Payload");
            using var projected = NeoGeneratedTypesSupport.ReadRequiredNSPropertyClass<NeoMemberClass>(
                client, NSGetterEvaluator.UnwrapRow(payload.value!, new NSGetterEvaluator.Context(client, null, null), NeoValueOwnership.Save), true, (_, node) => node, (_, node) => node);
            Assert.AreEqual(computed ? "initialized" : "from generic default",
                projected.Get<NeoMemberStringWritable>("Name").value!.value);
            // Runtime construction also checks completeness, unlike sparse replay.
            var fields = new[] { new NeoGeneratedTypesSupport.RuntimeConstructorField
                { schemaKey = "Name", memberId = "payload-name", value = "runtime" } };
            var resolved = NeoGeneratedTypesSupport.ResolveDeclaredConstructor(client, payloadType,
                null, Array.Empty<string>(), fields, binding.classArguments);
            var constructed = NeoGeneratedTypesSupport.ConstructDeclaredClassValue(resolved,
                new Dictionary<string, object?>(), fields, new NSGetterEvaluator.Context(client, null, null));
            Assert.AreEqual("runtime", constructed.Get<NeoMemberStringWritable>("Name").value!.value);

        }

        [Test]
        public void AuthoredImmutableInstanceReplaysWithoutAllowingRuntimeConstruction()
        {
            ProjectData data = BuildProjectData();
            data.classes["thing-class"].allowedStorage = NeoMemberStorage.Immutable;
            data.classes["save-root-class"].schema.Remove("Thing");
            data.classes["assets-root-class"].schema["Thing"] = "thing-member";
            data.members["thing-member"].Storage = NeoMemberStorage.Immutable;
            ((ObjectMemberValue)data.values["value-save"]).value!.Remove("Thing");
            ((ObjectMemberValue)data.values["value-assets"]).value!["Thing"] = "thing-instance";

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);

            Assert.AreEqual(5d, client.assets.Get<NeoMemberClass>("Thing")
                .Get<NeoMemberInt>("Count").value!.value);
            Assert.Throws<InvalidOperationException>(() =>
                NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(client, "thing-class", null,
                    Array.Empty<NeoDeclaredConstructorArgument>(), Array.Empty<NeoGeneratedConstructorValue>()));
        }

        [Test]
        public void SaveScopedReadFallsThroughToAnAssetOwnedVirtualRow()
        {
            // Assets underlie every graph, so a Save- or Session-scoped read
            // that misses its own store falls through to them. A virtual row
            // has to fall through exactly like the authored row it stands in
            // for — an NSProperty getter invoked with Save ownership over an
            // asset-owned instance reads its receiver through this path.
            ProjectData data = BuildProjectData();
            data.classes["thing-class"].allowedStorage = NeoMemberStorage.Immutable;
            data.classes["save-root-class"].schema.Remove("Thing");
            data.classes["assets-root-class"].schema["Thing"] = "thing-member";
            data.members["thing-member"].Storage = NeoMemberStorage.Immutable;
            ((ObjectMemberValue)data.values["value-save"]).value!.Remove("Thing");
            ((ObjectMemberValue)data.values["value-assets"]).value!["Thing"] = "thing-instance";

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            string virtualId = client.assets.Get<NeoMemberClass>("Thing")
                .Get<NeoMemberInt>("Count").value!.id;
            Assert.IsFalse(client.values.ContainsKey(virtualId),
                "The leaf has to be virtual for this to test anything.");

            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Asset, virtualId, out MemberValue? asAsset));
            Assert.AreEqual(virtualId, asAsset!.id);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save, virtualId, out MemberValue? asSave));
            Assert.AreEqual(virtualId, asSave!.id);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session, virtualId, out MemberValue? asSession));
            Assert.AreEqual(virtualId, asSession!.id);
        }

        [Test]
        public void AssetScopedReadStillRefusesASaveOwnedVirtualRow()
        {
            // The reverse never falls through: assets must not resolve through
            // a writable graph the caller asked to stay out of.
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            string virtualId = client.save.Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberIntWritable>("Count").value!.id;
            Assert.IsFalse(client.values.ContainsKey(virtualId));

            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save, virtualId, out MemberValue? asSave));
            Assert.AreEqual(virtualId, asSave!.id);
            Assert.IsFalse(client.TryGetValue(
                NeoValueOwnership.Asset, virtualId, out MemberValue? _));
        }

        [Test]
        public void VariantReferenceClonePreservesLookupIdentityWithoutAliasing()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var source = new VariantMemberValue
            {
                id = "variant-ref",
                value = new VariantRefValue { classId = "target", variantId = "variant", rowValueId = "row" },
            };

            var clone = (VariantMemberValue)client.CloneRowForWrite(source);

            Assert.AreEqual("target", clone.value!.classId);
            Assert.AreEqual("variant", clone.value.variantId);
            Assert.AreEqual("row", clone.value.rowValueId);
            clone.value.rowValueId = "changed";
            Assert.AreEqual("row", source.value.rowValueId);
        }

        [Test]
        public void NullOptionalClassDoesNotMaterializeItsComputedChildren()
        {
            ProjectData data = BuildProjectData();
            var placement = (ClassMember)data.members["thing-member"];
            placement.Requirement = NeoMemberRequirementKind.Optional;
            placement.defaultValue = new ObjectMemberValueBase { value = null };
            ((ObjectMemberValue)data.values["value-save"]).value!.Remove("Thing");
            data.values.Remove("thing-instance");
            ((IntMember)data.members["thing-count"]).defaultValue = new NumberMemberValueBase
            {
                init = ReturnVariableInitializer("InitialCount",
                    new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                    Array.Empty<Variable>(), "__arg_0__"),
            };

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);

            NeoMemberClassWritable thing = client.save.Get<NeoMemberClassWritable>("Thing");
            Assert.IsNull(thing.value!.value);
            Assert.IsFalse(thing.TryGet<NeoMemberIntWritable>("Count", out _));
        }

        [Test]
        public void ReplayTakesAbstractSlotFromTheStoredConcreteChild()
        {
            ProjectData data = BuildNestedProjectData();
            data.classes["nested-class"].Modifier = NeoClassModifierKind.Abstract;
            ((ClassMember)data.members["thing-nested"]).defaultValue = null;
            NeoSchemaClass concrete = SchemaClass("concrete-class", "Concrete", NeoMemberStorage.Save);
            concrete.extendsClassId = "nested-class";
            data.classes[concrete.id] = concrete;
            data.values["concrete-child"] = ObjectValue("concrete-child", concrete.id);
            ((ObjectMemberValue)data.values["thing-instance"]).value!["Nested"] = "concrete-child";

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);

            NeoMemberClassWritable child = client.save.Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberClassWritable>("Nested");
            Assert.AreEqual("concrete-class", child.value!.classId);
            Assert.AreEqual(5d, child.Get<NeoMemberClassWritable>("Deep")
                .Get<NeoMemberIntWritable>("Count").value!.value);
        }

        [Test]
        public void SparseSpineRowsAtVirtualIdsPreserveDeeperOverrides()
        {
            string nestedId;
            string deepId;
            string countId;
            using (NeoClient probe = NeoTestSaveStack.ClientFromSchema(
                BuildNestedProjectData()))
            {
                NeoMemberClassWritable nested = probe.save
                    .Get<NeoMemberClassWritable>("Thing")
                    .Get<NeoMemberClassWritable>("Nested");
                NeoMemberClassWritable deep = nested
                    .Get<NeoMemberClassWritable>("Deep");
                nestedId = nested.value!.id;
                deepId = deep.value!.id;
                countId = deep.Get<NeoMemberIntWritable>("Count").value!.id;
            }

            ProjectData data = BuildNestedProjectData();
            // Web sparse writes keep Class spine rows at their virtual ids,
            // without reattaching them to each ancestor body.
            data.values[nestedId] = ObjectValue(nestedId, "nested-class");
            data.values[deepId] = ObjectValue(deepId, "deep-class");
            data.values[countId] = new NumberMemberValue
            {
                id = countId,
                value = 91,
            };

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoMemberClassWritable nestedValue = client.save
                .Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberClassWritable>("Nested");
            NeoMemberClassWritable deepValue = nestedValue
                .Get<NeoMemberClassWritable>("Deep");

            Assert.AreEqual(nestedId, nestedValue.value!.id);
            Assert.AreEqual(deepId, deepValue.value!.id);
            Assert.AreEqual(
                91d,
                deepValue.Get<NeoMemberIntWritable>("Count").value!.value);
        }

        [Test]
        public void PersistedNestedSparseRootAtItsVirtualIdReplaysAfterItsParent()
        {
            string nestedId;
            string deepId;
            string saved;
            using (NeoClient first = NeoTestSaveStack.ClientFromSchema(
                BuildNestedProjectData()))
            {
                NeoMemberClassWritable nested = first.save
                    .Get<NeoMemberClassWritable>("Thing")
                    .Get<NeoMemberClassWritable>("Nested");
                nestedId = nested.value!.id;
                deepId = nested.Get<NeoMemberClassWritable>("Deep").value!.id;
                // Make Base a real selection change so the public ToVariant
                // seam persists the virtual receiver instead of returning early.
                nested.value.instanceVariantId = "previous-variant";
                var generated = new SparseNestedValue(first, nested);

                NeoGeneratedTypesSupport.ApplyVariant(
                    generated,
                    NeoGeneratedTypesSupport.ResolveBaseVariant<SparseNestedValue>(
                        first,
                        "nested-class"));

                Assert.IsTrue(first.saveValues.ContainsKey(nestedId));
                Assert.IsFalse(first.saveValues.ContainsKey("thing-instance"),
                    "ToVariant writes the nested stable id without materializing its sparse parent link.");
                saved = first.SerializeSaveData();
            }

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(
                BuildNestedProjectData(),
                loadedSaveContent: saved);

            NeoMemberClassWritable nestedValue = client.save
                .Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberClassWritable>("Nested");
            Assert.AreEqual(nestedId, nestedValue.value!.id);
            Assert.AreEqual(deepId, nestedValue.Get<NeoMemberClassWritable>("Deep").value!.id);
            Assert.IsTrue(client.TryGetValue<ObjectMemberValue>(deepId, out var restoredDefault));
            Assert.AreEqual(deepId, restoredDefault!.id);
            Assert.IsFalse(client.saveValues.ContainsKey(deepId), "Reconstructed defaults remain virtual.");
            Assert.AreEqual(
                5d,
                nestedValue
                    .Get<NeoMemberClassWritable>("Deep")
                    .Get<NeoMemberIntWritable>("Count")
                    .value!.value);
        }

        private sealed class SparseNestedValue : NeoGeneratedClassValue
        {
            internal SparseNestedValue(
                NeoClient client,
                NeoMemberClassWritable node)
                : base(
                    client,
                    node,
                    "nested-class",
                    isReadOnly: false,
                    inheritedStorageOwnership: NeoValueOwnership.Save)
            {
            }

            internal static SparseNestedValue CreateWritable(
                NeoClient client,
                NeoMemberClassWritable node)
            {
                return new SparseNestedValue(client, node);
            }
        }

        [Test]
        public void NestedAggregateConstructorArgumentUsesDurableVirtualId()
        {
            const string ExternalArgumentId = "external-deep";
            const string UuidLookingLiteral =
                "12345678-1234-1234-1234-123456789abc";
            string nestedId;
            string deepId;
            string saved;
            using (NeoClient first = NeoTestSaveStack.ClientFromSchema(
                BuildNestedAggregateConstructorProjectData()))
            {
                NeoMemberClassWritable nested = first.save
                    .Get<NeoMemberClassWritable>("Thing")
                    .Get<NeoMemberClassWritable>("Holder")
                    .Get<NeoMemberClassWritable>("Nested");
                nestedId = nested.value!.id;
                deepId = nested.Get<NeoMemberClassWritable>("Deep").value!.id;
                string replayArgumentId = nested.value
                    .constructorArgs!["__arg_0__"]!.Value<string>();
                Assert.AreEqual(
                    deepId,
                    replayArgumentId,
                    "the virtual nested root must not retain its discarded Session constructor argument id");
                AssertAggregateReferenceArguments(
                    nested.value,
                    ExternalArgumentId,
                    UuidLookingLiteral);
                Assert.AreEqual(
                    nested.Get<NeoMemberList>("Items").value!.id,
                    nested.value.constructorArgs["__arg_3__"]!.Value<string>());
                Assert.AreEqual(
                    nested.Get<NeoMemberDictionary>("Labels").value!.id,
                    nested.value.constructorArgs["__arg_4__"]!.Value<string>());
                nested.value.instanceVariantId = "previous-variant";
                var generated = new SparseNestedValue(first, nested);

                Assert.DoesNotThrow(() => NeoGeneratedTypesSupport.ApplyVariant(
                    generated,
                    NeoGeneratedTypesSupport.ResolveBaseVariant<SparseNestedValue>(
                        first,
                        "nested-class")));

                var written = (ObjectMemberValue)first.saveValues[nestedId];
                Assert.AreEqual(
                    deepId,
                    written.constructorArgs!["__arg_0__"]!.Value<string>());
                AssertAggregateReferenceArguments(
                    written,
                    ExternalArgumentId,
                    UuidLookingLiteral);
                // Match the persisted sparse shape: construction-equal
                // collection fields are omitted, so replay must attach the
                // recorded aggregate arguments rather than read stored body
                // links that happen to mask a copied CLR value.
                written.value!.Remove("Items");
                written.value.Remove("Labels");
                first.RefreshVirtualInstanceVariant(
                    nested,
                    NeoValueOwnership.Save);
                Assert.AreEqual(
                    "item value",
                    ((NeoMemberStringWritable)nested
                        .Get<NeoMemberList>("Items")
                        .Single()).value!.value);
                Assert.AreEqual(
                    "label value",
                    ((NeoMemberStringWritable)((NeoMemberList)nested
                        .Get<NeoMemberDictionary>("Labels")
                        .Single().Value).Single()).value!.value);
                saved = first.SerializeSaveData();
            }

            using NeoClient second = NeoTestSaveStack.ClientFromSchema(
                BuildNestedAggregateConstructorProjectData(),
                loadedSaveContent: saved);
            NeoMemberClassWritable reloaded = second.save
                .Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberClassWritable>("Holder")
                .Get<NeoMemberClassWritable>("Nested");
            Assert.AreEqual(nestedId, reloaded.value!.id);
            AssertAggregateReferenceArguments(
                reloaded.value,
                ExternalArgumentId,
                UuidLookingLiteral);
            Assert.AreEqual(
                "item value",
                ((NeoMemberStringWritable)reloaded
                    .Get<NeoMemberList>("Items")
                    .Single()).value!.value);
            Assert.AreEqual(
                "label value",
                ((NeoMemberStringWritable)((NeoMemberList)reloaded
                    .Get<NeoMemberDictionary>("Labels")
                    .Single().Value).Single()).value!.value);
            Assert.AreEqual(
                5d,
                reloaded
                    .Get<NeoMemberClassWritable>("Deep")
                    .Get<NeoMemberIntWritable>("Count")
                    .value!.value);
        }

        [Test]
        public void SystemRootPropagatesItsNamespaceToVirtualChildren()
        {
            ProjectData data = BuildProjectData();
            ObjectMemberValue root = (ObjectMemberValue)data.values["thing-instance"];
            data.values.Remove(root.id);
            root.id = "system_thing-instance";
            data.values[root.id] = root;
            ((ObjectMemberValue)data.values["value-save"]).value!["Thing"] = root.id;

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            string countId = client.save
                .Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberIntWritable>("Count")
                .value!.id;

            Assert.AreEqual(
                "system_35f55577-5ef0-5bf5-861b-070aa19817f5",
                countId);
        }

        [Test]
        public void ExternalSaveApplyRebuildsSparseVirtualSpines()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(
                BuildNestedProjectData());
            NeoMemberClassWritable thing = client.save
                .Get<NeoMemberClassWritable>("Thing");
            NeoMemberClassWritable nested = thing
                .Get<NeoMemberClassWritable>("Nested");
            NeoMemberClassWritable deep = nested
                .Get<NeoMemberClassWritable>("Deep");
            string nestedId = nested.value!.id;
            string deepId = deep.value!.id;
            string countId = deep.Get<NeoMemberIntWritable>("Count").value!.id;

            JObject incoming = JObject.Parse(client.SerializeSaveData());
            var values = (JObject)incoming["values"]!;
            values[nestedId] = JObject.FromObject(ObjectValue(
                nestedId,
                "nested-class"));
            values[deepId] = JObject.FromObject(ObjectValue(
                deepId,
                "deep-class"));
            values[countId] = JObject.FromObject(new NumberMemberValue
            {
                id = countId,
                value = 73,
            });

            client.ApplyExternalSaveContent(incoming.ToString());

            Assert.AreEqual(
                73d,
                thing
                    .Get<NeoMemberClassWritable>("Nested")
                    .Get<NeoMemberClassWritable>("Deep")
                    .Get<NeoMemberIntWritable>("Count")
                    .value!.value);
        }

        [Test]
        public void ResolveValueRowReadsVirtualValuesForUntypedConsumers()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            NeoMemberIntWritable count = client.save
                .Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberIntWritable>("Count");

            NumberMemberValue row = (NumberMemberValue)client.ResolveValueRow(
                count.value!.id)!;

            Assert.AreEqual(5d, row.value);
            Assert.AreEqual(count.value.id, row.id);
        }

        [Test]
        public void VirtualUnorderedListRegistersItsEntriesAndContainment()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(
                BuildUnorderedListProjectData());
            NeoMemberList items = client.save
                .Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberList>("Items");

            string[] entryIds = items
                .Select(item => item.value!.id)
                .OrderBy(id => id, System.StringComparer.Ordinal)
                .ToArray();

            Assert.AreEqual(2, entryIds.Length);
            CollectionAssert.AreEqual(
                entryIds,
                client.GetUnorderedListEntryIds(items.value!.id).ToArray());
            foreach (string entryId in entryIds)
            {
                Assert.IsTrue(client.TryResolveContainerIdForValueId(
                    entryId,
                    out string? containerId));
                Assert.AreEqual(items.value.id, containerId);
                Assert.IsInstanceOf<StringMemberValue>(
                    client.ResolveValueRow(entryId));
            }
        }

        [Test]
        public void UnsetTombstonesAnOmittedMemberOnASparseRoot()
        {
            ProjectData data = BuildProjectData();
            ((IntMember)data.members["thing-count"]).DeclaredRequirement = NeoMemberRequirementKind.Optional;
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoMemberClassWritable thing =
                client.save.Get<NeoMemberClassWritable>("Thing");
            string virtualId = thing.Get<NeoMemberIntWritable>("Count").value!.id;

            thing.Unset("Count");

            // The omitted member is virtual, so its stable id lives only in the
            // instance index. Reading presence off the sparse body alone makes
            // Unset — and therefore every generated `property = null` — a
            // silent no-op on a constructed instance.
            Assert.IsTrue(
                client.saveValues.TryGetValue(virtualId, out MemberValue? row),
                "Unset must tombstone the omitted member at its virtual id.");
            Assert.IsTrue(row!.IsRemoved);
            Assert.IsTrue(thing.TryGet("Count", out NeoMemberIntWritable? refetched));
            Assert.IsNull(refetched!.value);
        }

        [Test]
        public void AssigningAnOmittedUnorderedListKeepsItsVirtualId()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(
                BuildUnorderedListProjectData());
            NeoMemberClassWritable thing =
                client.save.Get<NeoMemberClassWritable>("Thing");
            string virtualListId = thing.Get<NeoMemberList>("Items").value!.id;

            NeoGeneratedTypesSupport.SetValue(
                thing,
                "Items",
                NeoValueWritePayload.FromValue(new[] { "thing-item-a" }));

            // The unordered whole-list assignment bypasses the virtual-child
            // lookup the scalar path uses, so it minted a fresh random id and
            // materialized the spine. Both runtimes must land the same write at
            // the same deterministic id, and the root must stay sparse.
            Assert.AreEqual(
                virtualListId,
                thing.Get<NeoMemberList>("Items").value!.id);
            Assert.IsFalse(thing.value!.value!.ContainsKey("Items"));
        }

        // A NeoScript member write is an ordinary instance write: the value it
        // names is its own instance whether or not it has been materialized
        // yet, and writing it is what materializes it. The write target
        // therefore resolves the bound child body-then-virtual, exactly like
        // `SetSerializedValue`, and lands at the deterministic virtual id the
        // web writes to. The variant-Apply pin path was taught the same layer
        // in the same change — see P67VariantIRTests'
        // VariantApply_ReappliedClosureWriteToAnOmittedMemberClearsItsPin.
        [Test]
        public void NeoScriptAssignmentWritesAnOmittedMemberAtItsVirtualId()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            string virtualId = client.save
                .Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberIntWritable>("Count")
                .value!.id;

            var ctx = new NSGetterEvaluator.Context(client, null, null);
            Dictionary<string, object?> root = NeoScriptRuntimeRoot(client, ctx);
            ctx = ctx.WithRoot(root);
            var action = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = System.Array.Empty<Variable>(),
                typeInfo = new PrimitiveTypeInfo
                {
                    type = MemberKind.Null,
                    required = true,
                },
                instructions = new Instruction[]
                {
                    new AssignInstruction
                    {
                        type = InstructionKind.Assign,
                        target = new WriteTarget
                        {
                            pointer = PointerKeyOf(
                                PointerKeyOf(
                                    PointerKeyOf(RootPointer(), "Save"),
                                    "Thing"),
                                "Count"),
                            typeInfo = IntTypeInfo(),
                            writability = WritabilityKind.Runtime,
                        },
                        operatorValue = "=",
                        pointer = IntLiteral(9),
                    },
                },
            };

            NeoScriptExecutor.Execute(
                client,
                action,
                new Dictionary<string, object?> { ["__root__"] = root },
                ctx);

            // Body-only resolution made the write mint a fresh random id and
            // materialize the spine instead of shadowing the deterministic
            // virtual id both runtimes agree on.
            NeoMemberIntWritable count = client.save
                .Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberIntWritable>("Count");
            Assert.AreEqual(virtualId, count.value!.id);
            Assert.AreEqual(9d, count.value.value);
            // Only the member that changed materializes; the root stays sparse.
            Assert.IsFalse(
                client.save.Get<NeoMemberClassWritable>("Thing")
                    .value!.value!.ContainsKey("Count"),
                "a write to one omitted member must not materialize the key on the root");
        }

        /// <summary>
        /// P62 §3.2 x P75 §6 — <c>action += listener</c> reads the current
        /// listener set through the write target before writing the merged
        /// set back. On a sparse root the action member is omitted, so a
        /// body-only read answers null and the merged set silently DROPS every
        /// listener the construction installed; the subscription then lands at
        /// a fresh random id instead of the action's deterministic virtual one.
        /// </summary>
        [Test]
        public void NeoScriptActionSubscriptionKeepsAnOmittedMembersConstructedListeners()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(
                BuildActionProjectData());
            NeoMemberClassWritable thing =
                client.save.Get<NeoMemberClassWritable>("Thing");
            string virtualActionId = thing.Get<NeoMemberAction>("OnPing").value!.id;
            var untouchedCount = thing.Get<NeoMemberIntWritable>("Count").value;

            var ctx = new NSGetterEvaluator.Context(client, null, null);
            Dictionary<string, object?> root = NeoScriptRuntimeRoot(client, ctx);
            ctx = ctx.WithRoot(root);
            var body = new FunctionWithReturnType
            {
                parameters = System.Array.Empty<Variable>(),
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                typeInfo = new VoidTypeInfo
                {
                    type = MemberKind.Void,
                    required = true,
                },
                instructions = new Instruction[]
                {
                    new AddActionListenerInstruction
                    {
                        type = InstructionKind.AddActionListener,
                        target = new WriteTarget
                        {
                            pointer = PointerKeyOf(
                                PointerKeyOf(
                                    PointerKeyOf(RootPointer(), "Save"),
                                    "Thing"),
                                "OnPing"),
                            typeInfo = ActionTypeInfo(),
                            writability = WritabilityKind.Save,
                        },
                        listener = ListenerPointer("thing-late"),
                    },
                },
            };

            NeoScriptExecutor.Execute(
                client,
                body,
                new Dictionary<string, object?> { ["__root__"] = root },
                ctx);

            Assert.AreSame(untouchedCount, thing.Get<NeoMemberIntWritable>("Count").value,
                "Changing listeners must retain unrelated constructor defaults.");
            NeoMemberAction subscribed = client.save
                .Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberAction>("OnPing");
            Assert.AreEqual(
                virtualActionId,
                subscribed.value!.id,
                "the subscription must land at the action's deterministic virtual id");
            CollectionAssert.AreEqual(
                new[] { "thing-early", "thing-late" },
                subscribed.value.value!.listeners
                    .Select(listener => listener.memberId)
                    .ToArray(),
                "`+=` must compose with the constructor-installed listener set");
        }

        [Test]
        public void SparseReplayFillsAnOmittedDefaultedConstructorArgument()
        {
            ProjectData data = BuildProjectData();
            ObjectMemberValue root = (ObjectMemberValue)data.values["thing-instance"];
            root.instanceConstructorId = "thing-ctor";
            root.constructorArgs = new Dictionary<string, JToken?>();
            data.classes["thing-class"].constructorIds = new[] { "thing-ctor" };
            var optional = new FunctionArgumentTypeInfo
            {
                name = "Label",
                type = MemberKind.String,
                required = true,
                defaultValue = new ParameterDefaultValue { value = "default" },
            };
            data.constructors = new Dictionary<string, ConstructorRecord>
            {
                ["thing-ctor"] = new ConstructorRecord
                {
                    id = "thing-ctor",
                    projectId = "p75-project",
                    classId = "thing-class",
                    argumentTypes = new[] { optional },
                    action = new FunctionWithReturnType
                    {
                        compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                        parameters = new[]
                        {
                            new Variable
                            {
                                id = "__this__",
                                typeInfo = new ClassTypeInfo
                                {
                                    type = MemberKind.Class,
                                    required = true,
                                    classId = "thing-class",
                                },
                            },
                            new Variable
                            {
                                id = "__root__",
                                typeInfo = new ClassTypeInfo
                                {
                                    type = MemberKind.Class,
                                    required = true,
                                    classId = "save-root-class",
                                },
                            },
                            new Variable { id = "__arg_0__", typeInfo = optional },
                        },
                        typeInfo = new PrimitiveTypeInfo
                        {
                            type = MemberKind.Null,
                            required = true,
                        },
                        instructions = System.Array.Empty<Instruction>(),
                    },
                },
            };

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);

            Assert.AreEqual(
                5d,
                client.save
                    .Get<NeoMemberClassWritable>("Thing")
                    .Get<NeoMemberIntWritable>("Count")
                    .value!.value);
        }

        [Test]
        public void ConstructorProvenanceRoundTripsExplicitImplicitNull()
        {
            const string json = @"{
  'id':'instance',
  'classId':'thing-class',
  'value':{},
  'constructorArgs':{},
  'instanceConstructorId':null,
  'createdAt':'2026-08-22T00:00:00.000Z',
  'updatedAt':'2026-08-22T00:00:00.000Z'
}";
            MemberValue row = Newtonsoft.Json.JsonConvert
                .DeserializeObject<MemberValue>(json)!;

            Assert.IsTrue(row.hasInstanceConstructorId);
            Assert.IsNull(row.instanceConstructorId);
            StringAssert.Contains(
                "\"instanceConstructorId\":null",
                Newtonsoft.Json.JsonConvert.SerializeObject(row));
        }

        [Test]
        public void DetachedAuthoredSparseRootDoesNotExecuteItsConstructor()
        {
            ProjectData data = BuildProjectData();
            ObjectMemberValue orphan = ObjectValue("orphan-instance", "thing-class");
            orphan.constructorArgs = new Dictionary<string, JToken?>();
            orphan.instanceConstructorId = null;
            data.values[orphan.id] = orphan;

            orphan.instanceConstructorId = "must-not-execute";
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            Assert.AreEqual(5d, client.save.Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberIntWritable>("Count").value!.value);
        }

        [Test]
        public void RuntimeConstructionStampsTheCanonicalProvenancePair()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());

            NeoMemberClassWritable constructed =
                NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
                    client,
                    "thing-class",
                    constructorId: null,
                    Array.Empty<NeoDeclaredConstructorArgument>());

            ObjectMemberValue row = constructed.value!;
            Assert.IsTrue(
                row.hasInstanceConstructorId,
                "An implicitly constructed row must carry an EXPLICIT null constructor id.");
            Assert.IsNull(row.instanceConstructorId);
            Assert.IsNotNull(row.constructorArgs);
            Assert.AreEqual(0, row.constructorArgs!.Count);
            Assert.IsTrue(NeoClient.IsVirtualInstanceRoot(row));
            StringAssert.Contains(
                "\"instanceConstructorId\":null",
                Newtonsoft.Json.JsonConvert.SerializeObject(row));
        }

        [Test]
        public void ConstructorArgsWithoutAConstructorStampFailClosed()
        {
            Newtonsoft.Json.JsonSerializationException error = Assert.Throws<
                Newtonsoft.Json.JsonSerializationException>(() =>
                Newtonsoft.Json.JsonConvert.DeserializeObject<ObjectMemberValue>(@"{
  'id':'thing-instance',
  'classId':'thing-class',
  'value':{},
  'constructorArgs':{},
  'createdAt':'2026-08-22T00:00:00.000Z',
  'updatedAt':'2026-08-22T00:00:00.000Z'
}".Replace('\'', '"'))) !;

            StringAssert.Contains(
                "without a constructor or variant discriminator",
                error.Message);
        }

        [Test]
        public void ConstructorStampWithoutArgumentsFailsClosed()
        {
            Newtonsoft.Json.JsonSerializationException error = Assert.Throws<
                Newtonsoft.Json.JsonSerializationException>(() =>
                Newtonsoft.Json.JsonConvert.DeserializeObject<ObjectMemberValue>(@"{
  'id':'thing-instance',
  'classId':'thing-class',
  'value':{},
  'instanceConstructorId':'thing-ctor',
  'createdAt':'2026-08-22T00:00:00.000Z',
  'updatedAt':'2026-08-22T00:00:00.000Z'
}".Replace('\'', '"'))) !;

            StringAssert.Contains(
                "names a constructor without a 'constructorArgs' object",
                error.Message);
        }

        [Test]
        public void ExplicitImplicitConstructorNeedsNoArgumentMap()
        {
            ObjectMemberValue row = Newtonsoft.Json.JsonConvert
                .DeserializeObject<ObjectMemberValue>(@"{
  'id':'thing-instance',
  'classId':'thing-class',
  'value':{},
  'instanceConstructorId':null,
  'createdAt':'2026-08-22T00:00:00.000Z',
  'updatedAt':'2026-08-22T00:00:00.000Z'
}".Replace('\'', '"'))!;

            Assert.IsTrue(row.hasInstanceConstructorId);
            Assert.IsNull(row.instanceConstructorId);
            Assert.IsNull(row.constructorArgs);
        }

        [Test]
        public void VariantCanCarryArgumentsWithoutAConstructorStamp()
        {
            ObjectMemberValue row = Newtonsoft.Json.JsonConvert
                .DeserializeObject<ObjectMemberValue>(@"{
  'id':'thing-instance',
  'classId':'thing-class',
  'value':{},
  'constructorArgs':{'parameter-id':4},
  'instanceVariantId':'thing-variant',
  'createdAt':'2026-08-22T00:00:00.000Z',
  'updatedAt':'2026-08-22T00:00:00.000Z'
}".Replace('\'', '"'))!;

            Assert.AreEqual("thing-variant", row.instanceVariantId);
            Assert.AreEqual(4, row.constructorArgs!["parameter-id"]!.Value<int>());
        }

        [Test]
        public void VariantRowWithoutVariantFailsClosed()
        {
            Newtonsoft.Json.JsonSerializationException error = Assert.Throws<
                Newtonsoft.Json.JsonSerializationException>(() =>
                Newtonsoft.Json.JsonConvert.DeserializeObject<ObjectMemberValue>(@"{
  'id':'thing-instance',
  'classId':'thing-class',
  'value':{},
  'instanceVariantRowValueId':'thing-variant-row',
  'createdAt':'2026-08-22T00:00:00.000Z',
  'updatedAt':'2026-08-22T00:00:00.000Z'
}".Replace('\'', '"'))) !;

            StringAssert.Contains(
                "without 'instanceVariantId'",
                error.Message);
        }

        [Test]
        public void ExplicitParameterlessConstructorRejectsArguments()
        {
            Newtonsoft.Json.JsonSerializationException error = Assert.Throws<
                Newtonsoft.Json.JsonSerializationException>(() =>
                Newtonsoft.Json.JsonConvert.DeserializeObject<ObjectMemberValue>(@"{
  'id':'thing-instance',
  'classId':'thing-class',
  'value':{},
  'constructorArgs':{'parameter-id':4},
  'instanceConstructorId':null,
  'createdAt':'2026-08-22T00:00:00.000Z',
  'updatedAt':'2026-08-22T00:00:00.000Z'
}".Replace('\'', '"'))) !;

            StringAssert.Contains(
                "implicit parameterless constructor",
                error.Message);
        }

        [Test]
        public void ClearingAVariantToBaseKeepsTheInstanceExpanding()
        {
            ProjectData data = BuildProjectData();
            // A variant-only root: the web stamped the selected variant and
            // nothing else, so `instanceVariantId` is its ONLY eligibility
            // marker.
            var authored = (ObjectMemberValue)data.values["thing-instance"];
            data.values["thing-instance"] = ObjectValue(authored.id, authored.classId!);

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoMemberClassWritable thing = client.save
                .Get<NeoMemberClassWritable>("Thing");
            ((ObjectMemberValue)client.values["thing-instance"]).instanceVariantId =
                "thing-variant";

            client.StampVirtualInstanceVariant(
                thing,
                NeoValueOwnership.Save,
                variantId: null,
                rowValueId: null);

            NeoMemberIntWritable count = thing.Get<NeoMemberIntWritable>("Count");
            Assert.AreEqual(5d, count.value!.value);
            string virtualId = count.value.id;

            string content = client.SerializeSaveData();
            var written = (JObject)JObject.Parse(content)["values"]!["thing-instance"]!;
            Assert.IsTrue(
                written.TryGetValue("instanceConstructorId", out JToken? stampedId),
                "Clearing to Base must leave the row a valid P75 root.");
            Assert.AreEqual(JTokenType.Null, stampedId!.Type);
            Assert.IsNotNull(written["constructorArgs"]);

            client.ApplyExternalSaveContent(content);

            NeoMemberIntWritable reapplied = client.save
                .Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberIntWritable>("Count");
            Assert.AreEqual(5d, reapplied.value!.value);
            Assert.AreEqual(virtualId, reapplied.value.id);
        }

        [Test]
        public void NullClassAssignmentRemovesOwnedSparseChildrenBeforeSaveReload()
        {
            var data = BuildNestedProjectData();
            ((ClassMember)data.members["thing-member"]).Requirement = NeoMemberRequirementKind.Optional;
            string saved;
            using (var client = NeoTestSaveStack.ClientFromSchema(data))
            {
                var thing = client.save.Get<NeoMemberClassWritable>("Thing");
                var nested = thing.Get<NeoMemberClassWritable>("Nested");
                var nestedRow = (ObjectMemberValue)client.CloneRowForWrite(nested.value!);
                client.SetSaveValue(nestedRow);
                var thingRow = (ObjectMemberValue)client.CloneRowForWrite(thing.value!);
                thingRow.value!["Nested"] = nestedRow.id;
                client.SetSaveValue(thingRow);
                using var secondProjection = new NeoMemberClassWritable(client, nested.member, nestedRow.id, NeoValueOwnership.Save);
                var ctx = new NSGetterEvaluator.Context(client, null, null);
                var root = NeoScriptRuntimeRoot(client, ctx);
                ctx = ctx.WithRoot(root);
                var classType = new ClassTypeInfo { type = MemberKind.Class, classId = "thing-class", required = false };
                Assert.IsNotNull(NSGetterEvaluator.EvaluatePointer(
                    PointerKeyOf(PointerKeyOf(RootPointer(), "Save"), "Thing"),
                    new Dictionary<string, object?> { ["__root__"] = root }, ctx));
                NeoScriptExecutor.Execute(client, new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = Array.Empty<Variable>(),
                    typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                    instructions = new Instruction[] { new AssignInstruction
                    {
                        type = InstructionKind.Assign, operatorValue = "=",
                        target = new WriteTarget
                        {
                            pointer = PointerKeyOf(PointerKeyOf(RootPointer(), "Save"), "Thing"),
                            typeInfo = classType, writability = WritabilityKind.Save,
                        },
                        pointer = new ValuePointer { type = PointerKind.Value, value = new Value
                            { typeInfo = classType, value = JValue.CreateNull() } },
                    } },
                }, new Dictionary<string, object?> { ["__root__"] = root }, ctx);
                Assert.IsNull(NSGetterEvaluator.EvaluatePointer(
                    PointerKeyOf(PointerKeyOf(RootPointer(), "Save"), "Thing"),
                    new Dictionary<string, object?> { ["__root__"] = root }, ctx));
                Assert.IsFalse(client.saveValues.ContainsKey(nestedRow.id));
                Assert.IsTrue(nested.isDisposed, "Superseded cache entries may still have live subscribers.");
                Assert.IsTrue(secondProjection.isDisposed);
                saved = client.SerializeSaveData();
            }
            using var reopened = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: saved);
            Assert.IsNull(reopened.save.Get<NeoMemberClassWritable>("Thing").value?.value);
        }

        [Test]
        public void GarbageCollectorKeepsOverridesWrittenUnderASparseSpine()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(
                BuildNestedProjectData());
            NeoMemberIntWritable count = client.save
                .Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberClassWritable>("Nested")
                .Get<NeoMemberClassWritable>("Deep")
                .Get<NeoMemberIntWritable>("Count");

            count.Set(42);
            string virtualId = count.value!.id;
            Assert.IsTrue(client.saveValues.ContainsKey(virtualId));

            // The spine is sparse by design: no ancestor body links this id,
            // so only the virtual index can prove it reachable.
            CollectionAssert.IsEmpty(client.FindUnlinkedSaveValueIds());
            Assert.AreEqual(0, client.RunGarbageCollector());

            Assert.IsTrue(client.saveValues.ContainsKey(virtualId));
            Assert.AreEqual(
                42d,
                client.save
                    .Get<NeoMemberClassWritable>("Thing")
                    .Get<NeoMemberClassWritable>("Nested")
                    .Get<NeoMemberClassWritable>("Deep")
                    .Get<NeoMemberIntWritable>("Count")
                    .value!.value);
        }

        [Test]
        public void GarbageCollectorKeepsOverrideWrittenUnderVirtualOrderedList()
        {
            ProjectData data = BuildUnorderedListProjectData("thing-item-a");
            ((ListMember)data.members["thing-items"]).ListKind =
                NeoListKind.Ordered;
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoMemberList items = client.save
                .Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberList>("Items");
            var entry = (NeoMemberStringWritable)items.Single();
            string listId = items.value!.id;
            string entryId = entry.value!.id;

            entry.Set("Saved override");

            Assert.IsFalse(client.saveValues.ContainsKey(listId),
                "writing an omitted child keeps its virtual container sparse");
            Assert.IsTrue(client.saveValues.ContainsKey(entryId));
            CollectionAssert.IsEmpty(client.FindUnlinkedSaveValueIds());
            Assert.AreEqual(0, client.RunGarbageCollector());
            Assert.AreEqual(
                "Saved override",
                ((StringMemberValue)client.saveValues[entryId]).value);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LocalSparseWriteDoesNotNotifyUnchangedVirtualSiblings(bool includeUnchangedParent)
        {
            ProjectData data = BuildProjectData();
            data.classes["thing-class"].schema["Sibling"] = "thing-sibling";
            data.members["thing-sibling"] = new IntMember
            {
                id = "thing-sibling", projectId = "p75-project", name = "Sibling",
                kind = MemberKind.Int, Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new NumberMemberValueBase { value = 8 },
            };
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            var thing = client.save.Get<NeoMemberClassWritable>("Thing");
            var count = thing.Get<NeoMemberIntWritable>("Count");
            var sibling = thing.Get<NeoMemberIntWritable>("Sibling");
            string siblingId = sibling.value!.id;
            Assert.IsTrue(client.TryGetValue<NumberMemberValue>(siblingId, out var cachedSibling));
            var notified = new HashSet<string>();
            client.OnWritableValueChanged += (_, id) => notified.Add(id);

            if (includeUnchangedParent)
            {
                var parent = JObject.FromObject(thing.value!).ToObject<ObjectMemberValue>()!;
                client.SetWritableValues(NeoValueOwnership.Save, new MemberValue[]
                {
                    parent,
                    new NumberMemberValue { id = count.value!.id, value = 42 },
                });
            }
            else count.Set(42);

            Assert.AreEqual(42, count.value!.value);
            Assert.AreEqual(8, sibling.value!.value);
            Assert.IsFalse(sibling.isDisposed);
            Assert.IsTrue(client.TryGetValue<NumberMemberValue>(siblingId, out var retainedSibling));
            Assert.AreSame(cachedSibling, retainedSibling, "An ordinary overlay must reuse the cached sibling default.");
            Assert.That(notified, Does.Contain(count.value.id));
            Assert.That(notified, Does.Not.Contain(siblingId));
        }

        [Test]
        public void LiveApplyRejectsMalformedReplayBeforeChangingRowsOrHeldWrappers()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(
                BuildTwoRootProjectData());
            Assert.AreEqual(
                5d,
                client.save
                    .Get<NeoMemberClassWritable>("Thing")
                    .Get<NeoMemberIntWritable>("Count")
                    .value!.value);

            NeoMemberIntWritable heldCount = client.save.Get<NeoMemberClassWritable>("Thing").Get<NeoMemberIntWritable>("Count");
            string beforeSave = client.SerializeSaveData();
            string beforeSession = Newtonsoft.Json.JsonConvert.SerializeObject(client.sessionValues);
            int changed = 0;
            client.OnWritableValueChanged += (_, __) => changed++;
            JObject incoming = JObject.Parse(beforeSave);
            var values = (JObject)incoming["values"]!;
            values[heldCount.value!.id] = JObject.FromObject(new NumberMemberValue
            { id = heldCount.value.id, value = 42 });
            // The first root has a valid edit; the second recipe is invalid.
            // Neither root may change when the complete batch is rejected.
            values["other-instance"] = JObject.Parse(@"{
  'id':'other-instance',
  'classId':'thing-class',
  'value':{},
  'constructorArgs':{},
  'instanceConstructorId':'missing-ctor',
  'createdAt':'2026-08-22T00:00:00.000Z',
  'updatedAt':'2026-08-22T00:00:00.000Z'
}".Replace('\'', '"'));

            Assert.Throws<InvalidOperationException>(() => client.ApplyExternalSaveContent(incoming.ToString()));
            Assert.AreEqual(beforeSave, client.SerializeSaveData());
            Assert.AreEqual(beforeSession, Newtonsoft.Json.JsonConvert.SerializeObject(client.sessionValues));
            Assert.AreEqual(0, changed);
            Assert.IsFalse(heldCount.isDisposed);
            Assert.AreEqual(5d, heldCount.value!.value);

            Assert.AreEqual(
                5d,
                client.save
                    .Get<NeoMemberClassWritable>("Thing")
                    .Get<NeoMemberIntWritable>("Count")
                    .value!.value,
                "A malformed root must not cost every other root its virtual values.");
        }

        [Test]
        public void FullVirtualRebuildRetiresWrappersHoldingTheOldExpansion()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(
                BuildNestedProjectData());
            NeoMemberClassWritable thing = client.save
                .Get<NeoMemberClassWritable>("Thing");
            NeoMemberIntWritable held = thing
                .Get<NeoMemberClassWritable>("Nested")
                .Get<NeoMemberClassWritable>("Deep")
                .Get<NeoMemberIntWritable>("Count");
            Assert.AreEqual(5d, held.value!.value);
            string countId = held.value.id;

            JObject incoming = JObject.Parse(client.SerializeSaveData());
            ((JObject)incoming["values"]!)[countId] = JObject.FromObject(
                new NumberMemberValue { id = countId, value = 73 });

            client.ApplyExternalSaveContent(incoming.ToString());

            // A rebuild mints new rows at the same deterministic ids, so a
            // wrapper that survives keeps serving the previous expansion while
            // the resolver serves the new one.
            Assert.IsTrue(
                held.isDisposed,
                "A wrapper bound to a replaced virtual row must be retired, not left serving the old value.");
            Assert.AreEqual(
                73d,
                thing
                    .Get<NeoMemberClassWritable>("Nested")
                    .Get<NeoMemberClassWritable>("Deep")
                    .Get<NeoMemberIntWritable>("Count")
                    .value!.value);
        }

        // -------------------------------------------------------------------
        // Three-way id parity. Every literal below is uuidv5 (RFC 4122,
        // SHA-1, big-endian) of "{bareRootId}:{sourceIdentity}" under the P75
        // namespace 3e8ca0b3-e3f1-5d5f-bf2f-6ab5ee3896d0, so the TypeScript
        // suite can assert the same strings from the same two inputs.
        // -------------------------------------------------------------------

        [Test]
        public void UnorderedListEntryIdsFollowTheDeclaredDefaultOrder()
        {
            // The parity risk: C# indexes unordered entries in wrapper
            // enumeration order and TypeScript in resolver visit order. A
            // replayed entry carries no authored-child provenance, so the id
            // falls back to the POSITIONAL identity and the two runtimes agree
            // only while both walk the declared default in its own order. The
            // literals below pin that walk; the entry VALUE ids the fixture
            // declares deliberately do not appear in them.
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(
                BuildUnorderedListProjectData("entry-a", "entry-b", "entry-c"));
            NeoMemberList items = client.save
                .Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberList>("Items");

            string[] entryIds = items
                .Select(item => item.value!.id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();

            CollectionAssert.AreEqual(
                new[]
                {
                    // path:thing-item:$/{class Items}/{list 1}
                    "3e2e712d-a6f4-596c-9add-a0f2457f78c0",
                    // path:thing-item:$/{class Items}/{list 2}
                    "4b374fc6-bee8-5552-a484-70e70e67c4e5",
                    // path:thing-item:$/{class Items}/{list 0}
                    "631ac656-2e46-555b-8664-420a080fc2a0",
                },
                entryIds);
        }

        [Test]
        public void PositionalSourceIdentitySpellsAnIdLessMemberAsInline()
        {
            var row = new NumberMemberValue { id = "unused" };
            var named = new IntMember
            {
                id = "thing-count",
                name = "Count",
                kind = MemberKind.Int,
            };
            var inline = new IntMember { id = string.Empty, name = "Count", kind = MemberKind.Int };
            const string path = "$/{\"kind\":\"class\",\"schemaKey\":\"Count\"}";

            Assert.AreEqual(
                "path:thing-count:" + path,
                NeoClient.VirtualSourceIdentity(row, named, path));
            Assert.AreEqual(
                "path:<inline>:" + path,
                NeoClient.VirtualSourceIdentity(row, inline, path));
            Assert.AreEqual(
                "35f55577-5ef0-5bf5-861b-070aa19817f5",
                NeoClient.VirtualValueId(
                    "thing-instance",
                    NeoClient.VirtualSourceIdentity(row, named, path)));
            Assert.AreEqual(
                "77712819-a4bd-5105-a9bf-eb4925f94bc3",
                NeoClient.VirtualValueId(
                    "thing-instance",
                    NeoClient.VirtualSourceIdentity(row, inline, path)));
        }

        [Test]
        public void OrderedListEntryIdsArePositional()
        {
            const string listPath = "$/{\"kind\":\"class\",\"schemaKey\":\"Items\"}";
            var expected = new[]
            {
                "631ac656-2e46-555b-8664-420a080fc2a0",
                "3e2e712d-a6f4-596c-9add-a0f2457f78c0",
                "4b374fc6-bee8-5552-a484-70e70e67c4e5",
            };

            for (int index = 0; index < expected.Length; index++)
            {
                Assert.AreEqual(
                    expected[index],
                    NeoClient.VirtualValueId(
                        "thing-instance",
                        $"path:thing-item:{listPath}/{{\"kind\":\"list\",\"index\":{index}}}"),
                    $"Ordered entry {index} must derive from its index, not its identity.");
            }
        }

        [Test]
        public void LiveApplyReexpandsOnlyTheRootsThePatchTouches()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(
                BuildTwoRootProjectData());
            NeoMemberIntWritable thingCount = client.save
                .Get<NeoMemberClassWritable>("Thing")
                .Get<NeoMemberIntWritable>("Count");
            NeoMemberIntWritable otherCount = client.save
                .Get<NeoMemberClassWritable>("Other")
                .Get<NeoMemberIntWritable>("Count");
            Assert.AreEqual(5d, thingCount.value!.value);
            Assert.AreEqual(5d, otherCount.value!.value);
            string thingCountId = thingCount.value.id;

            JObject incoming = JObject.Parse(client.SerializeSaveData());
            ((JObject)incoming["values"]!)[thingCountId] = JObject.FromObject(
                new NumberMemberValue { id = thingCountId, value = 31 });

            client.ApplyExternalSaveContent(incoming.ToString());

            Assert.IsTrue(thingCount.isDisposed);
            Assert.IsFalse(
                otherCount.isDisposed,
                "An untouched root must not be re-expanded by a patch that never reached it.");
            Assert.AreEqual(5d, otherCount.value!.value);
            Assert.AreEqual(
                31d,
                client.save
                    .Get<NeoMemberClassWritable>("Thing")
                    .Get<NeoMemberIntWritable>("Count")
                    .value!.value);
        }

        [Test]
        public void LiveApplyTracksSharedRetargetedAndRetiredConstructorArgumentRoots()
        {
            ProjectData data = BuildLiveConstructorArgumentProjectData();
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);

            NeoMemberClassWritable thing = client.save
                .Get<NeoMemberClassWritable>("Thing");
            NeoMemberClassWritable other = client.save
                .Get<NeoMemberClassWritable>("Other");
            Assert.AreEqual("from constructor argument", PayloadName(thing));
            Assert.AreEqual("from constructor argument", PayloadName(other));

            JObject incoming = JObject.Parse(client.SerializeSaveData());
            NeoMemberClassWritable heldThingPayload = thing
                .Get<NeoMemberClassWritable>("Payload");
            NeoMemberClassWritable heldOtherPayload = other
                .Get<NeoMemberClassWritable>("Payload");
            ReplacePayload(incoming, "constructor-payload", "shared-update");
            client.ApplyExternalSaveContent(incoming.ToString());

            Assert.IsTrue(heldThingPayload.isDisposed);
            Assert.IsTrue(heldOtherPayload.isDisposed);
            Assert.AreEqual("shared-update", PayloadName(thing));
            Assert.AreEqual("shared-update", PayloadName(other));

            incoming = JObject.Parse(client.SerializeSaveData());
            var retargetedThing = ObjectValue("thing-instance", "thing-class");
            retargetedThing.instanceConstructorId = "thing-ctor";
            retargetedThing.constructorArgs = new Dictionary<string, JToken?>
            {
                ["__arg_0__"] = "alternate-payload",
            };
            retargetedThing.genericBindings = new Dictionary<string, string>(
                ((ObjectMemberValue)data.values["thing-instance"]).genericBindings!);
            ((JObject)incoming["values"]!)[retargetedThing.id] =
                JObject.FromObject(retargetedThing);
            client.ApplyExternalSaveContent(incoming.ToString());

            heldThingPayload = thing
                .Get<NeoMemberClassWritable>("Payload");
            heldOtherPayload = other
                .Get<NeoMemberClassWritable>("Payload");
            Assert.AreEqual("alternate", PayloadName(thing));

            incoming = JObject.Parse(client.SerializeSaveData());
            ReplacePayload(incoming, "constructor-payload", "updated-original");
            client.ApplyExternalSaveContent(incoming.ToString());

            Assert.IsFalse(
                heldThingPayload.isDisposed,
                "Retargeting a root must remove its old constructor-argument edge.");
            Assert.IsTrue(heldOtherPayload.isDisposed);
            Assert.AreEqual("alternate", PayloadName(thing));
            Assert.AreEqual("updated-original", PayloadName(other));

            heldThingPayload = thing.Get<NeoMemberClassWritable>("Payload");
            incoming = JObject.Parse(client.SerializeSaveData());
            ReplacePayload(incoming, "alternate-payload", "updated-alternate");
            client.ApplyExternalSaveContent(incoming.ToString());

            Assert.IsTrue(heldThingPayload.isDisposed);
            Assert.AreEqual("updated-alternate", PayloadName(thing));

            incoming = JObject.Parse(client.SerializeSaveData());
            var retiredThing = ObjectValue(
                "thing-instance",
                "thing-class",
                new Dictionary<string, string>
                {
                    ["Payload"] = "alternate-payload",
                });
            ((JObject)incoming["values"]!)[retiredThing.id] =
                JObject.FromObject(retiredThing);
            client.ApplyExternalSaveContent(incoming.ToString());
            heldThingPayload = thing.Get<NeoMemberClassWritable>("Payload");

            incoming = JObject.Parse(client.SerializeSaveData());
            ReplacePayload(incoming, "alternate-payload", "after-retirement");
            client.ApplyExternalSaveContent(incoming.ToString());

            Assert.IsFalse(
                heldThingPayload.isDisposed,
                "Retiring a sparse root must remove its constructor-argument edge.");
        }

        private static ProjectData BuildTwoRootProjectData()
        {
            ProjectData data = BuildProjectData();
            data.classes["save-root-class"].schema["Other"] = "other-member";
            data.members["other-member"] = new ClassMember
            {
                id = "other-member",
                projectId = "p75-project",
                name = "Other",
                kind = MemberKind.Class,
                classId = "thing-class",
                Requirement = NeoMemberRequirementKind.Required,
                Storage = NeoMemberStorage.Save,
            };
            ObjectMemberValue other = ObjectValue("other-instance", "thing-class");
            other.constructorArgs = new Dictionary<string, JToken?>();
            other.instanceConstructorId = null;
            data.values[other.id] = other;
            ((ObjectMemberValue)data.values["value-save"]).value!["Other"] = other.id;
            return data;
        }

        private static ProjectData BuildLiveConstructorArgumentProjectData()
        {
            ProjectData data = BuildGenericConstructorProjectData();
            ConstructorRecord constructor = data.constructors["thing-ctor"];
            data.members["thing-payload"] = new ClassMember
            {
                id = "thing-payload",
                projectId = "p75-project",
                name = "Payload",
                kind = MemberKind.Class,
                classId = "payload-class",
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new ObjectMemberValueBase
                {
                    init = ReturnVariableInitializer(
                        "Payload",
                        ClassType("payload-class"),
                        constructor.action!.parameters,
                        "__arg_0__"),
                },
            };
            data.classes["save-root-class"].schema["Other"] = "other-member";
            data.members["other-member"] = new ClassMember
            {
                id = "other-member",
                projectId = "p75-project",
                name = "Other",
                kind = MemberKind.Class,
                classId = "thing-class",
                Requirement = NeoMemberRequirementKind.Required,
                Storage = NeoMemberStorage.Save,
            };
            var other = ObjectValue("other-instance", "thing-class");
            other.instanceConstructorId = "thing-ctor";
            other.constructorArgs = new Dictionary<string, JToken?>
            {
                ["__arg_0__"] = "constructor-payload",
            };
            other.genericBindings = new Dictionary<string, string>(
                ((ObjectMemberValue)data.values["thing-instance"]).genericBindings!);
            data.values[other.id] = other;
            ((ObjectMemberValue)data.values["value-save"]).value!["Other"] =
                other.id;
            data.values["alternate-payload"] = ObjectValue(
                "alternate-payload",
                "payload-class",
                new Dictionary<string, string>
                {
                    ["Name"] = "alternate-payload-name",
                });
            data.values["alternate-payload-name"] = new StringMemberValue
            {
                id = "alternate-payload-name",
                value = "alternate",
            };
            return data;
        }

        private static string? PayloadName(NeoMemberClassWritable root) =>
            root.Get<NeoMemberClassWritable>("Payload")
                .Get<NeoMemberStringWritable>("Name")
                .value!.value;

        private static void ReplacePayload(
            JObject save,
            string payloadId,
            string text)
        {
            string nameId = $"{payloadId}-live-name-{text}";
            var values = (JObject)save["values"]!;
            values[payloadId] = JObject.FromObject(ObjectValue(
                payloadId,
                "payload-class",
                new Dictionary<string, string> { ["Name"] = nameId }));
            values[nameId] = JObject.FromObject(new StringMemberValue
            {
                id = nameId,
                value = text,
            });
        }

        private static void UsePlacementGenericBinding(ProjectData data)
        {
            var root = (ObjectMemberValue)data.values["thing-instance"];
            ((ClassMember)data.members["thing-member"]).classArguments = root.genericBindings!
                .ToDictionary(pair => pair.Key, pair => new GenericBinding
                {
                    kind = NeoGenericBindingKind.Member, memberId = pair.Value,
                });
            root.genericBindings = null;
        }

        private static ProjectData BuildGenericConstructorProjectData()
        {
            const string paramT = "thing-param-t";
            ProjectData data = BuildProjectData();
            NeoSchemaClass thingClass = data.classes["thing-class"];
            thingClass.schema.Remove("Count");
            thingClass.schema["Payload"] = "thing-payload";
            thingClass.genericParams = new List<GenericParamDeclaration>
            {
                new() { id = paramT, name = "T" },
            };
            thingClass.constructorIds = new[] { "thing-ctor" };
            data.members.Remove("thing-count");
            data.members["thing-payload"] = new GenericMember
            {
                id = "thing-payload",
                projectId = "p75-project",
                name = "Payload",
                kind = MemberKind.Generic,
                genericParamId = paramT,
            };

            NeoSchemaClass payloadClass = SchemaClass(
                "payload-class",
                "Payload",
                NeoMemberStorage.Save);
            payloadClass.schema["Name"] = "payload-name";
            data.classes[payloadClass.id] = payloadClass;
            data.members["payload-name"] = new StringMember
            {
                id = "payload-name",
                projectId = "p75-project",
                name = "Name",
                kind = MemberKind.String,
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new StringMemberValueBase { value = "default" },
            };
            data.members["payload-binding"] = new ClassMember
            {
                id = "payload-binding",
                projectId = "p75-project",
                name = "PayloadBinding",
                kind = MemberKind.Class,
                classId = payloadClass.id,
                Requirement = NeoMemberRequirementKind.Required,
            };
            ((GenericMember)data.members["thing-payload"]).defaultValue = new NullMemberValueBase
            {
                classId = payloadClass.id,
                value = new Dictionary<string, string> { ["Name"] = "payload-default-name" },
            };
            data.members["placement-string-binding"] = new StringMember
            {
                id = "placement-string-binding",
                projectId = "p75-project",
                name = "PlacementStringBinding",
                kind = MemberKind.String,
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new StringMemberValueBase
                {
                    value = "placement fallback",
                },
            };
            data.values["payload-default-name"] = new StringMemberValue
            {
                id = "payload-default-name",
                value = "from generic default",
            };
            data.values["constructor-payload"] = ObjectValue(
                "constructor-payload",
                payloadClass.id,
                new Dictionary<string, string>
                {
                    ["Name"] = "constructor-payload-name",
                });
            data.values["constructor-payload-name"] = new StringMemberValue
            {
                id = "constructor-payload-name",
                value = "from constructor argument",
            };

            var placement = (ClassMember)data.members["thing-member"];
            placement.classArguments = new Dictionary<string, GenericBinding>
            {
                [paramT] = new()
                {
                    kind = NeoGenericBindingKind.Member,
                    memberId = "placement-string-binding",
                },
            };
            var root = (ObjectMemberValue)data.values["thing-instance"];
            root.genericBindings = new Dictionary<string, string>
            {
                [paramT] = "payload-binding",
            };
            root.instanceConstructorId = "thing-ctor";
            root.constructorArgs = new Dictionary<string, JToken?>
            {
                ["__arg_0__"] = JToken.FromObject("constructor-payload"),
            };
            var genericArgument = new FunctionArgumentTypeInfo
            {
                name = "Payload",
                type = MemberKind.Generic,
                required = true,
                ownerClassId = thingClass.id,
                genericParamId = paramT,
            };
            data.constructors["thing-ctor"] = new ConstructorRecord
            {
                id = "thing-ctor",
                projectId = "p75-project",
                classId = thingClass.id,
                argumentTypes = new[] { genericArgument },
                action = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = new[]
                    {
                        new Variable
                        {
                            id = "__this__",
                            typeInfo = new ClassTypeInfo
                            {
                                type = MemberKind.Class,
                                required = true,
                                classId = thingClass.id,
                            },
                        },
                        new Variable
                        {
                            id = "__root__",
                            typeInfo = new ClassTypeInfo
                            {
                                type = MemberKind.Class,
                                required = true,
                                classId = "save-root-class",
                            },
                        },
                        new Variable
                        {
                            id = "__arg_0__",
                            typeInfo = genericArgument,
                        },
                    },
                    typeInfo = new PrimitiveTypeInfo
                    {
                        type = MemberKind.Null,
                        required = true,
                    },
                    instructions = Array.Empty<Instruction>(),
                },
            };

            return data;
        }

        private static ProjectData BuildProjectData(double defaultCount = 5)
        {
            const string projectId = "p75-project";
            var assetsRootClass = SchemaClass("assets-root-class", "AssetsRoot", NeoMemberStorage.Immutable);
            var saveRootClass = SchemaClass("save-root-class", "SaveRoot", NeoMemberStorage.Save);
            var sessionRootClass = SchemaClass("session-root-class", "SessionRoot", NeoMemberStorage.Session);
            var thingClass = SchemaClass("thing-class", "Thing", NeoMemberStorage.Save);

            saveRootClass.schema["Thing"] = "thing-member";
            thingClass.schema["Count"] = "thing-count";

            var assetsRoot = RootMember(
                projectId,
                "assets-root",
                "Assets",
                assetsRootClass.id,
                NeoMemberStorage.Immutable,
                "value-assets");
            var saveRoot = RootMember(
                projectId,
                "save-root",
                "Save",
                saveRootClass.id,
                NeoMemberStorage.Save,
                "value-save");
            var sessionRoot = RootMember(
                projectId,
                "session-root",
                "Session",
                sessionRootClass.id,
                NeoMemberStorage.Session,
                "value-session");
            var thingMember = new ClassMember
            {
                id = "thing-member",
                projectId = projectId,
                name = "Thing",
                kind = MemberKind.Class,
                classId = thingClass.id,
                Requirement = NeoMemberRequirementKind.Required,
                Storage = NeoMemberStorage.Save,
            };
            var countMember = new IntMember
            {
                id = "thing-count",
                projectId = projectId,
                name = "Count",
                kind = MemberKind.Int,
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new NumberMemberValueBase { value = defaultCount },
            };
            var thing = ObjectValue("thing-instance", thingClass.id);
            thing.constructorArgs = new Dictionary<string, JToken?>();
            thing.instanceConstructorId = null;

            return new ProjectData
            {
                project = new Project
                {
                    id = projectId,
                    name = "P75",
                    rootAssetsMemberId = assetsRoot.id,
                    rootSaveFileMemberId = saveRoot.id,
                    rootSessionMemberId = sessionRoot.id,
                },
                members = new Dictionary<string, JsonMember>
                {
                    [assetsRoot.id] = assetsRoot,
                    [saveRoot.id] = saveRoot,
                    [sessionRoot.id] = sessionRoot,
                    [thingMember.id] = thingMember,
                    [countMember.id] = countMember,
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["value-assets"] = ObjectValue("value-assets", assetsRootClass.id),
                    ["value-save"] = ObjectValue(
                        "value-save",
                        saveRootClass.id,
                        new Dictionary<string, string> { ["Thing"] = thing.id }),
                    ["value-session"] = ObjectValue("value-session", sessionRootClass.id),
                    [thing.id] = thing,
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [assetsRootClass.id] = assetsRootClass,
                    [saveRootClass.id] = saveRootClass,
                    [sessionRootClass.id] = sessionRootClass,
                    [thingClass.id] = thingClass,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
        }

        private static ProjectData BuildUnorderedListProjectData(
            params string[] entryValueIds)
        {
            if (entryValueIds.Length == 0)
                entryValueIds = new[] { "thing-item-a", "thing-item-b" };
            ProjectData data = BuildProjectData();
            data.classes["thing-class"].schema.Remove("Count");
            data.classes["thing-class"].schema["Items"] = "thing-items";
            data.members.Remove("thing-count");
            data.members["thing-item"] = new StringMember
            {
                id = "thing-item",
                projectId = "p75-project",
                name = "Item",
                kind = MemberKind.String,
                Requirement = NeoMemberRequirementKind.Required,
            };
            data.members["thing-items"] = new ListMember
            {
                id = "thing-items",
                projectId = "p75-project",
                name = "Items",
                kind = MemberKind.List,
                Requirement = NeoMemberRequirementKind.Required,
                ListKind = NeoListKind.Unordered,
                entryMemberId = "thing-item",
                defaultValue = new ArrayMemberValueBase { value = entryValueIds },
            };
            for (int index = 0; index < entryValueIds.Length; index++)
            {
                data.values[entryValueIds[index]] = new StringMemberValue
                {
                    id = entryValueIds[index],
                    value = ((char)('A' + index)).ToString(),
                };
            }
            return data;
        }

        private static ProjectData BuildNestedProjectData()
        {
            ProjectData data = BuildProjectData();
            data.classes["thing-class"].schema.Remove("Count");
            data.classes["thing-class"].schema["Nested"] = "thing-nested";
            data.members.Remove("thing-count");
            data.members["thing-nested"] = new ClassMember
            {
                id = "thing-nested",
                projectId = "p75-project",
                name = "Nested",
                kind = MemberKind.Class,
                classId = "nested-class",
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new ObjectMemberValueBase { value = new Dictionary<string, string>() },
            };
            data.members["nested-deep"] = new ClassMember
            {
                id = "nested-deep",
                projectId = "p75-project",
                name = "Deep",
                kind = MemberKind.Class,
                classId = "deep-class",
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new ObjectMemberValueBase { value = new Dictionary<string, string>() },
            };
            data.members["deep-count"] = new IntMember
            {
                id = "deep-count",
                projectId = "p75-project",
                name = "Count",
                kind = MemberKind.Int,
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new NumberMemberValueBase { value = 5 },
            };
            data.classes["nested-class"] = SchemaClass(
                "nested-class",
                "Nested",
                NeoMemberStorage.Save);
            data.classes["nested-class"].schema["Deep"] = "nested-deep";
            data.classes["deep-class"] = SchemaClass(
                "deep-class",
                "Deep",
                NeoMemberStorage.Save);
            data.classes["deep-class"].schema["Count"] = "deep-count";
            return data;
        }

        private static ProjectData BuildNestedAggregateConstructorProjectData()
        {
            ProjectData data = BuildNestedProjectData();
            var deepType = new ClassTypeInfo
            {
                type = MemberKind.Class,
                required = true,
                classId = "deep-class",
            };
            var deepArgument = new FunctionArgumentTypeInfo
            {
                name = "InitialDeep",
                type = MemberKind.Class,
                required = true,
                classId = "deep-class",
            };
            var externalDeepArgument = new FunctionArgumentTypeInfo
            {
                name = "ExternalDeep",
                type = MemberKind.Class,
                required = true,
                classId = "deep-class",
            };
            var literalArgument = new FunctionArgumentTypeInfo
            {
                name = "Literal",
                type = MemberKind.String,
                required = true,
            };
            var stringType = new PrimitiveTypeInfo
            {
                type = MemberKind.String,
                required = true,
            };
            var stringListType = new CollectionTypeInfo
            {
                type = MemberKind.List,
                required = true,
                entryTypeInfo = stringType,
            };
            var itemsArgument = new FunctionArgumentTypeInfo
            {
                name = "InitialItems",
                type = MemberKind.List,
                required = true,
                entryTypeInfo = stringType,
            };
            var labelsArgument = new FunctionArgumentTypeInfo
            {
                name = "InitialLabels",
                type = MemberKind.Dictionary,
                required = true,
                entryTypeInfo = stringListType,
            };
            Variable[] constructorParameters =
            {
                ConstructorVariable("__this__", ClassType("nested-class")),
                ConstructorVariable("__root__", ClassType("save-root-class")),
                ConstructorVariable("__arg_0__", deepArgument),
                ConstructorVariable("__arg_1__", externalDeepArgument),
                ConstructorVariable("__arg_2__", literalArgument),
                ConstructorVariable("__arg_3__", itemsArgument),
                ConstructorVariable("__arg_4__", labelsArgument),
            };
            data.classes["nested-class"].constructorIds =
                new[] { "nested-ctor" };
            data.constructors["nested-ctor"] = new ConstructorRecord
            {
                id = "nested-ctor",
                projectId = "p75-project",
                classId = "nested-class",
                argumentTypes = new[]
                {
                    deepArgument,
                    externalDeepArgument,
                    literalArgument,
                    itemsArgument,
                    labelsArgument,
                },
                code = string.Empty,
                action = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = constructorParameters,
                    typeInfo = new PrimitiveTypeInfo
                    {
                        type = MemberKind.Null,
                        required = true,
                    },
                    instructions = Array.Empty<Instruction>(),
                },
            };
            ((ClassMember)data.members["nested-deep"]).defaultValue =
                new ObjectMemberValueBase
                {
                    init = ReturnVariableInitializer(
                        "InitialDeep",
                        deepType,
                        constructorParameters,
                        "__arg_0__"),
                };
            data.members["nested-entry"] = new StringMember
            {
                id = "nested-entry",
                projectId = "p75-project",
                name = "Entry",
                kind = MemberKind.String,
                Requirement = NeoMemberRequirementKind.Required,
            };
            data.members["nested-items"] = new ListMember
            {
                id = "nested-items",
                projectId = "p75-project",
                name = "Items",
                kind = MemberKind.List,
                Requirement = NeoMemberRequirementKind.Required,
                ListKind = NeoListKind.Ordered,
                entryMemberId = "nested-entry",
                defaultValue = new ArrayMemberValueBase
                {
                    init = AggregateArgumentInitializer(
                        "InitialItems",
                        MemberKind.List,
                        stringType,
                        constructorParameters,
                        "__arg_3__"),
                },
            };
            data.members["nested-labels"] = new DictionaryMember
            {
                id = "nested-labels",
                projectId = "p75-project",
                name = "Labels",
                kind = MemberKind.Dictionary,
                Requirement = NeoMemberRequirementKind.Required,
                entryMemberId = "nested-label-entry",
                KeyKind = NeoDictionaryKeyKind.String,
                defaultValue = new ObjectMemberValueBase
                {
                    init = AggregateArgumentInitializer(
                        "InitialLabels",
                        MemberKind.Dictionary,
                        stringListType,
                        constructorParameters,
                        "__arg_4__"),
                },
            };
            data.members["nested-label-entry"] = new ListMember
            {
                id = "nested-label-entry",
                projectId = "p75-project",
                name = "Label Entry",
                kind = MemberKind.List,
                Requirement = NeoMemberRequirementKind.Required,
                ListKind = NeoListKind.Ordered,
                entryMemberId = "nested-entry",
            };
            data.classes["nested-class"].schema["Items"] = "nested-items";
            data.classes["nested-class"].schema["Labels"] = "nested-labels";
            data.classes["thing-class"].schema.Remove("Nested");
            data.classes["thing-class"].schema["Holder"] = "thing-holder";
            data.classes["holder-class"] = SchemaClass(
                "holder-class",
                "Holder",
                NeoMemberStorage.Save);
            data.classes["holder-class"].schema["Nested"] = "thing-nested";
            data.members["thing-holder"] = new ClassMember
            {
                id = "thing-holder",
                projectId = "p75-project",
                name = "Holder",
                kind = MemberKind.Class,
                classId = "holder-class",
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new ObjectMemberValueBase
                {
                    classId = "holder-class",
                    value = new Dictionary<string, string>
                    {
                        ["Nested"] = "nested-template",
                    },
                },
            };
            data.values["nested-template"] = new ObjectMemberValue
            {
                id = "nested-template",
                classId = "nested-class",
                value = new Dictionary<string, string>
                {
                    ["Deep"] = "deep-template",
                    ["Items"] = "items-template",
                    ["Labels"] = "labels-template",
                },
                instanceConstructorId = "nested-ctor",
                constructorArgs = new Dictionary<string, JToken?>
                {
                    ["__arg_0__"] = "deep-template",
                    ["__arg_1__"] = "external-deep",
                    ["__arg_2__"] =
                        "12345678-1234-1234-1234-123456789abc",
                    ["__arg_3__"] = "items-template",
                    ["__arg_4__"] = "labels-template",
                },
            };
            data.values["deep-template"] = ObjectValue(
                "deep-template",
                "deep-class");
            data.values["external-deep"] = ObjectValue(
                "external-deep",
                "deep-class");
            data.values["items-template"] = new ArrayMemberValue
            {
                id = "items-template",
                value = new[] { "item-template" },
            };
            data.values["item-template"] = new StringMemberValue
            {
                id = "item-template",
                value = "item value",
            };
            data.values["labels-template"] = new ObjectMemberValue
            {
                id = "labels-template",
                value = new Dictionary<string, string>
                {
                    ["primary"] = "label-template",
                },
            };
            data.values["label-template"] = new ArrayMemberValue
            {
                id = "label-template",
                value = new[] { "label-item-template" },
            };
            data.values["label-item-template"] = new StringMemberValue
            {
                id = "label-item-template",
                value = "label value",
            };
            ((ClassMember)data.members["thing-nested"]).valueId =
                "nested-template";
            return data;
        }

        private static InitializerBody AggregateArgumentInitializer(
            string code,
            MemberKind kind,
            TypeInfo entryTypeInfo,
            Variable[] parameters,
            string variableId) =>
            ReturnVariableInitializer(
                code,
                new CollectionTypeInfo
                {
                    type = kind,
                    required = true,
                    entryTypeInfo = entryTypeInfo,
                },
                parameters,
                variableId);

        private static InitializerBody ReturnVariableInitializer(
            string code,
            TypeInfo typeInfo,
            Variable[] parameters,
            string variableId) => new()
        {
            code = code,
            compiled = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = parameters,
                typeInfo = typeInfo,
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = new VariablePointer
                        {
                            type = PointerKind.Variable,
                            variableId = variableId,
                        },
                    },
                },
            },
        };

        private static void AssertAggregateReferenceArguments(
            ObjectMemberValue value,
            string externalArgumentId,
            string uuidLookingLiteral)
        {
            Assert.AreEqual(
                externalArgumentId,
                value.constructorArgs!["__arg_1__"]!.Value<string>(),
                "an aggregate argument that settles no child remains an external durable reference");
            Assert.AreEqual(
                uuidLookingLiteral,
                value.constructorArgs["__arg_2__"]!.Value<string>(),
                "a String argument is data even when it looks like a value id");
        }

        private static ClassTypeInfo ClassType(string classId) => new()
        {
            type = MemberKind.Class,
            required = true,
            classId = classId,
        };

        private static Variable ConstructorVariable(string id, TypeInfo typeInfo) =>
            new() { id = id, typeInfo = typeInfo };

        private static NeoSchemaClass SchemaClass(
            string id,
            string name,
            NeoMemberStorage storage)
        {
            return new NeoSchemaClass
            {
                id = id,
                projectId = "p75-project",
                name = name,
                allowedStorage = storage,
                schema = new Dictionary<string, string>(),
            };
        }

        private static ClassMember RootMember(
            string projectId,
            string id,
            string name,
            string classId,
            NeoMemberStorage storage,
            string valueId)
        {
            return new ClassMember
            {
                id = id,
                projectId = projectId,
                name = name,
                kind = MemberKind.Class,
                classId = classId,
                Requirement = NeoMemberRequirementKind.Required,
                Storage = storage,
                valueId = valueId,
            };
        }

        private static Dictionary<string, object?> NeoScriptRuntimeRoot(
            NeoClient client,
            NSGetterEvaluator.Context ctx)
        {
            return new Dictionary<string, object?>
            {
                ["Assets"] = client.assets.value is ObjectMemberValue assets
                    ? NSGetterEvaluator.UnwrapRow(assets, ctx, NeoValueOwnership.Asset)
                    : null,
                ["Save"] = client.save.value is ObjectMemberValue save
                    ? NSGetterEvaluator.UnwrapRow(save, ctx, NeoValueOwnership.Save)
                    : null,
                ["Session"] = client.session.value is ObjectMemberValue session
                    ? NSGetterEvaluator.UnwrapRow(session, ctx, NeoValueOwnership.Session)
                    : null,
            };
        }

        private static PrimitiveTypeInfo IntTypeInfo() => new()
        {
            type = MemberKind.Int,
            required = true,
        };

        private static NumberMemberValueBase ComputedIntInitializer(int value) =>
            new()
            {
                init = new InitializerBody
                {
                    code = value.ToString(),
                    compiled = new FunctionWithReturnType
                    {
                        compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                        parameters = Array.Empty<Variable>(),
                        typeInfo = IntTypeInfo(),
                        instructions = new Instruction[]
                        {
                            new ReturnInstruction
                            {
                                type = InstructionKind.Return,
                                pointer = IntLiteral(value),
                            },
                        },
                    },
                },
            };

        private static ClassMember ClassPlacement(
            string id,
            string name,
            string classId,
            NeoMemberStorage storage,
            NeoMemberRequirementKind requirement = NeoMemberRequirementKind.Required) => new()
        {
            id = id,
            projectId = "p75-project",
            name = name,
            kind = MemberKind.Class,
            classId = classId,
            Storage = storage,
            Requirement = requirement,
        };

        private static FunctionPointer ApplyBaseVariant(Pointer receiver, string classId) => new()
        {
            type = PointerKind.Function,
            function = new VariantApplyFunction
            {
                type = FunctionKind.VariantApply,
                info = new FunctionVariantApplyInfo
                {
                    receiverPointer = receiver,
                    variantPointer = new VariantPointer
                    {
                        type = PointerKind.Variant,
                        classId = classId,
                        variantId = null,
                    },
                    schemaClassInfo = ClassType(classId),
                },
            },
        };

        private static AssignInstruction AssignClass(
            Pointer target,
            Pointer value,
            string classId,
            string writability) => new()
        {
            type = InstructionKind.Assign,
            target = new WriteTarget
            {
                pointer = target,
                typeInfo = ClassType(classId),
                writability = writability,
            },
            operatorValue = "=",
            pointer = value,
        };

        private static FunctionPointer CloneClass(Pointer receiver, string classId) => new()
        {
            type = PointerKind.Function,
            function = new ClassCloneFunction
            {
                type = FunctionKind.ClassClone,
                info = new FunctionClassCloneInfo
                {
                    receiverPointer = receiver,
                    schemaClassInfo = ClassType(classId),
                },
            },
        };

        private static VariablePointer RootPointer() => new()
        {
            type = PointerKind.Variable,
            variableId = "__root__",
        };

        private static ActionTypeInfo ActionTypeInfo() => new()
        {
            type = MemberKind.NSAction,
            required = true,
            argumentTypes = Array.Empty<TypeInfo>(),
        };

        /// <summary>
        /// The pointer form a method group lowers to at a delegate position
        /// (P62 §3.2): a literal member target, never a closure.
        /// </summary>
        private static ValuePointer ListenerPointer(string memberId) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = ActionTypeInfo(),
                value = new JObject
                {
                    ["memberId"] = memberId,
                    ["valueId"] = JValue.CreateNull(),
                },
            },
        };

        /// <summary>
        /// <see cref="BuildProjectData"/> plus an NSAction member on Thing
        /// whose declaration default carries one listener — the "constructor
        /// installed a listener set" shape — and two void NSFunctions to
        /// subscribe.
        /// </summary>
        private static ProjectData BuildActionProjectData()
        {
            const string projectId = "p75-project";
            ProjectData data = BuildProjectData();
            data.classes["thing-class"].schema["OnPing"] = "thing-ping";
            data.classes["thing-class"].schema["Early"] = "thing-early";
            data.classes["thing-class"].schema["Late"] = "thing-late";
            var earlyListeners = new NeoActionValue();
            earlyListeners.listeners.Add(new NeoDelegateValue
            {
                memberId = "thing-early",
                valueId = null,
            });
            data.members["thing-ping"] = new ActionMember
            {
                id = "thing-ping",
                projectId = projectId,
                name = "OnPing",
                kind = MemberKind.NSAction,
                // Never nullable: the empty set is the rest state (P62 §2.1).
                Requirement = NeoMemberRequirementKind.Optional,
                argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                defaultValue = new ActionMemberValueBase { value = earlyListeners },
                createdAt = "x",
                updatedAt = "x",
            };
            data.members["thing-early"] = VoidNSFunction(projectId, "thing-early", "Early");
            data.members["thing-late"] = VoidNSFunction(projectId, "thing-late", "Late");
            return data;
        }

        private static NSFunctionMember VoidNSFunction(
            string projectId,
            string id,
            string name) => new()
        {
            id = id,
            projectId = projectId,
            name = name,
            kind = MemberKind.NSFunction,
            code = "compiled test listener",
            returnTypeInfo = new VoidTypeInfo
            {
                type = MemberKind.Void,
                required = true,
            },
            argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
            Dispatch = NeoFunctionDispatchKind.Synchronous,
            action = new FunctionWithReturnType
            {
                parameters = new[]
                {
                    new Variable
                    {
                        id = "__this__",
                        typeInfo = new ClassTypeInfo
                        {
                            type = MemberKind.Class,
                            required = true,
                            classId = "thing-class",
                        },
                    },
                    new Variable
                    {
                        id = "__root__",
                        typeInfo = new ClassTypeInfo
                        {
                            type = MemberKind.Class,
                            required = true,
                            classId = "save-root-class",
                        },
                    },
                },
                instructions = Array.Empty<Instruction>(),
                // A void NSFunction's compiled body carries the Null
                // statement-body result marker, not Void.
                typeInfo = new PrimitiveTypeInfo
                {
                    type = MemberKind.Null,
                    required = true,
                },
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
            },
            createdAt = "x",
            updatedAt = "x",
        };

        private static ValuePointer IntLiteral(double value) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = IntTypeInfo(),
                value = JToken.FromObject(value),
            },
        };

        private static KeyOfPointer PointerKeyOf(Pointer receiver, string key) => new()
        {
            type = PointerKind.KeyOf,
            keyOf = new KeyOf
            {
                pointer = receiver,
                key = new ValuePointer
                {
                    type = PointerKind.Value,
                    value = new Value
                    {
                        typeInfo = new PrimitiveTypeInfo
                        {
                            type = MemberKind.String,
                            required = true,
                        },
                        value = JToken.FromObject(key),
                    },
                },
            },
        };

        private static ObjectMemberValue ObjectValue(
            string id,
            string classId,
            Dictionary<string, string>? value = null)
        {
            return new ObjectMemberValue
            {
                id = id,
                classId = classId,
                value = value ?? new Dictionary<string, string>(),
            };
        }

        /// <summary>
        /// A declaration-default delegate argument — <c>this.SelectSclera</c>
        /// on the row that declares it — records a null <c>valueId</c>.
        /// Recording and replay are the two halves of one round trip, so the
        /// token the first writes must be a token the second accepts.
        /// </summary>
        [Test]
        public void DeclarationDefaultDelegateArgument_RecordsATokenReplayCanRead()
        {
            JToken? token = NeoClient.ConstructorArgumentToken(
                new NeoDelegateValue { memberId = "member-select" },
                "'selector' of constructor 'constructor-track'");

            Assert.AreEqual(JTokenType.Null, token!["valueId"]!.Type);
            NeoDelegateValue replayed = token.ToObject<NeoDelegateValue>()!;
            Assert.AreEqual("member-select", replayed.memberId);
            Assert.IsNull(replayed.valueId);
        }
    }
}
