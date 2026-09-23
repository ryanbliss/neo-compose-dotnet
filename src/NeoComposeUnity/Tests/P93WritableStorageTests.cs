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
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P93 — <c>Writable</c> member storage in the SDK. The fixture places one
    /// <c>Part</c> under each root: <c>Size</c> (the parameterized storage),
    /// <c>Mood</c> (Session), <c>Plain</c> (Inherit), and a <c>Writable</c>
    /// class member <c>Bag</c> with an Inherit <c>V</c> and a Save
    /// <c>Pinned</c>. Save also holds <c>Narrow</c>, a Part whose runtime class
    /// narrows <c>Size</c> to Session, and <c>Crate</c>, a
    /// <c>Writable</c>-constrained class that Session holds too.
    /// </summary>
    public class P93WritableStorageTests
    {
        private const string ProjectId = "project-p93";

        [Test]
        public void AuthoredOwnership_FollowsTheChildStorageRuleUnderEachRoot()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(Build());
            var expected = new (string valueId, NeoValueOwnership ownership)[]
            {
                // Writable resolves Session in an Immutable context.
                ("a-size", NeoValueOwnership.Session),
                ("a-bag", NeoValueOwnership.Session),
                ("a-bag-v", NeoValueOwnership.Session),
                ("a-bag-pinned", NeoValueOwnership.Save),
                ("s-size", NeoValueOwnership.Save),
                ("s-bag", NeoValueOwnership.Save),
                ("s-bag-v", NeoValueOwnership.Save),
                ("s-bag-pinned", NeoValueOwnership.Save),
                ("x-size", NeoValueOwnership.Session),
                ("x-bag", NeoValueOwnership.Session),
                ("x-bag-v", NeoValueOwnership.Session),
                ("x-bag-pinned", NeoValueOwnership.Save),
                // The runtime class's Session override beats the Writable base.
                ("n-size", NeoValueOwnership.Session),
                // A Writable class constraint pins nothing; placement decides.
                ("s-crate-count", NeoValueOwnership.Save),
                ("x-crate-count", NeoValueOwnership.Session),
            };
            foreach (var (valueId, ownership) in expected)
            {
                Assert.IsTrue(client.TryGetValueOwnership(valueId, out NeoValueOwnership actual), valueId);
                Assert.AreEqual(ownership, actual, valueId);
            }
            Assert.IsTrue(client.TryGetValueOwnership("a-plain", out NeoValueOwnership plain));
            Assert.AreEqual(NeoValueOwnership.Asset, plain);
        }

        [Test]
        public void AssetRow_ExposesAWritableMemberAsAWritableSessionNode()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(Build());
            NeoMemberClass thing = client.AssetsRoot.Get<NeoMemberClass>("Thing");
            Assert.IsFalse(thing is NeoMemberClassWritable);

            var size = thing.Get<NeoMemberIntWritable>("Size");
            Assert.AreEqual(NeoValueOwnership.Session, size.ownership);
            Assert.IsFalse(thing.Get<NeoMember>("Plain") is NeoMemberIntWritable);

            size.Set(5);
            Assert.AreEqual(5d, size.value!.value);
            StringAssert.DoesNotContain("a-size", client.SerializeSaveData());
        }

        [TestCase(NeoMemberStorage.Writable, 2d)]
        [TestCase(NeoMemberStorage.Session, 1d)]
        public void ConstructedIntoSave_CallSiteValueSurvivesReloadOnlyWhenWritable(
            NeoMemberStorage sizeStorage,
            double reloadedSize)
        {
            ProjectData data = Build(sizeStorage);
            string saved;
            using (NeoClient client = NeoTestSaveStack.ClientFromSchema(data))
            {
                var ctx = new NSGetterEvaluator.Context(client, null, null);
                object? constructed = NSGetterEvaluator.Evaluate(
                    Return(new FunctionPointer
                    {
                        type = PointerKind.Function,
                        function = new DeclaredConstructorFunction
                        {
                            type = FunctionKind.DeclaredConstructor,
                            info = new DeclaredConstructorInfo
                            {
                                schemaClassInfo = PartType(),
                                constructorId = null,
                                args = Array.Empty<DeclaredConstructorArgument>(),
                                fields = new[]
                                {
                                    Field("Size", "size-member", 2),
                                    Field("Mood", "mood-member", 9),
                                },
                            },
                        },
                    }),
                    ctx);
                string partId = NSGetterEvaluator.FindRowIdByReference(constructed, ctx)!;
                client.save.SetSerializedValue("Thing", NeoValueWritePayload.FromValueReference(partId));
                var placed = client.save.Get<NeoMemberClassWritable>("Thing");
                Assert.AreEqual(2d, placed.Get<NeoMemberInt>("Size").value!.value);
                Assert.AreEqual(9d, placed.Get<NeoMemberInt>("Mood").value!.value);
                saved = client.SerializeSaveData();
            }

            using NeoClient reopened = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: saved);
            var reloaded = reopened.save.Get<NeoMemberClassWritable>("Thing");
            Assert.AreEqual(reloadedSize, reloaded.Get<NeoMemberInt>("Size").value!.value);
            Assert.AreEqual(1d, reloaded.Get<NeoMemberInt>("Mood").value!.value);
        }

        [Test]
        public void RuntimeWrite_ToAWritableMemberOfAnAssetRowLandsInSessionAndResets()
        {
            ProjectData data = Build();
            string saved;
            using (NeoClient client = NeoTestSaveStack.ClientFromSchema(data))
            {
                ExecuteRuntimeWrite(client, "Assets", "Thing", "Size", 5);
                Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Session, "a-size", out NumberMemberValue? written));
                Assert.AreEqual(5d, written!.value);
                saved = client.SerializeSaveData();
            }
            using NeoClient reopened = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: saved);
            Assert.AreEqual(1d, reopened.AssetsRoot.Get<NeoMemberClass>("Thing").Get<NeoMemberInt>("Size").value!.value);
        }

        [Test]
        public void RuntimeWrite_ToAWritableMemberOfASaveRowLandsInSave()
        {
            ProjectData data = Build();
            string saved;
            using (NeoClient client = NeoTestSaveStack.ClientFromSchema(data))
            {
                ExecuteRuntimeWrite(client, "Save", "Thing", "Size", 6);
                saved = client.SerializeSaveData();
            }
            using NeoClient reopened = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: saved);
            Assert.AreEqual(6d, reopened.save.Get<NeoMemberClassWritable>("Thing").Get<NeoMemberInt>("Size").value!.value);
        }

        [Test]
        public void RuntimeWrite_ToAnInheritMemberOfAnAssetRowStillThrows()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(Build());
            var error = Assert.Throws<NSGetterRuntimeError>(() =>
                ExecuteRuntimeWrite(client, "Assets", "Thing", "Plain", 5));
            StringAssert.Contains("Asset-owned", error!.Message);
        }

        [Test]
        public void RuntimeWrite_TypedAsTheWritableBaseRoutesToTheNarrowedSessionOverride()
        {
            ProjectData data = Build();
            string saved;
            using (NeoClient client = NeoTestSaveStack.ClientFromSchema(data))
            {
                ExecuteRuntimeWrite(client, "Save", "Narrow", "Size", 7);
                Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Session, "n-size", out NumberMemberValue? written));
                Assert.AreEqual(7d, written!.value);
                saved = client.SerializeSaveData();
            }
            StringAssert.DoesNotContain("n-size", saved);
        }

        [Test]
        public void WritableConstrainedClass_PersistsInSaveAndResetsInSession()
        {
            ProjectData data = Build();
            string saved;
            using (NeoClient client = NeoTestSaveStack.ClientFromSchema(data))
            {
                Assert.IsFalse(client.TryResolveSchemaClassAllowedOwnership("crate-class", out _));
                Assert.AreEqual(
                    NeoValueOwnership.Session,
                    client.ResolveStaticOwnership((JsonMember)data.members["crate-total-member"]));
                client.save.Get<NeoMemberClassWritable>("Crate").Get<NeoMemberIntWritable>("Count").Set(4);
                client.session.Get<NeoMemberClassWritable>("Crate").Get<NeoMemberIntWritable>("Count").Set(4);
                saved = client.SerializeSaveData();
            }
            using NeoClient reopened = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: saved);
            Assert.AreEqual(4d, reopened.save.Get<NeoMemberClassWritable>("Crate").Get<NeoMemberInt>("Count").value!.value);
            Assert.AreEqual(1d, reopened.session.Get<NeoMemberClassWritable>("Crate").Get<NeoMemberInt>("Count").value!.value);
        }

        [Test]
        public void ReadOnlyMember_RejectsAWritableDescendant()
        {
            ProjectData data = Build();
            data.members["frozen-member"] = new ClassMember
            {
                id = "frozen-member", projectId = ProjectId, name = "Frozen", kind = MemberKind.Class,
                classId = "frozen-class", Storage = NeoMemberStorage.Immutable,
                Mutability = NeoMemberMutabilityKind.ReadOnly,
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new ObjectMemberValueBase { classId = "frozen-class", value = new Dictionary<string, string>() },
                createdAt = "x", updatedAt = "x",
            };
            data.classes["frozen-class"] = new NeoSchemaClass
            {
                id = "frozen-class", projectId = ProjectId, name = "Frozen",
                schema = new Dictionary<string, string> { ["Size"] = "size-member" },
                createdAt = "x", updatedAt = "x",
            };
            data.classes["assets-class"].schema["Frozen"] = "frozen-member";
            var error = Assert.Throws<InvalidOperationException>(() => NeoTestSaveStack.ClientFromSchema(data));
            StringAssert.Contains("owns writable descendant member 'Size'", error!.Message);
        }

        private static void ExecuteRuntimeWrite(
            NeoClient client,
            string root,
            string receiver,
            string key,
            double value)
        {
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var runtimeRoot = new Dictionary<string, object?>
            {
                ["Assets"] = NSGetterEvaluator.UnwrapRow((ObjectMemberValue)client.assets.value!, ctx, NeoValueOwnership.Asset),
                ["Save"] = NSGetterEvaluator.UnwrapRow((ObjectMemberValue)client.save.value!, ctx, NeoValueOwnership.Save),
            };
            ctx = ctx.WithRoot(runtimeRoot);
            var action = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = Array.Empty<Variable>(),
                typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                instructions = new Instruction[]
                {
                    new AssignInstruction
                    {
                        type = InstructionKind.Assign,
                        target = new WriteTarget
                        {
                            pointer = KeyOf(KeyOf(KeyOf(new VariablePointer
                            {
                                type = PointerKind.Variable,
                                variableId = "__root__",
                            }, root), receiver), key),
                            typeInfo = IntType(),
                            writability = WritabilityKind.Runtime,
                        },
                        operatorValue = "=",
                        pointer = Number(value),
                    },
                },
            };
            NeoScriptExecutor.Execute(
                client,
                action,
                new Dictionary<string, object?> { ["__root__"] = runtimeRoot },
                ctx);
        }

        private static ProjectData Build(NeoMemberStorage sizeStorage = NeoMemberStorage.Writable)
        {
            var members = new Dictionary<string, JsonMember>();
            void Add(JsonMember member) => members[member.id] = member;
            Add(Root("root-assets", "Assets", "assets-class", "v-assets", NeoMemberStorage.Immutable));
            Add(Root("root-save", "Save", "save-class", "v-save", NeoMemberStorage.Save));
            Add(Root("root-session", "Session", "session-class", "v-session", NeoMemberStorage.Session));
            Add(ClassField("thing-member", "Thing", "part-class", NeoMemberStorage.Inherit));
            Add(ClassField("narrow-member", "Narrow", "part-class", NeoMemberStorage.Inherit));
            Add(ClassField("crate-member", "Crate", "crate-class", NeoMemberStorage.Inherit));
            Add(ClassField("bag-member", "Bag", "bag-class", NeoMemberStorage.Writable));
            Add(IntField("size-member", "Size", sizeStorage));
            Add(IntField("mood-member", "Mood", NeoMemberStorage.Session));
            Add(IntField("plain-member", "Plain", NeoMemberStorage.Inherit));
            Add(IntField("v-member", "V", NeoMemberStorage.Inherit));
            Add(IntField("pinned-member", "Pinned", NeoMemberStorage.Save));
            Add(IntField("count-member", "Count", NeoMemberStorage.Inherit));
            var narrowSize = IntField("size-narrow-member", "Size", NeoMemberStorage.Session);
            narrowSize.extendsMemberId = "size-member";
            Add(narrowSize);
            var total = IntField("crate-total-member", "Total", NeoMemberStorage.Inherit);
            total.Modifier = NeoMemberModifierKind.Static;
            Add(total);

            var classes = new Dictionary<string, NeoSchemaClass>();
            void AddClass(string id, params (string key, string memberId)[] schema)
            {
                var map = new Dictionary<string, string>();
                foreach (var (key, memberId) in schema) map[key] = memberId;
                classes[id] = new NeoSchemaClass
                {
                    id = id, projectId = ProjectId, name = id, schema = map,
                    createdAt = "x", updatedAt = "x",
                };
            }
            AddClass("assets-class", ("Thing", "thing-member"));
            AddClass("save-class", ("Thing", "thing-member"), ("Narrow", "narrow-member"), ("Crate", "crate-member"));
            AddClass("session-class", ("Thing", "thing-member"), ("Crate", "crate-member"));
            AddClass("part-class", ("Size", "size-member"), ("Mood", "mood-member"), ("Plain", "plain-member"), ("Bag", "bag-member"));
            AddClass("narrow-part-class", ("Size", "size-narrow-member"));
            classes["narrow-part-class"].extendsClassId = "part-class";
            AddClass("bag-class", ("V", "v-member"), ("Pinned", "pinned-member"));
            AddClass("crate-class", ("Count", "count-member"), ("Total", "crate-total-member"));
            classes["crate-class"].allowedStorage = NeoMemberStorage.Writable;

            var values = new Dictionary<string, MemberValue>();
            void Part(string prefix, string classId)
            {
                foreach (string key in new[] { "size", "mood", "plain", "bag-v", "bag-pinned" })
                    values[$"{prefix}-{key}"] = new NumberMemberValue { id = $"{prefix}-{key}", value = 1, createdAt = "x", updatedAt = "x" };
                values[$"{prefix}-bag"] = Record($"{prefix}-bag", "bag-class", ("V", $"{prefix}-bag-v"), ("Pinned", $"{prefix}-bag-pinned"));
                values[$"{prefix}-thing"] = Record($"{prefix}-thing", classId,
                    ("Size", $"{prefix}-size"), ("Mood", $"{prefix}-mood"), ("Plain", $"{prefix}-plain"), ("Bag", $"{prefix}-bag"));
            }
            void Crate(string prefix)
            {
                values[$"{prefix}-crate-count"] = new NumberMemberValue { id = $"{prefix}-crate-count", value = 1, createdAt = "x", updatedAt = "x" };
                values[$"{prefix}-crate"] = Record($"{prefix}-crate", "crate-class", ("Count", $"{prefix}-crate-count"));
            }
            Part("a", "part-class");
            Part("s", "part-class");
            Part("x", "part-class");
            Part("n", "narrow-part-class");
            Crate("s");
            Crate("x");
            values["v-assets"] = Record("v-assets", "assets-class", ("Thing", "a-thing"));
            values["v-save"] = Record("v-save", "save-class", ("Thing", "s-thing"), ("Narrow", "n-thing"), ("Crate", "s-crate"));
            values["v-session"] = Record("v-session", "session-class", ("Thing", "x-thing"), ("Crate", "x-crate"));

            return new ProjectData
            {
                project = new Project
                {
                    id = ProjectId, name = "P93",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                    createdAt = "x", updatedAt = "x",
                },
                members = members,
                classes = classes,
                values = values,
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
        }

        private static ClassMember Root(string id, string name, string classId, string valueId, NeoMemberStorage storage) => new()
        {
            id = id, projectId = ProjectId, name = name, kind = MemberKind.Class,
            classId = classId, valueId = valueId, Storage = storage, createdAt = "x", updatedAt = "x",
        };

        private static ClassMember ClassField(string id, string name, string classId, NeoMemberStorage storage) => new()
        {
            id = id, projectId = ProjectId, name = name, kind = MemberKind.Class,
            classId = classId, Storage = storage, createdAt = "x", updatedAt = "x",
        };

        private static IntMember IntField(string id, string name, NeoMemberStorage storage) => new()
        {
            id = id, projectId = ProjectId, name = name, kind = MemberKind.Int, Storage = storage,
            defaultValue = new NumberMemberValueBase { value = 1 }, createdAt = "x", updatedAt = "x",
        };

        private static ObjectMemberValue Record(string id, string classId, params (string key, string valueId)[] fields)
        {
            var value = new Dictionary<string, string>();
            foreach (var (key, valueId) in fields) value[key] = valueId;
            return new ObjectMemberValue { id = id, classId = classId, value = value, createdAt = "x", updatedAt = "x" };
        }

        private static ClassTypeInfo PartType() => new()
        {
            type = MemberKind.Class,
            required = true,
            classId = "part-class",
        };

        private static PrimitiveTypeInfo IntType() => new() { type = MemberKind.Int, required = true };

        private static ValuePointer Number(double value) => new()
        {
            type = PointerKind.Value,
            value = new Value { typeInfo = IntType(), value = JToken.FromObject(value) },
        };

        private static KeyOfPointer KeyOf(Pointer receiver, string key) => new()
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
                        typeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true },
                        value = JToken.FromObject(key),
                    },
                },
            },
        };

        private static FunctionClassConstructorField Field(string key, string memberId, double value) => new()
        {
            schemaKey = key,
            memberId = memberId,
            valuePointer = Number(value),
        };

        private static FunctionWithReturnType Return(Pointer pointer) => new()
        {
            compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
            parameters = Array.Empty<Variable>(),
            typeInfo = PartType(),
            instructions = new Instruction[]
            {
                new ReturnInstruction { type = InstructionKind.Return, pointer = pointer },
            },
        };
    }
}
