// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using Newtonsoft.Json;
using NUnit.Framework;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Attribute = NeoCompose.Runtime.Json.Attribute;

namespace NeoCompose.Tests
{
    /// <summary>
    /// Shared fixture for the custom-type-generics suite
    /// (specs/custom-type-generics.md §9). Mirrors the spec's motivating
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
            var rootType = new CustomType
            {
                id = "root-type",
                projectId = "project-a",
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            var saveRootType = new CustomType
            {
                id = "save-root-type",
                projectId = "project-a",
                name = "Save Root",
                schema = new Dictionary<string, string>
                {
                    ["Card"] = "attr-card",
                    ["StringCard"] = "attr-string-card",
                    ["Constructed"] = "attr-constructed-slot",
                },
            };
            var cardBase = new CustomType
            {
                id = "type-card-base",
                projectId = "project-a",
                name = "Card",
                schema = new Dictionary<string, string>(),
            };
            var enchant = new CustomType
            {
                id = "type-enchant",
                projectId = "project-a",
                name = "EnchantCard",
                extendsTypeId = cardBase.id,
                isAbstract = true,
                schema = new Dictionary<string, string>
                {
                    ["Speed"] = "attr-speed",
                    ["Values"] = "attr-values",
                },
                genericParams = new List<GenericParamDeclaration>
                {
                    new() { id = ParamT, name = "T" },
                },
            };
            var middle = new CustomType
            {
                id = "type-middle",
                projectId = "project-a",
                name = "MiddleCard",
                extendsTypeId = enchant.id,
                schema = new Dictionary<string, string>(),
                genericParams = new List<GenericParamDeclaration>
                {
                    new() { id = ParamU, name = "U" },
                },
                extendsGenericBindings = new Dictionary<string, GenericBinding>
                {
                    [ParamT] = new()
                    {
                        kind = NeoGenericBindingKinds.Generic,
                        genericParamId = ParamU,
                    },
                },
            };
            var damage = new CustomType
            {
                id = "type-damage",
                projectId = "project-a",
                name = "DamageCard",
                extendsTypeId = middle.id,
                schema = new Dictionary<string, string>(),
                extendsGenericBindings = new Dictionary<string, GenericBinding>
                {
                    [ParamU] = new()
                    {
                        kind = NeoGenericBindingKinds.Attribute,
                        attributeId = "attr-binding-float",
                    },
                },
            };
            var stringCard = new CustomType
            {
                id = "type-string-card",
                projectId = "project-a",
                name = "StringCard",
                extendsTypeId = enchant.id,
                schema = new Dictionary<string, string>(),
                extendsGenericBindings = new Dictionary<string, GenericBinding>
                {
                    [ParamT] = new()
                    {
                        kind = NeoGenericBindingKinds.Attribute,
                        attributeId = "attr-binding-string",
                    },
                },
            };
            var optionalFloatCard = new CustomType
            {
                id = "type-optional-float",
                projectId = "project-a",
                name = "OptionalFloatCard",
                extendsTypeId = enchant.id,
                schema = new Dictionary<string, string>(),
                extendsGenericBindings = new Dictionary<string, GenericBinding>
                {
                    [ParamT] = new()
                    {
                        kind = NeoGenericBindingKinds.Attribute,
                        attributeId = "attr-binding-float-optional",
                    },
                },
            };

            return new ProjectData
            {
                project = new Project
                {
                    id = "project-a",
                    _id = "project-a",
                    name = "Custom Type Generics",
                    rootAssetsAttributeId = "root-assets",
                    rootSaveFileAttributeId = "root-save",
                    rootSessionAttributeId = "root-session",
                },
                attributes = new Dictionary<string, Attribute>
                {
                    ["root-assets"] = RootAttribute("root-assets", "root-assets-value", rootType.id),
                    ["root-save"] = RootAttribute("root-save", "root-save-value", saveRootType.id),
                    ["root-session"] = RootAttribute("root-session", "root-session-value", rootType.id),
                    ["attr-card"] = new CustomAttribute
                    {
                        id = "attr-card",
                        projectId = "project-a",
                        name = "Card",
                        type = AttributeType.Custom,
                        customTypeId = damage.id,
                        required = true,
                    },
                    ["attr-string-card"] = new CustomAttribute
                    {
                        id = "attr-string-card",
                        projectId = "project-a",
                        name = "StringCard",
                        type = AttributeType.Custom,
                        customTypeId = stringCard.id,
                        required = true,
                    },
                    ["attr-speed"] = new GenericAttribute
                    {
                        id = "attr-speed",
                        projectId = "project-a",
                        name = "Speed",
                        type = AttributeType.Generic,
                        genericParamId = ParamT,
                        locked = true,
                        isVirtual = false,
                        isAbstract = true,
                    },
                    ["attr-values"] = new ListAttribute
                    {
                        id = "attr-values",
                        projectId = "project-a",
                        name = "Values",
                        type = AttributeType.List,
                        entryAttributeId = "attr-values-entry",
                    },
                    ["attr-values-entry"] = new GenericAttribute
                    {
                        id = "attr-values-entry",
                        projectId = "project-a",
                        name = "Value",
                        type = AttributeType.Generic,
                        genericParamId = ParamT,
                    },
                    ["attr-plain-list"] = new ListAttribute
                    {
                        id = "attr-plain-list",
                        projectId = "project-a",
                        name = "PlainList",
                        type = AttributeType.List,
                        entryAttributeId = "attr-binding-string",
                    },
                    ["attr-binding-float"] = new FloatAttribute
                    {
                        id = "attr-binding-float",
                        projectId = "project-a",
                        name = "FloatBinding",
                        type = AttributeType.Float,
                        required = true,
                        minValue = 0f,
                        isVirtual = true,
                        isAbstract = false,
                        // The binding's storage must NOT leak into the slot
                        // (substitution partition — Decision 10).
                        storage = "save",
                        defaultValue = new NumberAttributeValueBase { value = 3.5 },
                    },
                    ["attr-binding-float-optional"] = new FloatAttribute
                    {
                        id = "attr-binding-float-optional",
                        projectId = "project-a",
                        name = "OptionalFloatBinding",
                        type = AttributeType.Float,
                        required = false,
                    },
                    ["attr-binding-string"] = new StringAttribute
                    {
                        id = "attr-binding-string",
                        projectId = "project-a",
                        name = "StringBinding",
                        type = AttributeType.String,
                        required = false,
                        localizable = false,
                    },
                    ["attr-constructed-slot"] = new CustomAttribute
                    {
                        id = "attr-constructed-slot",
                        projectId = "project-a",
                        name = "ConstructedSlot",
                        type = AttributeType.Custom,
                        customTypeId = enchant.id,
                        customTypeArguments = new Dictionary<string, GenericBinding>
                        {
                            [ParamT] = new()
                            {
                                kind = NeoGenericBindingKinds.Attribute,
                                attributeId = "attr-binding-float",
                            },
                        },
                    },
                },
                values = new Dictionary<string, AttributeValue>
                {
                    ["root-assets-value"] = ObjectValue("root-assets-value", rootType.id, new()),
                    ["root-save-value"] = ObjectValue(
                        "root-save-value",
                        saveRootType.id,
                        new Dictionary<string, string>
                        {
                            ["Card"] = "card-value",
                            ["StringCard"] = "string-card-value",
                            ["Constructed"] = "constructed-value",
                        }),
                    ["root-session-value"] = ObjectValue("root-session-value", rootType.id, new()),
                    ["card-value"] = ObjectValue("card-value", damage.id, new()),
                    ["string-card-value"] = ObjectValue("string-card-value", stringCard.id, new()),
                    // A constructed-slot instance: no named subtype exists,
                    // so the row carries `typeId: null` (the DECLARED open
                    // type) — the slot's arguments close it (§4.1).
                    ["constructed-value"] = ObjectValue(
                        "constructed-value",
                        null,
                        new Dictionary<string, string>
                        {
                            ["Speed"] = "constructed-speed-value",
                        }),
                    ["constructed-speed-value"] = new NumberAttributeValue
                    {
                        id = "constructed-speed-value",
                        value = 7.25,
                    },
                },
                types = new Dictionary<string, CustomType>
                {
                    [rootType.id] = rootType,
                    [saveRootType.id] = saveRootType,
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

        private static CustomAttribute RootAttribute(string id, string valueId, string customTypeId)
        {
            return new CustomAttribute
            {
                id = id,
                projectId = "project-a",
                name = id,
                type = AttributeType.Custom,
                required = true,
                valueId = valueId,
                customTypeId = customTypeId,
            };
        }

        private static ObjectAttributeValue ObjectValue(
            string id,
            string? typeId,
            Dictionary<string, string> record)
        {
            return new ObjectAttributeValue
            {
                id = id,
                typeId = typeId,
                value = record,
            };
        }
    }

    /// <summary>
    /// specs/custom-type-generics.md §9: environment resolution (forward
    /// chains included), the Decision-10 substitution field partition, the
    /// Decision-9 stamp lifecycle, constructed-slot admission, and the JSON
    /// read layer for the new wire fields. The dotnet side mirrors
    /// <c>src/models/custom-types/generics.ts</c> — behavioral assertions
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
        public void Json_CustomType_ReadsGenericParamsAndBindings()
        {
            const string json = @"{
                ""id"": ""t1"", ""projectId"": ""p"", ""name"": ""Foo"",
                ""schema"": {},
                ""extendsTypeId"": ""t0"",
                ""genericParams"": [
                    { ""id"": ""p1"", ""name"": ""T"" },
                    { ""id"": ""p2"", ""name"": ""U"",
                      ""constraint"": { ""kind"": ""enum"", ""enumId"": ""e1"" } }
                ],
                ""extendsGenericBindings"": {
                    ""pp1"": { ""kind"": ""generic"", ""genericParamId"": ""p1"" },
                    ""pp2"": { ""kind"": ""attribute"", ""attributeId"": ""a1"" }
                }
            }";
            var type = JsonConvert.DeserializeObject<CustomType>(json)!;

            Assert.AreEqual(2, type.genericParams!.Count);
            Assert.AreEqual("p1", type.genericParams[0].id);
            Assert.IsNull(type.genericParams[0].constraint);
            Assert.AreEqual(NeoGenericConstraintKinds.Enum, type.genericParams[1].constraint!.kind);
            Assert.AreEqual("e1", type.genericParams[1].constraint!.enumId);

            Assert.AreEqual(2, type.extendsGenericBindings!.Count);
            Assert.IsTrue(type.extendsGenericBindings["pp1"].IsForward);
            Assert.AreEqual("p1", type.extendsGenericBindings["pp1"].genericParamId);
            Assert.IsFalse(type.extendsGenericBindings["pp2"].IsForward);
            Assert.AreEqual("a1", type.extendsGenericBindings["pp2"].attributeId);
        }

        [Test]
        public void Json_CustomType_AbsentGenericFieldsStayNull()
        {
            const string json = @"{""id"": ""t1"", ""projectId"": ""p"", ""name"": ""Foo"", ""schema"": {}}";
            var type = JsonConvert.DeserializeObject<CustomType>(json)!;
            Assert.IsNull(type.genericParams);
            Assert.IsNull(type.extendsGenericBindings);
        }

        [Test]
        public void Json_GenericAttribute_ReadsOrdinal21()
        {
            const string json = @"{""id"": ""a1"", ""projectId"": ""p"", ""name"": ""Slot"", ""type"": 21, ""isStatic"": false, ""genericParamId"": ""p1""}";
            var attribute = JsonConvert.DeserializeObject<Attribute>(json);
            Assert.IsInstanceOf<GenericAttribute>(attribute);
            Assert.AreEqual("p1", ((GenericAttribute)attribute!).genericParamId);
        }

        [Test]
        public void Json_AttributeVirtualAndAbstractFlagsPreserveAbsenceAndValues()
        {
            const string absentJson = @"{""id"": ""a1"", ""projectId"": ""p"", ""name"": ""Name"", ""type"": 5, ""isStatic"": false}";
            const string trueJson = @"{""id"": ""a2"", ""projectId"": ""p"", ""name"": ""Name"", ""type"": 5, ""isStatic"": false, ""isVirtual"": true, ""isAbstract"": true}";
            const string falseJson = @"{""id"": ""a3"", ""projectId"": ""p"", ""name"": ""Name"", ""type"": 5, ""isStatic"": false, ""isVirtual"": false, ""isAbstract"": false}";

            var absent = JsonConvert.DeserializeObject<Attribute>(absentJson)!;
            var trueValues = JsonConvert.DeserializeObject<Attribute>(trueJson)!;
            var falseValues = JsonConvert.DeserializeObject<Attribute>(falseJson)!;

            Assert.IsNull(absent.isVirtual);
            Assert.IsNull(absent.isAbstract);
            Assert.AreEqual(true, trueValues.isVirtual);
            Assert.AreEqual(true, trueValues.isAbstract);
            Assert.AreEqual(false, falseValues.isVirtual);
            Assert.AreEqual(false, falseValues.isAbstract);
        }

        [Test]
        public void Json_CustomAttribute_ReadsTypeArguments()
        {
            const string json = @"{
                ""id"": ""a1"", ""projectId"": ""p"", ""name"": ""Slot"", ""type"": 7, ""isStatic"": false,
                ""customTypeId"": ""t1"",
                ""customTypeArguments"": {
                    ""p1"": { ""kind"": ""attribute"", ""attributeId"": ""a2"" }
                }
            }";
            var attribute = JsonConvert.DeserializeObject<Attribute>(json);
            Assert.IsInstanceOf<CustomAttribute>(attribute);
            var custom = (CustomAttribute)attribute!;
            Assert.AreEqual(1, custom.customTypeArguments!.Count);
            Assert.AreEqual("a2", custom.customTypeArguments["p1"].attributeId);
        }

