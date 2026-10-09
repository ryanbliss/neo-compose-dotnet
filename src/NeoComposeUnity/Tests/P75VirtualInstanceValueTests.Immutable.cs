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
    /// Runtime construction never creates an Immutable value: the instance
    /// reads its declaration in session, after joining a Save list, and after
    /// a reload, and no copy is stranded in another store.
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

        [Test]
        public void RuntimeConstructionReadsAConstructedImmutableClassMember()
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
                defaultValue = new StringMemberValueBase { value = "gold" },
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
                        code = "new Badge()",
                        compiled = new FunctionWithReturnType
                        {
                            compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                            parameters = Array.Empty<Variable>(),
                            typeInfo = BadgeType(),
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
                                                schemaClassInfo = BadgeType(),
                                                fields = Array.Empty<FunctionClassConstructorField>(),
                                            },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            };
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            NeoMemberClassWritable first = NeoGeneratedTypesSupport.CreateWritableClassValue(client, "thing-class");
            NeoMemberClassWritable second = NeoGeneratedTypesSupport.CreateWritableClassValue(client, "thing-class");
            foreach (NeoMemberClassWritable thing in new[] { first, second })
            {
                NeoMemberClass badge = thing.Get<NeoMemberClass>("Badge");
                Assert.AreEqual("gold", badge.Get<NeoMemberString>("Label").value!.value);
                Assert.IsFalse(((ObjectMemberValue)thing.value!).value!.ContainsKey("Badge"));
            }
            string badgeId = first.Get<NeoMemberClass>("Badge").value!.id;
            Assert.AreEqual(badgeId, second.Get<NeoMemberClass>("Badge").value!.id, "one declaration value serves every instance");
            Assert.IsFalse(client.sessionValues.ContainsKey(badgeId), "the declaration value is not Session data");

            static ClassTypeInfo BadgeType() => new()
            {
                type = MemberKind.Class,
                required = true,
                classId = "badge-class"
            };
        }

        [Test]
        public void ConstructorArgumentImmutableMemberReadsItsReplayInSession()
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
