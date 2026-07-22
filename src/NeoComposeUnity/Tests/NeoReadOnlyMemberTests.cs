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
                            keyOf = new KeyOf
                            {
                                memberId = "member-base-damage",
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

        [TestCase("save", null, false, false, "Immutable storage")]
        [TestCase("immutable", null, true, false, "cannot be static")]
        [TestCase("immutable", null, false, true, "cannot be abstract")]
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
                createdAt = "x",
                updatedAt = "x",
            };

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                NeoTestSaveStack.ClientFromSchema(data));

            StringAssert.Contains("GenericClosed", error!.Message);
            StringAssert.Contains("explicit defaultValue", error.Message);
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

        private static NeoClient LoadClient(ProjectData? data = null) =>
            NeoTestSaveStack.ClientFromSchema(data ?? BuildProjectData());

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
            string valueId) => new()
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