        [Test]
        public void Json_ValueRow_RoundTripsGenericBindingsStamp()
        {
            const string json = @"{""id"": ""v1"", ""value"": [], ""genericBindings"": {""p1"": ""a1""}}";
            var row = JsonConvert.DeserializeObject<AttributeValue>(json)!;
            Assert.AreEqual("a1", row.genericBindings!["p1"]);

            string serialized = JsonConvert.SerializeObject(row);
            var reparsed = JsonConvert.DeserializeObject<AttributeValue>(serialized)!;
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
            var env = NeoGenericResolution.ResolveEnv(client, "type-damage");

            // T (declared by EnchantCard) forwards through MiddleCard's U to
            // the leaf's concrete binding; U resolves to the same terminal.
            Assert.IsTrue(env[NeoGenericTestFixture.ParamT].IsBound);
            Assert.AreEqual("attr-binding-float", env[NeoGenericTestFixture.ParamT].attributeId);
            Assert.IsTrue(env[NeoGenericTestFixture.ParamU].IsBound);
            Assert.AreEqual("attr-binding-float", env[NeoGenericTestFixture.ParamU].attributeId);
            Assert.IsTrue(NeoGenericResolution.IsClosedType(client, "type-damage"));
        }

        [Test]
        public void ResolveEnv_ForwardingMiddle_LeavesDeepestForwardTargetUnbound()
        {
            var client = LoadClient();
            var env = NeoGenericResolution.ResolveEnv(client, "type-middle");

            // T's terminal at MiddleCard is the forward target U — the param
            // a further descendant must bind.
            Assert.IsFalse(env[NeoGenericTestFixture.ParamT].IsBound);
            Assert.AreEqual(NeoGenericTestFixture.ParamU, env[NeoGenericTestFixture.ParamT].unboundParamId);
            Assert.IsFalse(env[NeoGenericTestFixture.ParamU].IsBound);
            Assert.IsFalse(NeoGenericResolution.IsClosedType(client, "type-middle"));
        }

