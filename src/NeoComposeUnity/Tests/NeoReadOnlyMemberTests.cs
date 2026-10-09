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
        public void MemberDto_RoundTripsSparseNullableMutability()
        {
            var absent = JsonConvert.DeserializeObject<Member>(
                "{\"id\":\"m1\",\"projectId\":\"p\",\"name\":\"Damage\",\"kind\":2}");
            var enabled = JsonConvert.DeserializeObject<Member>(
                "{\"id\":\"m2\",\"projectId\":\"p\",\"name\":\"Damage\",\"kind\":2,\"mutability\":1}");

            Assert.IsNull(absent!.DeclaredMutability);
            Assert.AreEqual(NeoMemberMutabilityKind.Mutable, absent.Mutability);
            Assert.AreEqual(NeoMemberMutabilityKind.ReadOnly, enabled!.Mutability);
            StringAssert.Contains("\"mutability\":1", JsonConvert.SerializeObject(enabled));
        }

        [TestCase(null)]
        [TestCase(NeoMemberStorage.Save)]
        [TestCase(NeoMemberStorage.Immutable)]
        public void ReadOnlyMember_StoresPerInstanceUnderAnyStorage(NeoMemberStorage? storage)
        {
            ProjectData data = BuildProjectData();
            ((IntMember)data.members["member-base-damage"]).DeclaredStorage = storage;
            ((ObjectMemberValue)data.values["value-weapon-asset"]).value!["BaseDamage"] = "value-asset-damage";
            data.values["value-asset-damage"] = new NumberMemberValue
            {
                id = "value-asset-damage",
                createdAt = "x",
                updatedAt = "x",
                value = 40,
            };

            using NeoClient client = LoadClient(data);

            Assert.AreEqual(
                40,
                client.AssetsRoot.Get<NeoMemberClass>("Weapon").Get<NeoMemberInt>("BaseDamage").value!.value,
                "an instance value wins");
            Assert.AreEqual(
                12,
                client.SaveRoot.Get<NeoMemberClass>("Weapon").Get<NeoMemberInt>("BaseDamage").value!.value,
                "a sparse instance reads the declaration default");
        }

        [Test]
        public void ReadOnlyMember_RejectsWritesAfterConstruction()
        {
            ProjectData data = BuildProjectData();
            ((IntMember)data.members["member-base-damage"]).DeclaredStorage = null;
            using NeoClient client = LoadClient(data);
            var weapon = client.SaveRoot.Get<NeoMemberClassWritable>("Weapon");

            var set = Assert.Throws<System.InvalidOperationException>(() =>
                weapon.SetPlacementValue("BaseDamage", NeoValueWritePayload.FromValue(40)));
            StringAssert.Contains("is set only at construction", set!.Message);
            var remove = Assert.Throws<System.InvalidOperationException>(() => weapon.Remove("BaseDamage"));
            StringAssert.Contains("is set only at construction", remove!.Message);
            Assert.AreEqual(12, weapon.Get<NeoMemberInt>("BaseDamage").value!.value);
        }

        [Test]
        public void ReadOnlyClassMember_KeepsItsEntryMembersWritable()
        {
            ProjectData data = BuildProjectData();
            ((ClassMember)data.members["member-details"]).DeclaredStorage = null;
            string saved;
            using (NeoClient client = LoadClient(data))
            {
                client.SaveRoot
                    .Get<NeoMemberClassWritable>("Weapon")
                    .Get<NeoMemberClassWritable>("Details")
                    .Get<NeoMemberStringWritable>("Name")
                    .Set("renamed");
                saved = client.SerializeSaveData();
            }

            using NeoClient reopened = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: saved);
            Assert.AreEqual(
                "renamed",
                reopened.SaveRoot
                    .Get<NeoMemberClass>("Weapon")
                    .Get<NeoMemberClass>("Details")
                    .Get<NeoMemberString>("Name")
                    .value!.value);
        }

        [Test]
        public void ReadOnlyMember_IsSetAtConstruction()
        {
            ProjectData data = BuildProjectData();
            ((IntMember)data.members["member-base-damage"]).DeclaredStorage = null;
            ((ClassMember)data.members["member-details"]).DeclaredStorage = null;
            using NeoClient client = LoadClient(data);

            NeoMemberClassWritable weapon = NeoGeneratedTypesSupport.CreateWritableClassValue(
                client,
                "class-weapon",
                new NeoGeneratedConstructorValue("BaseDamage", "member-base-damage", 30));

            Assert.AreEqual(30, weapon.Get<NeoMemberInt>("BaseDamage").value!.value);
        }

        [TestCase("static", "cannot be static")]
        [TestCase("writable", "cannot declare Writable storage")]
        [TestCase("root", "cannot be a project root")]
        [TestCase("unplaced", "is not placed directly in a Class schema")]
        public void SchemaValidation_RejectsInvalidReadOnlyPlacement(string shape, string expected)
        {
            ProjectData data = BuildProjectData();
            var member = (IntMember)data.members["member-base-damage"];
            switch (shape)
            {
                case "static":
                    member.DeclaredModifier = NeoMemberModifierKind.Static;
                    break;
                case "writable":
                    member.DeclaredStorage = NeoMemberStorage.Writable;
                    break;
                case "root":
                    data.members["member-root-save"].DeclaredMutability = NeoMemberMutabilityKind.ReadOnly;
                    break;
                case "unplaced":
                    data.classes["class-weapon"].schema.Remove("BaseDamage");
                    break;
            }

            var error = Assert.Throws<System.InvalidOperationException>(() => LoadClient(data));
            StringAssert.Contains(expected, error!.Message);
        }

        [Test]
        public void NeoScriptCompilerRevision_AcceptsOnlyTheCurrentStamp()
        {
            NeoClient client = LoadClient();
            var context = new NSGetterEvaluator.Context(client, null, null);

            var body = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
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

            Assert.AreEqual(7d, NSGetterEvaluator.Evaluate(body, context));

            body.compilerRevision = null;
            var unstampedError = Assert.Throws<NeoScriptPreExecutionValidationError>(() =>
                NSGetterEvaluator.Evaluate(body, context));
            StringAssert.Contains(
                "carries no compiler revision stamp", unstampedError!.Message);

            body.compilerRevision = FunctionWithReturnType.CurrentCompilerRevision - 1;
            var staleError = Assert.Throws<NeoScriptPreExecutionValidationError>(() =>
                NSGetterEvaluator.Evaluate(body, context));
            StringAssert.Contains(
                "this SDK executes only revision "
                    + FunctionWithReturnType.CurrentCompilerRevision,
                staleError!.Message);

            body.compilerRevision = FunctionWithReturnType.CurrentCompilerRevision + 1;
            var futureError = Assert.Throws<NeoScriptPreExecutionValidationError>(() =>
                NSGetterEvaluator.Evaluate(body, context));
            StringAssert.Contains(
                "is stamped compiler revision "
                    + (FunctionWithReturnType.CurrentCompilerRevision + 1),
                futureError!.Message);
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

        [Test]
        public void AbstractReadonly_ConcreteReadonlyOverrideReadsItsDefault()
        {
            ProjectData data = BuildProjectData();
            ConfigureAbstractDamageContract(data);

            NeoClient client = LoadClient(data);

            Assert.AreEqual(
                27,
                client.AssetsRoot
                    .Get<NeoMemberClass>("Weapon")
                    .Get<NeoMemberInt>("BaseDamage")
                    .value!.value);
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
                Storage = NeoMemberStorage.Immutable,
                Mutability = NeoMemberMutabilityKind.ReadOnly,
                createdAt = "x",
                updatedAt = "x",
                Modifier = NeoMemberModifierKind.Abstract,
            };
            data.members[computed.id] = computed;
            data.classes["class-weapon"].schema["Computed"] = computed.id;

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                LoadClient(data));

            StringAssert.Contains("must be value-bearing", error!.Message);
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
                baseStorage: NeoMemberStorage.Save);

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
                        kind = NeoInterfaceMemberKind.Property,
                        Access = NeoMemberAccessKind.Public,
                        typeInfo = new PrimitiveTypeInfo
                        {
                            type = MemberKind.Int,
                            required = true,
                        },
                        Accessors = NeoPropertyAccessorsKind.GetSet,
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

        private static void ConfigureAbstractDamageContract(
            ProjectData data,
            bool addOverride = true,
            bool overrideReadOnly = true,
            bool baseReadOnly = true,
            NeoMemberStorage baseStorage = NeoMemberStorage.Immutable)
        {
            var abstractDamage = (IntMember)data.members["member-base-damage"];
            abstractDamage.DeclaredModifier = NeoMemberModifierKind.Abstract;
            abstractDamage.DeclaredMutability = baseReadOnly
                ? NeoMemberMutabilityKind.ReadOnly
                : null;
            abstractDamage.DeclaredStorage = baseStorage;
            abstractDamage.defaultValue = null;
            data.classes["class-weapon"].DeclaredModifier = NeoClassModifierKind.Abstract;

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
                    Requirement = NeoMemberRequirementKind.Required,
                    Storage = NeoMemberStorage.Immutable,
                    Modifier = NeoMemberModifierKind.Virtual,
                    Mutability = overrideReadOnly
                        ? NeoMemberMutabilityKind.ReadOnly
                        : NeoMemberMutabilityKind.Mutable,
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
                Requirement = NeoMemberRequirementKind.Required,
                Storage = NeoMemberStorage.Immutable,
                Mutability = NeoMemberMutabilityKind.ReadOnly,
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
                Requirement = NeoMemberRequirementKind.Required,
                Format = NeoStringFormatKind.Plain,
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
                Requirement = NeoMemberRequirementKind.Required,
                Storage = NeoMemberStorage.Immutable,
                Mutability = NeoMemberMutabilityKind.ReadOnly,
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
                Requirement = NeoMemberRequirementKind.Optional,
                Storage = NeoMemberStorage.Immutable,
                defaultValue = new NumberMemberValueBase { value = 10 },
                createdAt = "x",
                updatedAt = "x",
            };
            var assetWeapon = ClassMemberOf(
                "member-asset-weapon", "Weapon", "class-weapon", "value-weapon-asset");
            var saveWeapon = ClassMemberOf(
                "member-save-weapon", "Weapon", "class-weapon", "value-weapon-save");
            var rootAssets = RootMember(
                "member-root-assets", "Assets", "class-root-assets", "value-root-assets", NeoMemberStorage.Immutable);
            var rootSave = RootMember(
                "member-root-save", "Save", "class-root-save", "value-root-save", NeoMemberStorage.Save);
            var rootSession = RootMember(
                "member-root-session", "Session", "class-root-session", "value-root-session", NeoMemberStorage.Session);

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
            }.WithUserRoot();
        }

        private static void AddSparseSaveClass(ProjectData data)
        {
            var count = new IntMember
            {
                id = "member-sparse-count",
                projectId = ProjectId,
                name = "Count",
                kind = MemberKind.Int,
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new NumberMemberValueBase { value = 5 },
                createdAt = "x",
                updatedAt = "x",
            };
            var sparse = ClassMemberOf(
                "member-sparse",
                "Sparse",
                "class-sparse",
                "value-sparse-save");
            sparse.Requirement = NeoMemberRequirementKind.Required;
            sparse.Storage = NeoMemberStorage.Save;

            data.members[count.id] = count;
            data.members[sparse.id] = sparse;
            data.classes["class-root-save"].schema[sparse.name] = sparse.id;
            NeoSchemaClass sparseClass = ClassOf(
                "class-sparse",
                "Sparse",
                new Dictionary<string, string>
                {
                    [count.name] = count.id,
                });
            sparseClass.allowedStorage = NeoMemberStorage.Save;
            data.classes[sparseClass.id] = sparseClass;

            ObjectMemberValue sparseValue = RecordValue(
                "value-sparse-save",
                sparseClass.id,
                new Dictionary<string, string>());
            sparseValue.instanceConstructorId = null;
            sparseValue.constructorArgs = new Dictionary<string, JToken?>();
            data.values[sparseValue.id] = sparseValue;
            ((ObjectMemberValue)data.values["value-root-save"])
                .value![sparse.name] = sparseValue.id;
        }

        private static string SparseCountValueId(ProjectData data)
        {
            using NeoClient probe = NeoTestSaveStack.ClientFromSchema(data);
            return probe.SaveRoot
                .Get<NeoMemberClassWritable>("Sparse")
                .Get<NeoMemberIntWritable>("Count")
                .value!.id;
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
            NeoMemberStorage storage)
        {
            ClassMember member = ClassMemberOf(id, name, classId, valueId);
            member.DeclaredStorage = storage;
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
