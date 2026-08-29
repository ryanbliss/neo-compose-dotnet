// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

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
                modifier = NeoClassModifierKind.Abstract,
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
                extendsGenericBindings = new Dictionary<string, GenericBinding>
                {
                    [ParamT] = new()
                    {
                        kind = NeoGenericBindingKind.Generic,
                        genericParamId = ParamU,
                    },
                },
            };
            var damage = new NeoSchemaClass
            {
                id = "class-damage",
                projectId = "project-a",
                name = "DamageCard",
                extendsClassId = middle.id,
                schema = new Dictionary<string, string>(),
                extendsGenericBindings = new Dictionary<string, GenericBinding>
                {
                    [ParamU] = new()
                    {
                        kind = NeoGenericBindingKind.Member,
                        memberId = "member-binding-float",
                    },
                },
            };
            var stringCard = new NeoSchemaClass
            {
                id = "class-string-card",
                projectId = "project-a",
                name = "StringCard",
                extendsClassId = enchant.id,
                schema = new Dictionary<string, string>(),
                extendsGenericBindings = new Dictionary<string, GenericBinding>
                {
                    [ParamT] = new()
                    {
                        kind = NeoGenericBindingKind.Member,
                        memberId = "member-binding-string",
                    },
                },
            };
            var optionalFloatCard = new NeoSchemaClass
            {
                id = "class-optional-float",
                projectId = "project-a",
                name = "OptionalFloatCard",
                extendsClassId = enchant.id,
                schema = new Dictionary<string, string>(),
                extendsGenericBindings = new Dictionary<string, GenericBinding>
                {
                    [ParamT] = new()
                    {
                        kind = NeoGenericBindingKind.Member,
                        memberId = "member-binding-float-optional",
                    },
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
                        classId = damage.id,
                        requirement = NeoMemberRequirementKind.Required,
                    },
                    ["member-string-card"] = new ClassMember
                    {
                        id = "member-string-card",
                        projectId = "project-a",
                        name = "StringCard",
                        kind = MemberKind.Class,
                        classId = stringCard.id,
                        requirement = NeoMemberRequirementKind.Required,
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
                        access = NeoMemberAccessKind.Protected,
                        modifier = NeoMemberModifierKind.Abstract,
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
                        requirement = NeoMemberRequirementKind.Required,
                        minValue = 0f,
                        access = NeoMemberAccessKind.Public,
                        // The binding's storage must NOT leak into the slot
                        // (substitution partition — Decision 10).
                        storage = NeoMemberStorage.Save,
                        defaultValue = new NumberMemberValueBase { value = 3.5 },
                        modifier = NeoMemberModifierKind.Virtual,
                    },
                    ["member-binding-float-optional"] = new FloatMember
                    {
                        id = "member-binding-float-optional",
                        projectId = "project-a",
                        name = "OptionalFloatBinding",
                        kind = MemberKind.Float,
                        requirement = NeoMemberRequirementKind.Optional,
                    },
                    ["member-binding-string"] = new StringMember
                    {
                        id = "member-binding-string",
                        projectId = "project-a",
                        name = "StringBinding",
                        kind = MemberKind.String,
                        requirement = NeoMemberRequirementKind.Optional,
                        format = NeoStringFormatKind.Plain,
                    },
                    ["member-constructed-slot"] = new ClassMember
                    {
                        id = "member-constructed-slot",
                        projectId = "project-a",
                        name = "ConstructedSlot",
                        kind = MemberKind.Class,
                        classId = enchant.id,
                        classArguments = new Dictionary<string, GenericBinding>
                        {
                            [ParamT] = new()
                            {
                                kind = NeoGenericBindingKind.Member,
                                memberId = "member-binding-float",
                            },
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
            };
        }

        private static ClassMember RootMember(string id, string valueId, string classId)
        {
            return new ClassMember
            {
                id = id,
                projectId = "project-a",
                name = id,
                kind = MemberKind.Class,
                requirement = NeoMemberRequirementKind.Required,
                valueId = valueId,
                classId = classId,
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
                      ""constraint"": { ""kind"": 1, ""enumId"": ""e1"" } }
                ],
                ""extendsGenericBindings"": {
                    ""pp1"": { ""kind"": 0, ""genericParamId"": ""p1"" },
                    ""pp2"": { ""kind"": 1, ""memberId"": ""a1"" }
                }
            }";
            var schemaClass = JsonConvert.DeserializeObject<NeoSchemaClass>(json)!;

            Assert.AreEqual(2, schemaClass.genericParams!.Count);
            Assert.AreEqual("p1", schemaClass.genericParams[0].id);
            Assert.IsNull(schemaClass.genericParams[0].constraint);
            Assert.AreEqual(NeoGenericParamConstraintKind.Enum, schemaClass.genericParams[1].constraint!.kind);
            Assert.AreEqual("e1", schemaClass.genericParams[1].constraint!.enumId);

            Assert.AreEqual(2, schemaClass.extendsGenericBindings!.Count);
            Assert.IsTrue(schemaClass.extendsGenericBindings["pp1"].IsForward);
            Assert.AreEqual("p1", schemaClass.extendsGenericBindings["pp1"].genericParamId);
            Assert.IsFalse(schemaClass.extendsGenericBindings["pp2"].IsForward);
            Assert.AreEqual("a1", schemaClass.extendsGenericBindings["pp2"].memberId);
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

            Assert.IsNull(absent.modifier);
            Assert.AreEqual(NeoMemberModifierKind.Virtual, absent.EffectiveModifier);
            Assert.AreEqual(NeoMemberModifierKind.Virtual, virtualMember.modifier);
            Assert.AreEqual(NeoMemberModifierKind.Abstract, abstractMember.modifier);
        }

        [Test]
        public void Json_ClassMember_ReadsClassArguments()
        {
            const string json = @"{
                ""id"": ""a1"", ""projectId"": ""p"", ""name"": ""Slot"", ""kind"": 7,
                ""classId"": ""t1"",
                ""classArguments"": {
                    ""p1"": { ""kind"": 1, ""memberId"": ""a2"" }
                }
            }";
            var member = JsonConvert.DeserializeObject<Member>(json);
            Assert.IsInstanceOf<ClassMember>(member);
            var classMember = (ClassMember)member!;
            Assert.AreEqual(1, classMember.classArguments!.Count);
            Assert.AreEqual("a2", classMember.classArguments["p1"].memberId);
        }

        [Test]
        public void Json_ValueRow_RoundTripsGenericBindingsStamp()
        {
            const string json = @"{""id"": ""v1"", ""value"": [], ""genericBindings"": {""p1"": ""a1""}}";
            var row = JsonConvert.DeserializeObject<MemberValue>(json)!;
            Assert.AreEqual("a1", row.genericBindings!["p1"]);

            string serialized = JsonConvert.SerializeObject(row);
            var reparsed = JsonConvert.DeserializeObject<MemberValue>(serialized)!;
            Assert.AreEqual("a1", reparsed.genericBindings!["p1"]);
        }

        [Test]
        public void Json_UnknownBindingKind_Throws()
        {
            const string json = @"{""kind"": ""wildcard""}";
            Assert.Throws<JsonSerializationException>(
                () => JsonConvert.DeserializeObject<GenericBinding>(json));
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
            Assert.AreEqual("member-binding-float", env[NeoGenericTestFixture.ParamT].memberId);
            Assert.IsTrue(env[NeoGenericTestFixture.ParamU].IsBound);
            Assert.AreEqual("member-binding-float", env[NeoGenericTestFixture.ParamU].memberId);
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
            Assert.AreEqual("member-binding-float", env[NeoGenericTestFixture.ParamT].memberId);
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
                new Dictionary<string, GenericBinding>
                {
                    [NeoGenericTestFixture.ParamT] = new()
                    {
                        kind = NeoGenericBindingKind.Member,
                        memberId = "member-binding-string",
                    },
                });

            Assert.IsTrue(env[NeoGenericTestFixture.ParamT].IsBound);
            Assert.AreEqual("member-binding-float", env[NeoGenericTestFixture.ParamT].memberId);
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
                new Dictionary<string, GenericBinding>
                {
                    [NeoGenericTestFixture.ParamT] = new()
                    {
                        kind = NeoGenericBindingKind.Generic,
                        genericParamId = NeoGenericTestFixture.ParamU,
                    },
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
            var env = NeoGenericResolution.ResolveEnv(client, "class-damage");
            var slot = client.members["member-speed"];

            var substituted = NeoGenericResolution.SubstituteMember(client, slot, env);

            Assert.IsInstanceOf<FloatMember>(substituted);
            // Slot identity/placement fields win.
            Assert.AreEqual("member-speed", substituted.id);
            Assert.AreEqual("Speed", substituted.name);
            Assert.AreEqual(NeoMemberAccessKind.Protected, substituted.EffectiveAccess,
                "the slot's accessibility must not come from its binding");
            Assert.AreEqual(false, substituted.EffectiveModifier == NeoMemberModifierKind.Virtual,
                "the slot's virtual declaration must not come from its binding");
            Assert.AreEqual(true, substituted.EffectiveModifier == NeoMemberModifierKind.Abstract,
                "the slot's abstract declaration must not come from its binding");
            Assert.AreEqual(NeoMemberStorage.Inherit, substituted.storage,
                "the binding's storage declaration must not leak into the slot");
            Assert.IsNull(substituted.extendsMemberId);
            // Binding type/config/required/defaultValue win.
            var substitutedFloat = (FloatMember)substituted;
            Assert.IsTrue(substitutedFloat.EffectiveRequirement == NeoMemberRequirementKind.Required);
            Assert.AreEqual(0f, substitutedFloat.minValue);
            Assert.AreEqual(3.5, substitutedFloat.defaultValue!.value);
        }

        [Test]
        public void SubstituteMember_AbsentSlotShapeClearsBindingModifier()
        {
            var client = LoadClient();
            var env = NeoGenericResolution.ResolveEnv(client, "class-damage");
            var slot = client.members["member-speed"];
            slot.modifier = null;
            var binding = client.members["member-binding-float"];
            binding.modifier = NeoMemberModifierKind.Abstract;
            NeoMemberShapeResolution.ResolveAll(client.members);

            var substituted = NeoGenericResolution.SubstituteMember(client, slot, env);

            Assert.AreEqual(NeoMemberModifierKind.Virtual, substituted.EffectiveModifier,
                "an absent slot declaration must use the default Virtual modifier");
            Assert.AreNotEqual(NeoMemberModifierKind.Abstract, substituted.EffectiveModifier,
                "an absent slot declaration must clear the binding's Abstract modifier");
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
                classId = "class-card-base",
                payload = NeoMemberPayloadKind.Full,
            };
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            var slot = new GenericMember
            {
                id = "member-partial-slot",
                projectId = "project-a",
                name = "Overrides",
                kind = MemberKind.Generic,
                genericParamId = NeoGenericTestFixture.ParamT,
                payload = NeoMemberPayloadKind.Partial,
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

            Assert.AreEqual(NeoMemberPayloadKind.Partial, substituted.EffectivePayload);
            Assert.AreEqual("class-card-base", substituted.classId);
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

            var error = Assert.Throws<System.InvalidOperationException>(
                () => NeoGenericResolution.SubstituteMember(client, slot, openEnv))!;
            StringAssert.Contains("unbound", error.Message);
            StringAssert.Contains("Speed", error.Message);
        }

        // ------------------------------------------------------------------
        // Stamps (Decision 9).
        // ------------------------------------------------------------------

        [Test]
        public void ComputeGenericBindingsStamp_RoundTripsThroughEnvFromStamp()
        {
            var client = LoadClient();
            var env = NeoGenericResolution.ResolveEnv(client, "class-damage");
            var listMember = client.members["member-values"];

            var stamp = NeoGenericResolution.ComputeGenericBindingsStamp(client, listMember, env)!;
            Assert.AreEqual(1, stamp.Count);
            Assert.AreEqual("member-binding-float", stamp[NeoGenericTestFixture.ParamT]);

            var rebuilt = NeoGenericResolution.EnvFromStamp(stamp);
            Assert.IsTrue(rebuilt[NeoGenericTestFixture.ParamT].IsBound);
            Assert.AreEqual("member-binding-float", rebuilt[NeoGenericTestFixture.ParamT].memberId);
        }

        [Test]
        public void ComputeGenericBindingsStamp_NonGenericSubtree_IsNull()
        {
            var client = LoadClient();
            var env = NeoGenericResolution.ResolveEnv(client, "class-damage");
            var plainList = client.members["member-plain-list"];
            Assert.IsNull(NeoGenericResolution.ComputeGenericBindingsStamp(client, plainList, env));
        }

        [Test]
        public void ComputeGenericBindingsStamp_UnboundEnv_Throws()
        {
            var client = LoadClient();
            var openEnv = NeoGenericResolution.ResolveEnv(client, "class-middle");
            var listMember = client.members["member-values"];
            var error = Assert.Throws<System.InvalidOperationException>(
                () => NeoGenericResolution.ComputeGenericBindingsStamp(client, listMember, openEnv))!;
            StringAssert.Contains("unbound", error.Message);
        }

        // ------------------------------------------------------------------
        // Live node substitution + SDK-side stamping.
        // ------------------------------------------------------------------

        [Test]
        public void ClassNode_SubstitutesGenericChildBeforeDispatch()
        {
            var client = LoadClient();
            var card = client.save.Get<NeoMemberClassWritable>("Card");

            // The T slot on a closed Float instance constructs the Float
            // wrapper and materializes the BINDING's default (3.5).
            var speed = card.Get<NeoMemberFloatWritable>("Speed");
            Assert.AreEqual(3.5, speed.value?.value);

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
        public void CollectionRow_MintedBySdk_CarriesStampAndTypedEntries()
        {
            var client = LoadClient();
            var card = client.save.Get<NeoMemberClassWritable>("Card");

            var values = card.GetOrCreateCollection<NeoMemberListWritable>("Values");
            Assert.IsNotNull(values.value, "GetOrCreateCollection binds an empty list row");
            Assert.IsNotNull(values.value!.genericBindings, "SDK-minted generic collection rows must be stamped");
            Assert.AreEqual(
                "member-binding-float",
                values.value!.genericBindings![NeoGenericTestFixture.ParamT]);

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
        public void Factory_CreateWritableClassValue_SubstitutesRequiredDefaults()
        {
            var client = LoadClient();

            var node = NeoGeneratedTypesSupport.CreateWritableClassValue(
                client,
                "class-damage",
                new Dictionary<string, string>(),
                System.Array.Empty<MemberValue>());

            // Speed substitutes to the required Float binding, so the
            // factory materializes its default row.
            Assert.AreEqual(3.5, node.Get<NeoMemberFloatWritable>("Speed").value?.value);
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
        public void ConstructedSlotAccepts_OpenClass_Rejected()
        {
            var client = LoadClient();
            var slot = (ClassMember)client.members["member-constructed-slot"];
            var admission = NeoGenericResolution.ConstructedSlotAccepts(
                client, slot, "class-middle", NeoGenericResolution.EmptyEnv);
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