        [Test]
        public void ResolveEnv_NonGenericChain_IsEmptyAndClosed()
        {
            var client = LoadClient();
            Assert.AreEqual(0, NeoGenericResolution.ResolveEnv(client, "type-card-base").Count);
            Assert.IsTrue(NeoGenericResolution.IsClosedType(client, "type-card-base"));
        }

        // ------------------------------------------------------------------
        // Instance env overlay (spec §4.1).
        // ------------------------------------------------------------------

        [Test]
        public void ResolveInstanceEnv_OverlaysConstructedSlotArgumentsOverOpenChainEnv()
        {
            var client = LoadClient();
            var slot = (CustomAttribute)client.attributes["attr-constructed-slot"];

            // EnchantCard's chain leaves T unbound; the slot's Float
            // argument is the terminal binding at the usage site.
            var env = NeoGenericResolution.ResolveInstanceEnv(
                client, "type-enchant", slot.customTypeArguments);

            Assert.IsTrue(env[NeoGenericTestFixture.ParamT].IsBound);
            Assert.AreEqual("attr-binding-float", env[NeoGenericTestFixture.ParamT].attributeId);
            Assert.IsNull(NeoGenericResolution.FirstUnboundParamId(env));
        }

        [Test]
        public void ResolveInstanceEnv_ChainBindingsWinOverSlotArguments()
        {
            var client = LoadClient();

            // A named closed subtype picked into a constructed slot: the
            // chain's own binding stays authoritative (admission guarantees
            // signature equality, so they agree — here they deliberately
            // differ to prove precedence).
            var env = NeoGenericResolution.ResolveInstanceEnv(
                client,
                "type-damage",
                new Dictionary<string, GenericBinding>
                {
                    [NeoGenericTestFixture.ParamT] = new()
                    {
                        kind = NeoGenericBindingKinds.Attribute,
                        attributeId = "attr-binding-string",
                    },
                });

            Assert.IsTrue(env[NeoGenericTestFixture.ParamT].IsBound);
            Assert.AreEqual("attr-binding-float", env[NeoGenericTestFixture.ParamT].attributeId);
        }

