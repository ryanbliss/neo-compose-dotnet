// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine.TestTools;

namespace NeoCompose.Tests
{
    public class NeoReadOnlyMemberTests
    {
        private const string ProjectId = "project-readonly";

        [Test]
        public void MemberDto_RoundTripsSparseNullableReadOnlyFlag()
        {
            var absent = JsonConvert.DeserializeObject<Member>(
                "{\"id\":\"m1\",\"projectId\":\"p\",\"name\":\"Damage\",\"kind\":2,\"isStatic\":false,\"accessModifierKind\":\"public\"}");
            var enabled = JsonConvert.DeserializeObject<Member>(
                "{\"id\":\"m2\",\"projectId\":\"p\",\"name\":\"Damage\",\"kind\":2,\"isStatic\":false,\"accessModifierKind\":\"public\",\"isReadOnly\":true}");

            Assert.IsNull(absent!.isReadOnly);
            Assert.AreEqual(true, enabled!.isReadOnly);
            StringAssert.Contains("\"isReadOnly\":true", JsonConvert.SerializeObject(enabled));
        }

        [Test]
        public void SchemaProjections_SeparateSurfaceStoredAndReadOnlyMembers()
        {
            ProjectData data = BuildProjectData();
            IList<NeoSchemaClass> chain = NeoSchemaClassInheritance.ResolveChain(
                "class-weapon",
                id => data.classes.TryGetValue(id, out NeoSchemaClass match) ? match : null);
            Member? Lookup(string id) => data.members.TryGetValue(id, out Member match) ? match : null;

            IList<MergedSchemaEntry> surface =
                NeoSchemaClassInheritance.MergeInstanceSurfaceSchema(chain, Lookup);
            IList<MergedSchemaEntry> stored =
                NeoSchemaClassInheritance.MergeStoredInstanceSchema(chain, Lookup);
            IList<MergedSchemaEntry> readOnly =
                NeoSchemaClassInheritance.MergeReadOnlyMembers(chain, Lookup);

            CollectionAssert.AreEqual(
                new[] { "BaseDamage", "Details", "RolledDamage" },
                SchemaKeys(surface));
            CollectionAssert.AreEqual(new[] { "RolledDamage" }, SchemaKeys(stored));
            CollectionAssert.AreEqual(new[] { "BaseDamage", "Details" }, SchemaKeys(readOnly));
        }

        [Test]
        public void RuntimeNodes_ResolveOneSyntheticPrimitiveAndCompositeDefault()
        {
            NeoClient client = LoadClient();
            Assert.AreSame(
                client.ResolveClassInheritanceChain("class-weapon"),
                client.ResolveClassInheritanceChain("class-weapon"));
            Assert.AreSame(
                client.ResolveInstanceSurfaceSchema("class-weapon"),
                client.ResolveInstanceSurfaceSchema("class-weapon"));
            var assetWeapon = client.AssetsRoot.Get<NeoMemberClass>("Weapon");
            var saveWeapon = client.SaveRoot.Get<NeoMemberClassWritable>("Weapon");

            NeoMemberInt assetDamage = assetWeapon.Get<NeoMemberInt>("BaseDamage");
            NeoMemberInt saveDamage = saveWeapon.Get<NeoMemberInt>("BaseDamage");
            Assert.AreSame(assetDamage, saveDamage);
            Assert.AreEqual(12, assetDamage.value!.value);
            Assert.AreEqual("__neo_readonly_default:member-base-damage", assetDamage.value.id);
            Assert.IsNull(assetDamage.parent);

            NeoMemberClass assetDetails = assetWeapon.Get<NeoMemberClass>("Details");
            NeoMemberClass saveDetails = saveWeapon.Get<NeoMemberClass>("Details");
            Assert.AreSame(assetDetails, saveDetails);
            Assert.AreEqual("__neo_readonly_default:member-details", assetDetails.value!.id);
            Assert.AreEqual("shared", assetDetails.Get<NeoMemberString>("Name").value!.value);
            Assert.IsNull(assetDetails.parent);
            Assert.AreSame(assetDetails, assetDetails.Get<NeoMemberString>("Name").parent);

            Assert.IsFalse(saveWeapon["BaseDamage"] is NeoMemberIntWritable);
            Assert.IsFalse(saveWeapon["Details"] is NeoMemberClassWritable);
        }

        [Test]
        public void RuntimeNodes_ResolveSharedListDictionaryAndLookupDefaults()
        {
            ProjectData data = BuildProjectData();
            AddReadOnlyCollectionMembers(data);
            NeoClient client = LoadClient(data);
            var assetWeapon = client.AssetsRoot.Get<NeoMemberClass>("Weapon");
            var saveWeapon = client.SaveRoot.Get<NeoMemberClassWritable>("Weapon");

            var assetBonuses = assetWeapon.Get<NeoMemberList>("Bonuses");
            var saveBonuses = saveWeapon.Get<NeoMemberList>("Bonuses");
            Assert.AreSame(assetBonuses, saveBonuses);
            Assert.AreEqual(2, assetBonuses.Count);
            Assert.AreEqual(3, ((NeoMemberInt)assetBonuses[0]).value!.value);
            Assert.IsFalse(assetBonuses is NeoMemberListWritable);

            var assetLabels = assetWeapon.Get<NeoMemberDictionary>("Labels");
            var saveLabels = saveWeapon.Get<NeoMemberDictionary>("Labels");
            Assert.AreSame(assetLabels, saveLabels);
            Assert.AreEqual("primary", ((NeoMemberString)assetLabels["main"]).value!.value);
            Assert.IsFalse(assetLabels is NeoMemberDictionaryWritable);

            var assetFavorite = assetWeapon.Get<NeoMemberLookup>("Favorite");
            var saveFavorite = saveWeapon.Get<NeoMemberLookup>("Favorite");
            Assert.AreSame(assetFavorite, saveFavorite);
            Assert.IsFalse(assetFavorite is NeoMemberLookupWritable);
            var selected = assetFavorite.GetSelected();
            Assert.AreEqual(1, selected.Count);
            Assert.AreEqual(
                "lookup target",
                ((NeoMemberClass)selected[0]).Get<NeoMemberString>("Name").value!.value);
        }

        [Test]
        public void RegularOverrideWithoutDefault_DoesNotInheritBaseDefault()
        {
            ProjectData data = BuildProjectData();
            var baseMember = new IntMember
            {
                id = "member-regular-base",
                projectId = ProjectId,
                name = "RegularBase",
                kind = MemberKind.Int,
                defaultValue = new NumberMemberValueBase { value = 5 },
                createdAt = "x",
                updatedAt = "x",
            };
            var overrideMember = new IntMember
            {
                id = "member-regular-override",
                projectId = ProjectId,
                name = "RegularOverride",
                kind = MemberKind.Int,
                extendsMemberId = baseMember.id,
                createdAt = "x",
                updatedAt = "x",
            };
            data.members[baseMember.id] = baseMember;
            data.members[overrideMember.id] = overrideMember;

            NeoClient client = LoadClient(data);
            var node = new NeoMemberInt(client, overrideMember, overrideValueId: null);

            Assert.IsNull(node.value);
        }

        [Test]
        public void RuntimeWritesAndFactories_RejectReadOnlyInstanceEdges()
        {
            NeoClient client = LoadClient();
            var saveWeapon = client.SaveRoot.Get<NeoMemberClassWritable>("Weapon");

            var writeError = Assert.Throws<System.InvalidOperationException>(() =>
                NeoGeneratedTypesSupport.SetValue(
                    saveWeapon,
                    "BaseDamage",
                    NeoValueWritePayload.FromValue(99d)));
            StringAssert.Contains("read-only declaration member", writeError!.Message);

            var removeError = Assert.Throws<System.InvalidOperationException>(() =>
                saveWeapon.Remove("BaseDamage"));
            StringAssert.Contains("read-only declaration member", removeError!.Message);

            var unsetError = Assert.Throws<System.InvalidOperationException>(() =>
                saveWeapon.Unset("BaseDamage"));
            StringAssert.Contains("read-only declaration member", unsetError!.Message);

            var bindError = Assert.Throws<System.InvalidOperationException>(() =>
                saveWeapon.BindChildValueId(
                    saveWeapon.Get<NeoMemberInt>("BaseDamage"),
                    "value-illegal-binding"));
            StringAssert.Contains("read-only declaration member", bindError!.Message);

            NeoMemberClassWritable created =
                NeoGeneratedTypesSupport.CreateWritableClassValue(
                    client,
                    "class-weapon",
                    System.Array.Empty<NeoGeneratedConstructorValue>());
            Assert.AreEqual(12, created.Get<NeoMemberInt>("BaseDamage").value!.value);
            Assert.IsFalse(created.value!.value!.ContainsKey("BaseDamage"));
            Assert.IsFalse(created.value.value.ContainsKey("Details"));

            var constructorError = Assert.Throws<System.InvalidOperationException>(() =>
                NeoGeneratedTypesSupport.CreateWritableClassValue(
                    client,
                    "class-weapon",
                    new NeoGeneratedConstructorValue(
                        "BaseDamage",
                        "member-base-damage",
                        99)));
            StringAssert.Contains("read-only declaration member", constructorError!.Message);
            StringAssert.DoesNotContain("__neo_readonly_default", client.SerializeSaveData());
        }

