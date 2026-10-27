// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using NUnit.Framework;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Member = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    /// <summary>
    /// Shared fixture for the class-generics suite
    /// (specs/class-generics.md §9). Mirrors the spec's motivating
    /// shape: an open abstract base with a <c>T</c> member and a
    /// <c>List&lt;T&gt;</c> member, a forwarding middle, and closed leaves
    /// binding Float / String:
    ///
    /// <code>
    ///   Card                                   (non-generic root)
    ///   EnchantCard&lt;T&gt; : Card            Speed: T, Values: List&lt;T&gt;
    ///   MiddleCard&lt;U&gt;  : EnchantCard&lt;U&gt;   (forwards T → U)
    ///   DamageCard      : MiddleCard&lt;Float(required, default 3.5)&gt;
    ///   StringCard      : EnchantCard&lt;String(optional, literal)&gt;
    ///   OptionalFloatCard : EnchantCard&lt;Float(optional)&gt;
    /// </code>
    /// </summary>
    internal static class NeoGenericTestFixture
    {
        public const string ParamT = "param-t";
        public const string ParamU = "param-u";

        /// <summary>DamageCard's and the constructed slot's <c>float</c>.</summary>
        public static readonly TypeBinding FloatArgument = TestTypeBindings.Of(MemberKind.Float);
        public static readonly TypeBinding StringArgument = TestTypeBindings.Of(MemberKind.String, required: false);
        public static readonly TypeBinding OptionalFloatArgument = TestTypeBindings.Of(MemberKind.Float, required: false);

        /// <summary>The fixture class declaring <paramref name="paramId"/>, forwarding it.</summary>
        public static TypeBinding Forward(string paramId) =>
            TestTypeBindings.Open(paramId == ParamT ? "class-enchant" : "class-middle", paramId);

        /// <summary>T bound to the schema's float member, whose own fields substitution must partition.</summary>
        public static Dictionary<string, NeoGenericEnvEntry> FloatMemberEnv() =>
            new Dictionary<string, NeoGenericEnvEntry>
            {
                [ParamT] = NeoGenericEnvEntry.Bound("member-binding-float"),
                [ParamU] = NeoGenericEnvEntry.Bound("member-binding-float"),
            };

        public static ProjectData BuildProjectData()
        {
            var rootClass = new NeoSchemaClass
            {
                id = "root-class",
                projectId = "project-a",
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            var saveRootClass = new NeoSchemaClass
            {
                id = "save-root-class",
                projectId = "project-a",
                name = "Save Root",
                schema = new Dictionary<string, string>
                {
                    ["Card"] = "member-card",
                    ["StringCard"] = "member-string-card",
                    ["Constructed"] = "member-constructed-slot",
                },
            };
            var cardBase = new NeoSchemaClass
            {
                id = "class-card-base",
                projectId = "project-a",
                name = "Card",
                schema = new Dictionary<string, string>(),
            };
            var enchant = new NeoSchemaClass
            {
                id = "class-enchant",
                projectId = "project-a",
                name = "EnchantCard",
                extendsClassId = cardBase.id,
                Modifier = NeoClassModifierKind.Abstract,
                schema = new Dictionary<string, string>
                {
                    ["Speed"] = "member-speed",
                    ["Values"] = "member-values",
                },
                genericParams = new List<GenericParamDeclaration>
                {
                    new() { id = ParamT, name = "T" },
                },
            };
            var middle = new NeoSchemaClass
            {
                id = "class-middle",
                projectId = "project-a",
                name = "MiddleCard",
                extendsClassId = enchant.id,
                schema = new Dictionary<string, string>(),
                genericParams = new List<GenericParamDeclaration>
                {
                    new() { id = ParamU, name = "U" },
                },
                extendsGenericBindings = new Dictionary<string, TypeBinding>
                {
                    [ParamT] = NeoGenericTestFixture.Forward(ParamU),
                },
            };
            var damage = new NeoSchemaClass
            {
                id = "class-damage",
                projectId = "project-a",
                name = "DamageCard",
                extendsClassId = middle.id,
                schema = new Dictionary<string, string>(),
                extendsGenericBindings = new Dictionary<string, TypeBinding>
                {
                    [ParamU] = NeoGenericTestFixture.FloatArgument,
                },
            };
            var stringCard = new NeoSchemaClass
            {
                id = "class-string-card",
                projectId = "project-a",
                name = "StringCard",
                extendsClassId = enchant.id,
                schema = new Dictionary<string, string>(),
                extendsGenericBindings = new Dictionary<string, TypeBinding>
                {
                    [ParamT] = NeoGenericTestFixture.StringArgument,
                },
            };
            var optionalFloatCard = new NeoSchemaClass
            {
                id = "class-optional-float",
                projectId = "project-a",
                name = "OptionalFloatCard",
                extendsClassId = enchant.id,
                schema = new Dictionary<string, string>(),
                extendsGenericBindings = new Dictionary<string, TypeBinding>
                {
                    [ParamT] = NeoGenericTestFixture.OptionalFloatArgument,
                },
            };

            return new ProjectData
            {
                project = new Project
                {
                    id = "project-a",
                    _id = "project-a",
                    name = "Class Generics",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = new Dictionary<string, Member>
                {
                    ["root-assets"] = RootMember("root-assets", "root-assets-value", rootClass.id),
                    ["root-save"] = RootMember("root-save", "root-save-value", saveRootClass.id),
                    ["root-session"] = RootMember("root-session", "root-session-value", rootClass.id),
                    ["member-card"] = new ClassMember
                    {
                        id = "member-card",
                        projectId = "project-a",
                        name = "Card",
                        kind = MemberKind.Class,
                        DeclaredClassId = damage.id,
                        Requirement = NeoMemberRequirementKind.Required,
                    },
                    // A reference-type binding, as `Money` in P127 §4.2.
                    ["member-money"] = new ClassMember
                    {
                        id = "member-money",
                        projectId = "project-a",
                        name = "Money",
                        kind = MemberKind.Class,
                        DeclaredClassId = "class-card-base",
                        Requirement = NeoMemberRequirementKind.Required,
                    },
                    ["member-string-card"] = new ClassMember
                    {
                        id = "member-string-card",
                        projectId = "project-a",
                        name = "StringCard",
                        kind = MemberKind.Class,
                        DeclaredClassId = stringCard.id,
                        Requirement = NeoMemberRequirementKind.Required,
                    },
                    ["member-speed"] = new GenericMember
                    {
                        id = "member-speed",
                        projectId = "project-a",
                        name = "Speed",
                        kind = MemberKind.Generic,
                        genericParamId = ParamT,
                        // Deliberately differs from the binding's modifier so
                        // the partition test can prove slot ownership.
                        Access = NeoMemberAccessKind.Protected,
                        Modifier = NeoMemberModifierKind.Abstract,
                    },
                    ["member-values"] = new ListMember
                    {
                        id = "member-values",
                        projectId = "project-a",
                        name = "Values",
                        kind = MemberKind.List,
                        entryMemberId = "member-values-entry",
                    },
                    ["member-values-entry"] = new GenericMember
                    {
                        id = "member-values-entry",
                        projectId = "project-a",
                        name = "Value",
                        kind = MemberKind.Generic,
                        genericParamId = ParamT,
                    },
                    ["member-plain-list"] = new ListMember
                    {
                        id = "member-plain-list",
                        projectId = "project-a",
                        name = "PlainList",
                        kind = MemberKind.List,
                        entryMemberId = "member-binding-string",
                    },
                    ["member-binding-float"] = new FloatMember
                    {
                        id = "member-binding-float",
                        projectId = "project-a",
                        name = "FloatBinding",
                        kind = MemberKind.Float,
                        Requirement = NeoMemberRequirementKind.Required,
                        minValue = 0f,
                        Access = NeoMemberAccessKind.Public,
                        // The binding's storage must NOT leak into the slot
                        // (substitution partition — Decision 10).
                        Storage = NeoMemberStorage.Save,
                        defaultValue = new NumberMemberValueBase { value = 3.5 },
                        Modifier = NeoMemberModifierKind.Virtual,
                    },
                    ["member-binding-float-optional"] = new FloatMember
                    {
                        id = "member-binding-float-optional",
                        projectId = "project-a",
                        name = "OptionalFloatBinding",
                        kind = MemberKind.Float,
                        Requirement = NeoMemberRequirementKind.Optional,
                    },
                    ["member-binding-string"] = new StringMember
                    {
                        id = "member-binding-string",
                        projectId = "project-a",
                        name = "StringBinding",
                        kind = MemberKind.String,
                        Requirement = NeoMemberRequirementKind.Optional,
                        Format = NeoStringFormatKind.Plain,
                    },
                    ["member-constructed-slot"] = new ClassMember
                    {
                        id = "member-constructed-slot",
                        projectId = "project-a",
                        name = "ConstructedSlot",
                        kind = MemberKind.Class,
                        DeclaredClassId = enchant.id,
                        classArguments = new Dictionary<string, TypeBinding>
                        {
                            [ParamT] = NeoGenericTestFixture.FloatArgument,
                        },
                    },
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["root-assets-value"] = ObjectValue("root-assets-value", rootClass.id, new()),
                    ["root-save-value"] = ObjectValue(
                        "root-save-value",
                        saveRootClass.id,
                        new Dictionary<string, string>
                        {
                            ["Card"] = "card-value",
                            ["StringCard"] = "string-card-value",
                            ["Constructed"] = "constructed-value",
                        }),
                    ["root-session-value"] = ObjectValue("root-session-value", rootClass.id, new()),
                    ["card-value"] = ObjectValue("card-value", damage.id, new()),
                    ["string-card-value"] = ObjectValue("string-card-value", stringCard.id, new()),
                    // A constructed-slot instance: no named subclass exists,
                    // so the row carries `classId: null` (the DECLARED open
                    // type) — the slot's arguments close it (§4.1).
                    ["constructed-value"] = ObjectValue(
                        "constructed-value",
                        null,
                        new Dictionary<string, string>
                        {
                            ["Speed"] = "constructed-speed-value",
                        }),
                    ["constructed-speed-value"] = new NumberMemberValue
                    {
                        id = "constructed-speed-value",
                        value = 7.25,
                    },
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [rootClass.id] = rootClass,
                    [saveRootClass.id] = saveRootClass,
                    [cardBase.id] = cardBase,
                    [enchant.id] = enchant,
                    [middle.id] = middle,
                    [damage.id] = damage,
                    [stringCard.id] = stringCard,
                    [optionalFloatCard.id] = optionalFloatCard,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            }.WithUserRoot();
        }

        private static ClassMember RootMember(string id, string valueId, string classId)
        {
            return new ClassMember
            {
                id = id,
                projectId = "project-a",
                name = id,
                kind = MemberKind.Class,
                Requirement = NeoMemberRequirementKind.Required,
                valueId = valueId,
                DeclaredClassId = classId,
            };
        }

        private static ObjectMemberValue ObjectValue(
            string id,
            string? classId,
            Dictionary<string, string> record)
        {
            return new ObjectMemberValue
            {
                id = id,
                classId = classId,
                value = record,
            };
        }
    }

    /// <summary>
    /// specs/class-generics.md §9: environment resolution (forward
    /// chains included), the Decision-10 substitution field partition, the
    /// Decision-9 stamp lifecycle, constructed-slot admission, and the JSON
    /// read layer for the new wire fields. The dotnet side mirrors
    /// <c>src/models/classes/generics.ts</c> — behavioral assertions
    /// here should track that module's colocated tests.
    /// </summary>
    public class NeoGenericResolutionTests
    {
        private static NeoClient LoadClient()
        {
            return NeoTestSaveStack.ClientFromSchema(NeoGenericTestFixture.BuildProjectData());
        }

        // ------------------------------------------------------------------
        // JSON layer.
        // ------------------------------------------------------------------

        [Test]
        public void Json_NeoSchemaClass_ReadsGenericParamsAndBindings()
        {
            const string json = @"{
                ""id"": ""t1"", ""projectId"": ""p"", ""name"": ""Foo"",
                ""schema"": {},
                ""extendsClassId"": ""t0"",
                ""genericParams"": [
                    { ""id"": ""p1"", ""name"": ""T"" },
                    { ""id"": ""p2"", ""name"": ""U"",
                      ""constraints"": [{ ""kind"": 1, ""enumId"": ""e1"" }] }
                ],
                ""extendsGenericBindings"": {
                    ""pp1"": { ""kind"": 1, ""owner"": { ""type"": 7, ""classId"": ""t1"" }, ""param"": ""p1"" },
                    ""pp2"": { ""kind"": 0, ""type"": { ""type"": 2, ""required"": true } }
                }
            }";
            var schemaClass = JsonConvert.DeserializeObject<NeoSchemaClass>(json)!;

            Assert.AreEqual(2, schemaClass.genericParams!.Count);
            Assert.AreEqual("p1", schemaClass.genericParams[0].id);
            Assert.IsNull(schemaClass.genericParams[0].constraints);
            Assert.AreEqual(NeoGenericParamConstraintKind.Enum, schemaClass.genericParams[1].constraints![0].kind);
            Assert.AreEqual("e1", schemaClass.genericParams[1].constraints![0].enumId);

            Assert.AreEqual(2, schemaClass.extendsGenericBindings!.Count);
            Assert.AreEqual(TestTypeBindings.Open("t1", "p1"), schemaClass.extendsGenericBindings["pp1"]);
            Assert.AreEqual(TestTypeBindings.Of(MemberKind.Int), schemaClass.extendsGenericBindings["pp2"]);
        }

        [Test]
        public void Json_NeoSchemaClass_AbsentGenericFieldsStayNull()
        {
            const string json = @"{""id"": ""t1"", ""projectId"": ""p"", ""name"": ""Foo"", ""schema"": {}}";
            var schemaClass = JsonConvert.DeserializeObject<NeoSchemaClass>(json)!;
            Assert.IsNull(schemaClass.genericParams);
            Assert.IsNull(schemaClass.extendsGenericBindings);
        }

        [Test]
        public void Json_GenericMember_ReadsOrdinal21()
        {
            const string json = @"{""id"": ""a1"", ""projectId"": ""p"", ""name"": ""Slot"", ""kind"": 21, ""genericParamId"": ""p1""}";
            var member = JsonConvert.DeserializeObject<Member>(json);
            Assert.IsInstanceOf<GenericMember>(member);
            Assert.AreEqual("p1", ((GenericMember)member!).genericParamId);
        }

        [Test]
        public void Json_MemberModifierPreservesAbsenceAndNumericValues()
        {
            const string absentJson = @"{""id"": ""a1"", ""projectId"": ""p"", ""name"": ""Name"", ""kind"": 5}";
            const string virtualJson = @"{""id"": ""a2"", ""projectId"": ""p"", ""name"": ""Name"", ""kind"": 5, ""modifier"": 0}";
            const string abstractJson = @"{""id"": ""a3"", ""projectId"": ""p"", ""name"": ""Name"", ""kind"": 5, ""modifier"": 2}";

            var absent = JsonConvert.DeserializeObject<Member>(absentJson)!;
            var virtualMember = JsonConvert.DeserializeObject<Member>(virtualJson)!;
            var abstractMember = JsonConvert.DeserializeObject<Member>(abstractJson)!;

            Assert.IsNull(absent.DeclaredModifier);
            Assert.AreEqual(NeoMemberModifierKind.Virtual, absent.Modifier);
            Assert.AreEqual(NeoMemberModifierKind.Virtual, virtualMember.DeclaredModifier);
            Assert.AreEqual(NeoMemberModifierKind.Abstract, abstractMember.DeclaredModifier);
        }

        [Test]
        public void Json_ClassMember_ReadsClassArguments()
        {
            const string json = @"{
                ""id"": ""a1"", ""projectId"": ""p"", ""name"": ""Slot"", ""kind"": 7,
                ""classId"": ""t1"",
                ""classArguments"": {
                    ""p1"": { ""kind"": 0, ""type"": { ""type"": 7, ""required"": true, ""classId"": ""t2"" } }
                }
            }";
            var member = JsonConvert.DeserializeObject<Member>(json);
            Assert.IsInstanceOf<ClassMember>(member);
            var classMember = (ClassMember)member!;
            Assert.AreEqual(1, classMember.classArguments!.Count);
            Assert.AreEqual(TestTypeBindings.Class("t2"), classMember.classArguments["p1"]);
        }

        [Test]
        public void Json_ValueRow_RoundTripsGenericBindingsStamp()
        {
            const string json = @"{""id"": ""v1"", ""value"": [], ""genericBindings"": {""p1"": {""kind"": 0, ""type"": {""type"": 2, ""required"": true}}}}";
            var row = JsonConvert.DeserializeObject<MemberValue>(json)!;
            Assert.AreEqual(TestTypeBindings.Of(MemberKind.Int), row.genericBindings!["p1"]);

            string serialized = JsonConvert.SerializeObject(row);
            var reparsed = JsonConvert.DeserializeObject<MemberValue>(serialized)!;
            Assert.AreEqual(TestTypeBindings.Of(MemberKind.Int), reparsed.genericBindings!["p1"]);
        }

        [Test]
        public void Json_UnknownBindingKind_Throws()
        {
            const string json = @"{""kind"": ""wildcard""}";
            Assert.Throws<JsonSerializationException>(
                () => JsonConvert.DeserializeObject<TypeBinding>(json));
        }

        // ------------------------------------------------------------------
        // Environment resolution (forward chains).
        // ------------------------------------------------------------------

        [Test]
        public void ResolveEnv_ClosedLeaf_ResolvesThroughForwardChain()
        {
            var client = LoadClient();
            var env = NeoGenericResolution.ResolveEnv(client, "class-damage");

            // T (declared by EnchantCard) forwards through MiddleCard's U to
            // the leaf's concrete binding; U resolves to the same terminal.
            Assert.IsTrue(env[NeoGenericTestFixture.ParamT].IsBound);
            Assert.AreEqual(NeoGenericTestFixture.FloatArgument, NeoTypeBindingViews.BindingOf(env[NeoGenericTestFixture.ParamT].memberId!));
            Assert.IsTrue(env[NeoGenericTestFixture.ParamU].IsBound);
            Assert.AreEqual(NeoGenericTestFixture.FloatArgument, NeoTypeBindingViews.BindingOf(env[NeoGenericTestFixture.ParamU].memberId!));
            Assert.IsTrue(NeoGenericResolution.IsClosedClass(client, "class-damage"));
        }

        [Test]
        public void ResolveEnv_ForwardingMiddle_LeavesDeepestForwardTargetUnbound()
        {
            var client = LoadClient();
            var env = NeoGenericResolution.ResolveEnv(client, "class-middle");

            // T's terminal at MiddleCard is the forward target U — the param
            // a further descendant must bind.
            Assert.IsFalse(env[NeoGenericTestFixture.ParamT].IsBound);
            Assert.AreEqual(NeoGenericTestFixture.ParamU, env[NeoGenericTestFixture.ParamT].unboundParamId);
            Assert.IsFalse(env[NeoGenericTestFixture.ParamU].IsBound);
            Assert.IsFalse(NeoGenericResolution.IsClosedClass(client, "class-middle"));
        }

        [Test]
        public void SubstituteMember_ConcreteScalarDoesNotAllocate()
        {
            using var client = LoadClient();
            var member = client.members["member-binding-float"];
            var env = NeoGenericResolution.ResolveEnv(client, "class-damage");
            Assert.That(NeoGenericResolution.SubstituteMember(client, member, env), Is.SameAs(member));
            Assert.That((TestDelegate)(() => { NeoGenericResolution.SubstituteMember(client, member, env); }),
                new NUnit.Framework.Constraints.NotConstraint(
                    new UnityEngine.TestTools.Constraints.AllocatingGCMemoryConstraint()));
        }

        [Test]
        public void ResolveEnv_NonGenericChain_IsEmptyAndClosed()
        {
            var client = LoadClient();
            Assert.AreEqual(0, NeoGenericResolution.ResolveEnv(client, "class-card-base").Count);
            Assert.IsTrue(NeoGenericResolution.IsClosedClass(client, "class-card-base"));
        }

        // ------------------------------------------------------------------
        // Instance env overlay (spec §4.1).
        // ------------------------------------------------------------------

        [Test]
        public void ResolveInstanceEnv_OverlaysConstructedSlotArgumentsOverOpenChainEnv()
        {
            var client = LoadClient();
            var slot = (ClassMember)client.members["member-constructed-slot"];

            // EnchantCard's chain leaves T unbound; the slot's Float
            // argument is the terminal binding at the usage site.
            var env = NeoGenericResolution.ResolveInstanceEnv(
                client, "class-enchant", slot.classArguments);

            Assert.IsTrue(env[NeoGenericTestFixture.ParamT].IsBound);
            Assert.AreEqual(NeoGenericTestFixture.FloatArgument, NeoTypeBindingViews.BindingOf(env[NeoGenericTestFixture.ParamT].memberId!));
            Assert.IsNull(NeoGenericResolution.FirstUnboundParamId(env));
        }

        [Test]
        public void ResolveInstanceEnv_ChainBindingsWinOverSlotArguments()
        {
            var client = LoadClient();

            // A named closed subclass picked into a constructed slot: the
            // chain's own binding stays authoritative (admission guarantees
            // signature equality, so they agree — here they deliberately
            // differ to prove precedence).
            var env = NeoGenericResolution.ResolveInstanceEnv(
                client,
                "class-damage",
                new Dictionary<string, TypeBinding>
                {
                    [NeoGenericTestFixture.ParamT] = NeoGenericTestFixture.StringArgument,
                });

            Assert.IsTrue(env[NeoGenericTestFixture.ParamT].IsBound);
            Assert.AreEqual(NeoGenericTestFixture.FloatArgument, NeoTypeBindingViews.BindingOf(env[NeoGenericTestFixture.ParamT].memberId!));
        }

        [Test]
        public void ResolveInstanceEnv_ForwardsStayUnbound_AndIdentityWithoutArguments()
        {
            var client = LoadClient();

            // Generic-kind arguments (still-open forwards) stay unbound —
            // enclosing contexts substitute the slot before descent.
            var forwarded = NeoGenericResolution.ResolveInstanceEnv(
                client,
                "class-enchant",
                new Dictionary<string, TypeBinding>
                {
                    [NeoGenericTestFixture.ParamT] = NeoGenericTestFixture.Forward(NeoGenericTestFixture.ParamU),
                });
            Assert.AreEqual(
                NeoGenericTestFixture.ParamT,
                NeoGenericResolution.FirstUnboundParamId(forwarded));

            // Identity with ResolveEnv for slots without arguments.
            var bare = NeoGenericResolution.ResolveInstanceEnv(client, "class-enchant", null);
            Assert.AreEqual(
                NeoGenericTestFixture.ParamT,
                NeoGenericResolution.FirstUnboundParamId(bare));
        }

        // ------------------------------------------------------------------
        // Substitution (Decision-10 field partition).
        // ------------------------------------------------------------------

        [Test]
        public void SubstituteMember_PartitionsSlotAndBindingFields()
        {
            var client = LoadClient();
            var env = NeoGenericTestFixture.FloatMemberEnv();
            var slot = client.members["member-speed"];

            var substituted = NeoGenericResolution.SubstituteMember(client, slot, env);

            Assert.IsInstanceOf<FloatMember>(substituted);
            // Slot identity/placement fields win.
            Assert.AreEqual("member-speed", substituted.id);
            Assert.AreEqual("Speed", substituted.name);
            Assert.AreEqual(NeoMemberAccessKind.Protected, substituted.Access,
                "the slot's accessibility must not come from its binding");
            Assert.AreEqual(false, substituted.Modifier == NeoMemberModifierKind.Virtual,
                "the slot's virtual declaration must not come from its binding");
            Assert.AreEqual(true, substituted.Modifier == NeoMemberModifierKind.Abstract,
                "the slot's abstract declaration must not come from its binding");
            Assert.AreEqual(NeoMemberStorage.Inherit, substituted.DeclaredStorage,
                "the binding's storage declaration must not leak into the slot");
            Assert.IsNull(substituted.extendsMemberId);
            // The binding supplies type/config/required, never a default.
            var substitutedFloat = (FloatMember)substituted;
            Assert.IsTrue(substitutedFloat.Requirement == NeoMemberRequirementKind.Required);
            Assert.AreEqual(0f, substitutedFloat.minValue);
            Assert.IsNull(substitutedFloat.defaultValue);
        }

        /// <summary>
        /// P127 §4.2. A <c>T?</c> slot or signature type closes over its
        /// binding alone: a value kind keeps its own nullability
        /// (<c>IEquatable&lt;int&gt;.Equals(T? other)</c> takes an
        /// <c>int</c>), and a reference-like binding takes the <c>?</c>
        /// (<c>Money</c> closes to <c>Money?</c>).
        /// </summary>
        [TestCase("member-binding-float", true, NeoMemberRequirementKind.Required)]
        [TestCase("member-binding-float-optional", true, NeoMemberRequirementKind.Optional)]
        [TestCase("member-money", true, NeoMemberRequirementKind.Optional)]
        [TestCase("member-money", false, NeoMemberRequirementKind.Required)]
        [TestCase("member-binding-string", false, NeoMemberRequirementKind.Optional)]
        public void NullableTypeParametersCloseOverTheirBinding(string bindingId, bool declaredNullable, NeoMemberRequirementKind expected)
        {
            using NeoClient client = LoadClient();
            var slot = (GenericMember)client.members["member-speed"];
            slot.Requirement = declaredNullable ? NeoMemberRequirementKind.Optional : NeoMemberRequirementKind.Required;
            NeoMemberShapeResolution.ResolveAll(client.members);
            var env = new Dictionary<string, NeoGenericEnvEntry>
            {
                [NeoGenericTestFixture.ParamT] = NeoGenericEnvEntry.Bound(bindingId),
            };
            bool required = expected == NeoMemberRequirementKind.Required;

            Assert.AreEqual(expected, NeoGenericResolution.SubstituteMember(client, slot, env).Requirement, "storage");
            var type = new GenericTypeInfo
            {
                type = MemberKind.Generic,
                required = !declaredNullable,
                ownerClassId = "class-enchant",
                genericParamId = NeoGenericTestFixture.ParamT,
            };
            Assert.AreEqual(required, NeoGenericResolution.SubstituteTypeInfo(client, type, env).required, "return type");
            var parameter = new FunctionArgumentTypeInfo
            {
                type = MemberKind.Generic,
                required = !declaredNullable,
                name = "other",
                ownerClassId = "class-enchant",
                genericParamId = NeoGenericTestFixture.ParamT,
            };
            Assert.AreEqual(required, NeoGenericResolution.SubstituteTypeInfo(client, parameter, env).required, "parameter");
        }

        [TestCase(MemberKind.Int, true, true)]
        [TestCase(MemberKind.Int, false, false)]
        [TestCase(MemberKind.String, true, false)]
        public void NullableMethodTypeParametersCloseOverTheirArgument(MemberKind kind, bool argumentRequired, bool expected)
        {
            var table = new TypeInfo[] { new PrimitiveTypeInfo { type = kind, required = argumentRequired } };
            var type = new FunctionGenericTypeInfo { type = MemberKind.Generic, required = false, ownerMemberId = "m", index = 0 };
            var parameter = new FunctionArgumentTypeInfo { type = MemberKind.Generic, required = false, name = "other", ownerMemberId = "m", index = 0 };

            Assert.AreEqual(expected, NeoCompose.Runtime.NeoScript.NeoFunctionTypeArguments.Bind(type, table).required);
            Assert.AreEqual(expected, NeoCompose.Runtime.NeoScript.NeoFunctionTypeArguments.Bind(parameter, table).required);
        }

        private enum Mood
        {
            Calm,
        }

        /// <summary>
        /// P127 §4.2 in generated code. A <c>T</c> slot takes its type
        /// argument's own nullability, and a <c>T?</c> slot is nullable
        /// unless the argument is a non-nullable value type: <c>T?</c> over
        /// <c>int</c> holds an <c>int</c>, while <c>int?</c> is
        /// <c>Nullable&lt;int&gt;</c> and optional either way.
        /// </summary>
        [TestCase(typeof(int), false, true)]
        [TestCase(typeof(int), true, true)]
        [TestCase(typeof(int?), false, false)]
        [TestCase(typeof(int?), true, false)]
        [TestCase(typeof(string), false, true)]
        [TestCase(typeof(string), true, false)]
        [TestCase(typeof(Mood), false, true)]
        [TestCase(typeof(Mood), true, true)]
        public void GeneratedSlotsCloseTheirRequirementOverTheirArgument(Type argument, bool declaredNullable, bool expected)
        {
            using NeoClient client = LoadClient();
            client.TypeArguments.Register(
                new Dictionary<Type, string>(),
                new Dictionary<Type, string> { [typeof(Mood)] = "enum-mood" });
            var close = typeof(NeoGenericResolutionTests).GetMethod(nameof(CloseSlot), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(argument);

            var (required, elementRequired) = ((bool, bool))close.Invoke(null, new object[] { client, declaredNullable })!;

            Assert.AreEqual(expected, required, "helper");
            Assert.AreEqual(expected, elementRequired, "sequence element");
        }

        private static (bool, bool) CloseSlot<T>(NeoClient client, bool declaredNullable) => (
            NeoGeneratedTypesSupport.ClosedSlotRequired<T>(client, declaredNullable),
            NeoSequences.ElementType<T>(client, declaredNullable).required);

        [Test]
        public void SubstituteMember_AbsentSlotShapeClearsBindingModifier()
        {
            var client = LoadClient();
            var env = NeoGenericTestFixture.FloatMemberEnv();
            var slot = client.members["member-speed"];
            slot.DeclaredModifier = null;
            var binding = client.members["member-binding-float"];
            binding.DeclaredModifier = NeoMemberModifierKind.Abstract;
            NeoMemberShapeResolution.ResolveAll(client.members);

            var substituted = NeoGenericResolution.SubstituteMember(client, slot, env);

            Assert.AreEqual(NeoMemberModifierKind.Virtual, substituted.Modifier,
                "an absent slot declaration must use the default Virtual modifier");
            Assert.AreNotEqual(NeoMemberModifierKind.Abstract, substituted.Modifier,
                "an absent slot declaration must clear the binding's Abstract modifier");
        }

        [Test]
        public void SubstituteMember_PreservesGenericSlotInitializer()
        {
            using NeoClient client = LoadClient();
            var slot = (GenericMember)client.members["member-speed"];
            slot.defaultValue = new NullMemberValueBase
            {
                init = new InitializerBody { code = "value" },
            };
            var env = NeoGenericResolution.ResolveEnv(client, "class-damage");

            var substituted = (FloatMember)NeoGenericResolution.SubstituteMember(
                client,
                client.members[slot.id],
                env);

            Assert.AreEqual("value", substituted.defaultValue?.init?.code);
            Assert.AreSame(slot.defaultValue.init, substituted.defaultValue?.init);
            Assert.IsNull(substituted.defaultValue?.value,
                "the binding's literal fallback must not replace the declaration initializer");
            Assert.AreEqual(NeoMemberRequirementKind.Required, substituted.Requirement,
                "the concrete binding still owns the slot's nullability");
        }

        [TestCase(null)]
        [TestCase(8.25)]
        public void SubstituteMember_PreservesDeclarationLiteral(double? value)
        {
            using var client = LoadClient();
            var slot = (GenericMember)client.members["member-speed"];
            slot.defaultValue = new NullMemberValueBase { value = value };
            var substituted = (FloatMember)NeoGenericResolution.SubstituteMember(
                client, slot, NeoGenericResolution.ResolveEnv(client, "class-damage"));
            Assert.IsNotNull(substituted.defaultValue);
            Assert.AreEqual(value, substituted.defaultValue!.value);
        }

        [Test]
        public void SubstituteMember_ConcretizesPartiallyGenericClassBinding()
        {
            ProjectData data = NeoGenericTestFixture.BuildProjectData();
            data.members["member-forwarded-class-binding"] = new ClassMember
            {
                id = "member-forwarded-class-binding",
                projectId = "project-a",
                name = "ForwardedClassBinding",
                kind = MemberKind.Class,
                DeclaredClassId = "class-enchant",
                classArguments = new Dictionary<string, TypeBinding>
                {
                    [NeoGenericTestFixture.ParamT] = NeoGenericTestFixture.Forward(NeoGenericTestFixture.ParamU),
                },
            };
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            var env = new Dictionary<string, NeoGenericEnvEntry>
            {
                [NeoGenericTestFixture.ParamT] =
                    NeoGenericEnvEntry.Bound("member-forwarded-class-binding"),
                [NeoGenericTestFixture.ParamU] =
                    NeoGenericEnvEntry.Bound("member-binding-float"),
            };

            var substituted = (ClassMember)NeoGenericResolution.SubstituteMember(
                client,
                client.members["member-speed"],
                env);

            Assert.AreEqual("member-speed", substituted.id);
            Assert.AreEqual("Speed", substituted.name);
            Assert.AreEqual(
                NeoGenericTestFixture.FloatArgument,
                substituted.classArguments![NeoGenericTestFixture.ParamT]);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SubstituteMember_ConcretizesDelegateAndActionTypePositions(bool useRequired)
        {
            using NeoClient client = LoadClient();
            var env = new Dictionary<string, NeoGenericEnvEntry>
            {
                [NeoGenericTestFixture.ParamT] =
                    NeoGenericEnvEntry.Bound("member-binding-float"),
                [NeoGenericTestFixture.ParamU] =
                    NeoGenericEnvEntry.Bound("member-binding-string"),
            };
            var callable = new DelegateMember
            {
                id = "member-callable",
                projectId = "project-a",
                name = "Callable",
                kind = MemberKind.NSDelegate,
                returnTypeInfo = new GenericTypeInfo
                {
                    type = MemberKind.Generic,
                    required = useRequired,
                    ownerClassId = "class-middle",
                    genericParamId = NeoGenericTestFixture.ParamT,
                },
                argumentTypes = new[]
                {
                    new FunctionArgumentTypeInfo
                    {
                        name = "values",
                        type = MemberKind.List,
                        entryTypeInfo = new GenericTypeInfo
                        {
                            type = MemberKind.Generic,
                            required = useRequired,
                            ownerClassId = "class-middle",
                            genericParamId = NeoGenericTestFixture.ParamU,
                        },
                    },
                },
            };
            var action = new ActionMember
            {
                id = "member-action",
                projectId = "project-a",
                name = "Action",
                kind = MemberKind.NSAction,
                argumentTypes = new[]
                {
                    new FunctionArgumentTypeInfo
                    {
                        name = "value",
                        type = MemberKind.Generic,
                        required = useRequired,
                        ownerClassId = "class-middle",
                        genericParamId = NeoGenericTestFixture.ParamT,
                    },
                },
            };

            var substitutedDelegate = (DelegateMember)
                NeoGenericResolution.SubstituteMember(client, callable, env);
            var substitutedAction = (ActionMember)
                NeoGenericResolution.SubstituteMember(client, action, env);

            Assert.AreEqual(MemberKind.Float, substitutedDelegate.returnTypeInfo.type);
            // P127 §4.2: a value binding keeps its own nullability under T?.
            Assert.IsTrue(substitutedDelegate.returnTypeInfo.required);
            Assert.AreEqual("values", substitutedDelegate.argumentTypes[0].name);
            Assert.AreEqual(MemberKind.String,
                substitutedDelegate.argumentTypes[0].entryTypeInfo!.type);
            Assert.IsFalse(
                substitutedDelegate.argumentTypes[0].entryTypeInfo!.required);
            Assert.AreEqual("value", substitutedAction.argumentTypes[0].name);
            Assert.AreEqual(MemberKind.Float,
                substitutedAction.argumentTypes[0].type);
            Assert.IsTrue(substitutedAction.argumentTypes[0].required);
            Assert.AreEqual(MemberKind.Generic, callable.returnTypeInfo.type,
                "substitution must not mutate the declared open signature");
        }

        [Test]
        public void SubstituteMember_ConcretizesOnlyVariantTargetType()
        {
            ProjectData data = NeoGenericTestFixture.BuildProjectData();
            data.members["member-binding-class"] = new ClassMember
            {
                id = "member-binding-class",
                projectId = "project-a",
                name = "ClassBinding",
                kind = MemberKind.Class,
                DeclaredClassId = "class-card-base",
                Requirement = NeoMemberRequirementKind.Required,
            };
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            var env = new Dictionary<string, NeoGenericEnvEntry>
            {
                [NeoGenericTestFixture.ParamT] =
                    NeoGenericEnvEntry.Bound("member-binding-class"),
                [NeoGenericTestFixture.ParamU] =
                    NeoGenericEnvEntry.Bound("member-binding-float"),
            };
            var valueTypeInfo = new GenericTypeInfo
            {
                type = MemberKind.Generic,
                ownerClassId = "class-middle",
                genericParamId = NeoGenericTestFixture.ParamU,
            };
            var variant = new VariantMember
            {
                id = "member-variant",
                projectId = "project-a",
                name = "Variant",
                kind = MemberKind.Variant,
                targetTypeInfo = new GenericTypeInfo
                {
                    type = MemberKind.Generic,
                    ownerClassId = "class-middle",
                    genericParamId = NeoGenericTestFixture.ParamT,
                },
                valueTypeInfo = valueTypeInfo,
            };

            var substituted = (VariantMember)
                NeoGenericResolution.SubstituteMember(client, variant, env);

            var target = (ClassTypeInfo)substituted.targetTypeInfo!;
            Assert.AreEqual("class-card-base", target.classId);
            Assert.IsTrue(target.required);
            Assert.AreSame(valueTypeInfo, substituted.valueTypeInfo,
                "the canonical web substitution changes only targetTypeInfo");
            Assert.AreEqual(MemberKind.Generic, variant.targetTypeInfo!.type,
                "substitution must not mutate the declared open target");
        }

        [Test]
        public void MemberShapeResolution_GenericOverrideInheritsDefaultButUsesBindingRequirement()
        {
            var inherited = new FloatMember
            {
                id = "member-inherited-float",
                projectId = "project-a",
                name = "Inherited",
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new NumberMemberValueBase { value = 4.5 },
            };
            var generic = new GenericMember
            {
                id = "member-generic-override",
                projectId = "project-a",
                name = "Generic",
                extendsMemberId = inherited.id,
                genericParamId = NeoGenericTestFixture.ParamT,
            };
            var members = new Dictionary<string, Member>
            {
                [inherited.id] = inherited,
                [generic.id] = generic,
            };

            NeoMemberShapeResolution.ResolveAll(members);

            Assert.AreEqual(
                NeoMemberRequirementKind.Optional,
                generic.Requirement);
            Assert.AreEqual(4.5, generic.defaultValue!.value);
        }

        [TestCase(false, false, true)]
        [TestCase(true, false, true)]
        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, true)]
        [TestCase(true, true, true)]
        public void NullDefaultCarrierInheritanceSurvivesRoundTrip(bool generic, bool literalNull, bool baseHasDefault)
        {
            Member parent = generic
                ? new GenericMember
                {
                    id = "default-base",
                    genericParamId = NeoGenericTestFixture.ParamT,
                    defaultValue = baseHasDefault ? new NullMemberValueBase { value = 4.5 } : null
                }
                : new FloatMember
                {
                    id = "default-base",
                    kind = MemberKind.Float,
                    defaultValue = baseHasDefault ? new NumberMemberValueBase { value = 4.5 } : null
                };
            var wire = new Newtonsoft.Json.Linq.JObject
            {
                ["id"] = "default-override",
                ["extendsMemberId"] = parent.id,
                ["kind"] = (int)(generic ? MemberKind.Generic : MemberKind.Float),
                ["defaultValue"] = literalNull
                    ? new Newtonsoft.Json.Linq.JObject { ["value"] = Newtonsoft.Json.Linq.JValue.CreateNull() }
                    : Newtonsoft.Json.Linq.JValue.CreateNull(),
            };
            if (generic)
                wire["genericParamId"] = NeoGenericTestFixture.ParamT;
            Member child = wire.ToObject<Member>()!;
            var members = new Dictionary<string, Member> { [parent.id] = parent, [child.id] = child };
            NeoMemberShapeResolution.ResolveAll(members);
            object? Default(Member declaration) => declaration is GenericMember slot ? slot.defaultValue : ((FloatMember)declaration).defaultValue;
            object? DefaultValue(Member declaration) => declaration is GenericMember slot ? slot.defaultValue?.value : ((FloatMember)declaration).defaultValue?.value;
            Assert.AreEqual(baseHasDefault || literalNull, Default(child) is not null);
            Assert.AreEqual(literalNull ? null : baseHasDefault ? 4.5 : (object?)null, DefaultValue(child));
            var serialized = Newtonsoft.Json.Linq.JObject.Parse(JsonConvert.SerializeObject(child));
            Assert.AreEqual(literalNull, serialized.ContainsKey("defaultValue"));
            if (literalNull)
                Assert.AreEqual(Newtonsoft.Json.Linq.JTokenType.Null, serialized["defaultValue"]!["value"]!.Type);
            Member roundTripped = serialized.ToObject<Member>()!;
            members[child.id] = roundTripped;
            NeoMemberShapeResolution.ResolveAll(members);
            Assert.AreEqual(DefaultValue(child), DefaultValue(roundTripped));
            Assert.AreEqual(Default(child) is not null, Default(roundTripped) is not null);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void NullOverrideUsesBaseDefaultOrRequiresConstructionArgument(bool generic, bool baseHasDefault)
        {
            ProjectData data = NeoGenericTestFixture.BuildProjectData();
            if (generic)
                ((GenericMember)data.members["member-speed"]).defaultValue = baseHasDefault ? new NullMemberValueBase { value = 4.5 } : null;
            else
                data.members["member-speed"] = new FloatMember
                {
                    id = "member-speed",
                    projectId = "project-a",
                    name = "Speed",
                    kind = MemberKind.Float,
                    Requirement = NeoMemberRequirementKind.Required,
                    defaultValue = baseHasDefault ? new NumberMemberValueBase { value = 4.5 } : null,
                };
            var wire = new Newtonsoft.Json.Linq.JObject
            {
                ["id"] = "speed-override",
                ["projectId"] = "project-a",
                ["name"] = "Speed",
                ["extendsMemberId"] = "member-speed",
                ["kind"] = (int)(generic ? MemberKind.Generic : MemberKind.Float),
                ["defaultValue"] = Newtonsoft.Json.Linq.JValue.CreateNull(),
            };
            if (generic)
                wire["genericParamId"] = NeoGenericTestFixture.ParamT;
            data.members["speed-override"] = wire.ToObject<Member>()!;
            data.classes["class-damage"].schema["Speed"] = "speed-override";
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            NeoMemberClassWritable Construct() => NeoGeneratedTypesSupport.CreateWritableClassValue(
                client, "class-damage", new Dictionary<string, string>(), System.Array.Empty<MemberValue>());
            if (baseHasDefault)
                Assert.AreEqual(4.5, Construct().Get<NeoMemberFloatWritable>("Speed").value!.value);
            else
                StringAssert.Contains("missing required member", Assert.Throws<System.InvalidOperationException>(() => Construct())!.Message);
        }

        [Test]
        public void SubstituteMember_GenericPartialPayloadSurvivesClassBinding()
        {
            ProjectData data = NeoGenericTestFixture.BuildProjectData();
            data.members["member-binding-class"] = new ClassMember
            {
                id = "member-binding-class",
                projectId = "project-a",
                name = "TargetBinding",
                kind = MemberKind.Class,
                DeclaredClassId = "class-card-base",
                Payload = NeoMemberPayloadKind.Full,
            };
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            var slot = new GenericMember
            {
                id = "member-partial-slot",
                projectId = "project-a",
                name = "Overrides",
                kind = MemberKind.Generic,
                genericParamId = NeoGenericTestFixture.ParamT,
                Payload = NeoMemberPayloadKind.Partial,
            };
            var env = new Dictionary<string, NeoGenericEnvEntry>
            {
                [NeoGenericTestFixture.ParamT] =
                    NeoGenericEnvEntry.Bound("member-binding-class"),
            };

            var substituted = (ClassMember)NeoGenericResolution.SubstituteMember(
                client,
                slot,
                env);

            Assert.AreEqual(NeoMemberPayloadKind.Partial, substituted.Payload);
            Assert.AreEqual("class-card-base", substituted.DeclaredClassId);
            Assert.AreEqual(slot.id, substituted.id);
        }

        [Test]
        public void SubstituteMember_NonGenericRecord_IsIdentity()
        {
            var client = LoadClient();
            var env = NeoGenericResolution.ResolveEnv(client, "class-damage");
            var plain = client.members["member-binding-string"];
            Assert.AreSame(plain, NeoGenericResolution.SubstituteMember(client, plain, env));
        }

        [Test]
        public void SubstituteMember_UnboundParam_ThrowsDescriptively()
        {
            var client = LoadClient();
            var openEnv = NeoGenericResolution.ResolveEnv(client, "class-middle");
            var slot = client.members["member-speed"];

            var error = Assert.Throws<NeoOpenTypeArgumentException>(
                () => NeoGenericResolution.SubstituteMember(client, slot, openEnv))!;
            StringAssert.Contains("unbound", error.Message);
            StringAssert.Contains("Speed", error.Message);
        }

        // ------------------------------------------------------------------
        // Live node substitution + SDK-side stamping.
        // ------------------------------------------------------------------

        [Test]
        public void ClassNode_SubstitutesGenericChildBeforeDispatch()
        {
            var client = LoadClient();
            var card = client.save.Get<NeoMemberClassWritable>("Card");

            // A type argument does not supply a default for the T slot.
            var speed = card.Get<NeoMemberFloatWritable>("Speed");
            Assert.IsNull(speed.value?.value);

            // The same slot on the String leaf constructs the String wrapper.
            var stringCard = client.save.Get<NeoMemberClassWritable>("StringCard");
            Assert.IsTrue(stringCard.TryGet("Speed", out NeoMemberString? stringSpeed));
            Assert.IsNull(stringSpeed!.value?.value);
        }

        [Test]
        public void ClassNode_WritesGenericMemberThroughSubstitutedKind()
        {
            var client = LoadClient();
            var card = client.save.Get<NeoMemberClassWritable>("Card");

            card.SetSerializedValue("Speed", NeoValueWritePayload.FromValue((double?)2.5));

            Assert.AreEqual(2.5, card.Get<NeoMemberFloatWritable>("Speed").value?.value);
        }

        [Test]
        public void CollectionRow_MintedBySdk_CarriesTypeSourceAndTypedEntries()
        {
            var client = LoadClient();
            var card = client.save.Get<NeoMemberClassWritable>("Card");

            var values = card.GetOrCreateCollection<NeoMemberListWritable>("Values");
            Assert.IsNotNull(values.value, "GetOrCreateCollection binds an empty list row");
            // P127 §12.1: DamageCard closes T through its chain, so the list
            // isn't a binding node and stores its own closed type.
            Assert.IsNull(values.value!.genericBindings, "only a binding node stores a stamp");
            Assert.AreEqual(
                NeoTypeSource.OfType(TestTypeBindings.List(TestTypeBindings.Of(MemberKind.Float), required: false)),
                values.value!.typeSource);

            values.AddSerialized(NeoValueWritePayload.FromValue((double?)1.25));
            Assert.AreEqual(1, values.Count);
            Assert.IsInstanceOf<NeoMemberFloat>(values[0],
                "the stamped row substitutes the Generic entry to the Float wrapper");
            Assert.AreEqual(1.25, ((NeoMemberFloat)values[0]).value?.value);
        }

        [Test]
        public void ClassNode_ConstructedSlotInstance_ReadsChildThroughSlotArguments()
        {
            var client = LoadClient();

            // The instance row carries `classId: null` (the DECLARED open
            // type) — its params are bound only by the slot's constructed
            // arguments. Descending previously threw "param ... is unbound
            // in the binding environment — only closed classes are
            // instantiable" (spec §4.1).
            var constructed = client.save.Get<NeoMemberClassWritable>("Constructed");
            var speed = constructed.Get<NeoMemberFloatWritable>("Speed");
            Assert.AreEqual(7.25, speed.value?.value);
        }

        [Test]
        public void Factory_CreateWritableClassValue_RequiresGenericMemberWithoutDeclarationDefault()
        {
            using var client = LoadClient();
            var error = Assert.Throws<System.InvalidOperationException>(() =>
                NeoGeneratedTypesSupport.CreateWritableClassValue(
                    client,
                    "class-damage",
                    new Dictionary<string, string>(),
                    System.Array.Empty<MemberValue>()));
            StringAssert.Contains("missing required member", error!.Message);
        }

        [TestCase(false, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, true)]
        public void ClassDefaultRejectsInvalidChildEvenWhenFieldCanBeOmitted(bool requiredWithDefault, bool storedClass, bool mismatchedCarrier)
        {
            ProjectData data = NeoGenericTestFixture.BuildProjectData();
            data.classes["dangling-target"] = new NeoSchemaClass
            {
                id = "dangling-target",
                name = "Target",
                projectId = "project-a",
                schema = new Dictionary<string, string> { ["Count"] = "dangling-count" },
            };
            data.members["dangling-count"] = new FloatMember
            {
                id = "dangling-count",
                name = "Count",
                projectId = "project-a",
                kind = MemberKind.Float,
                Requirement = requiredWithDefault ? NeoMemberRequirementKind.Required : NeoMemberRequirementKind.Optional,
                defaultValue = requiredWithDefault ? new NumberMemberValueBase { value = 4.5 } : null,
            };
            data.classes["dangling-host"] = new NeoSchemaClass
            {
                id = "dangling-host",
                name = "Host",
                projectId = "project-a",
                schema = new Dictionary<string, string> { ["Target"] = "dangling-slot" },
            };
            if (mismatchedCarrier)
                data.values["missing-count-row"] = new StringMemberValue { id = "missing-count-row", value = "wrong carrier" };
            var contents = new Dictionary<string, string> { ["Count"] = "missing-count-row" };
            data.members["dangling-slot"] = new ClassMember
            {
                id = "dangling-slot",
                name = "Target",
                projectId = "project-a",
                kind = MemberKind.Class,
                DeclaredClassId = "dangling-target",
                defaultValue = new ObjectMemberValueBase { value = contents },
            };
            string constructionClass = "dangling-host";
            if (storedClass)
            {
                data.values["stored-target"] = new ObjectMemberValue
                {
                    id = "stored-target",
                    classId = "dangling-target",
                    value = contents,
                };
                data.classes["dangling-wrapper"] = new NeoSchemaClass
                {
                    id = "dangling-wrapper",
                    name = "Wrapper",
                    projectId = "project-a",
                    schema = new Dictionary<string, string> { ["Host"] = "dangling-host-slot" },
                };
                data.members["dangling-host-slot"] = new ClassMember
                {
                    id = "dangling-host-slot",
                    name = "Host",
                    projectId = "project-a",
                    kind = MemberKind.Class,
                    DeclaredClassId = "dangling-host",
                    defaultValue = new ObjectMemberValueBase
                    {
                        value = new Dictionary<string, string> { ["Target"] = "stored-target" }
                    },
                };
                constructionClass = "dangling-wrapper";
            }
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var error = Assert.Throws<System.InvalidOperationException>(() =>
                NeoGeneratedTypesSupport.CreateWritableClassValue(client, constructionClass,
                    new Dictionary<string, string>(), System.Array.Empty<MemberValue>()));
            StringAssert.Contains("missing-count-row", error!.Message);
        }

        [TestCase(MemberKind.Variant)]
        [TestCase(MemberKind.NSDelegate)]
        [TestCase(MemberKind.NSAction)]
        public void ClassDefaultCopiesStoredHandle(MemberKind kind)
        {
            ProjectData data = NeoGenericTestFixture.BuildProjectData();
            data.classes["variant-target"] = new NeoSchemaClass
            {
                id = "variant-target",
                name = "Target",
                projectId = "project-a",
                schema = new Dictionary<string, string> { ["Variant"] = "variant-slot" },
            };
            data.members["variant-slot"] = new VariantMember
            {
                id = "variant-slot",
                name = "Variant",
                projectId = "project-a",
                kind = MemberKind.Variant,
                targetTypeInfo = new ClassTypeInfo { type = MemberKind.Class, classId = "class-card-base", required = true },
            };
            data.classes["variant-host"] = new NeoSchemaClass
            {
                id = "variant-host",
                name = "Host",
                projectId = "project-a",
                schema = new Dictionary<string, string> { ["Target"] = "variant-host-slot" },
            };
            data.members["variant-host-slot"] = new ClassMember
            {
                id = "variant-host-slot",
                name = "Target",
                projectId = "project-a",
                kind = MemberKind.Class,
                DeclaredClassId = "variant-target",
                defaultValue = new ObjectMemberValueBase
                {
                    value = new Dictionary<string, string> { ["Variant"] = "stored-variant" }
                },
            };
            data.values["stored-variant"] = new VariantMemberValue
            {
                id = "stored-variant",
                value = new VariantRefValue { classId = "class-card-base" },
            };
            if (kind != MemberKind.Variant)
            {
                var voidType = new VoidTypeInfo { type = MemberKind.Void };
                var arguments = System.Array.Empty<FunctionArgumentTypeInfo>();
                data.members["copy-callback"] = new FunctionMember
                {
                    id = "copy-callback",
                    name = "Callback",
                    kind = MemberKind.Function,
                    returnTypeInfo = voidType,
                    argumentTypes = arguments,
                };
                data.classes["variant-target"].schema["Callback"] = "copy-callback";
                data.members["variant-slot"] = kind == MemberKind.NSDelegate
                    ? new DelegateMember { returnTypeInfo = voidType, argumentTypes = arguments }
                    : new ActionMember { argumentTypes = arguments };
                data.members["variant-slot"].id = "variant-slot";
                data.members["variant-slot"].name = "Variant";
                data.members["variant-slot"].kind = kind;
                data.values["stored-variant"] = kind == MemberKind.NSDelegate
                    ? new DelegateMemberValue { value = new NeoDelegateValue { memberId = "copy-callback" } }
                    : new ActionMemberValue { value = new NeoActionValue() };
                data.values["stored-variant"].id = "stored-variant";
            }
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var host = NeoGeneratedTypesSupport.CreateWritableClassValue(client, "variant-host",
                new Dictionary<string, string>(), System.Array.Empty<MemberValue>());
            var target = host.Get<NeoMemberClassWritable>("Target");
            string clonedId = target.value!.value!["Variant"];
            Assert.IsTrue(client.TryGetValue(clonedId, out MemberValue? cloned));
            Assert.IsTrue(client.TryGetValue("stored-variant", out MemberValue? source));
            Assert.AreNotEqual(source!.id, cloned!.id);
            object? Body(MemberValue row) => row switch
            {
                VariantMemberValue variant => variant.value,
                DelegateMemberValue callback => callback.value,
                ActionMemberValue action => action.value,
                _ => throw new System.InvalidOperationException(),
            };
            Assert.AreEqual(source.GetType(), cloned.GetType());
            if (kind == MemberKind.NSDelegate)
            {
                Assert.IsNull(((DelegateMemberValue)source).value!.valueId);
                Assert.AreEqual(target.value.id, ((DelegateMemberValue)cloned).value!.valueId);
            }
            else
            {
                Assert.AreEqual(JsonConvert.SerializeObject(Body(source)),
                    JsonConvert.SerializeObject(Body(cloned)));
            }
            Assert.AreNotSame(Body(source), Body(cloned));
        }

        [TestCase(false, false, false, false, false)]
        [TestCase(true, false, true, false, false)]
        [TestCase(true, true, false, false, false)]
        [TestCase(true, true, false, true, false)]
        [TestCase(true, true, false, true, true)]
        public void Factory_ClassDefaultMustBeDeclaredAndComplete(
            bool hasDefault, bool childRequiresValue, bool succeeds, bool referencedChild, bool replay)
        {
            ProjectData data = NeoGenericTestFixture.BuildProjectData();
            data.classes["default-container"] = new NeoSchemaClass
            {
                id = "default-container",
                projectId = "project-a",
                name = "DefaultContainer",
                schema = new Dictionary<string, string> { ["Child"] = "default-child" },
            };
            data.classes["default-target"] = new NeoSchemaClass
            {
                id = "default-target",
                projectId = "project-a",
                name = "DefaultTarget",
                schema = childRequiresValue
                    ? new Dictionary<string, string> { ["Value"] = "default-required" }
                    : new Dictionary<string, string>(),
            };
            data.members["default-child"] = new ClassMember
            {
                id = "default-child",
                projectId = "project-a",
                name = "Child",
                kind = MemberKind.Class,
                DeclaredClassId = "default-target",
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = hasDefault
                    ? new ObjectMemberValueBase { value = new Dictionary<string, string>() }
                    : null,
            };
            data.members["default-required"] = new FloatMember
            {
                id = "default-required",
                projectId = "project-a",
                name = "Value",
                kind = MemberKind.Float,
                Requirement = NeoMemberRequirementKind.Required,
            };
            string constructedClassId = "default-container";
            if (referencedChild)
            {
                ((ClassMember)data.members["default-child"]).defaultValue = null;
                data.values["empty-child"] = new ObjectMemberValue
                {
                    id = "empty-child",
                    classId = "default-target",
                    value = new Dictionary<string, string>(),
                };
                data.classes["default-host"] = new NeoSchemaClass
                {
                    id = "default-host",
                    projectId = "project-a",
                    name = "DefaultHost",
                    schema = new Dictionary<string, string> { ["Container"] = "host-container" },
                };
                data.members["host-container"] = new ClassMember
                {
                    id = "host-container",
                    projectId = "project-a",
                    name = "Container",
                    kind = MemberKind.Class,
                    DeclaredClassId = "default-container",
                    defaultValue = new ObjectMemberValueBase
                    {
                        value = new Dictionary<string, string> { ["Child"] = "empty-child" },
                    },
                };
                constructedClassId = "default-host";
            }
            if (replay)
            {
                data.classes["save-root-class"].schema["Host"] = "replay-host";
                data.members["replay-host"] = new ClassMember
                {
                    id = "replay-host",
                    projectId = "project-a",
                    name = "Host",
                    kind = MemberKind.Class,
                    DeclaredClassId = constructedClassId,
                };
                data.values["replay-host-value"] = new ObjectMemberValue
                {
                    id = "replay-host-value",
                    classId = constructedClassId,
                    value = new Dictionary<string, string>(),
                    instanceConstructorId = null,
                    constructorArgs = new Dictionary<string, Newtonsoft.Json.Linq.JToken?>(),
                };
                ((ObjectMemberValue)data.values["root-save-value"]).value!["Host"] = "replay-host-value";
            }
            void Construct()
            {
                using var client = NeoTestSaveStack.ClientFromSchema(data);
                if (!replay)
                    NeoGeneratedTypesSupport.CreateWritableClassValue(
                    client, constructedClassId, new Dictionary<string, string>(),
                    System.Array.Empty<MemberValue>());
            }
            if (succeeds)
                Assert.DoesNotThrow(Construct);
            else
                StringAssert.Contains("missing required member",
                Assert.Throws<System.InvalidOperationException>(Construct)!.ToString());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NestedClassDefaultPreservesExplicitNull(bool required)
        {
            ProjectData data = NeoGenericTestFixture.BuildProjectData();
            data.classes["null-container"] = new NeoSchemaClass
            {
                id = "null-container",
                projectId = "project-a",
                name = "NullContainer",
                schema = new Dictionary<string, string> { ["Child"] = "null-child" },
            };
            data.members["null-child"] = new ClassMember
            {
                id = "null-child",
                projectId = "project-a",
                name = "Child",
                kind = MemberKind.Class,
                DeclaredClassId = "class-damage",
                Requirement = required ? NeoMemberRequirementKind.Required : NeoMemberRequirementKind.Optional,
            };
            data.classes["null-host"] = new NeoSchemaClass
            {
                id = "null-host",
                projectId = "project-a",
                name = "NullHost",
                schema = new Dictionary<string, string> { ["Container"] = "null-default" },
            };
            data.members["null-default"] = new ClassMember
            {
                id = "null-default",
                projectId = "project-a",
                name = "Container",
                kind = MemberKind.Class,
                DeclaredClassId = "null-container",
                defaultValue = new ObjectMemberValueBase
                {
                    value = new Dictionary<string, string> { ["Child"] = "explicit-null-child" },
                },
            };
            data.values["explicit-null-child"] = new ObjectMemberValue { id = "explicit-null-child", value = null };
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            NeoMemberClassWritable Construct() => NeoGeneratedTypesSupport.CreateWritableClassValue(
                client, "null-host", new Dictionary<string, string>(), System.Array.Empty<MemberValue>());
            if (required)
            {
                StringAssert.Contains("null required value", Assert.Throws<System.InvalidOperationException>(() => Construct())!.Message);
                return;
            }
            var child = Construct().Get<NeoMemberClassWritable>("Container").Get<NeoMemberClassWritable>("Child");
            Assert.IsNull(child.value?.value);
        }

        [Test]
        public void UnboundClassWriteDoesNotBypassRequiredGenericArguments()
        {
            using var client = LoadClient();
            var node = new NeoMemberClassWritable(client,
                (ClassMember)client.members["member-card"], null, NeoValueOwnership.Save);
            var error = Assert.Throws<System.InvalidOperationException>(() =>
                node.SetSerializedValue("Speed", NeoValueWritePayload.FromValue(3.5)));
            StringAssert.Contains("Construct and assign", error!.Message);
            Assert.IsNull(node.value);
            int rowCount = client.saveValues.Count;
            Assert.Throws<System.InvalidOperationException>(() =>
                node.Get<NeoMemberFloatWritable>("Speed").Set(3.5f));
            Assert.AreEqual(rowCount, client.saveValues.Count);
            Assert.Throws<System.InvalidOperationException>(() =>
                node.Get<NeoMemberListWritable>("Values").AddSerialized(NeoValueWritePayload.FromValue(1.5)));
            Assert.AreEqual(rowCount, client.saveValues.Count);
        }

        [Test]
        public void Factory_CreateWritableClassValue_RejectsOpenClass()
        {
            var client = LoadClient();
            var error = Assert.Throws<System.InvalidOperationException>(
                () => NeoGeneratedTypesSupport.CreateWritableClassValue(
                    client,
                    "class-middle",
                    new Dictionary<string, string>(),
                    System.Array.Empty<MemberValue>()))!;
            StringAssert.Contains("open generic class", error.Message);
        }

        // ------------------------------------------------------------------
        // Constructed-slot admission (spec §3.4).
        // ------------------------------------------------------------------

        [Test]
        public void ConstructedSlotAccepts_MatchingClosedDescendant_Ok()
        {
            var client = LoadClient();
            var slot = (ClassMember)client.members["member-constructed-slot"];

            var admission = NeoGenericResolution.ConstructedSlotAccepts(
                client, slot, "class-damage", NeoGenericResolution.EmptyEnv);

            Assert.IsTrue(admission.ok, admission.reason);
        }

        [Test]
        public void ConstructedSlotAccepts_DeclaredOpenClassClosedBySlotArguments_Ok()
        {
            var client = LoadClient();
            var slot = (ClassMember)client.members["member-constructed-slot"];

            // The DECLARED open class itself is admissible when the slot's
            // arguments close it — rejecting it as "open" would leave
            // constructed slots with no valid pick at all (spec §3.4).
            var admission = NeoGenericResolution.ConstructedSlotAccepts(
                client, slot, "class-enchant", NeoGenericResolution.EmptyEnv);

            Assert.IsTrue(admission.ok, admission.reason);
        }

        [Test]
        public void ConstructedSlotAccepts_SignatureMismatch_Rejected()
        {
            var client = LoadClient();
            var slot = (ClassMember)client.members["member-constructed-slot"];

            // String argument vs the slot's Float argument.
            var stringLeaf = NeoGenericResolution.ConstructedSlotAccepts(
                client, slot, "class-string-card", NeoGenericResolution.EmptyEnv);
            Assert.IsFalse(stringLeaf.ok);
            StringAssert.Contains("signature mismatch", stringLeaf.reason);

            // Optional Float vs required Float — `required` is part of the
            // signature (nullability is part of the type, Decision 8).
            var optionalLeaf = NeoGenericResolution.ConstructedSlotAccepts(
                client, slot, "class-optional-float", NeoGenericResolution.EmptyEnv);
            Assert.IsFalse(optionalLeaf.ok);
            StringAssert.Contains("signature mismatch", optionalLeaf.reason);
        }

        [Test]
        public void ConstructedSlotAccepts_OpenClass_ClosedOnlyBySlotArguments()
        {
            var client = LoadClient();
            var slot = (ClassMember)client.members["member-constructed-slot"];
            // MiddleCard<U> : Enchant<U> in an Enchant<float> slot is a MiddleCard<float>.
            Assert.IsTrue(NeoGenericResolution.ConstructedSlotAccepts(
                client, slot, "class-middle", NeoGenericResolution.EmptyEnv).ok);
            var raw = new ClassMember
            {
                id = "member-raw-enchant",
                projectId = "project-a",
                name = "RawEnchant",
                kind = MemberKind.Class,
                DeclaredClassId = "class-enchant",
            };
            var admission = NeoGenericResolution.ConstructedSlotAccepts(
                client, raw, "class-middle", NeoGenericResolution.EmptyEnv);
            Assert.IsFalse(admission.ok);
            StringAssert.Contains("open", admission.reason);
        }

        [Test]
        public void ConstructedSlotAccepts_NonDescendant_Rejected()
        {
            var client = LoadClient();
            var slot = (ClassMember)client.members["member-constructed-slot"];
            var admission = NeoGenericResolution.ConstructedSlotAccepts(
                client, slot, "root-class", NeoGenericResolution.EmptyEnv);
            Assert.IsFalse(admission.ok);
            StringAssert.Contains("descendants", admission.reason);
        }

        [Test]
        public void ConstructedSlotAccepts_MissingClass_Rejected()
        {
            var client = LoadClient();
            var slot = (ClassMember)client.members["member-constructed-slot"];
            var admission = NeoGenericResolution.ConstructedSlotAccepts(
                client, slot, "class-nope", NeoGenericResolution.EmptyEnv);
            Assert.IsFalse(admission.ok);
            StringAssert.Contains("does not exist", admission.reason);
        }
    }
}