        [Test]
        public void ResolveInstanceEnv_ForwardsStayUnbound_AndIdentityWithoutArguments()
        {
            var client = LoadClient();

            // Generic-kind arguments (still-open forwards) stay unbound —
            // enclosing contexts substitute the slot before descent.
            var forwarded = NeoGenericResolution.ResolveInstanceEnv(
                client,
                "type-enchant",
                new Dictionary<string, GenericBinding>
                {
                    [NeoGenericTestFixture.ParamT] = new()
                    {
                        kind = NeoGenericBindingKinds.Generic,
                        genericParamId = NeoGenericTestFixture.ParamU,
                    },
                });
            Assert.AreEqual(
                NeoGenericTestFixture.ParamT,
                NeoGenericResolution.FirstUnboundParamId(forwarded));

            // Identity with ResolveEnv for slots without arguments.
            var bare = NeoGenericResolution.ResolveInstanceEnv(client, "type-enchant", null);
            Assert.AreEqual(
                NeoGenericTestFixture.ParamT,
                NeoGenericResolution.FirstUnboundParamId(bare));
        }

        // ------------------------------------------------------------------
        // Substitution (Decision-10 field partition).
        // ------------------------------------------------------------------

        [Test]
        public void SubstituteAttribute_PartitionsSlotAndBindingFields()
        {
            var client = LoadClient();
            var env = NeoGenericResolution.ResolveEnv(client, "type-damage");
            var slot = client.attributes["attr-speed"];

            var substituted = NeoGenericResolution.SubstituteAttribute(client, slot, env);

            Assert.IsInstanceOf<FloatAttribute>(substituted);
            // Slot identity/placement fields win.
            Assert.AreEqual("attr-speed", substituted.id);
            Assert.AreEqual("Speed", substituted.name);
            Assert.IsTrue(substituted.locked);
            Assert.AreEqual(false, substituted.isVirtual,
                "the slot's virtual declaration must not come from its binding");
            Assert.AreEqual(true, substituted.isAbstract,
                "the slot's abstract declaration must not come from its binding");
            Assert.IsNull(substituted.storage,
                "the binding's storage declaration must not leak into the slot");
            Assert.IsNull(substituted.extendsAttributeId);
            // Binding type/config/required/defaultValue win.
            var substitutedFloat = (FloatAttribute)substituted;
            Assert.IsTrue(substitutedFloat.required);
            Assert.AreEqual(0f, substitutedFloat.minValue);
            Assert.AreEqual(3.5, substitutedFloat.defaultValue!.value);
        }