        [Test]
        public void NeoScriptRead_UsesDeclarationDefaultWhenInstanceKeyIsAbsent()
        {
            NeoClient client = LoadClient();
            var context = new NSGetterEvaluator.Context(client, null, null);
            var getter = new FunctionWithReturnType
            {
                compilerRevision = 2,
                parameters = System.Array.Empty<Variable>(),
                typeInfo = new PrimitiveTypeInfo
                {
                    type = MemberKind.Int,
                    required = true,
                },
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = new KeyOfPointer
                        {
                            type = PointerKind.KeyOf,
                            memberId = "member-base-damage",
                            keyOf = new KeyOf
                            {
                                pointer = new ReferencePointer
                                {
                                    type = PointerKind.Reference,
                                    valueId = "value-weapon-asset",
                                },
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
                                        value = JToken.FromObject("BaseDamage"),
                                    },
                                },
                            },
                        },
                    },
                },
            };

            Assert.AreEqual(12d, NSGetterEvaluator.Evaluate(getter, context));
        }

        [Test]
        public void NeoScriptRead_DeclarationDefaultWinsStaleInstanceKey()
        {
            ProjectData data = BuildProjectData();
            NeoClient client = LoadClient(data);
            ((ObjectMemberValue)data.values["value-weapon-save"]).value!["BaseDamage"] =
                "value-stale-base-damage";
            data.values["value-stale-base-damage"] = new NumberMemberValue
            {
                id = "value-stale-base-damage",
                createdAt = "x",
                updatedAt = "x",
                value = 99,
            };

            Assert.AreEqual(
                12d,
                EvaluateMemberAccess(
                    client,
                    "value-weapon-save",
                    "BaseDamage",
                    "member-base-damage",
                    MemberKind.Int));
        }

        [Test]
        public void NeoScriptRead_UsesRuntimeReadonlyMemberForInheritedAndInterfacePointers()
        {
            ProjectData data = BuildProjectData();
            data.classes["class-derived-weapon"] = new NeoSchemaClass
            {
                id = "class-derived-weapon",
                projectId = ProjectId,
                name = "DerivedWeapon",
                schema = new Dictionary<string, string>(),
                extendsClassId = "class-weapon",
                implementsInterfaceIds = new List<string> { "interface-damage" },
                createdAt = "x",
                updatedAt = "x",
            };
            ((ObjectMemberValue)data.values["value-weapon-save"]).classId =
                "class-derived-weapon";
            NeoClient client = LoadClient(data);

            Assert.AreEqual(
                12d,
                EvaluateMemberAccess(
                    client,
                    "value-weapon-save",
                    "BaseDamage",
                    "member-base-damage",
                    MemberKind.Int));
            Assert.AreEqual(
                12d,
                EvaluateMemberAccess(
                    client,
                    "value-weapon-save",
                    "BaseDamage",
                    "interface-member-damage",
                    MemberKind.Int));

            ProjectData overrideData = BuildProjectData();
            var regularOverride = new IntMember
            {
                id = "member-derived-damage",
                projectId = ProjectId,
                name = "BaseDamage",
                kind = MemberKind.Int,
                required = true,
                storage = "immutable",
                extendsMemberId = "member-base-damage",
                createdAt = "x",
                updatedAt = "x",
            };
            overrideData.members[regularOverride.id] = regularOverride;
            overrideData.classes["class-derived-weapon"] = new NeoSchemaClass
            {
                id = "class-derived-weapon",
                projectId = ProjectId,
                name = "DerivedWeapon",
                schema = new Dictionary<string, string>
                {
                    ["BaseDamage"] = regularOverride.id,
                },
                extendsClassId = "class-weapon",
                createdAt = "x",
                updatedAt = "x",
            };
            var overrideRow = (ObjectMemberValue)overrideData.values["value-weapon-save"];
            overrideRow.classId = "class-derived-weapon";
            overrideRow.value!["BaseDamage"] = "value-derived-damage";
            overrideData.values["value-derived-damage"] = new NumberMemberValue
            {
                id = "value-derived-damage",
                createdAt = "x",
                updatedAt = "x",
                value = 33,
            };
            NeoClient overrideClient = LoadClient(overrideData);

            Assert.AreEqual(
                33d,
                EvaluateMemberAccess(
                    overrideClient,
                    "value-weapon-save",
                    "BaseDamage",
                    "member-base-damage",
                    MemberKind.Int));
        }

        [Test]
        public void NeoScriptRead_InterfacePinnedFallbackUsesRegisteredReadonlyDeclaration()
        {
            ProjectData data = BuildProjectData();
            var interfaceDeclaration = new IntMember
            {
                id = "interface-member-damage",
                projectId = ProjectId,
                name = "InterfaceDamage",
                kind = MemberKind.Int,
                required = true,
                storage = "immutable",
                isReadOnly = true,
                defaultValue = new NumberMemberValueBase { value = 44 },
                createdAt = "x",
                updatedAt = "x",
            };
            data.members[interfaceDeclaration.id] = interfaceDeclaration;
            data.classes["class-interface-declarations"] = ClassOf(
                "class-interface-declarations",
                "InterfaceDeclarations",
                new Dictionary<string, string>
                {
                    ["InterfaceDamage"] = interfaceDeclaration.id,
                });
            data.interfaces["interface-damage"] = new Interface
            {
                id = "interface-damage",
                projectId = ProjectId,
                name = "Damage",
                members = new Dictionary<string, InterfaceMember>
                {
                    ["InterfaceDamage"] = new InterfaceMember
                    {
                        kind = "property",
                        accessModifierKind = "public",
                        typeInfo = new PrimitiveTypeInfo
                        {
                            type = MemberKind.Int,
                            required = true,
                        },
                        settable = false,
                    },
                },
                createdAt = "x",
                updatedAt = "x",
            };
            data.classes["class-weapon"].implementsInterfaceIds =
                new List<string> { "interface-damage" };

            // A raw legacy key proves the result came from the pinned
            // declaration fallback rather than ordinary record lookup.
            ((ObjectMemberValue)data.values["value-weapon-save"]).value!["InterfaceDamage"] =
                "value-stale-interface-damage";
            data.values["value-stale-interface-damage"] = new NumberMemberValue
            {
                id = "value-stale-interface-damage",
                createdAt = "x",
                updatedAt = "x",
                value = 99,
            };

            NeoClient client = LoadClient(data);

            Assert.IsTrue(client.TryGetMember(interfaceDeclaration.id, out Member? registered));
            Assert.AreSame(interfaceDeclaration, registered);
            Assert.AreEqual(
                44d,
                EvaluateMemberAccess(
                    client,
                    "value-weapon-save",
                    "InterfaceDamage",
                    interfaceDeclaration.id,
                    MemberKind.Int));
        }

        [Test]
        public void NeoScriptRead_LocalizesReadonlyStringAndDereferencesSingleLookup()
        {
            ProjectData localizedData = BuildProjectData();
            var title = new StringMember
            {
                id = "member-readonly-title",
                projectId = ProjectId,
                name = "Title",
                kind = MemberKind.String,
                required = true,
                storage = "immutable",
                isReadOnly = true,
                localizable = true,
                defaultValue = new StringMemberValueBase
                {
                    value = "text-readonly-title",
                },
                createdAt = "x",
                updatedAt = "x",
            };
            localizedData.members[title.id] = title;
            localizedData.classes["class-weapon"].schema["Title"] = title.id;
            localizedData.localization = new ProjectLocalizationExport
            {
                schemaVersion = 1,
                mainLocale = "en-US",
                supportedLocales = new[]
                {
                    new ProjectLocalizationLocale { locale = "en-US" },
                },
                textIds = new[] { "text-readonly-title" },
                mainLocaleFileName = "en-US.json",
            };
            NeoLocalization localization = NeoLocalization.LoadMain(
                localizedData.localization,
                new ReadOnlyLocalizationSource());
            NeoClient localizedClient = NeoTestSaveStack.ClientFromSchema(
                localizedData,
                localization: localization);

            Assert.AreEqual(
                "Localized declaration title",
                EvaluateMemberAccess(
                    localizedClient,
                    "value-weapon-save",
                    "Title",
                    title.id,
                    MemberKind.String));

            ProjectData lookupData = BuildProjectData();
            AddReadOnlyCollectionMembers(lookupData);
            NeoClient lookupClient = LoadClient(lookupData);
            Pointer favorite = MemberAccessPointer(
                new ReferencePointer
                {
                    type = PointerKind.Reference,
                    valueId = "value-weapon-save",
                },
                "Favorite",
                "member-readonly-favorite");
            Pointer name = MemberAccessPointer(
                favorite,
                "Name",
                "member-detail-name");

            Assert.AreEqual(
                "lookup target",
                EvaluatePointer(lookupClient, name, MemberKind.String));
        }

        [Test]
        public void NeoScriptCompilerRevision_AcceptsLegacyAndCurrentAndRejectsFuture()
        {
            NeoClient client = LoadClient();
            var context = new NSGetterEvaluator.Context(client, null, null);

            var legacy = new FunctionWithReturnType
            {
                parameters = System.Array.Empty<Variable>(),
                typeInfo = new PrimitiveTypeInfo
                {
                    type = MemberKind.Int,
                    required = true,
                },
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
                                typeInfo = new PrimitiveTypeInfo
                                {
                                    type = MemberKind.Int,
                                    required = true,
                                },
                                value = JToken.FromObject(7),
                            },
                        },
                    },
                },
            };

            Assert.AreEqual(7d, NSGetterEvaluator.Evaluate(legacy, context));

            legacy.compilerRevision = FunctionWithReturnType.CurrentCompilerRevision;
            Assert.AreEqual(7d, NSGetterEvaluator.Evaluate(legacy, context));

            legacy.compilerRevision = FunctionWithReturnType.CurrentCompilerRevision + 1;
            var futureError = Assert.Throws<NeoScriptPreExecutionValidationError>(() =>
                NSGetterEvaluator.Evaluate(legacy, context));
            StringAssert.Contains("Unsupported NeoScript compiler revision", futureError!.Message);

            legacy.compilerRevision = 0;
            Assert.Throws<NeoScriptPreExecutionValidationError>(() =>
                NSGetterEvaluator.Evaluate(legacy, context));
        }

        [Test]
        public void KeyOfPointer_RoundTripsPinnedMemberIdAtWireTopLevel()
        {
            const string json = @"{
  ""type"": ""keyOf"",
  ""memberId"": ""member-base-damage"",
  ""keyOf"": {
    ""pointer"": { ""type"": ""reference"", ""valueId"": ""value-weapon-asset"" },
    ""key"": {
      ""type"": ""value"",
      ""value"": {
        ""typeInfo"": { ""type"": 1, ""required"": true },
        ""value"": ""BaseDamage""
      }
    }
  }
}";

            var pointer = (KeyOfPointer)JsonConvert.DeserializeObject<Pointer>(json)!;
            Assert.AreEqual("member-base-damage", pointer.memberId);

            JObject roundTripped = JObject.Parse(JsonConvert.SerializeObject(pointer));
            Assert.AreEqual("member-base-damage", roundTripped["memberId"]!.Value<string>());
            Assert.IsNull(roundTripped["keyOf"]!["memberId"]);
        }

        [TestCase("save", null, false, false, "Immutable storage")]
        [TestCase("immutable", null, true, false, "cannot be static")]
        [TestCase("immutable", "value-illegal", false, false, "valueId")]
        public void SchemaValidation_RejectsInvalidReadOnlyDeclaration(
            string storage,
            string? valueId,
            bool isStatic,
            bool isAbstract,
            string expected)
        {
            ProjectData data = BuildProjectData();
            var member = (IntMember)data.members["member-base-damage"];
            member.storage = storage;
            member.valueId = valueId;
            member.isStatic = isStatic;
            member.isAbstract = isAbstract;

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                NeoTestSaveStack.ClientFromSchema(data));
            StringAssert.Contains(expected, error!.Message);
        }

        [Test]
        public void SchemaValidation_RejectsMissingDefaultAndInstanceKey()
        {
            ProjectData missingDefault = BuildProjectData();
            ((IntMember)missingDefault.members["member-base-damage"]).defaultValue = null;
            var defaultError = Assert.Throws<System.InvalidOperationException>(() =>
                NeoTestSaveStack.ClientFromSchema(missingDefault));
            StringAssert.Contains("explicit defaultValue", defaultError!.Message);

            ProjectData instanceKey = BuildProjectData();
            ((ObjectMemberValue)instanceKey.values["value-weapon-asset"]).value!["BaseDamage"] =
                "value-illegal";
            instanceKey.values["value-illegal"] = new NumberMemberValue
            {
                id = "value-illegal",
                createdAt = "x",
                updatedAt = "x",
                value = 99,
            };
            var keyError = Assert.Throws<System.InvalidOperationException>(() =>
                NeoTestSaveStack.ClientFromSchema(instanceKey));
            StringAssert.Contains("cannot have instance values", keyError!.Message);
        }

        [Test]
        public void SchemaValidation_RejectsReadOnlyOverrideWithInheritedOnlyDefault()
        {
            ProjectData data = BuildProjectData();
            var inheritedDefault = new IntMember
            {
                id = "member-inherited-default",
                projectId = ProjectId,
                name = "InheritedDefault",
                kind = MemberKind.Int,
                defaultValue = new NumberMemberValueBase { value = 5 },
                createdAt = "x",
                updatedAt = "x",
            };
            var readOnlyOverride = new IntMember
            {
                id = "member-readonly-inherited-only",
                projectId = ProjectId,
                name = "InheritedOnly",
                kind = MemberKind.Int,
                required = true,
                storage = "immutable",
                isReadOnly = true,
                extendsMemberId = inheritedDefault.id,
                createdAt = "x",
                updatedAt = "x",
            };
            data.members[inheritedDefault.id] = inheritedDefault;
            data.members[readOnlyOverride.id] = readOnlyOverride;
            data.classes["class-weapon"].schema["InheritedOnly"] = readOnlyOverride.id;

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                LoadClient(data));

            StringAssert.Contains("Read-only member 'InheritedOnly'", error!.Message);
            StringAssert.Contains("explicit defaultValue", error.Message);
        }

        [Test]
        public void AbstractReadonly_ConcreteReadonlyOverrideUsesDeclarationDefault()
        {
            ProjectData data = BuildProjectData();
            ConfigureAbstractDamageContract(data);

            NeoClient client = LoadClient(data);

            CollectionAssert.AreEqual(
                new[] { "Details" },
                SchemaKeys(client.ResolveReadOnlyMemberSchema("class-weapon")));
            CollectionAssert.AreEqual(
                new[] { "BaseDamage", "Details" },
                SchemaKeys(client.ResolveReadOnlyMemberSchema("class-concrete-weapon")));
            Assert.AreEqual(
                27,
                client.AssetsRoot
                    .Get<NeoMemberClass>("Weapon")
                    .Get<NeoMemberInt>("BaseDamage")
                    .value!.value);
        }

        [Test]
        public void AbstractReadonly_RejectsDeclarationDefault()
        {
            ProjectData data = BuildProjectData();
            ConfigureAbstractDamageContract(data);
            ((IntMember)data.members["member-base-damage"]).defaultValue =
                new NumberMemberValueBase { value = 12 };

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                LoadClient(data));

            StringAssert.Contains("abstract getter contract", error!.Message);
            StringAssert.Contains("cannot declare a defaultValue", error.Message);
        }

        [Test]
        public void AbstractReadonly_RejectsValueLessNeoScriptPropertyKind()
        {
            ProjectData data = BuildProjectData();
            var computed = new NSPropertyMember
            {
                id = "member-abstract-readonly-computed",
                projectId = ProjectId,
                name = "Computed",
                kind = MemberKind.NSProperty,
                storage = "immutable",
                isAbstract = true,
                isReadOnly = true,
                createdAt = "x",
                updatedAt = "x",
            };
            data.members[computed.id] = computed;
            data.classes["class-weapon"].schema["Computed"] = computed.id;

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                LoadClient(data));

            StringAssert.Contains("is not value-bearing", error!.Message);
        }

        [Test]
        public void AbstractReadonly_ConcreteClassMustProvideReadonlyOverride()
        {
            ProjectData data = BuildProjectData();
            ConfigureAbstractDamageContract(data, addOverride: false);

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                LoadClient(data));

            StringAssert.Contains("does not implement abstract read-only member", error!.Message);
        }

        [Test]
        public void AbstractReadonly_RejectsInstanceBackedOverride()
        {
            ProjectData data = BuildProjectData();
            ConfigureAbstractDamageContract(
                data,
                overrideReadOnly: false);

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                LoadClient(data));

            StringAssert.Contains("cannot implement abstract read-only member", error!.Message);
            StringAssert.Contains("non-read-only, instance-backed override", error.Message);
        }

        [Test]
        public void OverrideReadonly_ImplementsGetterOnlyImmutableAbstractMember()
        {
            ProjectData data = BuildProjectData();
            ConfigureAbstractDamageContract(data, baseReadOnly: false);

            NeoClient client = LoadClient(data);

            Assert.AreEqual(
                27,
                client.AssetsRoot
                    .Get<NeoMemberClass>("Weapon")
                    .Get<NeoMemberInt>("BaseDamage")
                    .value!.value);
        }

        [Test]
        public void OverrideReadonly_RejectsSetterRequiredAbstractMember()
        {
            ProjectData data = BuildProjectData();
            ConfigureAbstractDamageContract(
                data,
                baseReadOnly: false,
                baseStorage: "save");

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                LoadClient(data));

            StringAssert.Contains("setter-required abstract member", error!.Message);
        }

        [Test]
        public void OverrideReadonly_RejectsSettableInterfaceProperty()
        {
            ProjectData data = BuildProjectData();
            data.interfaces["interface-settable-damage"] = new Interface
            {
                id = "interface-settable-damage",
                projectId = ProjectId,
                name = "SettableDamage",
                members = new Dictionary<string, InterfaceMember>
                {
                    ["BaseDamage"] = new InterfaceMember
                    {
                        kind = "property",
                        accessModifierKind = "public",
                        typeInfo = new PrimitiveTypeInfo
                        {
                            type = MemberKind.Int,
                            required = true,
                        },
                        settable = true,
                    },
                },
                createdAt = "x",
                updatedAt = "x",
            };
            data.classes["class-weapon"].implementsInterfaceIds =
                new List<string> { "interface-settable-damage" };

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                LoadClient(data));

            StringAssert.Contains("cannot fulfill settable interface property", error!.Message);
        }

        [Test]
        public void SchemaValidation_RejectsReadonlyKeyInClassIdLessNestedPartitionRow()
        {
            ProjectData data = BuildProjectData();
            AddNestedReadOnlyClass(data);
            ((ObjectMemberValue)data.values["value-weapon-asset"]).value!["Nested"] =
                "value-nested-partition";
            var nested = RecordValue(
                "value-nested-partition",
                "class-nested-readonly",
                new Dictionary<string, string>
                {
                    ["Secret"] = "value-illegal-nested-secret",
                });
            nested.classId = null;
            nested.mapKey = "test:readonly";
            data.valuePartitions = new Dictionary<string, JToken>
            {
                ["test:readonly"] = JObject.FromObject(
                    new Dictionary<string, MemberValue>
                    {
                        [nested.id] = nested,
                    }),
            };

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                LoadClient(data));

            StringAssert.Contains("value-nested-partition", error!.Message);
            StringAssert.Contains("read-only declaration member key 'Secret'", error.Message);
        }

        [Test]
        public void SchemaValidation_SkipsComputedAggregateDefaultsDuringTrustedProjection()
        {
            ProjectData data = BuildProjectData();
            var computedDetails = new ClassMember
            {
                id = "member-computed-details",
                projectId = ProjectId,
                name = "ComputedDetails",
                kind = MemberKind.Class,
                classId = "class-details",
                required = true,
                defaultValue = new ObjectMemberValueBase
                {
                    init = new InitializerBody { code = "CreateDetails()" },
                },
                createdAt = "x",
                updatedAt = "x",
            };
            data.members[computedDetails.id] = computedDetails;
            data.classes["class-computed-default-owner"] = new NeoSchemaClass
            {
                id = "class-computed-default-owner",
                projectId = ProjectId,
                name = "ComputedDefaultOwner",
                schema = new Dictionary<string, string>
                {
                    ["ComputedDetails"] = computedDetails.id,
                },
                isAbstract = true,
                createdAt = "x",
                updatedAt = "x",
            };

            Assert.DoesNotThrow(() => LoadClient(data));
        }

        [Test]
        public void SchemaValidation_RejectsUnrelatedClassIdLessPlacementConflict()
        {
            ProjectData data = BuildProjectData();
            AddConflictingNestedPlacements(data);
            var weapon = (ObjectMemberValue)data.values["value-weapon-asset"];
            weapon.value!["NestedA"] = "value-shared-conflict";
            weapon.value["NestedB"] = "value-shared-conflict";
            data.values["value-shared-conflict"] = new ObjectMemberValue
            {
                id = "value-shared-conflict",
                createdAt = "x",
                updatedAt = "x",
                value = new Dictionary<string, string>(),
            };

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                LoadClient(data));

            StringAssert.Contains("value-shared-conflict", error!.Message);
            StringAssert.Contains("incompatible trusted Class placements", error.Message);
        }

        [Test]
        public void SchemaValidation_AllowsCompatibleBaseAndDerivedClassIdLessPlacements()
        {
            ProjectData data = BuildProjectData();
            var secret = new IntMember
            {
                id = "member-compatible-secret",
                projectId = ProjectId,
                name = "Secret",
                kind = MemberKind.Int,
                required = true,
                storage = "immutable",
                isReadOnly = true,
                defaultValue = new NumberMemberValueBase { value = 3 },
                createdAt = "x",
                updatedAt = "x",
            };
            var baseView = ClassMemberOf(
                "member-compatible-base-view",
                "BaseView",
                "class-compatible-base",
                valueId: null);
            var derivedView = ClassMemberOf(
                "member-compatible-derived-view",
                "DerivedView",
                "class-compatible-derived",
                valueId: null);
            data.members[secret.id] = secret;
            data.members[baseView.id] = baseView;
            data.members[derivedView.id] = derivedView;
            data.classes["class-compatible-base"] = ClassOf(
                "class-compatible-base",
                "CompatibleBase",
                new Dictionary<string, string> { ["Secret"] = secret.id });
            data.classes["class-compatible-derived"] = new NeoSchemaClass
            {
                id = "class-compatible-derived",
                projectId = ProjectId,
                name = "CompatibleDerived",
                schema = new Dictionary<string, string>(),
                extendsClassId = "class-compatible-base",
                createdAt = "x",
                updatedAt = "x",
            };
            data.classes["class-weapon"].schema["BaseView"] = baseView.id;
            data.classes["class-weapon"].schema["DerivedView"] = derivedView.id;
            var weapon = (ObjectMemberValue)data.values["value-weapon-asset"];
            weapon.value!["BaseView"] = "value-compatible-shared";
            weapon.value["DerivedView"] = "value-compatible-shared";
            data.values["value-compatible-shared"] = new ObjectMemberValue
            {
                id = "value-compatible-shared",
                createdAt = "x",
                updatedAt = "x",
                value = new Dictionary<string, string>(),
            };

            NeoClient client = LoadClient(data);

            Assert.IsNotNull(client);
            Assert.IsFalse(client.RetainsReadOnlyValidationProjection);
        }

        [Test]
        public void SchemaValidation_RejectsHandcraftedReadonlyLookupSelectionClearly()
        {
            ProjectData data = BuildProjectData();
            AddReadOnlyCollectionMembers(data);
            var favorite = (LookupMember)data.members["member-readonly-favorite"];
            favorite.defaultValue = new ArrayMemberValueBase
            {
                value = new[] { "value-not-in-target-collection" },
            };

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                LoadClient(data));

            StringAssert.Contains("Read-only member 'Favorite'", error!.Message);
            StringAssert.Contains("value-not-in-target-collection", error.Message);
            StringAssert.Contains("not present in collection 'value-target-list'", error.Message);
        }

        [Test]
        public void SchemaValidation_UnboundLookupIgnoresSameKeyOnUnrelatedClass()
        {
            ProjectData data = BuildProjectData();
            AddReadOnlyCollectionMembers(data);
            var targets = (ListMember)data.members["member-targets"];
            var favorite = (LookupMember)data.members["member-readonly-favorite"];
            targets.valueId = null;
            favorite.collectionValueId = null;
            ((ObjectMemberValue)data.values["value-root-assets"]).classId = null;

            var unrelatedEntry = new IntMember
            {
                id = "member-unrelated-entry",
                projectId = ProjectId,
                name = "UnrelatedEntry",
                kind = MemberKind.Int,
                createdAt = "x",
                updatedAt = "x",
            };
            var unrelatedTargets = new ListMember
            {
                id = "member-unrelated-targets",
                projectId = ProjectId,
                name = "Targets",
                kind = MemberKind.List,
                entryMemberId = unrelatedEntry.id,
                listKind = NeoListKinds.Ordered,
                createdAt = "x",
                updatedAt = "x",
            };
            data.members[unrelatedEntry.id] = unrelatedEntry;
            data.members[unrelatedTargets.id] = unrelatedTargets;
            data.classes["class-unrelated"] = ClassOf(
                "class-unrelated",
                "Unrelated",
                new Dictionary<string, string>
                {
                    ["Targets"] = unrelatedTargets.id,
                });
            data.values["value-unrelated"] = RecordValue(
                "value-unrelated",
                "class-unrelated",
                new Dictionary<string, string>
                {
                    ["Targets"] = "value-unrelated-target-list",
                });
            data.values["value-unrelated-target-list"] = new ArrayMemberValue
            {
                id = "value-unrelated-target-list",
                createdAt = "x",
                updatedAt = "x",
                value = System.Array.Empty<string>(),
            };

            NeoClient client = LoadClient(data);

            Assert.IsNotNull(client);
        }

        [Test]
        public void SchemaValidation_UnboundLookupMatchesCollectionMemberOverrideChain()
        {
            ProjectData data = BuildProjectData();
            AddReadOnlyCollectionMembers(data);
            var targets = (ListMember)data.members["member-targets"];
            var favorite = (LookupMember)data.members["member-readonly-favorite"];
            targets.valueId = null;
            favorite.collectionValueId = null;
            var targetsOverride = new ListMember
            {
                id = "member-targets-override",
                projectId = ProjectId,
                name = "Targets",
                kind = MemberKind.List,
                required = true,
                extendsMemberId = targets.id,
                entryMemberId = targets.entryMemberId,
                listKind = targets.listKind,
                createdAt = "x",
                updatedAt = "x",
            };
            data.members[targetsOverride.id] = targetsOverride;
            data.classes["class-derived-assets"] = new NeoSchemaClass
            {
                id = "class-derived-assets",
                projectId = ProjectId,
                name = "DerivedAssets",
                schema = new Dictionary<string, string>
                {
                    ["Targets"] = targetsOverride.id,
                },
                extendsClassId = "class-root-assets",
                createdAt = "x",
                updatedAt = "x",
            };
            ((ClassMember)data.members[data.project.rootAssetsMemberId]).classId =
                "class-derived-assets";
            ((ObjectMemberValue)data.values["value-root-assets"]).classId = null;

            NeoClient client = LoadClient(data);

            Assert.IsNotNull(client);
            Assert.IsFalse(client.RetainsReadOnlyValidationProjection);
        }

        [Test]
        public void SchemaValidation_RejectsPersistedSyntheticLookupTargetPrecisely()
        {
            ProjectData data = BuildProjectData();
            AddReadOnlyCollectionMembers(data);
            var favorite = (LookupMember)data.members["member-readonly-favorite"];
            favorite.collectionValueId = "__neo_readonly_default:member-targets";
            data.values[favorite.collectionValueId] = new ArrayMemberValue
            {
                id = favorite.collectionValueId,
                createdAt = "x",
                updatedAt = "x",
                value = new[] { "value-target-details" },
            };

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                LoadClient(data));

            StringAssert.Contains("runtime-only synthetic Lookup collection value", error!.Message);
            StringAssert.Contains(favorite.collectionValueId, error.Message);
        }

        [Test]
        public void SchemaValidation_RejectsSyntheticAndMissingPersistedLookupSelections()
        {
            ProjectData syntheticData = BuildProjectData();
            AddReadOnlyCollectionMembers(syntheticData);
            var syntheticFavorite =
                (LookupMember)syntheticData.members["member-readonly-favorite"];
            const string syntheticSelection =
                "__neo_readonly_default:member-target-entry";
            syntheticFavorite.defaultValue = new ArrayMemberValueBase
            {
                value = new[] { syntheticSelection },
            };
            ((ArrayMemberValue)syntheticData.values["value-target-list"]).value =
                new[] { syntheticSelection };

            var syntheticError = Assert.Throws<System.InvalidOperationException>(() =>
                LoadClient(syntheticData));
            StringAssert.Contains("runtime-only synthetic Lookup value", syntheticError!.Message);
            StringAssert.Contains(syntheticSelection, syntheticError.Message);

            ProjectData missingData = BuildProjectData();
            AddReadOnlyCollectionMembers(missingData);
            var missingFavorite =
                (LookupMember)missingData.members["member-readonly-favorite"];
            const string missingSelection = "value-missing-persisted-selection";
            missingFavorite.defaultValue = new ArrayMemberValueBase
            {
                value = new[] { missingSelection },
            };
            ((ArrayMemberValue)missingData.values["value-target-list"]).value =
                new[] { missingSelection };

            var missingError = Assert.Throws<System.InvalidOperationException>(() =>
                LoadClient(missingData));
            StringAssert.Contains("no persisted authored value row", missingError!.Message);
            StringAssert.Contains(missingSelection, missingError.Message);
        }

        [Test]
        public void DialogueActionAssignment_RejectsReadOnlyClassMember()
        {
            NeoClient client = LoadClient();
            var action = new FunctionWithReturnType
            {
                compilerRevision = 2,
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
                            pointer = MemberAccessPointer(
                                new ReferencePointer
                                {
                                    type = PointerKind.Reference,
                                    valueId = "value-weapon-save",
                                },
                                "BaseDamage",
                                "member-base-damage"),
                            typeInfo = new PrimitiveTypeInfo
                            {
                                type = MemberKind.Int,
                                required = true,
                            },
                            writability = WritabilityKind.Save,
                        },
                        operatorValue = "=",
                        pointer = new ValuePointer
                        {
                            type = PointerKind.Value,
                            value = new Value
                            {
                                typeInfo = new PrimitiveTypeInfo
                                {
                                    type = MemberKind.Int,
                                    required = true,
                                },
                                value = JToken.FromObject(99),
                            },
                        },
                    },
                },
            };
            var context = new NeoDialogueContext(
                "dialogue-readonly-write",
                null,
                null,
                null,
                new Dictionary<string, object?>());

            var error = Assert.Throws<NSGetterRuntimeError>(() =>
                NeoDialogueActionEvaluator.Execute(client, action, context));

            StringAssert.Contains("readonly", error!.Message);
            Assert.AreEqual(
                12,
                client.SaveRoot
                    .Get<NeoMemberClassWritable>("Weapon")
                    .Get<NeoMemberInt>("BaseDamage")
                    .value!.value);
            StringAssert.DoesNotContain("\"BaseDamage\"", client.SerializeSaveData());
        }

        [Test]
        public void ExistingSave_RecoversStaleReadonlyKeyAndOrphanedValue()
        {
            ProjectData data = BuildProjectData();
            var staleSave = new ProjectSaveData
            {
                name = "pre-conversion",
                projectId = ProjectId,
                version = new VersionData
                {
                    id = "unit-test-version",
                    label = "unit-test-version",
                },
                createdAt = "x",
                updatedAt = "x",
                values = new Dictionary<string, MemberValue>
                {
                    ["value-weapon-save"] = RecordValue(
                        "value-weapon-save",
                        "class-weapon",
                        new Dictionary<string, string>
                        {
                            ["BaseDamage"] = "value-stale-save-damage",
                        }),
                    ["value-stale-save-damage"] = new NumberMemberValue
                    {
                        id = "value-stale-save-damage",
                        createdAt = "x",
                        updatedAt = "x",
                        value = 88,
                    },
                },
            };

            NeoClient client = NeoTestSaveStack.ClientFromSchema(
                data,
                loadedSaveContent: JsonConvert.SerializeObject(staleSave));

            Assert.AreEqual(
                12,
                client.SaveRoot
                    .Get<NeoMemberClassWritable>("Weapon")
                    .Get<NeoMemberInt>("BaseDamage")
                    .value!.value);
            StringAssert.DoesNotContain("BaseDamage", client.SerializeSaveData());
            StringAssert.DoesNotContain("value-stale-save-damage", client.SerializeSaveData());
            CollectionAssert.IsEmpty(client.FindUnlinkedSaveValueIds());
        }

        [Test]
        public void ExistingSave_RecoversReadonlyKeyFromClassIdLessNestedRow()
        {
            ProjectData data = BuildProjectData();
            AddNestedReadOnlyClass(data);
            var staleSave = new ProjectSaveData
            {
                name = "classless-nested",
                projectId = ProjectId,
                version = new VersionData
                {
                    id = "unit-test-version",
                    label = "unit-test-version",
                },
                createdAt = "x",
                updatedAt = "x",
                values = new Dictionary<string, MemberValue>
                {
                    ["value-weapon-save"] = RecordValue(
                        "value-weapon-save",
                        "class-weapon",
                        new Dictionary<string, string>
                        {
                            ["Nested"] = "value-nested-save",
                        }),
                    ["value-nested-save"] = new ObjectMemberValue
                    {
                        id = "value-nested-save",
                        createdAt = "x",
                        updatedAt = "x",
                        value = new Dictionary<string, string>
                        {
                            ["Secret"] = "value-stale-nested-secret",
                        },
                    },
                    ["value-stale-nested-secret"] = new NumberMemberValue
                    {
                        id = "value-stale-nested-secret",
                        createdAt = "x",
                        updatedAt = "x",
                        value = 91,
                    },
                },
            };

            NeoClient client = NeoTestSaveStack.ClientFromSchema(
                data,
                loadedSaveContent: JsonConvert.SerializeObject(staleSave));
            string serialized = client.SerializeSaveData();

            StringAssert.DoesNotContain("\"Secret\"", serialized);
            StringAssert.DoesNotContain("value-stale-nested-secret", serialized);
            Assert.AreEqual(
                7,
                client.SaveRoot
                    .Get<NeoMemberClassWritable>("Weapon")
                    .Get<NeoMemberClassWritable>("Nested")
                    .Get<NeoMemberInt>("Secret")
                    .value!.value);
            CollectionAssert.IsEmpty(client.FindUnlinkedSaveValueIds());
        }

        [Test]
        public void ExistingSave_SkipsAndWarnsForConflictingClassIdLessPlacement()
        {
            ProjectData data = BuildProjectData();
            AddConflictingNestedPlacements(data);
            var staleSave = new ProjectSaveData
            {
                name = "conflicting-classless",
                projectId = ProjectId,
                version = new VersionData
                {
                    id = "unit-test-version",
                    label = "unit-test-version",
                },
                createdAt = "x",
                updatedAt = "x",
                values = new Dictionary<string, MemberValue>
                {
                    ["value-weapon-save"] = RecordValue(
                        "value-weapon-save",
                        "class-weapon",
                        new Dictionary<string, string>
                        {
                            ["NestedA"] = "value-conflicting-save-row",
                            ["NestedB"] = "value-conflicting-save-row",
                        }),
                    ["value-conflicting-save-row"] = new ObjectMemberValue
                    {
                        id = "value-conflicting-save-row",
                        createdAt = "x",
                        updatedAt = "x",
                        value = new Dictionary<string, string>
                        {
                            ["Secret"] = "value-conflicting-stale-secret",
                        },
                    },
                    ["value-conflicting-stale-secret"] = new NumberMemberValue
                    {
                        id = "value-conflicting-stale-secret",
                        createdAt = "x",
                        updatedAt = "x",
                        value = 92,
                    },
                },
            };
            LogAssert.Expect(
                UnityEngine.LogType.Warning,
                new Regex(
                    "Skipped read-only save recovery for classId-less Class value 'value-conflicting-save-row'.*incompatible Class placements"));

            NeoClient client = NeoTestSaveStack.ClientFromSchema(
                data,
                loadedSaveContent: JsonConvert.SerializeObject(staleSave));
            string serialized = client.SerializeSaveData();

            StringAssert.Contains("\"Secret\"", serialized);
            StringAssert.Contains("value-conflicting-stale-secret", serialized);
            Assert.IsFalse(client.RetainsReadOnlyValidationProjection);
        }

        [Test]
        public void Constructor_ReleasesPartitionValidationProjection()
        {
            ProjectData data = BuildProjectData();
            data.valuePartitions = new Dictionary<string, JToken>
            {
                ["test:retained"] = JObject.FromObject(
                    new Dictionary<string, MemberValue>
                    {
                        ["value-partition-validation-only"] = RecordValue(
                            "value-partition-validation-only",
                            "class-details",
                            new Dictionary<string, string>()),
                    }),
            };

            NeoClient client = LoadClient(data);

            Assert.IsFalse(client.RetainsReadOnlyValidationProjection);
        }

        [Test]
        public void GenericSubstitution_KeepsSlotReadOnlyMetadata()
        {
            ProjectData data = BuildProjectData();
            var binding = new IntMember
            {
                id = "member-binding",
                projectId = ProjectId,
                name = "Binding",
                kind = MemberKind.Int,
                isReadOnly = false,
                defaultValue = new NumberMemberValueBase { value = 7 },
                createdAt = "x",
                updatedAt = "x",
            };
            var slot = new GenericMember
            {
                id = "member-slot",
                projectId = ProjectId,
                name = "Value",
                kind = MemberKind.Generic,
                genericParamId = "param-t",
                isReadOnly = null,
                storage = "immutable",
                createdAt = "x",
                updatedAt = "x",
            };
            data.members[binding.id] = binding;
            data.members[slot.id] = slot;
            NeoClient client = LoadClient(data);
            slot.isReadOnly = true;

            Member substituted = NeoGenericResolution.SubstituteMember(
                client,
                slot,
                new Dictionary<string, NeoGenericEnvEntry>
                {
                    ["param-t"] = NeoGenericEnvEntry.Bound(binding.id),
                });

            Assert.IsInstanceOf<IntMember>(substituted);
            Assert.AreEqual(true, substituted.isReadOnly);
            Assert.AreEqual(7, ((IntMember)substituted).defaultValue!.value);
        }

        [Test]
        public void SchemaValidation_ValidatesReadOnlyGenericSlotsInClosedDescendants()
        {
            ProjectData data = BuildProjectData();
            var binding = new IntMember
            {
                id = "member-readonly-binding",
                projectId = ProjectId,
                name = "Binding",
                kind = MemberKind.Int,
                required = true,
                createdAt = "x",
                updatedAt = "x",
            };
            AddReadOnlyGenericSlot(data, binding);

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                NeoTestSaveStack.ClientFromSchema(data));

            StringAssert.Contains("GenericClosed", error!.Message);
            StringAssert.Contains("explicit defaultValue", error.Message);
        }

        [Test]
        public void AbstractReadonlyGenericSlot_ValidatesClosedKindWithoutDefault()
        {
            ProjectData data = BuildProjectData();
            var binding = new IntMember
            {
                id = "member-abstract-readonly-binding",
                projectId = ProjectId,
                name = "Binding",
                kind = MemberKind.Int,
                required = true,
                createdAt = "x",
                updatedAt = "x",
            };
            AddReadOnlyGenericSlot(
                data,
                binding,
                isAbstract: true,
                closedClassIsAbstract: true);

            NeoClient client = LoadClient(data);

            Assert.IsNotNull(client);
            CollectionAssert.IsEmpty(
                SchemaKeys(client.ResolveReadOnlyMemberSchema("class-generic-closed")));
        }

        [Test]
        public void ClosedGenericReadOnlySlots_KeepDistinctDefaultsForNodesAndNeoScript()
        {
            ProjectData data = BuildProjectData();
            var slot = new GenericMember
            {
                id = "member-readonly-slot",
                projectId = ProjectId,
                name = "Value",
                kind = MemberKind.Generic,
                genericParamId = "param-t",
                storage = "immutable",
                isReadOnly = true,
                createdAt = "x",
                updatedAt = "x",
            };
            var bindingA = new IntMember
            {
                id = "member-binding-a",
                projectId = ProjectId,
                name = "Binding A",
                kind = MemberKind.Int,
                required = true,
                defaultValue = new NumberMemberValueBase { value = 7 },
                createdAt = "x",
                updatedAt = "x",
            };
            var bindingB = new IntMember
            {
                id = "member-binding-b",
                projectId = ProjectId,
                name = "Binding B",
                kind = MemberKind.Int,
                required = true,
                defaultValue = new NumberMemberValueBase { value = 9 },
                createdAt = "x",
                updatedAt = "x",
            };
            var closedA = ClassMemberOf(
                "member-closed-a", "ClosedA", "class-closed-a", "value-closed-a");
            var closedB = ClassMemberOf(
                "member-closed-b", "ClosedB", "class-closed-b", "value-closed-b");
            data.members[slot.id] = slot;
            data.members[bindingA.id] = bindingA;
            data.members[bindingB.id] = bindingB;
            data.members[closedA.id] = closedA;
            data.members[closedB.id] = closedB;
            data.classes["class-generic-base"] = new NeoSchemaClass
            {
                id = "class-generic-base",
                projectId = ProjectId,
                name = "GenericBase",
                schema = new Dictionary<string, string> { ["Value"] = slot.id },
                genericParams = new List<GenericParamDeclaration>
                {
                    new() { id = "param-t", name = "T" },
                },
                createdAt = "x",
                updatedAt = "x",
            };
            data.classes["class-closed-a"] = ClosedGenericClass(
                "class-closed-a", "ClosedA", bindingA.id);
            data.classes["class-closed-b"] = ClosedGenericClass(
                "class-closed-b", "ClosedB", bindingB.id);
            data.classes["class-root-save"].schema["ClosedA"] = closedA.id;
            data.classes["class-root-save"].schema["ClosedB"] = closedB.id;
            ((ObjectMemberValue)data.values["value-root-save"]).value!["ClosedA"] =
                "value-closed-a";
            ((ObjectMemberValue)data.values["value-root-save"]).value!["ClosedB"] =
                "value-closed-b";
            data.values["value-closed-a"] = RecordValue(
                "value-closed-a", "class-closed-a", new Dictionary<string, string>());
            data.values["value-closed-b"] = RecordValue(
                "value-closed-b", "class-closed-b", new Dictionary<string, string>());

            NeoClient client = LoadClient(data);
            NeoMemberInt nodeA = client.SaveRoot
                .Get<NeoMemberClassWritable>("ClosedA")
                .Get<NeoMemberInt>("Value");
            NeoMemberInt nodeB = client.SaveRoot
                .Get<NeoMemberClassWritable>("ClosedB")
                .Get<NeoMemberInt>("Value");

            Assert.AreNotSame(nodeA, nodeB);
            Assert.AreEqual(7, nodeA.value!.value);
            Assert.AreEqual(9, nodeB.value!.value);
            Assert.AreNotEqual(nodeA.value.id, nodeB.value.id);

            Assert.AreEqual(7d, EvaluatePinnedReadOnlySlot(client, "value-closed-a", slot.id));
            Assert.AreEqual(9d, EvaluatePinnedReadOnlySlot(client, "value-closed-b", slot.id));
        }

        private static string[] SchemaKeys(IList<MergedSchemaEntry> entries)
        {
            var result = new string[entries.Count];
            for (int index = 0; index < entries.Count; index++)
            {
                result[index] = entries[index].schemaKey;
            }
            return result;
        }

        private static NeoSchemaClass ClosedGenericClass(
            string id,
            string name,
            string bindingMemberId) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            schema = new Dictionary<string, string>(),
            extendsClassId = "class-generic-base",
            extendsGenericBindings = new Dictionary<string, GenericBinding>
            {
                ["param-t"] = new()
                {
                    kind = NeoGenericBindingKinds.Member,
                    memberId = bindingMemberId,
                },
            },
            createdAt = "x",
            updatedAt = "x",
        };

        private static object? EvaluatePinnedReadOnlySlot(
            NeoClient client,
            string valueId,
            string memberId)
        {
            var getter = new FunctionWithReturnType
            {
                parameters = System.Array.Empty<Variable>(),
                typeInfo = new PrimitiveTypeInfo
                {
                    type = MemberKind.Int,
                    required = true,
                },
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = new KeyOfPointer
                        {
                            type = PointerKind.KeyOf,
                            memberId = memberId,
                            keyOf = new KeyOf
                            {
                                pointer = new ReferencePointer
                                {
                                    type = PointerKind.Reference,
                                    valueId = valueId,
                                },
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
                                        value = JToken.FromObject("Value"),
                                    },
                                },
                            },
                        },
                    },
                },
            };
            return NSGetterEvaluator.Evaluate(
                getter,
                new NSGetterEvaluator.Context(client, null, null));
        }

        private static object? EvaluateMemberAccess(
            NeoClient client,
            string valueId,
            string schemaKey,
            string memberId,
            MemberKind returnKind)
        {
            return EvaluatePointer(
                client,
                MemberAccessPointer(
                    new ReferencePointer
                    {
                        type = PointerKind.Reference,
                        valueId = valueId,
                    },
                    schemaKey,
                    memberId),
                returnKind);
        }

        private static KeyOfPointer MemberAccessPointer(
            Pointer receiver,
            string schemaKey,
            string memberId) => new()
        {
            type = PointerKind.KeyOf,
            memberId = memberId,
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
                        value = JToken.FromObject(schemaKey),
                    },
                },
            },
        };

        private static object? EvaluatePointer(
            NeoClient client,
            Pointer pointer,
            MemberKind returnKind)
        {
            var getter = new FunctionWithReturnType
            {
                parameters = System.Array.Empty<Variable>(),
                typeInfo = new PrimitiveTypeInfo
                {
                    type = returnKind,
                    required = true,
                },
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = pointer,
                    },
                },
            };
            return NSGetterEvaluator.Evaluate(
                getter,
                new NSGetterEvaluator.Context(client, null, null));
        }

        private static void AddNestedReadOnlyClass(ProjectData data)
        {
            var secret = new IntMember
            {
                id = "member-nested-secret",
                projectId = ProjectId,
                name = "Secret",
                kind = MemberKind.Int,
                required = true,
                storage = "immutable",
                isReadOnly = true,
                defaultValue = new NumberMemberValueBase { value = 7 },
                createdAt = "x",
                updatedAt = "x",
            };
            var nested = new ClassMember
            {
                id = "member-nested",
                projectId = ProjectId,
                name = "Nested",
                kind = MemberKind.Class,
                classId = "class-nested-readonly",
                required = true,
                createdAt = "x",
                updatedAt = "x",
            };
            data.members[secret.id] = secret;
            data.members[nested.id] = nested;
            data.classes["class-nested-readonly"] = ClassOf(
                "class-nested-readonly",
                "NestedReadonly",
                new Dictionary<string, string>
                {
                    ["Secret"] = secret.id,
                });
            data.classes["class-weapon"].schema["Nested"] = nested.id;
        }

        private static void ConfigureAbstractDamageContract(
            ProjectData data,
            bool addOverride = true,
            bool overrideReadOnly = true,
            bool baseReadOnly = true,
            string baseStorage = "immutable")
        {
            var abstractDamage = (IntMember)data.members["member-base-damage"];
            abstractDamage.isAbstract = true;
            abstractDamage.isReadOnly = baseReadOnly ? true : null;
            abstractDamage.storage = baseStorage;
            abstractDamage.defaultValue = null;
            data.classes["class-weapon"].isAbstract = true;

            var concrete = new NeoSchemaClass
            {
                id = "class-concrete-weapon",
                projectId = ProjectId,
                name = "ConcreteWeapon",
                schema = new Dictionary<string, string>(),
                extendsClassId = "class-weapon",
                createdAt = "x",
                updatedAt = "x",
            };
            data.classes[concrete.id] = concrete;

            if (addOverride)
            {
                var implementation = new IntMember
                {
                    id = "member-concrete-damage",
                    projectId = ProjectId,
                    name = "BaseDamage",
                    kind = MemberKind.Int,
                    required = true,
                    storage = "immutable",
                    isReadOnly = overrideReadOnly ? true : null,
                    extendsMemberId = abstractDamage.id,
                    defaultValue = overrideReadOnly
                        ? new NumberMemberValueBase { value = 27 }
                        : null,
                    createdAt = "x",
                    updatedAt = "x",
                };
                data.members[implementation.id] = implementation;
                concrete.schema["BaseDamage"] = implementation.id;
            }

            ((ClassMember)data.members["member-asset-weapon"]).classId = concrete.id;
            ((ClassMember)data.members["member-save-weapon"]).classId = concrete.id;
            ((ObjectMemberValue)data.values["value-weapon-asset"]).classId = concrete.id;
            ((ObjectMemberValue)data.values["value-weapon-save"]).classId = concrete.id;
        }

        private static void AddReadOnlyGenericSlot(
            ProjectData data,
            Member binding,
            bool isAbstract = false,
            bool closedClassIsAbstract = false)
        {
            var slot = new GenericMember
            {
                id = "member-readonly-slot",
                projectId = ProjectId,
                name = "Value",
                kind = MemberKind.Generic,
                genericParamId = "param-t",
                storage = "immutable",
                isAbstract = isAbstract ? true : null,
                isReadOnly = true,
                createdAt = "x",
                updatedAt = "x",
            };
            data.members[slot.id] = slot;
            data.members[binding.id] = binding;
            data.classes["class-generic-base"] = new NeoSchemaClass
            {
                id = "class-generic-base",
                projectId = ProjectId,
                name = "GenericBase",
                schema = new Dictionary<string, string> { ["Value"] = slot.id },
                genericParams = new List<GenericParamDeclaration>
                {
                    new() { id = "param-t", name = "T" },
                },
                isAbstract = isAbstract,
                createdAt = "x",
                updatedAt = "x",
            };
            data.classes["class-generic-closed"] = new NeoSchemaClass
            {
                id = "class-generic-closed",
                projectId = ProjectId,
                name = "GenericClosed",
                schema = new Dictionary<string, string>(),
                extendsClassId = "class-generic-base",
                extendsGenericBindings = new Dictionary<string, GenericBinding>
                {
                    ["param-t"] = new()
                    {
                        kind = NeoGenericBindingKinds.Member,
                        memberId = binding.id,
                    },
                },
                isAbstract = closedClassIsAbstract,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static void AddConflictingNestedPlacements(ProjectData data)
        {
            var secret = new IntMember
            {
                id = "member-conflict-secret",
                projectId = ProjectId,
                name = "Secret",
                kind = MemberKind.Int,
                required = true,
                storage = "immutable",
                isReadOnly = true,
                defaultValue = new NumberMemberValueBase { value = 8 },
                createdAt = "x",
                updatedAt = "x",
            };
            var nestedA = new ClassMember
            {
                id = "member-conflict-nested-a",
                projectId = ProjectId,
                name = "NestedA",
                kind = MemberKind.Class,
                classId = "class-conflict-a",
                required = true,
                createdAt = "x",
                updatedAt = "x",
            };
            var nestedB = new ClassMember
            {
                id = "member-conflict-nested-b",
                projectId = ProjectId,
                name = "NestedB",
                kind = MemberKind.Class,
                classId = "class-conflict-b",
                required = true,
                createdAt = "x",
                updatedAt = "x",
            };
            data.members[secret.id] = secret;
            data.members[nestedA.id] = nestedA;
            data.members[nestedB.id] = nestedB;
            data.classes["class-conflict-a"] = ClassOf(
                "class-conflict-a",
                "ConflictA",
                new Dictionary<string, string> { ["Secret"] = secret.id });
            data.classes["class-conflict-b"] = ClassOf(
                "class-conflict-b",
                "ConflictB",
                new Dictionary<string, string>());
            data.classes["class-weapon"].schema["NestedA"] = nestedA.id;
            data.classes["class-weapon"].schema["NestedB"] = nestedB.id;
        }

        private static void AddReadOnlyCollectionMembers(ProjectData data)
        {
            var bonusEntry = new IntMember
            {
                id = "member-bonus-entry",
                projectId = ProjectId,
                name = "BonusEntry",
                kind = MemberKind.Int,
                createdAt = "x",
                updatedAt = "x",
            };
            var bonuses = new ListMember
            {
                id = "member-readonly-bonuses",
                projectId = ProjectId,
                name = "Bonuses",
                kind = MemberKind.List,
                required = true,
                storage = "immutable",
                isReadOnly = true,
                listKind = NeoListKinds.Ordered,
                entryMemberId = bonusEntry.id,
                defaultValue = new ArrayMemberValueBase
                {
                    value = new[] { "value-bonus-1", "value-bonus-2" },
                },
                createdAt = "x",
                updatedAt = "x",
            };
            var labelEntry = new StringMember
            {
                id = "member-label-entry",
                projectId = ProjectId,
                name = "LabelEntry",
                kind = MemberKind.String,
                localizable = false,
                createdAt = "x",
                updatedAt = "x",
            };
            var labels = new DictionaryMember
            {
                id = "member-readonly-labels",
                projectId = ProjectId,
                name = "Labels",
                kind = MemberKind.Dictionary,
                required = true,
                storage = "immutable",
                isReadOnly = true,
                entryMemberId = labelEntry.id,
                defaultValue = new ObjectMemberValueBase
                {
                    value = new Dictionary<string, string>
                    {
                        ["main"] = "value-label-main",
                    },
                },
                createdAt = "x",
                updatedAt = "x",
            };
            var targetEntry = new ClassMember
            {
                id = "member-target-entry",
                projectId = ProjectId,
                name = "TargetEntry",
                kind = MemberKind.Class,
                classId = "class-details",
                createdAt = "x",
                updatedAt = "x",
            };
            var targets = new ListMember
            {
                id = "member-targets",
                projectId = ProjectId,
                name = "Targets",
                kind = MemberKind.List,
                required = true,
                listKind = NeoListKinds.Ordered,
                entryMemberId = targetEntry.id,
                valueId = "value-target-list",
                createdAt = "x",
                updatedAt = "x",
            };
            var favorite = new LookupMember
            {
                id = "member-readonly-favorite",
                projectId = ProjectId,
                name = "Favorite",
                kind = MemberKind.Lookup,
                required = true,
                storage = "immutable",
                isReadOnly = true,
                collectionMemberId = targets.id,
                collectionValueId = "value-target-list",
                multiselect = false,
                defaultValue = new ArrayMemberValueBase
                {
                    value = new[] { "value-target-details" },
                },
                createdAt = "x",
                updatedAt = "x",
            };

            data.members[bonusEntry.id] = bonusEntry;
            data.members[bonuses.id] = bonuses;
            data.members[labelEntry.id] = labelEntry;
            data.members[labels.id] = labels;
            data.members[targetEntry.id] = targetEntry;
            data.members[targets.id] = targets;
            data.members[favorite.id] = favorite;
            data.classes["class-weapon"].schema["Bonuses"] = bonuses.id;
            data.classes["class-weapon"].schema["Labels"] = labels.id;
            data.classes["class-weapon"].schema["Favorite"] = favorite.id;
            data.classes["class-root-assets"].schema["Targets"] = targets.id;
            ((ObjectMemberValue)data.values["value-root-assets"]).value!["Targets"] =
                "value-target-list";

            data.values["value-bonus-1"] = new NumberMemberValue
            {
                id = "value-bonus-1",
                createdAt = "x",
                updatedAt = "x",
                value = 3,
            };
            data.values["value-bonus-2"] = new NumberMemberValue
            {
                id = "value-bonus-2",
                createdAt = "x",
                updatedAt = "x",
                value = 7,
            };
            data.values["value-label-main"] = new StringMemberValue
            {
                id = "value-label-main",
                createdAt = "x",
                updatedAt = "x",
                value = "primary",
            };
            data.values["value-target-list"] = new ArrayMemberValue
            {
                id = "value-target-list",
                createdAt = "x",
                updatedAt = "x",
                value = new[] { "value-target-details" },
            };
            data.values["value-target-details"] = RecordValue(
                "value-target-details",
                "class-details",
                new Dictionary<string, string>
                {
                    ["Name"] = "value-target-name",
                });
            data.values["value-target-name"] = new StringMemberValue
            {
                id = "value-target-name",
                createdAt = "x",
                updatedAt = "x",
                value = "lookup target",
            };
        }

        private static NeoClient LoadClient(ProjectData? data = null) =>
            NeoTestSaveStack.ClientFromSchema(data ?? BuildProjectData());

        private sealed class ReadOnlyLocalizationSource
            : INeoLocalizationLocaleFileSource
        {
            public bool TryLoadResourcesLocale(
                ProjectLocalizationExport localization,
                string locale,
                out ProjectLocalizationLocaleFile? file)
            {
                file = new ProjectLocalizationLocaleFile
                {
                    schemaVersion = 1,
                    projectId = ProjectId,
                    versionId = "unit-test-version",
                    locale = locale,
                    values = new Dictionary<string, string?>
                    {
                        ["text-readonly-title"] = "Localized declaration title",
                    },
                };
                return true;
            }

            public Task<ProjectLocalizationLocaleFile?> LoadStreamingAssetsLocaleAsync(
                ProjectLocalizationExport localization,
                string locale,
                string streamingAssetsRelativePath) =>
                Task.FromResult<ProjectLocalizationLocaleFile?>(null);
        }

        private static ProjectData BuildProjectData()
        {
            var baseDamage = new IntMember
            {
                id = "member-base-damage",
                projectId = ProjectId,
                name = "BaseDamage",
                kind = MemberKind.Int,
                required = true,
                storage = "immutable",
                isReadOnly = true,
                defaultValue = new NumberMemberValueBase { value = 12 },
                createdAt = "x",
                updatedAt = "x",
            };
            var detailName = new StringMember
            {
                id = "member-detail-name",
                projectId = ProjectId,
                name = "Name",
                kind = MemberKind.String,
                required = true,
                localizable = false,
                createdAt = "x",
                updatedAt = "x",
            };
            var details = new ClassMember
            {
                id = "member-details",
                projectId = ProjectId,
                name = "Details",
                kind = MemberKind.Class,
                classId = "class-details",
                required = true,
                storage = "immutable",
                isReadOnly = true,
                defaultValue = new ObjectMemberValueBase
                {
                    classId = "class-details",
                    value = new Dictionary<string, string>
                    {
                        ["Name"] = "value-detail-name-default",
                    },
                },
                createdAt = "x",
                updatedAt = "x",
            };
            var rolledDamage = new IntMember
            {
                id = "member-rolled-damage",
                projectId = ProjectId,
                name = "RolledDamage",
                kind = MemberKind.Int,
                required = false,
                storage = "immutable",
                defaultValue = new NumberMemberValueBase { value = 10 },
                createdAt = "x",
                updatedAt = "x",
            };
            var assetWeapon = ClassMemberOf(
                "member-asset-weapon", "Weapon", "class-weapon", "value-weapon-asset");
            var saveWeapon = ClassMemberOf(
                "member-save-weapon", "Weapon", "class-weapon", "value-weapon-save");
            var rootAssets = RootMember(
                "member-root-assets", "Assets", "class-root-assets", "value-root-assets", "immutable");
            var rootSave = RootMember(
                "member-root-save", "Save", "class-root-save", "value-root-save", "save");
            var rootSession = RootMember(
                "member-root-session", "Session", "class-root-session", "value-root-session", "session");

            return new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    name = "Read only",
                    rootAssetsMemberId = rootAssets.id,
                    rootSaveFileMemberId = rootSave.id,
                    rootSessionMemberId = rootSession.id,
                    createdAt = "x",
                    updatedAt = "x",
                },
                members = new Dictionary<string, Member>
                {
                    [baseDamage.id] = baseDamage,
                    [detailName.id] = detailName,
                    [details.id] = details,
                    [rolledDamage.id] = rolledDamage,
                    [assetWeapon.id] = assetWeapon,
                    [saveWeapon.id] = saveWeapon,
                    [rootAssets.id] = rootAssets,
                    [rootSave.id] = rootSave,
                    [rootSession.id] = rootSession,
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    ["class-details"] = ClassOf("class-details", "Details", new Dictionary<string, string>
                    {
                        ["Name"] = detailName.id,
                    }),
                    ["class-weapon"] = ClassOf("class-weapon", "Weapon", new Dictionary<string, string>
                    {
                        ["BaseDamage"] = baseDamage.id,
                        ["Details"] = details.id,
                        ["RolledDamage"] = rolledDamage.id,
                    }),
                    ["class-root-assets"] = ClassOf("class-root-assets", "AssetsRoot", new Dictionary<string, string>
                    {
                        ["Weapon"] = assetWeapon.id,
                    }),
                    ["class-root-save"] = ClassOf("class-root-save", "SaveRoot", new Dictionary<string, string>
                    {
                        ["Weapon"] = saveWeapon.id,
                    }),
                    ["class-root-session"] = ClassOf("class-root-session", "SessionRoot", new Dictionary<string, string>()),
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["value-root-assets"] = RecordValue(
                        "value-root-assets", "class-root-assets", new Dictionary<string, string>
                        {
                            ["Weapon"] = "value-weapon-asset",
                        }),
                    ["value-weapon-asset"] = RecordValue(
                        "value-weapon-asset", "class-weapon", new Dictionary<string, string>()),
                    ["value-root-save"] = RecordValue(
                        "value-root-save", "class-root-save", new Dictionary<string, string>
                        {
                            ["Weapon"] = "value-weapon-save",
                        }),
                    ["value-weapon-save"] = RecordValue(
                        "value-weapon-save", "class-weapon", new Dictionary<string, string>()),
                    ["value-root-session"] = RecordValue(
                        "value-root-session", "class-root-session", new Dictionary<string, string>()),
                    ["value-detail-name-default"] = new StringMemberValue
                    {
                        id = "value-detail-name-default",
                        createdAt = "x",
                        updatedAt = "x",
                        value = "shared",
                    },
                },
            };
        }

        private static ClassMember ClassMemberOf(
            string id,
            string name,
            string classId,
            string? valueId) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            kind = MemberKind.Class,
            classId = classId,
            valueId = valueId,
            createdAt = "x",
            updatedAt = "x",
        };

        private static ClassMember RootMember(
            string id,
            string name,
            string classId,
            string valueId,
            string storage)
        {
            ClassMember member = ClassMemberOf(id, name, classId, valueId);
            member.storage = storage;
            return member;
        }

        private static NeoSchemaClass ClassOf(
            string id,
            string name,
            Dictionary<string, string> schema) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            schema = schema,
            createdAt = "x",
            updatedAt = "x",
        };

        private static ObjectMemberValue RecordValue(
            string id,
            string classId,
            Dictionary<string, string> value) => new()
        {
            id = id,
            classId = classId,
            createdAt = "x",
            updatedAt = "x",
            value = value,
        };
    }
}
