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

        [TestCase(false)]
        [TestCase(true)]
        public void ScriptMembershipChangesNotifyTheLiveListOnce(bool unordered)
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(unordered));
            client.SetWritableValues(NeoValueOwnership.Session, new MemberValue[]
            {
                new ObjectMemberValue { id = "script-item", classId = "item", value = new() },
            });
            var list = client.save.Get<NeoMemberListWritable>("Items");
            int listChanges = 0;
            int saveChanges = 0;
            int observedCount = -1;
            NeoListChangedArgs? observedChange = null;
            list.OnChanged += _ =>
            {
                listChanges++;
                observedCount = list.Count;
                observedChange = list.ActiveListChange;
            };
            client.save.OnChanged += _ => saveChanges++;
            var context = new NSGetterEvaluator.Context(client, null, null);

            Execute(client, context, new(), CollectionMutationKind.Add,
                new ReferencePointer { type = PointerKind.Reference, valueId = "script-item" });
            Assert.AreEqual(1, listChanges, "A NeoScript add must reach the live list once.");
            Assert.AreEqual(1, saveChanges, "A NeoScript add must bubble to the parent once.");
            Assert.AreEqual(1, observedCount);
            if (unordered)
            {
                Assert.AreEqual(NeoListChangeKind.Add, observedChange!.Kind, "The entry's own row change is part of the add.");
                CollectionAssert.AreEqual(new[] { "script-item" }, observedChange.AddedValueIds);
            }
            else
            {
                // An ordered list whose row another writer replaced can't
                // name the change; the entry's Set must not narrow that.
                Assert.AreEqual(NeoListChangeKind.Unknown, observedChange!.Kind);
            }

            listChanges = 0;
            saveChanges = 0;
            Execute(client, context, new(), CollectionMutationKind.Clear);
            Assert.AreEqual(1, listChanges, "A NeoScript clear must reach the live list once.");
            Assert.AreEqual(1, saveChanges, "A NeoScript clear must bubble to the parent once.");
            Assert.AreEqual(0, observedCount);
        }

        [Test]
        public void ScriptDictionaryWriteNotifiesTheLiveDictionaryOnce()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            var dictionary = client.save.Get<NeoMemberDictionaryWritable>("ByKey");
            int dictionaryChanges = 0;
            int saveChanges = 0;
            dictionary.OnChanged += _ => dictionaryChanges++;
            client.save.OnChanged += _ => saveChanges++;

            NeoScriptExecutor.Execute(client, Function(new AssignInstruction
            {
                type = InstructionKind.Assign,
                operatorValue = "=",
                target = new WriteTarget
                {
                    pointer = new KeyOfPointer
                    {
                        type = PointerKind.KeyOf,
                        keyOf = new KeyOf
                        {
                            pointer = new ReferencePointer { type = PointerKind.Reference, valueId = "by-key" },
                            key = StringPointer("a"),
                        },
                    },
                    typeInfo = StringType,
                    writability = WritabilityKind.Save,
                },
                pointer = StringPointer("written"),
            }), new Dictionary<string, object?>(), new NSGetterEvaluator.Context(client, null, null));

            Assert.AreEqual(1, dictionaryChanges, "A NeoScript entry write must reach the live dictionary once.");
            Assert.AreEqual(1, saveChanges, "A NeoScript entry write must bubble to the parent once.");
            Assert.AreEqual(1, dictionary.Count);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WholeListWriteNotifiesTheParentOnce(bool unordered)
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(unordered));
            client.SetWritableValues(NeoValueOwnership.Session, new MemberValue[]
            {
                new ObjectMemberValue { id = "script-item", classId = "item", value = new() },
            });
            var list = client.save.Get<NeoMemberListWritable>("Items");
            list.AddSerialized(NeoValueWritePayload.FromValueReference("script-item"));
            int saveChanges = 0;
            client.save.OnChanged += _ => saveChanges++;

            client.save.SetSerializedValue("Items", NeoValueWritePayload.FromValue(Array.Empty<string>()));

            Assert.AreEqual(1, saveChanges);
            Assert.AreEqual(0, client.save.Get<NeoMemberListWritable>("Items").Count);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WholeDictionaryWriteNotifiesTheParentOnce(bool unset)
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            client.save.Get<NeoMemberDictionaryWritable>("ByKey")
                .SetSerialized("a", NeoValueWritePayload.FromValue("written"));
            int saveChanges = 0;
            client.save.OnChanged += _ => saveChanges++;

            if (unset)
                client.save.Unset("ByKey");
            else
                client.save.SetSerializedValue("ByKey", NeoValueWritePayload.FromValue(new Dictionary<string, string>()));

            Assert.AreEqual(1, saveChanges);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ListEntryWriteNotifiesOnce(bool reference)
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            client.SetWritableValues(NeoValueOwnership.Session, new MemberValue[]
            {
                new StringMemberValue { id = "name-1", value = "one" },
            });
            var names = client.save.Get<NeoMemberListWritable>("Names");
            int listChanges = 0;
            int saveChanges = 0;
            NeoListChangedArgs? observedChange = null;
            names.OnChanged += _ =>
            {
                listChanges++;
                observedChange = names.ActiveListChange;
            };
            client.save.OnChanged += _ => saveChanges++;

            names.SetSerialized(0, reference
                ? NeoValueWritePayload.FromValueReference("name-1")
                : NeoValueWritePayload.FromValue("one"));

            Assert.AreEqual(1, listChanges);
            Assert.AreEqual(1, saveChanges);
            Assert.AreEqual(reference ? NeoListChangeKind.Replace : NeoListChangeKind.Set, observedChange!.Kind);
            Assert.AreEqual("one", ((NeoMemberString)names[0]).value!.value);
        }

        [Test]
        public void ClassFieldUnsetNotifiesTheParentOnce()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            var part = client.save.Get<NeoMemberClassWritable>("Part");
            int saveChanges = 0;
            client.save.OnChanged += _ => saveChanges++;

            client.save.Unset("Part");

            Assert.AreEqual(1, saveChanges, "A Class child retires on its tombstone, so the parent reports it.");
            Assert.IsTrue(part.isDisposed);
        }

        [Test]
        public void WholeClassWriteReportsEachChangedMemberOnce()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            var part = client.save.Get<NeoMemberClassWritable>("Part");
            var partName = part.Get<NeoMemberString>("Name");
            var changed = new List<NeoMember>();
            client.save.OnChanged += changed.Add;

            client.save.SetSerializedValue("Part", NeoValueWritePayload.FromValue(new NeoValuePayload(
                new Dictionary<string, string> { ["Name"] = "part-name-2" },
                "item",
                new MemberValue[] { new StringMemberValue { id = "part-name-2", value = "rewritten" } })));

            // The Part row and the Name leaf it rebound both changed.
            Assert.AreEqual(2, changed.Count);
            Assert.AreSame(part, changed[0]);
            Assert.AreSame(client.save.Get<NeoMemberClassWritable>("Part").Get<NeoMemberString>("Name"), changed[1]);
            Assert.AreEqual("rewritten", client.save.Get<NeoMemberClassWritable>("Part").Get<NeoMemberString>("Name").value!.value);
            Assert.IsTrue(partName.isDisposed);
        }

        [Test]
        public void ForeachSnapshotRetainsCollectionOwnershipForUnchangedAuthoredEntries()
        {
            var schema = Schema(false);
            schema.values["item-1"] = new ObjectMemberValue { id = "item-1", classId = "item", value = new() { ["Name"] = "name-1" } };
            schema.values["name-1"] = new StringMemberValue { id = "name-1", value = "original" };
            using var client = NeoTestSaveStack.ClientFromSchema(schema);
            var context = new NSGetterEvaluator.Context(client, null, null);
            var collection = NSGetterEvaluator.UnwrapRow(new ArrayMemberValue { id = "save-view", value = new[] { "item-1" } }, context, NeoValueOwnership.Save);
            var snapshot = System.Array.Empty<NSGetterEvaluator.CollectionEntrySnapshot>();
            NSGetterEvaluator.SnapshotCollectionEntries(collection, context, ref snapshot);
            var entry = snapshot[0].Resolve(context);
            Assert.That(NSGetterEvaluator.FindRowOwnershipByReference(entry, context), Is.EqualTo(NeoValueOwnership.Save));
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
                id = "static-items",
                name = "StaticItems",
                kind = MemberKind.List,
                entryMemberId = "entry",
                Storage = NeoMemberStorage.Save,
                Modifier = NeoMemberModifierKind.Static,
            };
            schema.classes["save"].schema["StaticItems"] = "static-items";
            using var client = NeoTestSaveStack.ClientFromSchema(schema);
            var binding = NeoGeneratedTypesSupport.StaticBinding(client, "static-items", NeoValueOwnership.Save);
            var pointer = new StaticMemberPointer { type = PointerKind.StaticMember, memberId = "static-items" };
            var context = new NSGetterEvaluator.Context(client, null, null);
            var scope = new Dictionary<string, object?>();
            string before = client.SerializeSaveData();
            Assert.Throws<NSGetterRuntimeError>(() => ExecuteTarget(client, context, scope, pointer,
                CollectionMutationKind.RemoveAt, new ValuePointer
                {
                    type = PointerKind.Value,
                    value = new Value
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

        private static readonly PrimitiveTypeInfo StringType = new() { type = MemberKind.String, required = true };

        private static ValuePointer StringPointer(string value) => new()
        {
            type = PointerKind.Value,
            value = new Value { typeInfo = StringType, value = new JValue(value) },
        };

        private static FunctionWithReturnType Function(Instruction instruction) => new()
        {
            compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
            parameters = Array.Empty<Variable>(),
            typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
            instructions = new[] { instruction },
        };

        private static ProjectData Schema(bool unordered)
        {
            return new ProjectData
            {
                project = new Project
                {
                    id = "dialogue-atomic",
                    _id = "dialogue-atomic",
                    name = "Atomic dialogue writes",
                    rootAssetsMemberId = "assets-root",
                    rootSaveFileMemberId = "save-root",
                    rootSessionMemberId = "session-root",
                },
                classes = new()
                {
                    ["empty"] = new NeoSchemaClass { id = "empty", name = "Empty", schema = new() },
                    ["save"] = new NeoSchemaClass { id = "save", name = "Save", schema = new() { ["Items"] = "items-member", ["ByKey"] = "by-key-member", ["Names"] = "names-member", ["Part"] = "part-member" } },
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
                    ["by-key-member"] = new DictionaryMember { id = "by-key-member", name = "ByKey", kind = MemberKind.Dictionary, entryMemberId = "by-key-entry", KeyKind = NeoDictionaryKeyKind.String, Requirement = NeoMemberRequirementKind.Optional },
                    ["by-key-entry"] = new StringMember { id = "by-key-entry", name = "Entry", kind = MemberKind.String, Requirement = NeoMemberRequirementKind.Required },
                    ["names-member"] = new ListMember { id = "names-member", name = "Names", kind = MemberKind.List, entryMemberId = "names-entry", Requirement = NeoMemberRequirementKind.Required },
                    ["names-entry"] = new StringMember { id = "names-entry", name = "Name", kind = MemberKind.String, Requirement = NeoMemberRequirementKind.Required },
                    ["part-member"] = new ClassMember { id = "part-member", name = "Part", kind = MemberKind.Class, classId = "item", Requirement = NeoMemberRequirementKind.Optional },
                },
                values = new()
                {
                    ["assets"] = new ObjectMemberValue { id = "assets", classId = "empty", value = new() },
                    ["save"] = new ObjectMemberValue { id = "save", classId = "save", value = new() { ["Items"] = "items", ["ByKey"] = "by-key", ["Names"] = "names", ["Part"] = "part" } },
                    ["session"] = new ObjectMemberValue { id = "session", classId = "empty", value = new() },
                    ["items"] = new ArrayMemberValue { id = "items", value = Array.Empty<string>() },
                    ["by-key"] = new ObjectMemberValue { id = "by-key", value = new() },
                    ["names"] = new ArrayMemberValue { id = "names", value = new[] { "name-0" } },
                    ["name-0"] = new StringMemberValue { id = "name-0", value = "zero" },
                    ["part"] = new ObjectMemberValue { id = "part", classId = "item", value = new() { ["Name"] = "part-name" } },
                    ["part-name"] = new StringMemberValue { id = "part-name", value = "part" },
                },
                enums = new(),
            };
        }

        private static ClassMember Root(string id, string valueId, string classId) => new()
        {
            id = id,
            name = id,
            kind = MemberKind.Class,
            valueId = valueId,
            classId = classId,
            Storage = valueId == "save" ? NeoMemberStorage.Save : valueId == "session" ? NeoMemberStorage.Session : NeoMemberStorage.Immutable,
            Requirement = NeoMemberRequirementKind.Required,
        };
    }
}