        [Test]
        public void SubstituteAttribute_AbsentSlotFlagsClearBindingFlags()
        {
            var client = LoadClient();
            var env = NeoGenericResolution.ResolveEnv(client, "type-damage");
            var slot = client.attributes["attr-speed"];
            slot.isVirtual = null;
            slot.isAbstract = null;
            var binding = client.attributes["attr-binding-float"];
            binding.isVirtual = false;
            binding.isAbstract = true;

            var substituted = NeoGenericResolution.SubstituteAttribute(client, slot, env);

            Assert.IsNull(substituted.isVirtual,
                "an absent slot declaration must clear the binding's virtual flag");
            Assert.IsNull(substituted.isAbstract,
                "an absent slot declaration must clear the binding's abstract flag");
        }

        [Test]
        public void SubstituteAttribute_NonGenericRecord_IsIdentity()
        {
            var client = LoadClient();
            var env = NeoGenericResolution.ResolveEnv(client, "type-damage");
            var plain = client.attributes["attr-binding-string"];
            Assert.AreSame(plain, NeoGenericResolution.SubstituteAttribute(client, plain, env));
        }

        [Test]
        public void SubstituteAttribute_UnboundParam_ThrowsDescriptively()
        {
            var client = LoadClient();
            var openEnv = NeoGenericResolution.ResolveEnv(client, "type-middle");
            var slot = client.attributes["attr-speed"];

            var error = Assert.Throws<System.InvalidOperationException>(
                () => NeoGenericResolution.SubstituteAttribute(client, slot, openEnv))!;
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
            var env = NeoGenericResolution.ResolveEnv(client, "type-damage");
            var listAttribute = client.attributes["attr-values"];

            var stamp = NeoGenericResolution.ComputeGenericBindingsStamp(client, listAttribute, env)!;
            Assert.AreEqual(1, stamp.Count);
            Assert.AreEqual("attr-binding-float", stamp[NeoGenericTestFixture.ParamT]);

            var rebuilt = NeoGenericResolution.EnvFromStamp(stamp);
            Assert.IsTrue(rebuilt[NeoGenericTestFixture.ParamT].IsBound);
            Assert.AreEqual("attr-binding-float", rebuilt[NeoGenericTestFixture.ParamT].attributeId);
        }

