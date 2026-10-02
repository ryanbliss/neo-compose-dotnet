// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
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
            var snapshot = new NSGetterEvaluator.CollectionSnapshot();
            snapshot.Take(collection, context);
            var entry = snapshot.Resolve(0, context);
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

        [Test]
        public void StoredListMutationsInOneExecutionCommitOnce()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            int publications = 0;
            client.OnWritableValuesChanged += _ => publications++;

            // Save.Names.Add("a"); Save.Names.Add("b"); Save.Names.Insert(0, "first");
            // Save.Names.RemoveAt(1); Save.Names.Add("c"); Save.Names.RemoveAt(3);
            Run(client, NullTypeJson,
                Call(Names, NamesTypeJson, "save", "Add", Text("a")),
                Call(Names, NamesTypeJson, "save", "Add", Text("b")),
                Call(Names, NamesTypeJson, "save", "Insert", Number(0), Text("first")),
                Call(Names, NamesTypeJson, "save", "RemoveAt", Number(1)),
                Call(Names, NamesTypeJson, "save", "Add", Text("c")),
                Call(Names, NamesTypeJson, "save", "RemoveAt", Number(3)));

            Assert.AreEqual(1, publications, "One execution's list mutations must publish together.");
            CollectionAssert.AreEqual(new[] { "first", "a", "b" }, StoredNames(client));
            Assert.IsFalse(client.saveValues.ContainsKey("name-0"), "A removed entry must be released.");
            CollectionAssert.DoesNotContain(StoredStrings(client), "c",
                "An entry added and removed in one execution must be released.");
        }

        [Test]
        public void RuntimeTargetedListMutationsCommitOnce()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            int publications = 0;
            client.OnWritableValuesChanged += _ => publications++;

            // As `this.Names` compiles in a method: Runtime writability, resolved from the receiver.
            // this.Names.Add("a"); this.Names.Add("b"); this.Names.RemoveAt(0);
            Run(client, NullTypeJson,
                Call(Names, NamesTypeJson, "runtime", "Add", Text("a")),
                Call(Names, NamesTypeJson, "runtime", "Add", Text("b")),
                Call(Names, NamesTypeJson, "runtime", "RemoveAt", Number(0)));

            Assert.AreEqual(1, publications, "Runtime-targeted mutations must batch like Save-targeted ones.");
            CollectionAssert.AreEqual(new[] { "a", "b" }, StoredNames(client));
        }

        [Test]
        public void LeafWriteHandlersSeeTheExecutionsPendingCollectionMutations()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            var names = client.save.Get<NeoMemberListWritable>("Names");
            var name = client.save.Get<NeoMemberClassWritable>("Part").Get<NeoMemberString>("Name");
            Func<int> namesCommits = CountCommits(client, "names");
            var order = new List<string>();
            int observedCount = -1;
            name.OnChanged += _ =>
            {
                order.Add("Name");
                observedCount = names.Count;
            };
            names.OnChanged += _ => order.Add("Names");

            // Save.Names.Add("a"); Save.Part.Name = "renamed"; Save.Names.Add("b");
            Run(client, NullTypeJson,
                Call(Names, NamesTypeJson, "save", "Add", Text("a")),
                Assign(Index(Field("save", "Part"), Text("Name")), StringTypeJson, "save", Text("renamed")),
                Call(Names, NamesTypeJson, "save", "Add", Text("b")));

            Assert.AreEqual(3, observedCount, "A handler must run after the execution's pending mutations are stored.");
            Assert.AreEqual(1, namesCommits(), "A leaf write must not store the pending list.");
            CollectionAssert.AreEquivalent(new[] { "Name", "Names" }, order);
        }

        [Test]
        public void StoredListReadsObserveEarlierMutations()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            Func<int> listCommits = CountCommits(client, "names");

            // Save.Names.Add("a"); var afterAdd = Save.Names.Count; Save.Names.Add("b");
            // return [afterAdd, Save.Names.Count];
            object? result = Run(client, ListTypeJson(NumberTypeJson),
                Call(Names, NamesTypeJson, "save", "Add", Text("a")),
                Declare("afterAdd", NumberTypeJson, Count(Names)),
                Call(Names, NamesTypeJson, "save", "Add", Text("b")),
                Return(NumberTypeJson, Var("afterAdd"), Count(Names)));

            CollectionAssert.AreEqual(new object?[] { 2d, 3d }, (object?[])result!);
            Assert.AreEqual(1, listCommits(), "A count reads the pending list without storing it.");
            CollectionAssert.AreEqual(new[] { "zero", "a", "b" }, StoredNames(client));
        }

        [Test]
        public void CaughtStoredListFailureKeepsTheExecutionsOtherMutations()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));

            // Save.Names.Add("a"); try { Save.Names.RemoveAt(5); } catch (e) { } Save.Names.Add("b");
            Run(client, NullTypeJson,
                Call(Names, NamesTypeJson, "save", "Add", Text("a")),
                "{'type':'try','instructions':[" + Call(Names, NamesTypeJson, "save", "RemoveAt", Number(5)) + "],"
                    + "'catches':[{'binding':{'id':'e','typeInfo':" + StringTypeJson + ",'readonly':true},'instructions':[]}]}",
                Call(Names, NamesTypeJson, "save", "Add", Text("b")));

            CollectionAssert.AreEqual(new[] { "zero", "a", "b" }, StoredNames(client));
        }

        [Test]
        public void ConstructedEntryStaysWritableAfterItsAdd()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));

            // var item = new Item(Name: "made"); Save.Items.Add(item); item.Name = "renamed";
            Run(client, NullTypeJson,
                Declare("item", ItemTypeJson, New("made")),
                Call(Field("save", "Items"), ItemsTypeJson, "save", "Add", Var("item")),
                Assign(Index(Var("item"), Text("Name")), StringTypeJson, "runtime", Text("renamed")));

            string[] items = ((ArrayMemberValue)client.saveValues["items"]).value!;
            Assert.AreEqual(1, items.Length);
            string nameId = ((ObjectMemberValue)client.saveValues[items[0]]).value!["Name"];
            Assert.AreEqual("renamed", ((StringMemberValue)client.saveValues[nameId]).value);
        }

        [Test]
        public void AddingOneConstructedEntryTwiceFails()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));

            // var item = new Item(Name: "made"); Save.Items.Add(item); Save.Items.Add(item);
            Assert.Catch(() => Run(client, NullTypeJson,
                Declare("item", ItemTypeJson, New("made")),
                Call(Field("save", "Items"), ItemsTypeJson, "save", "Add", Var("item")),
                Call(Field("save", "Items"), ItemsTypeJson, "save", "Add", Var("item"))));
        }

        [Test]
        public void ConstructedSessionEntriesSurviveTheExecutionsExit()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));

            // Session.Items.Add(new Item(Name: "first")); Session.Items.Add(new Item(Name: "second"));
            Run(client, NullTypeJson,
                Call(Field("session", "Items"), ItemsTypeJson, "session", "Add", New("first")),
                Call(Field("session", "Items"), ItemsTypeJson, "session", "Add", New("second")));

            Assert.IsTrue(client.TryGetValue("session-items", out ArrayMemberValue? items));
            Assert.AreEqual(2, items!.value!.Length);
            foreach (string id in items.value)
                Assert.IsTrue(client.TryGetValue(id, out ObjectMemberValue? _), $"Entry '{id}' must stay stored.");
        }

        [Test]
        public void DictionaryAliasObservesEarlierMutations()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            Func<int> dictionaryCommits = CountCommits(client, "by-key");
            string byKey = Field("save", "ByKey");

            // var alias = Save.ByKey; Save.ByKey.Add("b", "2"); alias["a"] = "1"; Save.ByKey["a"] = "3";
            // Save.ByKey.Add("c", "4"); Save.ByKey.Remove("c");
            // return [alias["a"], alias["b"], Save.ByKey["a"]];
            object? result = Run(client, ListTypeJson(StringTypeJson),
                Declare("alias", ByKeyTypeJson, byKey),
                Call(byKey, ByKeyTypeJson, "save", "Add", Text("b"), Text("2")),
                Assign(Index(Var("alias"), Text("a")), StringTypeJson, "save", Text("1")),
                Assign(Index(byKey, Text("a")), StringTypeJson, "save", Text("3")),
                Call(byKey, ByKeyTypeJson, "save", "Add", Text("c"), Text("4")),
                Call(byKey, ByKeyTypeJson, "save", "Remove", Text("c")),
                Return(StringTypeJson, Index(Var("alias"), Text("a")), Index(Var("alias"), Text("b")), Index(byKey, Text("a"))));

            CollectionAssert.AreEqual(new object?[] { "3", "2", "3" }, (object?[])result!);
            Assert.AreEqual(1, dictionaryCommits(), "Entry reads and writes must not store the pending dictionary.");
            var dictionary = client.save.Get<NeoMemberDictionaryWritable>("ByKey");
            Assert.AreEqual(2, dictionary.Count);
            CollectionAssert.DoesNotContain(StoredStrings(client), "4", "A removed entry must be released.");
        }

        [Test]
        public void DictionaryCompoundAssignmentsStayPending()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            Func<int> dictionaryCommits = CountCommits(client, "by-key");
            string byKey = Field("save", "ByKey");

            // Save.ByKey["a"] = "x"; Save.ByKey["a"] += "y"; Save.ByKey.Add("b", "2"); Save.ByKey["a"] += "z";
            Run(client, NullTypeJson,
                Assign(Index(byKey, Text("a")), StringTypeJson, "save", Text("x")),
                Assign(Index(byKey, Text("a")), StringTypeJson, "save", Plus(Index(byKey, Text("a")), Text("y")), "+="),
                Call(byKey, ByKeyTypeJson, "save", "Add", Text("b"), Text("2")),
                Assign(Index(byKey, Text("a")), StringTypeJson, "save", Plus(Index(byKey, Text("a")), Text("z")), "+="));

            Assert.AreEqual(1, dictionaryCommits(), "Compound entry writes must not store the pending dictionary.");
            var dictionary = client.save.Get<NeoMemberDictionaryWritable>("ByKey");
            Assert.AreEqual(2, dictionary.Count);
            Assert.AreEqual("xyz", ((NeoMemberString)dictionary["a"]).value!.value);
        }

        [Test]
        public void ProjectValueListMutationsStayPending()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            Func<int> listCommits = CountCommits(client, "names");
            string names = "{'type':'reference','valueId':'names'}";

            // Value("names").Add("a"); var count = Value("names").Count; Value("names").Add("b"); return [count];
            object? result = Run(client, ListTypeJson(NumberTypeJson),
                Call(names, NamesTypeJson, "save", "Add", Text("a")),
                Declare("count", NumberTypeJson, Count(names)),
                Call(names, NamesTypeJson, "save", "Add", Text("b")),
                Return(NumberTypeJson, Var("count")));

            CollectionAssert.AreEqual(new object?[] { 2d }, (object?[])result!);
            Assert.AreEqual(1, listCommits(), "A project value's mutations must not store the pending list.");
            CollectionAssert.AreEqual(new[] { "zero", "a", "b" }, StoredNames(client));
        }

        [Test]
        public void StoredListAliasMutationsStayPending()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            Func<int> listCommits = CountCommits(client, "names");

            // var alias = Save.Names; alias.Add("a"); alias.Insert(0, "first"); alias.Add("b"); alias[1] = "zz";
            Run(client, NullTypeJson,
                Declare("alias", NamesTypeJson, Names),
                Call(Var("alias"), NamesTypeJson, "save", "Add", Text("a")),
                Call(Var("alias"), NamesTypeJson, "save", "Insert", Number(0), Text("first")),
                Call(Var("alias"), NamesTypeJson, "save", "Add", Text("b")),
                Assign(Index(Var("alias"), Number(1)), StringTypeJson, "save", Text("zz")));

            Assert.AreEqual(1, listCommits(), "Mutations through an alias must not store the pending list.");
            CollectionAssert.AreEqual(new[] { "first", "zz", "a", "b" }, StoredNames(client));
        }

        [Test]
        public void StoredListIndexReadsAndWritesStayPending()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            Func<int> listCommits = CountCommits(client, "names");

            // Save.Names.Add("a"); var read = Save.Names[1]; Save.Names[0] = "zz"; Save.Names.Add("b");
            // return [read, Save.Names[0]];
            object? result = Run(client, ListTypeJson(StringTypeJson),
                Call(Names, NamesTypeJson, "save", "Add", Text("a")),
                Declare("read", StringTypeJson, Index(Names, Number(1))),
                Assign(Index(Names, Number(0)), StringTypeJson, "save", Text("zz")),
                Call(Names, NamesTypeJson, "save", "Add", Text("b")),
                Return(StringTypeJson, Var("read"), Index(Names, Number(0))));

            CollectionAssert.AreEqual(new object?[] { "a", "zz" }, (object?[])result!);
            Assert.AreEqual(1, listCommits(), "Index reads and writes must not store the pending list.");
            CollectionAssert.AreEqual(new[] { "zz", "a", "b" }, StoredNames(client));
        }

        [Test]
        public void StoredListContainsAndEntryWritesStayPending()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            Func<int> listCommits = CountCommits(client, "items");
            string items = Field("save", "Items");

            // var item = new Item(Name: "made"); Save.Items.Add(item); var has = Save.Items.Contains(item);
            // item.Name = "renamed"; Save.Items.Add(new Item(Name: "second")); return [has];
            object? result = Run(client, ListTypeJson(BoolTypeJson),
                Declare("item", ItemTypeJson, New("made")),
                Call(items, ItemsTypeJson, "save", "Add", Var("item")),
                Declare("has", BoolTypeJson, Contains(items, Var("item"))),
                Assign(Index(Var("item"), Text("Name")), StringTypeJson, "runtime", Text("renamed")),
                Call(items, ItemsTypeJson, "save", "Add", New("second")),
                Return(BoolTypeJson, Var("has")));

            CollectionAssert.AreEqual(new object?[] { true }, (object?[])result!);
            Assert.AreEqual(1, listCommits(), "Membership reads and entry writes must not store the pending list.");
            CollectionAssert.AreEqual(new[] { "renamed", "second" }, StoredItemNames(client, "items"));
        }

        [Test]
        public void MovingEntriesBetweenStoredListsStaysPending()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            Func<int> itemCommits = CountCommits(client, "items");
            Func<int> otherCommits = CountCommits(client, "others");
            string items = Field("save", "Items");
            string others = Field("save", "Others");

            // Save.Items.Add(new Item(Name: "a")); Save.Items.Add(new Item(Name: "b"));
            // var moved = Save.Items[0]; Save.Items.RemoveAt(0); Save.Others.Add(moved);
            // moved = Save.Items[0]; Save.Items.RemoveAt(0); Save.Others.Add(moved);
            Run(client, NullTypeJson,
                Call(items, ItemsTypeJson, "save", "Add", New("a")),
                Call(items, ItemsTypeJson, "save", "Add", New("b")),
                Declare("moved", ItemTypeJson, Index(items, Number(0))),
                Call(items, ItemsTypeJson, "save", "RemoveAt", Number(0)),
                Call(others, ItemsTypeJson, "save", "Add", Var("moved")),
                Assign(Var("moved"), ItemTypeJson, "local", Index(items, Number(0))),
                Call(items, ItemsTypeJson, "save", "RemoveAt", Number(0)),
                Call(others, ItemsTypeJson, "save", "Add", Var("moved")));

            Assert.AreEqual(1, itemCommits(), "Moving entries out must not store the pending list per move.");
            Assert.AreEqual(1, otherCommits(), "Moving entries in must not store the pending list per move.");
            CollectionAssert.IsEmpty(StoredItemNames(client, "items"));
            CollectionAssert.AreEqual(new[] { "a", "b" }, StoredItemNames(client, "others"));
        }

        [Test]
        public void MovingSessionEntriesIntoSaveStaysPending()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            Func<int> itemCommits = CountCommits(client, "items");
            string sessionItems = Field("session", "Items");
            string items = Field("save", "Items");

            // Session.Items.Add(new Item(Name: "a")); var moved = Session.Items[0];
            // Session.Items.RemoveAt(0); Save.Items.Add(moved); moved.Name = "renamed";
            // Save.Items.Add(new Item(Name: "b"));
            Run(client, NullTypeJson,
                Call(sessionItems, ItemsTypeJson, "session", "Add", New("a")),
                Declare("moved", ItemTypeJson, Index(sessionItems, Number(0))),
                Call(sessionItems, ItemsTypeJson, "session", "RemoveAt", Number(0)),
                Call(items, ItemsTypeJson, "save", "Add", Var("moved")),
                Assign(Index(Var("moved"), Text("Name")), StringTypeJson, "runtime", Text("renamed")),
                Call(items, ItemsTypeJson, "save", "Add", New("b")));

            Assert.AreEqual(1, itemCommits());
            CollectionAssert.AreEqual(new[] { "renamed", "b" }, StoredItemNames(client, "items"));
            Assert.IsTrue(client.TryGetValue("session-items", out ArrayMemberValue? session));
            CollectionAssert.IsEmpty(session!.value);
        }

        [Test]
        public void NativeCallsSeePendingMutations()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            Func<int> listCommits = CountCommits(client, "names");
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["count-names"] = (native, _, _) => native.save.Get<NeoMemberListWritable>("Names").Count,
            });

            // Save.Names.Add("a"); var seen = CountNames(); Save.Names.Add("b"); return [seen];
            object? result = Run(client, ListTypeJson(NumberTypeJson),
                Call(Names, NamesTypeJson, "save", "Add", Text("a")),
                Declare("seen", NumberTypeJson, CallNative("count-names")),
                Call(Names, NamesTypeJson, "save", "Add", Text("b")),
                Return(NumberTypeJson, Var("seen")));

            CollectionAssert.AreEqual(new object?[] { 2d }, (object?[])result!);
            Assert.AreEqual(2, listCommits(), "A native call stores the mutations before it, once.");
            CollectionAssert.AreEqual(new[] { "zero", "a", "b" }, StoredNames(client));
        }

        [Test]
        public void MutationsAfterAnAwaitBatchTogether()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            Func<int> listCommits = CountCommits(client, "names");
            NeoDeferredFunction<int>? pending = null;
            int seen = -1;
            client.RegisterDeferredNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
            {
                ["deferred-count"] = (native, _, _, deferred) =>
                {
                    seen = native.save.Get<NeoMemberListWritable>("Names").Count;
                    pending = NeoGeneratedTypesSupport.ResolveDeferredFunction<NeoDeferredFunction<int>>(deferred, "DeferredCount");
                },
            });

            // Save.Names.Add("a"); await DeferredCount(); Save.Names.Add("b"); Save.Names.Add("c");
            NeoScriptExecutionResult execution = Start(client, NeoScriptExecutionOptions.ForDirectFunction(client),
                Call(Names, NamesTypeJson, "save", "Add", Text("a")),
                Declare("awaited", NumberTypeJson, CallNative("deferred-count")),
                Call(Names, NamesTypeJson, "save", "Add", Text("b")),
                Call(Names, NamesTypeJson, "save", "Add", Text("c")));
            Assert.IsTrue(execution.IsPaused);
            Assert.AreEqual(2, seen, "The deferred call sees the mutations before it.");
            bool settled = false;
            execution.WhenDeferredSettled(_ => settled = true, error => throw error);
            pending!.Complete(1);

            Assert.IsTrue(settled);
            Assert.AreEqual(2, listCommits(), "The resumed continuation's mutations must commit together.");
            CollectionAssert.AreEqual(new[] { "zero", "a", "b", "c" }, StoredNames(client));
        }

        [Test]
        public void NestedEntryReadsAndWritesStayPending()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            Func<int> itemCommits = CountCommits(client, "items");
            string items = Field("save", "Items");
            string firstName = Index(Index(items, Number(0)), Text("Name"));

            // Save.Items.Add(new Item(Name: "a")); var read = Save.Items[0].Name;
            // Save.Items[0].Name = "renamed"; Save.Items.Add(new Item(Name: "b")); return [read];
            object? result = Run(client, ListTypeJson(StringTypeJson),
                Call(items, ItemsTypeJson, "save", "Add", New("a")),
                Declare("read", StringTypeJson, firstName),
                Assign(firstName, StringTypeJson, "save", Text("renamed")),
                Call(items, ItemsTypeJson, "save", "Add", New("b")),
                Return(StringTypeJson, Var("read")));

            CollectionAssert.AreEqual(new object?[] { "a" }, (object?[])result!);
            Assert.AreEqual(1, itemCommits(), "An entry's field must read and write through the pending list.");
            CollectionAssert.AreEqual(new[] { "renamed", "b" }, StoredItemNames(client, "items"));
        }

        [Test]
        public void DictionaryEntryListMutationsStayPending()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            Func<int> groupCommits = CountCommits(client, "groups");
            Func<int> entryCommits = CountCommits(client, "group-k");
            string groups = Field("save", "Groups");
            string group = Index(groups, Text("k"));

            // Save.Groups.Remove("j"); Save.Groups["k"].Add("a"); Save.Groups["k"].Add("b");
            // return [Save.Groups["k"].Count];
            object? result = Run(client, ListTypeJson(NumberTypeJson),
                Call(groups, GroupsTypeJson, "save", "Remove", Text("j")),
                Call(group, NamesTypeJson, "save", "Add", Text("a")),
                Call(group, NamesTypeJson, "save", "Add", Text("b")),
                Return(NumberTypeJson, Count(group)));

            CollectionAssert.AreEqual(new object?[] { 2d }, (object?[])result!);
            Assert.AreEqual(1, groupCommits(), "An entry's mutations must not store the pending dictionary.");
            Assert.AreEqual(1, entryCommits());
            CollectionAssert.AreEqual(new[] { "k" }, ((ObjectMemberValue)client.saveValues["groups"]).value!.Keys);
            CollectionAssert.AreEqual(new[] { "a", "b" }, ((ArrayMemberValue)client.saveValues["group-k"]).value!
                .Select(id => ((StringMemberValue)client.saveValues[id]).value));
        }

        [Test]
        public void PendingCollectionsMoveWithTheirEntry()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            string sessionItems = Field("session", "Items");
            string items = Field("save", "Items");
            string sessionTags = Index(Index(sessionItems, Number(0)), Text("Tags"));

            // Session.Items.Add(new Item(Name: "a")); Session.Items[0].Tags.Add("x"); Session.Items[0].Notes["j"] = "w";
            // var moved = Session.Items[0]; Session.Items.RemoveAt(0); Save.Items.Add(moved); moved.Tags.Add("y"); moved.Notes["k"] = "v";
            // Session.Items.Add(new Item(Name: "b")); Session.Items[0].Tags.Add("z"); Save.Items.Add(Session.Items[0]);
            Run(client, NullTypeJson,
                Call(sessionItems, ItemsTypeJson, "session", "Add", New("a")),
                Call(sessionTags, NamesTypeJson, "session", "Add", Text("x")),
                Assign(Index(Index(Index(sessionItems, Number(0)), Text("Notes")), Text("j")), StringTypeJson, "session", Text("w")),
                Declare("moved", ItemTypeJson, Index(sessionItems, Number(0))),
                Call(sessionItems, ItemsTypeJson, "session", "RemoveAt", Number(0)),
                Call(items, ItemsTypeJson, "save", "Add", Var("moved")),
                Call(Index(Var("moved"), Text("Tags")), NamesTypeJson, "runtime", "Add", Text("y")),
                Assign(Index(Index(Var("moved"), Text("Notes")), Text("k")), StringTypeJson, "runtime", Text("v")),
                Call(sessionItems, ItemsTypeJson, "session", "Add", New("b")),
                Call(sessionTags, NamesTypeJson, "session", "Add", Text("z")),
                Call(items, ItemsTypeJson, "save", "Add", Index(sessionItems, Number(0))));

            string[] stored = ((ArrayMemberValue)client.saveValues["items"]).value!;
            Assert.AreEqual(2, stored.Length);
            CollectionAssert.AreEqual(new[] { "x", "y" }, StoredTags(client, stored[0]), "A moved entry keeps its list's pending mutations.");
            var notes = (ObjectMemberValue)client.saveValues[((ObjectMemberValue)client.saveValues[stored[0]]).value!["Notes"]];
            CollectionAssert.AreEquivalent(new[] { "j", "k" }, notes.value!.Keys, "A moved dictionary takes writes in its new store.");
            Assert.AreEqual("v", ((StringMemberValue)client.saveValues[notes.value["k"]]).value);
            CollectionAssert.AreEqual(new[] { "z" }, StoredTags(client, stored[1]), "A copied entry copies its list's pending mutations.");
        }

        [Test]
        public void MovedPendingEntriesKeepTheirParent()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            string sessionItems = Field("session", "Items");
            string children = Index(Index(sessionItems, Number(0)), Text("Children"));

            // Session.Items.Add(new Item(Name: "a")); Session.Items[0].Children.Add(new Item(Name: "c"));
            // var child = Session.Items[0].Children[0]; var moved = Session.Items[0]; Session.Items.RemoveAt(0);
            // Save.Items.Add(moved); Save.Others.Add(child);
            Exception error = Assert.Catch(() => Run(client, NullTypeJson,
                Call(sessionItems, ItemsTypeJson, "session", "Add", New("a")),
                Call(children, ItemsTypeJson, "session", "Add", New("c")),
                Declare("child", ItemTypeJson, Index(children, Number(0))),
                Declare("moved", ItemTypeJson, Index(sessionItems, Number(0))),
                Call(sessionItems, ItemsTypeJson, "session", "RemoveAt", Number(0)),
                Call(Field("save", "Items"), ItemsTypeJson, "save", "Add", Var("moved")),
                Call(Field("save", "Others"), ItemsTypeJson, "save", "Add", Var("child"))));
            StringAssert.Contains("already owned", error.ToString());
        }

        [Test]
        public void SameIdCopiesKeepTheirOwnPendingCollections()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            string items = Field("save", "Items");
            string sessionItems = Field("session", "Items");

            // Save.Items.Add(new Item(Name: "a")); Save.Items[0].Tags.Add("a");
            // Session.Items.Add(Save.Items[0]); Session.Items[0].Tags.Add("b");
            Run(client, NullTypeJson,
                Call(items, ItemsTypeJson, "save", "Add", New("a")),
                Call(Index(Index(items, Number(0)), Text("Tags")), NamesTypeJson, "save", "Add", Text("a")),
                Call(sessionItems, ItemsTypeJson, "session", "Add", Index(items, Number(0))),
                Call(Index(Index(sessionItems, Number(0)), Text("Tags")), NamesTypeJson, "session", "Add", Text("b")));

            string saveItem = ((ArrayMemberValue)client.saveValues["items"]).value!.Single();
            string sessionItem = ((ArrayMemberValue)client.sessionValues["session-items"]).value!.Single();
            Assert.AreEqual(saveItem, sessionItem, "A Session copy of a Save graph keeps its ids.");
            CollectionAssert.AreEqual(new[] { "a" }, StoredTags(client, saveItem));
            string sessionTags = ((ObjectMemberValue)client.sessionValues[sessionItem]).value!["Tags"];
            CollectionAssert.AreEqual(new[] { "a", "b" }, ((ArrayMemberValue)client.sessionValues[sessionTags]).value!
                .Select(id => ((StringMemberValue)client.sessionValues[id]).value));
        }

        [Test]
        public void ReplacingAPendingCollectionDropsItsMutations()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            string group = Index(Field("save", "Groups"), Text("k"));
            string items = Field("save", "Items");
            string tags = Index(Index(items, Number(0)), Text("Tags"));

            // Save.Groups["k"].Add("g"); Save.Groups["k"] = ["x"]; Save.Groups["n"] = ["z"];
            // Save.Items.Add(new Item(Name: "a")); Save.Items[0].Tags.Add("t"); Save.Items[0].Tags = [];
            Run(client, NullTypeJson,
                Call(group, NamesTypeJson, "save", "Add", Text("g")),
                Assign(group, NamesTypeJson, "save", ListLiteral(NamesTypeJson, Text("x"))),
                Assign(Index(Field("save", "Groups"), Text("n")), NamesTypeJson, "save", ListLiteral(NamesTypeJson, Text("z"))),
                Call(items, ItemsTypeJson, "save", "Add", New("a")),
                Call(tags, NamesTypeJson, "save", "Add", Text("t")),
                Assign(tags, NamesTypeJson, "save", ListLiteral(NamesTypeJson)));

            Dictionary<string, string> groups = ((ObjectMemberValue)client.saveValues["groups"]).value!;
            CollectionAssert.AreEqual(new[] { "x" }, StoredList(client, groups["k"]));
            CollectionAssert.AreEqual(new[] { "z" }, StoredList(client, groups["n"]));
            CollectionAssert.IsEmpty(StoredTags(client, ((ArrayMemberValue)client.saveValues["items"]).value!.Single()));
            Assert.IsFalse(client.saveValues.Values.OfType<StringMemberValue>().Any(value => value.value is "g" or "t"),
                "A replaced pending collection's entries must not be stored.");
        }

        [Test]
        public void ReplacingAnEntryDropsItsPendingCollections()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            string entry = Index(Field("save", "Nests"), Text("a"));

            // Save.Nests["a"][0].Add("t"); Save.Nests["a"] = [];
            Run(client, NullTypeJson,
                Call(Index(entry, Number(0)), NamesTypeJson, "save", "Add", Text("t")),
                Assign(entry, ListTypeJson(NamesTypeJson), "save", ListLiteral(ListTypeJson(NamesTypeJson))));

            CollectionAssert.IsEmpty(((ArrayMemberValue)client.saveValues["nests-a"]).value!);
            Assert.IsFalse(client.saveValues.ContainsKey("nests-a-0"), "A replaced entry's pending list must not be stored again.");
            Assert.IsFalse(client.saveValues.Values.OfType<StringMemberValue>().Any(value => value.value == "t"));
        }

        [Test]
        public void StaticCollectionReadsStayPending()
        {
            var schema = Schema(false);
            schema.members["static-names"] = new ListMember
            {
                id = "static-names",
                name = "StaticNames",
                kind = MemberKind.List,
                entryMemberId = "names-entry",
                Storage = NeoMemberStorage.Save,
                Modifier = NeoMemberModifierKind.Static,
            };
            schema.classes["save"].schema["StaticNames"] = "static-names";
            using var client = NeoTestSaveStack.ClientFromSchema(schema);
            const string names = "{'type':'staticMember','memberId':'static-names'}";
            Run(client, NullTypeJson, Call(names, NamesTypeJson, "save", "Add", Text("a")));
            int publications = 0;
            client.OnWritableValuesChanged += _ =>
            {
                publications++;
            };

            // StaticNames.Add("b"); var seen = StaticNames.Count; StaticNames.Add("c"); return [seen];
            object? result = Run(client, ListTypeJson(NumberTypeJson),
                Call(names, NamesTypeJson, "save", "Add", Text("b")),
                Declare("seen", NumberTypeJson, Count(names)),
                Call(names, NamesTypeJson, "save", "Add", Text("c")),
                Return(NumberTypeJson, Var("seen")));

            CollectionAssert.AreEqual(new object?[] { 2d }, (object?[])result!);
            Assert.AreEqual(1, publications, "A static collection's count must read its pending mutations.");
        }

        [Test]
        public void HandlersWaitForTheOutermostExit()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            var names = client.save.Get<NeoMemberListWritable>("Names");
            var name = client.save.Get<NeoMemberClassWritable>("Part").Get<NeoMemberStringWritable>("Name");
            var counts = new List<int>();
            name.OnChanged += _ =>
            {
                counts.Add(names.Count);
                if (counts.Count == 1)
                    name.Set("handled");
            };
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["count-names"] = (native, _, _) =>
                {
                    names.AddSerialized(NeoValueWritePayload.FromValue("c"));
                    return names.Count;
                },
            });

            // Save.Names.Add("a"); Save.Part.Name = "renamed"; var seen = CountNames(); Save.Names.Add("b");
            Run(client, NullTypeJson,
                Call(Names, NamesTypeJson, "save", "Add", Text("a")),
                Assign(Index(Field("save", "Part"), Text("Name")), StringTypeJson, "save", Text("renamed")),
                Declare("seen", NumberTypeJson, CallNative("count-names")),
                Call(Names, NamesTypeJson, "save", "Add", Text("b")));

            CollectionAssert.AreEqual(new[] { 4, 4 }, counts, "A handler runs once the execution exits, never inside a C# write it called.");
            CollectionAssert.AreEqual(new[] { "zero", "a", "c", "b" }, StoredNames(client));
            Assert.AreEqual("handled", name.Text);
        }

        [Test]
        public void NativeViewsSeePendingMutations()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["count-names"] = (native, _, _) =>
                {
                    var view = new NeoReadOnlyList<string?>(native, native.save.Get<NeoMemberListWritable>("Names"),
                        (_, child) => ((NeoMemberString)child).Text);
                    int seen = 0;
                    foreach (string? _ in view)
                        seen++;
                    return seen;
                },
            });

            // Save.Names.Add("a"); return [CountNames()];
            object? result = Run(client, ListTypeJson(NumberTypeJson),
                Call(Names, NamesTypeJson, "save", "Add", Text("a")),
                Return(NumberTypeJson, CallNative("count-names")));

            CollectionAssert.AreEqual(new object?[] { 2d }, (object?[])result!);
        }

        [Test]
        public void MutationsInACatchAfterAFailedAwaitBatchTogether()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            Func<int> listCommits = CountCommits(client, "names");
            NeoDeferredFunction<int>? pending = null;
            client.RegisterDeferredNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
            {
                ["deferred-count"] = (_, _, _, deferred) =>
                    pending = NeoGeneratedTypesSupport.ResolveDeferredFunction<NeoDeferredFunction<int>>(deferred, "DeferredCount"),
            });

            // Save.Names.Add("a"); try { await DeferredCount(); } catch (e) { Save.Names.Add("b"); Save.Names.Add("c"); }
            NeoScriptExecutionResult execution = Start(client, NeoScriptExecutionOptions.ForDirectFunction(client),
                Call(Names, NamesTypeJson, "save", "Add", Text("a")),
                "{'type':'try','instructions':[" + Declare("awaited", NumberTypeJson, CallNative("deferred-count")) + "],"
                    + "'catches':[{'binding':{'id':'e','typeInfo':" + StringTypeJson + ",'readonly':true},'instructions':["
                    + Call(Names, NamesTypeJson, "save", "Add", Text("b")) + ","
                    + Call(Names, NamesTypeJson, "save", "Add", Text("c")) + "]}]}");
            Assert.IsTrue(execution.IsPaused);
            bool settled = false;
            execution.WhenDeferredSettled(_ => settled = true, error => throw error);
            pending!.Fail(new NSGetterRuntimeError("failed"));

            Assert.IsTrue(settled);
            Assert.AreEqual(2, listCommits(), "The catch's mutations must commit together.");
            CollectionAssert.AreEqual(new[] { "zero", "a", "b", "c" }, StoredNames(client));
        }

        private const string NullTypeJson = "{'type':0,'required':true}";
        private const string NumberTypeJson = "{'type':2,'required':true}";
        private const string BoolTypeJson = "{'type':1,'required':true}";
        private const string StringTypeJson = "{'type':3,'required':true}";
        private const string ItemTypeJson = "{'type':7,'required':true,'classId':'item'}";
        private const string NamesTypeJson = "{'type':6,'required':true,'entryTypeInfo':" + StringTypeJson + "}";
        private const string ItemsTypeJson = "{'type':6,'required':true,'entryTypeInfo':" + ItemTypeJson + "}";
        private const string ByKeyTypeJson = "{'type':5,'required':true,'entryTypeInfo':" + StringTypeJson + "}";
        private const string GroupsTypeJson = "{'type':5,'required':true,'entryTypeInfo':" + NamesTypeJson + "}";
        private static readonly string Names = Field("save", "Names");

        private static string ListTypeJson(string entry) => "{'type':6,'required':true,'entryTypeInfo':" + entry + "}";

        private static string Text(string value) => "{'type':'value','value':{'typeInfo':" + StringTypeJson + ",'value':'" + value + "'}}";

        private static string Number(int value) => "{'type':'value','value':{'typeInfo':" + NumberTypeJson + ",'value':" + value + "}}";

        private static string Var(string id) => "{'type':'variable','variableId':'" + id + "'}";

        private static string Index(string pointer, string key) => "{'type':'keyOf','keyOf':{'pointer':" + pointer + ",'key':" + key + "}}";

        private static string Field(string valueId, string key) => Index("{'type':'reference','valueId':'" + valueId + "'}", Text(key));

        private static string Count(string pointer) => "{'type':'function','function':{'type':'count','info':{'collectionPointer':" + pointer + "}}}";

        private static string Plus(string left, string right) =>
            "{'type':'operation','operation':{'type':'arithmetic','arithmetic':{'type':'+','pointers':[" + left + "," + right + "]}}}";

        private static string Contains(string collection, string value) =>
            "{'type':'function','function':{'type':'contains','info':{'collectionPointer':" + collection + ",'valuePointer':" + value + "}}}";

        private static string CallNative(string id) =>
            "{'type':'callFunction','memberId':'" + id + "','receiver':{'kind':'static','memberId':'" + id + "'},'args':[],'callSiteId':'" + id + "-site'}";

        private static string New(string name) =>
            "{'type':'function','function':{'type':'classConstructor','info':{'schemaClassInfo':" + ItemTypeJson
            + ",'fields':[{'schemaKey':'Name','memberId':'name','valuePointer':" + Text(name) + "},"
            + "{'schemaKey':'Tags','memberId':'tags-member','valuePointer':" + ListLiteral(NamesTypeJson) + "},"
            + "{'schemaKey':'Notes','memberId':'notes-member','valuePointer':{'type':'dictLiteral','typeInfo':" + ByKeyTypeJson + ",'entries':[]}},"
            + "{'schemaKey':'Children','memberId':'children-member','valuePointer':" + ListLiteral(ItemsTypeJson) + "}]}}}";

        private static string ListLiteral(string type, params string[] entries) =>
            "{'type':'listLiteral','typeInfo':" + type + ",'entries':[" + string.Join(",", entries) + "]}";

        private static string Declare(string id, string type, string pointer) =>
            "{'type':'variable','variable':{'id':'" + id + "','typeInfo':" + type + ",'pointer':" + pointer + "}}";

        private static string Call(string target, string type, string writability, string mutation, params string[] args) =>
            "{'type':'collectionCall','target':{'pointer':" + target + ",'typeInfo':" + type + ",'writability':'" + writability + "'},"
            + "'mutation':'" + mutation + "','args':[" + string.Join(",", args) + "]}";

        private static string Assign(string target, string type, string writability, string value, string op = "=") =>
            "{'type':'assign','target':{'pointer':" + target + ",'typeInfo':" + type + ",'writability':'" + writability + "'},"
            + "'operator':'" + op + "','pointer':" + value + "}";

        private static string Return(string entryType, params string[] entries) =>
            "{'type':'return','pointer':{'type':'listLiteral','typeInfo':" + ListTypeJson(entryType) + ",'entries':[" + string.Join(",", entries) + "]}}";

        private static object? Run(NeoClient client, string returnType, params string[] instructions)
        {
            NeoScriptExecutionResult result = Start(client, returnType, null, instructions);
            if (result.IsFailed)
                throw result.Failure!;
            return result.ReturnValue;
        }

        private static NeoScriptExecutionResult Start(NeoClient client, NeoScriptExecutionOptions options, params string[] instructions) =>
            Start(client, NullTypeJson, options, instructions);

        private static NeoScriptExecutionResult Start(NeoClient client, string returnType, NeoScriptExecutionOptions? options, string[] instructions)
        {
            var body = JsonConvert.DeserializeObject<FunctionWithReturnType>(
                "{'compilerRevision':" + FunctionWithReturnType.CurrentCompilerRevision + ",'parameters':[],'typeInfo':" + returnType
                + ",'instructions':[" + string.Join(",", instructions) + "]}")!;
            return NeoScriptExecutor.Execute(
                client, body, new Dictionary<string, object?>(), new NSGetterEvaluator.Context(client, null, null), options);
        }

        /// <summary>
        /// Counts the commits that store a change to row <paramref name="id"/>.
        /// A silent one only shadows an authored row its entry's write needs.
        /// </summary>
        private static Func<int> CountCommits(NeoClient client, string id)
        {
            int commits = 0;
            client.OnWritableValuesPublished += (_, plan) =>
            {
                var key = (NeoValueOwnership.Save, id);
                if (plan.Rows.ContainsKey(key) && !plan.IsSilent(key))
                    commits++;
            };
            return () => commits;
        }

        private static IEnumerable<string?> StoredNames(NeoClient client) =>
            ((ArrayMemberValue)client.saveValues["names"]).value!.Select(id =>
            {
                Assert.IsTrue(client.TryGetValue(id, out StringMemberValue? name), $"Entry '{id}' must be stored.");
                return name!.value;
            }).ToArray();

        private static IEnumerable<string?> StoredItemNames(NeoClient client, string listId) =>
            ((ArrayMemberValue)client.saveValues[listId]).value!.Select(id =>
            {
                string nameId = ((ObjectMemberValue)client.saveValues[id]).value!["Name"];
                return ((StringMemberValue)client.saveValues[nameId]).value;
            }).ToArray();

        private static IEnumerable<string?> StoredTags(NeoClient client, string itemId)
        {
            return StoredList(client, ((ObjectMemberValue)client.saveValues[itemId]).value!["Tags"]);
        }

        private static IEnumerable<string?> StoredList(NeoClient client, string listId) =>
            ((ArrayMemberValue)client.saveValues[listId]).value!
                .Select(id => ((StringMemberValue)client.saveValues[id]).value).ToArray();

        private static IEnumerable<string?> StoredStrings(NeoClient client) =>
            client.saveValues.Values.OfType<StringMemberValue>().Select(value => value.value).ToArray();

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
                    ["save"] = new NeoSchemaClass { id = "save", name = "Save", schema = new() { ["Items"] = "items-member", ["ByKey"] = "by-key-member", ["Names"] = "names-member", ["Part"] = "part-member", ["Others"] = "others-member", ["Groups"] = "groups-member", ["Nests"] = "nests-member", ["CountNames"] = "count-names", ["DeferredCount"] = "deferred-count" } },
                    ["item"] = new NeoSchemaClass { id = "item", name = "Item", schema = new() { ["Name"] = "name", ["Tags"] = "tags-member", ["Notes"] = "notes-member", ["Children"] = "children-member" } },
                    ["session"] = new NeoSchemaClass { id = "session", name = "Session", schema = new() { ["Items"] = "items-member" } },
                },
                members = new()
                {
                    ["assets-root"] = Root("assets-root", "assets", "empty"),
                    ["save-root"] = Root("save-root", "save", "save"),
                    ["session-root"] = Root("session-root", "session", "session"),
                    ["items-member"] = new ListMember { id = "items-member", name = "Items", kind = MemberKind.List, entryMemberId = "entry", ListKind = unordered ? NeoListKind.Unordered : NeoListKind.Ordered, Requirement = NeoMemberRequirementKind.Required },
                    ["entry"] = new ClassMember { id = "entry", name = "Item", kind = MemberKind.Class, classId = "item", Requirement = NeoMemberRequirementKind.Required },
                    ["name"] = new StringMember { id = "name", name = "Name", kind = MemberKind.String, Requirement = NeoMemberRequirementKind.Required },
                    ["by-key-member"] = new DictionaryMember { id = "by-key-member", name = "ByKey", kind = MemberKind.Dictionary, entryMemberId = "by-key-entry", KeyKind = NeoDictionaryKeyKind.String, Requirement = NeoMemberRequirementKind.Optional },
                    ["by-key-entry"] = new StringMember { id = "by-key-entry", name = "Entry", kind = MemberKind.String, Requirement = NeoMemberRequirementKind.Required },
                    ["names-member"] = new ListMember { id = "names-member", name = "Names", kind = MemberKind.List, entryMemberId = "names-entry", Requirement = NeoMemberRequirementKind.Required },
                    ["names-entry"] = new StringMember { id = "names-entry", name = "Name", kind = MemberKind.String, Requirement = NeoMemberRequirementKind.Required },
                    ["tags-member"] = new ListMember { id = "tags-member", name = "Tags", kind = MemberKind.List, entryMemberId = "names-entry", Requirement = NeoMemberRequirementKind.Required },
                    ["children-member"] = new ListMember { id = "children-member", name = "Children", kind = MemberKind.List, entryMemberId = "entry", Requirement = NeoMemberRequirementKind.Required },
                    ["notes-member"] = new DictionaryMember { id = "notes-member", name = "Notes", kind = MemberKind.Dictionary, entryMemberId = "by-key-entry", KeyKind = NeoDictionaryKeyKind.String, Requirement = NeoMemberRequirementKind.Required },
                    ["nests-member"] = new DictionaryMember { id = "nests-member", name = "Nests", kind = MemberKind.Dictionary, entryMemberId = "nests-entry", KeyKind = NeoDictionaryKeyKind.String, Requirement = NeoMemberRequirementKind.Required },
                    ["nests-entry"] = new ListMember { id = "nests-entry", name = "Nest", kind = MemberKind.List, entryMemberId = "groups-entry", Requirement = NeoMemberRequirementKind.Required },
                    ["groups-member"] = new DictionaryMember { id = "groups-member", name = "Groups", kind = MemberKind.Dictionary, entryMemberId = "groups-entry", KeyKind = NeoDictionaryKeyKind.String, Requirement = NeoMemberRequirementKind.Required },
                    ["groups-entry"] = new ListMember { id = "groups-entry", name = "Group", kind = MemberKind.List, entryMemberId = "names-entry", Requirement = NeoMemberRequirementKind.Required },
                    ["others-member"] = new ListMember { id = "others-member", name = "Others", kind = MemberKind.List, entryMemberId = "entry", Requirement = NeoMemberRequirementKind.Required },
                    ["count-names"] = NativeFunction("count-names", "CountNames", NeoFunctionDispatchKind.Synchronous),
                    ["deferred-count"] = NativeFunction("deferred-count", "DeferredCount", NeoFunctionDispatchKind.Asynchronous),
                    ["part-member"] = new ClassMember { id = "part-member", name = "Part", kind = MemberKind.Class, classId = "item", Requirement = NeoMemberRequirementKind.Optional },
                },
                values = new()
                {
                    ["assets"] = new ObjectMemberValue { id = "assets", classId = "empty", value = new() },
                    ["save"] = new ObjectMemberValue { id = "save", classId = "save", value = new() { ["Items"] = "items", ["ByKey"] = "by-key", ["Names"] = "names", ["Part"] = "part", ["Others"] = "others", ["Groups"] = "groups", ["Nests"] = "nests" } },
                    ["session"] = new ObjectMemberValue { id = "session", classId = "session", value = new() { ["Items"] = "session-items" } },
                    ["session-items"] = new ArrayMemberValue { id = "session-items", value = Array.Empty<string>() },
                    ["items"] = new ArrayMemberValue { id = "items", value = Array.Empty<string>() },
                    ["by-key"] = new ObjectMemberValue { id = "by-key", value = new() },
                    ["others"] = new ArrayMemberValue { id = "others", value = Array.Empty<string>() },
                    ["nests"] = new ObjectMemberValue { id = "nests", value = new() { ["a"] = "nests-a" } },
                    ["nests-a"] = new ArrayMemberValue { id = "nests-a", value = new[] { "nests-a-0" } },
                    ["nests-a-0"] = new ArrayMemberValue { id = "nests-a-0", value = Array.Empty<string>() },
                    ["groups"] = new ObjectMemberValue { id = "groups", value = new() { ["k"] = "group-k", ["j"] = "group-j" } },
                    ["group-k"] = new ArrayMemberValue { id = "group-k", value = Array.Empty<string>() },
                    ["group-j"] = new ArrayMemberValue { id = "group-j", value = Array.Empty<string>() },
                    ["names"] = new ArrayMemberValue { id = "names", value = new[] { "name-0" } },
                    ["name-0"] = new StringMemberValue { id = "name-0", value = "zero" },
                    ["part"] = new ObjectMemberValue { id = "part", classId = "item", value = new() { ["Name"] = "part-name" } },
                    ["part-name"] = new StringMemberValue { id = "part-name", value = "part" },
                },
                enums = new(),
            };
        }

        private static FunctionMember NativeFunction(string id, string name, NeoFunctionDispatchKind dispatch) => new()
        {
            id = id,
            name = name,
            kind = MemberKind.Function,
            Requirement = NeoMemberRequirementKind.Optional,
            returnTypeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
            argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
            Dispatch = dispatch,
            Modifier = NeoMemberModifierKind.Static,
        };

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
