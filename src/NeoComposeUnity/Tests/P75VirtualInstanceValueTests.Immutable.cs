// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// Runtime construction stores no Immutable value: each instance evaluates
    /// its Immutable members from their declarations, with its own constructor
    /// arguments, in session, after joining a Save list, and after a reload,
    /// and no copy is stranded in another store.
    /// </summary>
    public partial class P75VirtualInstanceValueTests
    {
        private sealed class ImmutableProbeReference : INeoValueReference
        {
            public string? valueId
            {
                get; set;
            }
        }

        private static ProjectData BuildImmutableRankProjectData(bool computed)
        {
            ProjectData data = BuildHostSlotProjectData();
            data.classes["thing-class"].schema["Rank"] = "thing-rank";
            data.members["thing-rank"] = new IntMember
            {
                id = "thing-rank",
                projectId = "p75-project",
                name = "Rank",
                kind = MemberKind.Int,
                Storage = NeoMemberStorage.Immutable,
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = computed
                    ? ComputedIntInitializer(7)
                    : new NumberMemberValueBase { value = 7 },
            };
            return data;
        }

        private static NeoMemberClassWritable SavedThing(NeoClient client) =>
            (NeoMemberClassWritable)client.save.Get<NeoMemberListWritable>("Things")[0];

        private static object? EvaluateRowMember(NeoClient client, string rowId, string key)
        {
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            ctx = ctx.WithRoot(NeoScriptRuntimeRoot(client, ctx));
            return NSGetterEvaluator.Evaluate(new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = Array.Empty<Variable>(),
                typeInfo = IntTypeInfo(),
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = PointerKeyOf(
                            new ReferencePointer { type = PointerKind.Reference, valueId = rowId },
                            key),
                    },
                },
            }, ctx);
        }

        private static void AssertRank(NeoClient client, NeoMemberClass thing, string phase)
        {
            Assert.AreEqual(7d, thing.Get<NeoMemberInt>("Rank").value!.value, $"{phase}: C# read");
            Assert.AreEqual(7d, EvaluateRowMember(client, thing.value!.id, "Rank"), $"{phase}: NeoScript read");
            var row = (ObjectMemberValue)thing.value!;
            Assert.IsFalse(row.value!.ContainsKey("Rank"), $"{phase}: the row stores no Immutable value");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RuntimeConstructionReadsImmutableMemberFromItsDeclaration(bool computed)
        {
            ProjectData data = BuildImmutableRankProjectData(computed);
            string saved;
            using (NeoClient client = NeoTestSaveStack.ClientFromSchema(data))
            {
                int sessionRows = client.sessionValues.Count;
                NeoMemberClassWritable constructed =
                    NeoGeneratedTypesSupport.CreateWritableClassValue(client, "thing-class");
                AssertRank(client, constructed, "constructed");

                client.save.Get<NeoMemberListWritable>("Things").AddSerialized(
                    NeoGeneratedTypesSupport.ValueReference(
                        new ImmutableProbeReference { valueId = constructed.value!.id }));
                AssertRank(client, SavedThing(client), "attached");
                Assert.AreEqual(sessionRows, client.sessionValues.Count, "nothing is stranded in Session");
                saved = client.SerializeSaveData();
            }
            using NeoClient reopened = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: saved);
            AssertRank(reopened, SavedThing(reopened), "reloaded");
        }

        // Thing.Badge is an Immutable `new Badge(Label: "gold")`: its body
        // carries the constructor envelope but reads neither `this` nor an
        // argument, so it is a constant.
        private static ProjectData BuildImmutableBadgeProjectData()
        {
            ProjectData data = BuildHostSlotProjectData();
            data.classes["badge-class"] = new NeoSchemaClass
            {
                id = "badge-class",
                name = "Badge",
                projectId = "p75-project",
                schema = new Dictionary<string, string> { ["Label"] = "badge-label" },
            };
            data.members["badge-label"] = new StringMember
            {
                id = "badge-label",
                name = "Label",
                kind = MemberKind.String,
                defaultValue = new StringMemberValueBase { value = "plain" },
            };
            var badgeType = new ClassTypeInfo
            {
                type = MemberKind.Class,
                required = true,
                classId = "badge-class",
            };
            data.classes["thing-class"].schema["Badge"] = "thing-badge";
            data.members["thing-badge"] = new ClassMember
            {
                id = "thing-badge",
                projectId = "p75-project",
                name = "Badge",
                kind = MemberKind.Class,
                classId = "badge-class",
                Storage = NeoMemberStorage.Immutable,
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new ObjectMemberValueBase
                {
                    init = new InitializerBody
                    {
                        code = "new Badge(Label: \"gold\")",
                        compiled = new FunctionWithReturnType
                        {
                            compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                            parameters = new[]
                            {
                                ConstructorVariable("__this__", ClassType("thing-class")),
                                ConstructorVariable("__root__", ClassType("save-root-class")),
                            },
                            typeInfo = badgeType,
                            instructions = new Instruction[]
                            {
                                new ReturnInstruction
                                {
                                    type = InstructionKind.Return,
                                    pointer = new FunctionPointer
                                    {
                                        type = PointerKind.Function,
                                        function = new ClassConstructorFunction
                                        {
                                            type = FunctionKind.ClassConstructor,
                                            info = new FunctionClassConstructorInfo
                                            {
                                                schemaClassInfo = badgeType,
                                                fields = new[]
                                                {
                                                    new FunctionClassConstructorField
                                                    {
                                                        schemaKey = "Label",
                                                        memberId = "badge-label",
                                                        valuePointer = StringLiteral("gold"),
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
            return data;
        }

        private static Member MemberOf(NeoClient client, string memberId)
        {
            Assert.IsTrue(client.TryGetMember(memberId, out Member? member), memberId);
            return member!;
        }

        private static string BadgeId(NeoMemberClass thing, string phase)
        {
            NeoMemberClass badge = thing.Get<NeoMemberClass>("Badge");
            Assert.AreEqual("gold", badge.Get<NeoMemberString>("Label").value!.value, $"{phase}: label");
            Assert.IsFalse(((ObjectMemberValue)thing.value!).value!.ContainsKey("Badge"), $"{phase}: the row stores no Immutable value");
            return badge.value!.id;
        }

        [Test]
        public void RuntimeConstructionSharesAnImmutableConstant()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(BuildImmutableBadgeProjectData());
            string first = BadgeId(NeoGeneratedTypesSupport.CreateWritableClassValue(client, "thing-class"), "first");
            string second = BadgeId(NeoGeneratedTypesSupport.CreateWritableClassValue(client, "thing-class"), "second");
            Assert.AreEqual(first, second, "instances share the constant");
            Assert.AreEqual(client.ImmutableDeclarationValueId(MemberOf(client, "thing-badge")), first);
            Assert.IsFalse(client.sessionValues.ContainsKey(first), "the constant is not Session data");
        }

        [Test]
        public void ImmutableConstantFollowsTheRowsItsDeclarationOwns()
        {
            ProjectData data = BuildImmutableBadgeProjectData();
            var badge = (ClassMember)data.members["thing-badge"];
            badge.defaultValue = new ObjectMemberValueBase
            {
                value = new Dictionary<string, string> { ["Label"] = "badge-label-row" },
            };
            var labelRow = new StringMemberValue { id = "badge-label-row", value = "gold" };
            data.values["badge-label-row"] = labelRow;
            data.members["thing-handler"] = new DelegateMember
            {
                id = "thing-handler",
                projectId = "p75-project",
                name = "Handler",
                kind = MemberKind.NSDelegate,
                Storage = NeoMemberStorage.Immutable,
                returnTypeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true },
                argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                // A method group binds `this`.
                defaultValue = new DelegateMemberValueBase
                {
                    value = new NeoDelegateValue { memberId = "thing-badge" },
                },
            };
            using (NeoClient client = NeoTestSaveStack.ClientFromSchema(data))
            {
                Assert.IsTrue(NeoGeneratedTypesSupport.IsImmutableConstant(client, MemberOf(client, "thing-badge")));
                Assert.IsFalse(NeoGeneratedTypesSupport.IsImmutableConstant(client, MemberOf(client, "thing-handler")));
            }
            labelRow.value = null;
            labelRow.init = ReturnVariableInitializer(
                "this.Label",
                new PrimitiveTypeInfo { type = MemberKind.String, required = true },
                new[] { ConstructorVariable("__this__", ClassType("badge-class")) },
                "__this__");
            using (NeoClient client = NeoTestSaveStack.ClientFromSchema(data))
                Assert.IsFalse(NeoGeneratedTypesSupport.IsImmutableConstant(client, MemberOf(client, "thing-badge")));
        }

        // Thing.Rank is an Immutable `InitialRank`, read from the declared
        // constructor's argument; the authored instance passes 5.
        private static ProjectData BuildConstructorRankProjectData()
        {
            ProjectData data = BuildImmutableRankProjectData(computed: false);
            var argument = new FunctionArgumentTypeInfo
            {
                name = "InitialRank",
                type = MemberKind.Int,
                required = true,
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
            ((IntMember)data.members["thing-rank"]).defaultValue = new NumberMemberValueBase
            {
                init = ReturnVariableInitializer("InitialRank", argument, parameters, "__arg_0__"),
            };
            var authored = (ObjectMemberValue)data.values["thing-instance"];
            authored.instanceConstructorId = "thing-ctor";
            authored.constructorArgs = new Dictionary<string, JToken?> { ["__arg_0__"] = 5 };
            return data;
        }

        [Test]
        public void ConstructorArgumentImmutableMemberReadsItsReplayInSession()
        {
            ProjectData data = BuildConstructorRankProjectData();
            string saved;
            using (NeoClient client = NeoTestSaveStack.ClientFromSchema(data))
            {
                int sessionRows = client.sessionValues.Count;
                NeoMemberClassWritable constructed = NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
                    client,
                    "thing-class",
                    "thing-ctor",
                    new[] { new NeoDeclaredConstructorArgument("InitialRank", 9d) });
                AssertConstructedRank(client, constructed, "constructed");

                client.save.Get<NeoMemberListWritable>("Things").AddSerialized(
                    NeoGeneratedTypesSupport.ValueReference(
                        new ImmutableProbeReference { valueId = constructed.value!.id }));
                AssertConstructedRank(client, SavedThing(client), "attached");
                Assert.AreEqual(sessionRows, client.sessionValues.Count, "nothing is stranded in Session");
                saved = client.SerializeSaveData();
            }
            using NeoClient reopened = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: saved);
            AssertConstructedRank(reopened, SavedThing(reopened), "reloaded");

            static void AssertConstructedRank(NeoClient client, NeoMemberClass thing, string phase)
            {
                Assert.AreEqual(9d, thing.Get<NeoMemberInt>("Rank").value!.value, $"{phase}: C# read");
                Assert.AreEqual(9d, EvaluateRowMember(client, thing.value!.id, "Rank"), $"{phase}: NeoScript read");
                var row = (ObjectMemberValue)thing.value!;
                Assert.IsFalse(row.value!.ContainsKey("Rank"), $"{phase}: the row stores no Immutable value");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NeoScriptConstructionReadsItsImmutableMemberInSave(bool computed)
        {
            ProjectData data = BuildImmutableRankProjectData(computed);
            string saved;
            using (NeoClient client = NeoTestSaveStack.ClientFromSchema(data))
            {
                int sessionRows = client.sessionValues.Count;
                ExecuteSaveInstruction(client, new AssignInstruction
                {
                    type = InstructionKind.Assign,
                    operatorValue = "=",
                    target = new WriteTarget
                    {
                        pointer = SavePointer("Thing"),
                        typeInfo = ClassType("thing-class"),
                        writability = WritabilityKind.Save,
                    },
                    pointer = new FunctionPointer
                    {
                        type = PointerKind.Function,
                        function = new ClassConstructorFunction
                        {
                            type = FunctionKind.ClassConstructor,
                            info = new FunctionClassConstructorInfo
                            {
                                schemaClassInfo = ClassType("thing-class"),
                                fields = Array.Empty<FunctionClassConstructorField>(),
                            },
                        },
                    },
                });
                AssertRank(client, client.save.Get<NeoMemberClassWritable>("Thing"), "assigned");
                Assert.AreEqual(sessionRows, client.sessionValues.Count, "nothing is stranded in Session");
                saved = client.SerializeSaveData();
            }
            using NeoClient reopened = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: saved);
            AssertRank(reopened, reopened.save.Get<NeoMemberClassWritable>("Thing"), "reloaded");
        }

        [Test]
        public void CloneSharesAnImmutableConstant()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(BuildImmutableBadgeProjectData());
            NeoMemberClassWritable source = NeoGeneratedTypesSupport.CreateWritableClassValue(client, "thing-class");
            NeoMemberClassWritable clone = NeoGeneratedTypesSupport.CloneClassValue(
                client,
                new ImmutableProbeReference { valueId = source.value!.id });
            Assert.AreEqual(BadgeId(source, "source"), BadgeId(clone, "cloned"));
        }

        [Test]
        public void CloneReplaysAPerInstanceImmutableMember()
        {
            ProjectData data = BuildConstructorRankProjectData();
            string saved;
            using (NeoClient client = NeoTestSaveStack.ClientFromSchema(data))
            {
                NeoMemberClassWritable source = NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(
                    client,
                    "thing-class",
                    "thing-ctor",
                    new[] { new NeoDeclaredConstructorArgument("InitialRank", 9d) });
                var sessionRows = new HashSet<string>(client.sessionValues.Keys);
                NeoMemberClassWritable clone = NeoGeneratedTypesSupport.CloneClassValue(
                    client,
                    new ImmutableProbeReference { valueId = source.value!.id });
                Assert.AreEqual(9d, clone.Get<NeoMemberInt>("Rank").value!.value);
                Assert.AreNotEqual(
                    source.Get<NeoMemberInt>("Rank").value!.id,
                    clone.Get<NeoMemberInt>("Rank").value!.id,
                    "the clone evaluates its own value");
                Assert.IsFalse(((ObjectMemberValue)clone.value!).value!.ContainsKey("Rank"));

                client.save.Get<NeoMemberListWritable>("Things").AddSerialized(
                    NeoGeneratedTypesSupport.ValueReference(
                        new ImmutableProbeReference { valueId = clone.value!.id }));
                CollectionAssert.IsSubsetOf(client.sessionValues.Keys, sessionRows, "nothing of the clone is stranded in Session");
                saved = client.SerializeSaveData();
            }
            using NeoClient reopened = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: saved);
            Assert.AreEqual(9d, SavedThing(reopened).Get<NeoMemberInt>("Rank").value!.value);
        }

        [Test]
        public void CloneSharesAnAuthoredImmutableValue()
        {
            ProjectData data = BuildImmutableRankProjectData(computed: false);
            data.values["thing-instance-rank"] = new NumberMemberValue { id = "thing-instance-rank", value = 4 };
            ((ObjectMemberValue)data.values["thing-instance"]).value!["Rank"] = "thing-instance-rank";
            string saved;
            using (NeoClient client = NeoTestSaveStack.ClientFromSchema(data))
            {
                NeoMemberClassWritable clone = NeoGeneratedTypesSupport.CloneClassValue(
                    client,
                    new ImmutableProbeReference { valueId = "thing-instance" });
                Assert.AreEqual(
                    "thing-instance-rank",
                    ((ObjectMemberValue)clone.value!).value!["Rank"],
                    "the clone shares the export's value");
                Assert.AreEqual(4d, clone.Get<NeoMemberInt>("Rank").value!.value);

                client.save.Get<NeoMemberListWritable>("Things").AddSerialized(
                    NeoGeneratedTypesSupport.ValueReference(
                        new ImmutableProbeReference { valueId = clone.value!.id }));
                saved = client.SerializeSaveData();
            }
            using NeoClient reopened = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: saved);
            Assert.AreEqual(4d, SavedThing(reopened).Get<NeoMemberInt>("Rank").value!.value);
        }

        [Test]
        public void RuntimeConstructionOmitsNestedImmutableMembers()
        {
            ProjectData data = BuildNestedProjectData();
            ((IntMember)data.members["deep-count"]).Storage = NeoMemberStorage.Immutable;
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoMemberClassWritable constructed =
                NeoGeneratedTypesSupport.CreateWritableClassValue(client, "thing-class");
            NeoMemberClass deep = constructed
                .Get<NeoMemberClass>("Nested")
                .Get<NeoMemberClass>("Deep");
            Assert.AreEqual(5d, deep.Get<NeoMemberInt>("Count").value!.value);
            Assert.IsFalse(((ObjectMemberValue)deep.value!).value!.ContainsKey("Count"));
        }

        [Test]
        public void RuntimeConstructionRejectsASuppliedImmutableMember()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(
                BuildImmutableRankProjectData(computed: false));
            var error = Assert.Throws<InvalidOperationException>(() =>
                NeoGeneratedTypesSupport.CreateWritableClassValue(
                    client,
                    "thing-class",
                    new NeoGeneratedConstructorValue("Rank", "thing-rank", 3)));
            StringAssert.Contains("sets Immutable member 'thing-rank'", error!.Message);
        }

        [Test]
        public void RuntimeConstructionRejectsARequiredImmutableMemberWithoutAnInitializer()
        {
            ProjectData data = BuildImmutableRankProjectData(computed: false);
            ((IntMember)data.members["thing-rank"]).defaultValue = null;
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            var error = Assert.Throws<InvalidOperationException>(() =>
                NeoGeneratedTypesSupport.CreateWritableClassValue(client, "thing-class"));
            StringAssert.Contains("Immutable member 'Rank'", error!.Message);
        }
    }
}