        [Test]
        public void ComputeGenericBindingsStamp_NonGenericSubtree_IsNull()
        {
            var client = LoadClient();
            var env = NeoGenericResolution.ResolveEnv(client, "type-damage");
            var plainList = client.attributes["attr-plain-list"];
            Assert.IsNull(NeoGenericResolution.ComputeGenericBindingsStamp(client, plainList, env));
        }

        [Test]
        public void ComputeGenericBindingsStamp_UnboundEnv_Throws()
        {
            var client = LoadClient();
            var openEnv = NeoGenericResolution.ResolveEnv(client, "type-middle");
            var listAttribute = client.attributes["attr-values"];
            var error = Assert.Throws<System.InvalidOperationException>(
                () => NeoGenericResolution.ComputeGenericBindingsStamp(client, listAttribute, openEnv))!;
            StringAssert.Contains("unbound", error.Message);
        }

        // ------------------------------------------------------------------
        // Live node substitution + SDK-side stamping.
        // ------------------------------------------------------------------

        [Test]
        public void CustomNode_SubstitutesGenericChildBeforeDispatch()
        {
            var client = LoadClient();
            var card = client.save.Get<NeoAttributeCustomWritable>("Card");

            // The T slot on a closed Float instance constructs the Float
            // wrapper and materializes the BINDING's default (3.5).
            var speed = card.Get<NeoAttributeFloatWritable>("Speed");
            Assert.AreEqual(3.5, speed.value?.value);

            // The same slot on the String leaf constructs the String wrapper.
            var stringCard = client.save.Get<NeoAttributeCustomWritable>("StringCard");
            Assert.IsTrue(stringCard.TryGet("Speed", out NeoAttributeString? stringSpeed));
            Assert.IsNull(stringSpeed!.value?.value);
        }

