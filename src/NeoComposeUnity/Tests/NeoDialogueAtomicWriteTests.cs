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
    public class NeoDialogueAtomicWriteTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void AddPublishesAdoptedGraphAndMembershipTogether(bool unordered)
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(unordered));
            client.SetWritableValues(NeoValueOwnership.Session, new MemberValue[]
            {
                new ObjectMemberValue { id = "new-item", classId = "item", value = new() { ["Name"] = "new-name" } },
                new StringMemberValue { id = "new-name", value = "new" },
            });
            int publications = 0;
            client.OnWritableValuesChanged += _ =>
            {
                publications++;
                Assert.IsTrue(client.saveValues.ContainsKey("new-item"));
                Assert.IsTrue(client.saveValues.ContainsKey("new-name"));
                Assert.IsFalse(client.sessionValues.ContainsKey("new-item"));
                Assert.IsFalse(client.sessionValues.ContainsKey("new-name"));
                if (unordered)
                    Assert.AreEqual("items", client.saveValues["new-item"].containerId);
                else
                    CollectionAssert.AreEqual(new[] { "new-item" }, ((ArrayMemberValue)client.saveValues["items"]).value);
            };
            var context = new NSGetterEvaluator.Context(client, null, null);
            var scope = new Dictionary<string, object?>();
            Execute(client, context, scope, CollectionMutationKind.Add,
                new ReferencePointer { type = PointerKind.Reference, valueId = "new-item" });
            Assert.AreEqual(1, publications);
            var list = client.save.Get<NeoMemberListWritable>("Items");
            Assert.AreEqual(1, list.Count, "The next script read must observe the committed membership.");
        }

        [Test]
        public void InvalidRemoveDoesNotPublishAnAuthoredShadow()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            string before = client.SerializeSaveData();
            int publications = 0;
            client.OnWritableValuesChanged += _ => publications++;
            Assert.Throws<NSGetterRuntimeError>(() => Execute(client,
                new NSGetterEvaluator.Context(client, null, null), new(),
                CollectionMutationKind.RemoveAt, new ValuePointer
                {
                    type = PointerKind.Value,
                    value = new Value
                    {
                        typeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                        value = new JValue(0),
                    },
                }));
            Assert.AreEqual(before, client.SerializeSaveData());
            Assert.AreEqual(0, publications);
        }

        [Test]
        public void MissingStaticCollectionPublishesOnlyAfterSuccessfulMutation()
        {
            var schema = Schema(false);
            schema.members["static-items"] = new ListMember
            {
                id = "static-items", name = "StaticItems", kind = MemberKind.List,
                entryMemberId = "entry", Storage = NeoMemberStorage.Save,
                Modifier = NeoMemberModifierKind.Static,
            };
            schema.classes["save"].schema["StaticItems"] = "static-items";
            using var client = NeoTestSaveStack.ClientFromSchema(schema);
            var binding = new NeoStaticBinding(client, "static-items", NeoValueOwnership.Save);
            var pointer = new StaticMemberPointer { type = PointerKind.StaticMember, memberId = "static-items" };
            var context = new NSGetterEvaluator.Context(client, null, null);
            var scope = new Dictionary<string, object?>();
            string before = client.SerializeSaveData();
            Assert.Throws<NSGetterRuntimeError>(() => ExecuteTarget(client, context, scope, pointer,
                CollectionMutationKind.RemoveAt, new ValuePointer
                {
                    type = PointerKind.Value, value = new Value
                    {
                        typeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                        value = new JValue(0),
                    },
                }));
            Assert.IsNull(binding.ValueId);
            Assert.AreEqual(before, client.SerializeSaveData());
            client.SetWritableValues(NeoValueOwnership.Session, new MemberValue[]
            {
                new ObjectMemberValue { id = "new-item", classId = "item", value = new() },
            });
            int publications = 0;
            client.OnWritableValuesChanged += _ =>
            {
                publications++;
                Assert.IsNotNull(binding.ValueId);
                CollectionAssert.AreEqual(new[] { "new-item" },
                    ((ArrayMemberValue)client.saveValues[binding.ValueId!]).value);
                Assert.IsTrue(client.saveValues.ContainsKey("new-item"));
            };
            ExecuteTarget(client, context, scope, pointer, CollectionMutationKind.Add,
                new ReferencePointer { type = PointerKind.Reference, valueId = "new-item" });
            Assert.AreEqual(1, publications);
        }

        private static void Execute(NeoClient client, NSGetterEvaluator.Context context,
            Dictionary<string, object?> scope, string mutation, params Pointer[] arguments) =>
            ExecuteTarget(client, context, scope,
                new ReferencePointer { type = PointerKind.Reference, valueId = "items" }, mutation, arguments);

        private static void ExecuteTarget(NeoClient client, NSGetterEvaluator.Context context,
            Dictionary<string, object?> scope, Pointer target, string mutation, params Pointer[] arguments)
        {
            NeoScriptExecutor.Execute(client, new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = Array.Empty<Variable>(),
                typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                instructions = new Instruction[]
                {
                    new CollectionCallInstruction
                    {
                        type = InstructionKind.CollectionCall,
                        target = new WriteTarget
                        {
                            pointer = target,
                            typeInfo = new CollectionTypeInfo
                            {
                                type = MemberKind.List, required = true,
                                entryTypeInfo = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = "item" },
                            },
                            writability = WritabilityKind.Save,
                        },
                        mutation = mutation, args = arguments,
                    },
                },
            }, scope, context);
        }

        private static ProjectData Schema(bool unordered)
        {
            return new ProjectData
            {
                project = new Project
                {
                    id = "dialogue-atomic", _id = "dialogue-atomic", name = "Atomic dialogue writes",
                    rootAssetsMemberId = "assets-root", rootSaveFileMemberId = "save-root", rootSessionMemberId = "session-root",
                },
                classes = new()
                {
                    ["empty"] = new NeoSchemaClass { id = "empty", name = "Empty", schema = new() },
                    ["save"] = new NeoSchemaClass { id = "save", name = "Save", schema = new() { ["Items"] = "items-member" } },
                    ["item"] = new NeoSchemaClass { id = "item", name = "Item", schema = new() { ["Name"] = "name" } },
                },
                members = new()
                {
                    ["assets-root"] = Root("assets-root", "assets", "empty"),
                    ["save-root"] = Root("save-root", "save", "save"),
                    ["session-root"] = Root("session-root", "session", "empty"),
                    ["items-member"] = new ListMember { id = "items-member", name = "Items", kind = MemberKind.List, entryMemberId = "entry", ListKind = unordered ? NeoListKind.Unordered : NeoListKind.Ordered, Requirement = NeoMemberRequirementKind.Required },
                    ["entry"] = new ClassMember { id = "entry", name = "Item", kind = MemberKind.Class, classId = "item", Requirement = NeoMemberRequirementKind.Required },
                    ["name"] = new StringMember { id = "name", name = "Name", kind = MemberKind.String, Requirement = NeoMemberRequirementKind.Required },
                },
                values = new()
                {
                    ["assets"] = new ObjectMemberValue { id = "assets", classId = "empty", value = new() },
                    ["save"] = new ObjectMemberValue { id = "save", classId = "save", value = new() { ["Items"] = "items" } },
                    ["session"] = new ObjectMemberValue { id = "session", classId = "empty", value = new() },
                    ["items"] = new ArrayMemberValue { id = "items", value = Array.Empty<string>() },
                },
                enums = new(),
            };
        }

        private static ClassMember Root(string id, string valueId, string classId) => new()
        {
            id = id, name = id, kind = MemberKind.Class, valueId = valueId, classId = classId,
            Storage = valueId == "save" ? NeoMemberStorage.Save : valueId == "session" ? NeoMemberStorage.Session : NeoMemberStorage.Immutable,
            Requirement = NeoMemberRequirementKind.Required,
        };
    }
}
