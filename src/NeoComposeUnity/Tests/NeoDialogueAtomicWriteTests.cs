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
        public void StoredListReadsObserveEarlierMutations()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(Schema(false));
            int publications = 0;
            client.OnWritableValuesChanged += _ => publications++;

            // Save.Names.Add("a"); var afterAdd = Save.Names.Count; Save.Names.Add("b");
            // return [afterAdd, Save.Names.Count];
            object? result = Run(client, ListTypeJson(NumberTypeJson),
                Call(Names, NamesTypeJson, "save", "Add", Text("a")),
                Declare("afterAdd", NumberTypeJson, Count(Names)),
                Call(Names, NamesTypeJson, "save", "Add", Text("b")),
                Return(NumberTypeJson, Var("afterAdd"), Count(Names)));

            CollectionAssert.AreEqual(new object?[] { 2d, 3d }, (object?[])result!);
            Assert.AreEqual(2, publications, "A read commits the mutations before it.");
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
            int publications = 0;
            client.OnWritableValuesChanged += _ => publications++;
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
            Assert.AreEqual(1, publications, "The mutations before the first read must publish together.");
            var dictionary = client.save.Get<NeoMemberDictionaryWritable>("ByKey");
            Assert.AreEqual(2, dictionary.Count);
            CollectionAssert.DoesNotContain(StoredStrings(client), "4", "A removed entry must be released.");
        }

        private const string NullTypeJson = "{'type':0,'required':true}";
        private const string NumberTypeJson = "{'type':2,'required':true}";
        private const string StringTypeJson = "{'type':3,'required':true}";
        private const string ItemTypeJson = "{'type':7,'required':true,'classId':'item'}";
        private const string NamesTypeJson = "{'type':6,'required':true,'entryTypeInfo':" + StringTypeJson + "}";
        private const string ItemsTypeJson = "{'type':6,'required':true,'entryTypeInfo':" + ItemTypeJson + "}";
        private const string ByKeyTypeJson = "{'type':5,'required':true,'entryTypeInfo':" + StringTypeJson + "}";
        private static readonly string Names = Field("save", "Names");

        private static string ListTypeJson(string entry) => "{'type':6,'required':true,'entryTypeInfo':" + entry + "}";

        private static string Text(string value) => "{'type':'value','value':{'typeInfo':" + StringTypeJson + ",'value':'" + value + "'}}";

        private static string Number(int value) => "{'type':'value','value':{'typeInfo':" + NumberTypeJson + ",'value':" + value + "}}";

        private static string Var(string id) => "{'type':'variable','variableId':'" + id + "'}";

        private static string Index(string pointer, string key) => "{'type':'keyOf','keyOf':{'pointer':" + pointer + ",'key':" + key + "}}";

        private static string Field(string valueId, string key) => Index("{'type':'reference','valueId':'" + valueId + "'}", Text(key));

        private static string Count(string pointer) => "{'type':'function','function':{'type':'count','info':{'collectionPointer':" + pointer + "}}}";

        private static string New(string name) =>
            "{'type':'function','function':{'type':'classConstructor','info':{'schemaClassInfo':" + ItemTypeJson
            + ",'fields':[{'schemaKey':'Name','memberId':'name','valuePointer':" + Text(name) + "}]}}}";

        private static string Declare(string id, string type, string pointer) =>
            "{'type':'variable','variable':{'id':'" + id + "','typeInfo':" + type + ",'pointer':" + pointer + "}}";

        private static string Call(string target, string type, string writability, string mutation, params string[] args) =>
            "{'type':'collectionCall','target':{'pointer':" + target + ",'typeInfo':" + type + ",'writability':'" + writability + "'},"
            + "'mutation':'" + mutation + "','args':[" + string.Join(",", args) + "]}";

        private static string Assign(string target, string type, string writability, string value) =>
            "{'type':'assign','target':{'pointer':" + target + ",'typeInfo':" + type + ",'writability':'" + writability + "'},"
            + "'operator':'=','pointer':" + value + "}";

        private static string Return(string entryType, params string[] entries) =>
            "{'type':'return','pointer':{'type':'listLiteral','typeInfo':" + ListTypeJson(entryType) + ",'entries':[" + string.Join(",", entries) + "]}}";

        private static object? Run(NeoClient client, string returnType, params string[] instructions)
        {
            var body = JsonConvert.DeserializeObject<FunctionWithReturnType>(
                "{'compilerRevision':" + FunctionWithReturnType.CurrentCompilerRevision + ",'parameters':[],'typeInfo':" + returnType
                + ",'instructions':[" + string.Join(",", instructions) + "]}")!;
            NeoScriptExecutionResult result = NeoScriptExecutor.Execute(
                client, body, new Dictionary<string, object?>(), new NSGetterEvaluator.Context(client, null, null));
            if (result.IsFailed)
                throw result.Failure!;
            return result.ReturnValue;
        }

        private static IEnumerable<string?> StoredNames(NeoClient client) =>
            ((ArrayMemberValue)client.saveValues["names"]).value!.Select(id =>
            {
                Assert.IsTrue(client.TryGetValue(id, out StringMemberValue? name), $"Entry '{id}' must be stored.");
                return name!.value;
            }).ToArray();

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
                    ["save"] = new NeoSchemaClass { id = "save", name = "Save", schema = new() { ["Items"] = "items-member", ["ByKey"] = "by-key-member", ["Names"] = "names-member", ["Part"] = "part-member" } },
                    ["item"] = new NeoSchemaClass { id = "item", name = "Item", schema = new() { ["Name"] = "name" } },
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
                    ["part-member"] = new ClassMember { id = "part-member", name = "Part", kind = MemberKind.Class, classId = "item", Requirement = NeoMemberRequirementKind.Optional },
                },
                values = new()
                {
                    ["assets"] = new ObjectMemberValue { id = "assets", classId = "empty", value = new() },
                    ["save"] = new ObjectMemberValue { id = "save", classId = "save", value = new() { ["Items"] = "items", ["ByKey"] = "by-key", ["Names"] = "names", ["Part"] = "part" } },
                    ["session"] = new ObjectMemberValue { id = "session", classId = "session", value = new() { ["Items"] = "session-items" } },
                    ["session-items"] = new ArrayMemberValue { id = "session-items", value = Array.Empty<string>() },
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