        [Test]
        public void CustomNode_WritesGenericMemberThroughSubstitutedKind()
        {
            var client = LoadClient();
            var card = client.save.Get<NeoAttributeCustomWritable>("Card");

            card.SetSerializedValue("Speed", NeoValueWritePayload.FromValue((double?)2.5));

            Assert.AreEqual(2.5, card.Get<NeoAttributeFloatWritable>("Speed").value?.value);
        }

        [Test]
        public void CollectionRow_MintedBySdk_CarriesStampAndTypedEntries()
        {
            var client = LoadClient();
            var card = client.save.Get<NeoAttributeCustomWritable>("Card");

            var values = card.GetOrCreateCollection<NeoAttributeListWritable>("Values");
            Assert.IsNotNull(values.value, "GetOrCreateCollection binds an empty list row");
            Assert.IsNotNull(values.value!.genericBindings, "SDK-minted generic collection rows must be stamped");
            Assert.AreEqual(
                "attr-binding-float",
                values.value!.genericBindings![NeoGenericTestFixture.ParamT]);

            values.AddSerialized(NeoValueWritePayload.FromValue((double?)1.25));
            Assert.AreEqual(1, values.Count);
            Assert.IsInstanceOf<NeoAttributeFloat>(values[0],
                "the stamped row substitutes the Generic entry to the Float wrapper");
            Assert.AreEqual(1.25, ((NeoAttributeFloat)values[0]).value?.value);
        }

        [Test]
        public void CustomNode_ConstructedSlotInstance_ReadsChildThroughSlotArguments()
        {
            var client = LoadClient();

            // The instance row carries `typeId: null` (the DECLARED open
            // type) — its params are bound only by the slot's constructed
            // arguments. Descending previously threw "param ... is unbound
            // in the binding environment — only closed types are
            // instantiable" (spec §4.1).
            var constructed = client.save.Get<NeoAttributeCustomWritable>("Constructed");
            var speed = constructed.Get<NeoAttributeFloatWritable>("Speed");
            Assert.AreEqual(7.25, speed.value?.value);
        }

        [Test]
        public void Factory_CreateWritableCustomValue_SubstitutesRequiredDefaults()
        {
            var client = LoadClient();

            var node = NeoGeneratedTypesSupport.CreateWritableCustomValue(
                client,
                "type-damage",
                new Dictionary<string, string>(),
                System.Array.Empty<AttributeValue>());

            // Speed substitutes to the required Float binding, so the
            // factory materializes its default row.
            Assert.AreEqual(3.5, node.Get<NeoAttributeFloatWritable>("Speed").value?.value);
        }

        [Test]
        public void Factory_CreateWritableCustomValue_RejectsOpenType()
        {
            var client = LoadClient();
            var error = Assert.Throws<System.InvalidOperationException>(
                () => NeoGeneratedTypesSupport.CreateWritableCustomValue(
                    client,
                    "type-middle",
                    new Dictionary<string, string>(),
                    System.Array.Empty<AttributeValue>()))!;
            StringAssert.Contains("open generic type", error.Message);
        }

        // ------------------------------------------------------------------
        // Constructed-slot admission (spec §3.4).
        // ------------------------------------------------------------------

        [Test]
        public void ConstructedSlotAccepts_MatchingClosedDescendant_Ok()
        {
            var client = LoadClient();
            var slot = (CustomAttribute)client.attributes["attr-constructed-slot"];

            var admission = NeoGenericResolution.ConstructedSlotAccepts(
                client, slot, "type-damage", NeoGenericResolution.EmptyEnv);

            Assert.IsTrue(admission.ok, admission.reason);
        }

        [Test]
        public void ConstructedSlotAccepts_DeclaredOpenTypeClosedBySlotArguments_Ok()
        {
            var client = LoadClient();
            var slot = (CustomAttribute)client.attributes["attr-constructed-slot"];

            // The DECLARED open type itself is admissible when the slot's
            // arguments close it — rejecting it as "open" would leave
            // constructed slots with no valid pick at all (spec §3.4).
            var admission = NeoGenericResolution.ConstructedSlotAccepts(
                client, slot, "type-enchant", NeoGenericResolution.EmptyEnv);

            Assert.IsTrue(admission.ok, admission.reason);
        }

        [Test]
        public void ConstructedSlotAccepts_SignatureMismatch_Rejected()
        {
            var client = LoadClient();
            var slot = (CustomAttribute)client.attributes["attr-constructed-slot"];

            // String argument vs the slot's Float argument.
            var stringLeaf = NeoGenericResolution.ConstructedSlotAccepts(
                client, slot, "type-string-card", NeoGenericResolution.EmptyEnv);
            Assert.IsFalse(stringLeaf.ok);
            StringAssert.Contains("signature mismatch", stringLeaf.reason);

            // Optional Float vs required Float — `required` is part of the
            // signature (nullability is part of the type, Decision 8).
            var optionalLeaf = NeoGenericResolution.ConstructedSlotAccepts(
                client, slot, "type-optional-float", NeoGenericResolution.EmptyEnv);
            Assert.IsFalse(optionalLeaf.ok);
            StringAssert.Contains("signature mismatch", optionalLeaf.reason);
        }

        [Test]
        public void ConstructedSlotAccepts_OpenType_Rejected()
        {
            var client = LoadClient();
            var slot = (CustomAttribute)client.attributes["attr-constructed-slot"];
            var admission = NeoGenericResolution.ConstructedSlotAccepts(
                client, slot, "type-middle", NeoGenericResolution.EmptyEnv);
            Assert.IsFalse(admission.ok);
            StringAssert.Contains("open", admission.reason);
        }

        [Test]
        public void ConstructedSlotAccepts_NonDescendant_Rejected()
        {
            var client = LoadClient();
            var slot = (CustomAttribute)client.attributes["attr-constructed-slot"];
            var admission = NeoGenericResolution.ConstructedSlotAccepts(
                client, slot, "root-type", NeoGenericResolution.EmptyEnv);
            Assert.IsFalse(admission.ok);
            StringAssert.Contains("descendants", admission.reason);
        }

        [Test]
        public void ConstructedSlotAccepts_MissingType_Rejected()
        {
            var client = LoadClient();
            var slot = (CustomAttribute)client.attributes["attr-constructed-slot"];
            var admission = NeoGenericResolution.ConstructedSlotAccepts(
                client, slot, "type-nope", NeoGenericResolution.EmptyEnv);
            Assert.IsFalse(admission.ok);
            StringAssert.Contains("does not exist", admission.reason);
        }
    }
}
