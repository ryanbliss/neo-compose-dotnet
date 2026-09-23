// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// Unordered List members (listKind: "unordered"): the stored value is
    /// only the null-vs-present discriminator; membership is the set of live
    /// rows carrying the list value's id as their containerId, layered like
    /// the value overlay (authored + save/session joins, tombstones subtract).
    /// </summary>
    public class NeoUnorderedListTests
    {
        private const string BagClassId = "bag-class";
        private const string ItemClassId = "item-class";
        private const string ItemsListValueId = "bag-items-list";
        private const string NullItemsListValueId = "bag-null-items-list";

        [TestCase(NeoMemberStorage.Save, NeoValueOwnership.Save)]
        [TestCase(NeoMemberStorage.Session, NeoValueOwnership.Session)]
        [TestCase(NeoMemberStorage.Immutable, NeoValueOwnership.Asset)]
        public void AuthoredUnorderedEntriesInheritPlacementOwnership(
            NeoMemberStorage storage, NeoValueOwnership expected)
        {
            var data = BuildProjectData();
            data.members["items-member"].Storage = storage;
            var name = new StringMember
            {
                id = "item-name", name = "Name", projectId = "project-a", kind = MemberKind.String,
            };
            data.members[name.id] = name;
            data.classes[ItemClassId].schema["Name"] = name.id;
            ((ObjectMemberValue)data.values["item-a"]).value!["Name"] = "item-a-name";
            data.values["item-a-name"] = new StringMemberValue { id = "item-a-name", value = "Before" };
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            Assert.IsTrue(client.TryGetValueOwnership("item-a", out var entryOwnership));
            Assert.AreEqual(expected, entryOwnership);
            Assert.IsTrue(client.TryGetValueOwnership("item-a-name", out var childOwnership));
            Assert.AreEqual(expected, childOwnership);
            if (expected == NeoValueOwnership.Asset) return;
            var target = new WriteTarget
            {
                writability = expected == NeoValueOwnership.Save ? WritabilityKind.Save : WritabilityKind.Session,
                typeInfo = new PrimitiveTypeInfo { type = MemberKind.String },
                pointer = new KeyOfPointer
                {
                    type = PointerKind.KeyOf,
                    keyOf = new KeyOf
                    {
                        pointer = new ReferencePointer { type = PointerKind.Reference, valueId = "item-a" },
                        key = new ValuePointer { type = PointerKind.Value, value = new Value
                        { typeInfo = new PrimitiveTypeInfo { type = MemberKind.String }, value = Newtonsoft.Json.Linq.JToken.FromObject("Name") } },
                    },
                },
            };
            NeoScriptExecutor.Execute(client, new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = System.Array.Empty<Variable>(),
                typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null },
                instructions = new Instruction[] { new AssignInstruction
                {
                    type = InstructionKind.Assign, operatorValue = "=", target = target,
                    pointer = new ValuePointer { type = PointerKind.Value, value = new Value
                    { typeInfo = new PrimitiveTypeInfo { type = MemberKind.String }, value = Newtonsoft.Json.Linq.JToken.FromObject("After") } },
                } },
            }, new Dictionary<string, object?>(), new NSGetterEvaluator.Context(client, null, null));
            Assert.AreEqual("Before", ((StringMemberValue)data.values["item-a-name"]).value);
            Assert.IsTrue(client.TryGetValue(expected, "item-a-name", out StringMemberValue? changed));
            Assert.AreEqual("After", changed!.value);
        }

        [Test]
        public void NeoScript_UnorderedReadsAndMutationsUseMembership(
            [Values(false, true)] bool alias, [Values(false, true)] bool primitive,
            [Values(false, true)] bool sessionInline)
        {
            ProjectData data = BuildProjectData();
            TypeInfo entryType = new ClassTypeInfo
            {
                type = MemberKind.Class, required = true, classId = ItemClassId,
            };
            if (primitive)
            {
                entryType = new PrimitiveTypeInfo { type = MemberKind.String, required = true };
                data.members["item-entry-member"] = new StringMember
                {
                    id = "item-entry-member", kind = MemberKind.String,
                    Requirement = NeoMemberRequirementKind.Required,
                };
                foreach (string id in new[] { "item-a", "item-b" })
                    data.values[id] = new StringMemberValue
                    {
                        id = id, containerId = ItemsListValueId, value = id,
                    };
            }
            if (sessionInline)
            {
                ((ClassMember)data.members["root-session"]).classId = "save-root-class";
                var sessionRoot = (ObjectMemberValue)data.values["root-session-value"];
                sessionRoot.classId = "save-root-class";
                sessionRoot.value = new Dictionary<string, string> { ["Bag"] = "bag-value" };
                ((ObjectMemberValue)data.values["root-save-value"]).value!.Clear();
                data.values["item-a"].containerId = null;
                data.values["item-b"].containerId = null;
                ((ArrayMemberValue)data.values[ItemsListValueId]).value = new[] { "item-a", "item-b" };
            }
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            if (sessionInline)
                client.SetWritableValue(NeoValueOwnership.Session, new ArrayMemberValue
                {
                    id = ItemsListValueId, value = new[] { "item-a", "item-b" },
                });
            NeoMemberListWritable Items() => sessionInline
                ? (NeoMemberListWritable)NeoMember.CreateWritable(client, data.members["items-member"], ItemsListValueId, NeoValueOwnership.Session)
                : ResolveItems(client);
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var reference = new ReferencePointer
            {
                type = PointerKind.Reference, valueId = ItemsListValueId,
            };
            var scope = new Dictionary<string, object?>();
            object? Eval(Pointer value) => NSGetterEvaluator.EvaluatePointer(value, scope, ctx);
            scope["items"] = Eval(reference);
            Pointer pointer = alias
                ? new VariablePointer { type = PointerKind.Variable, variableId = "items" }
                : reference;
            var listType = new CollectionTypeInfo
            {
                type = MemberKind.List, required = true, entryTypeInfo = entryType,
            };
            FunctionWithReturnType Body(params Instruction[] instructions) => new()
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = System.Array.Empty<Variable>(),
                typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                instructions = instructions,
            };
            void CheckCount(int expected)
            {
                Assert.AreEqual(expected, Eval(new FunctionPointer
                {
                    type = PointerKind.Function,
                    function = new CountFunction
                    {
                        type = FunctionKind.Count,
                        info = new FunctionCollectionOptionalBoolInfo { collectionPointer = pointer },
                    },
                }));
                Assert.AreEqual(expected, ((object?[])Eval(reference)!).Length);
                Assert.AreEqual(expected, Items().Count);
                scope["visited"] = 0;
                NeoScriptExecutor.Execute(client, Body(new ForEachInstruction
                {
                    type = InstructionKind.ForEach,
                    binding = new LoopBinding
                    {
                        id = "entry", typeInfo = entryType, isReadonly = true,
                        writability = WritabilityKind.ReadOnly,
                    },
                    collectionPointer = pointer, collectionTypeInfo = listType,
                    instructions = new Instruction[]
                    {
                        new AssignInstruction
                        {
                            type = InstructionKind.Assign, operatorValue = "+=",
                            target = new WriteTarget
                            {
                                pointer = new VariablePointer { type = PointerKind.Variable, variableId = "visited" },
                                typeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                                writability = WritabilityKind.Local,
                            },
                            pointer = new OperationPointer
                            {
                                type = PointerKind.Operation,
                                operation = new ArithmeticOperation
                                {
                                    type = OperationKind.Arithmetic,
                                    arithmetic = new ArithmeticOpInfo
                                    {
                                        type = ArithmeticOpKind.Addition,
                                        pointers = new Pointer[]
                                        {
                                            new VariablePointer { type = PointerKind.Variable, variableId = "visited" },
                                            new ValuePointer
                                            {
                                                type = PointerKind.Value,
                                                value = new Value
                                                {
                                                    typeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                                                    value = Newtonsoft.Json.Linq.JToken.FromObject(1),
                                                },
                                            },
                                        },
                                    },
                                },
                            },
                        },
                    },
                }), scope, ctx);
                Assert.AreEqual(expected, scope["visited"], "foreach must see current membership");
            }
            void Mutate(string mutation, params Pointer[] args) => NeoScriptExecutor.Execute(client,
                Body(new CollectionCallInstruction
                {
                    type = InstructionKind.CollectionCall,
                    target = new WriteTarget
                    {
                        pointer = pointer, typeInfo = listType,
                        writability = alias ? WritabilityKind.Local
                            : sessionInline ? WritabilityKind.Session : WritabilityKind.Save,
                    },
                    mutation = mutation, args = args,
                }), scope, ctx);
            Pointer Entry(string id) => new ReferencePointer { type = PointerKind.Reference, valueId = id };
            CheckCount(2);
            Mutate(CollectionMutationKind.Remove, Entry("item-a"));
            CheckCount(1);
            if (!sessionInline) Assert.IsTrue(client.saveValues["item-a"].IsRemoved);
            client.SetWritableValue<MemberValue>(NeoValueOwnership.Session, primitive
                ? new StringMemberValue { id = "new-item", value = "new" }
                : new ObjectMemberValue
                {
                    id = "new-item", classId = ItemClassId, value = new Dictionary<string, string>(),
                });
            Mutate(CollectionMutationKind.Add, Entry("new-item"));
            CheckCount(2);
            Assert.IsFalse(client.saveValues.ContainsKey(ItemsListValueId), "membership writes leave the discriminator alone");
            if (!sessionInline)
            {
                using var reloaded = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: client.SerializeSaveData());
                Assert.AreEqual(2, ResolveItems(reloaded).Count);
            }
            Mutate(CollectionMutationKind.Remove, Entry("new-item"));
            CheckCount(1);
            var first = Eval(new FunctionPointer
            {
                type = PointerKind.Function,
                function = new FirstOrDefaultFunction
                {
                    type = FunctionKind.FirstOrDefault,
                    info = new FunctionCollectionOptionalBoolInfo { collectionPointer = pointer },
                },
            });
            Assert.AreEqual(primitive ? "item-b" : Eval(Entry("item-b")), first);
            void Assign(object?[] replacement)
            {
                scope["replacement"] = replacement;
                NeoScriptExecutor.Execute(client, Body(new AssignInstruction
                {
                    type = InstructionKind.Assign,
                    target = new WriteTarget
                    {
                        pointer = new KeyOfPointer
                        {
                            type = PointerKind.KeyOf,
                            keyOf = new KeyOf
                            {
                                pointer = Entry("bag-value"),
                                key = new ValuePointer
                                {
                                    type = PointerKind.Value,
                                    value = new Value
                                    {
                                        typeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true },
                                        value = Newtonsoft.Json.Linq.JToken.FromObject("Items"),
                                    },
                                },
                            },
                        },
                        typeInfo = listType,
                        writability = sessionInline ? WritabilityKind.Session : WritabilityKind.Save,
                    },
                    pointer = new VariablePointer { type = PointerKind.Variable, variableId = "replacement" },
                }), scope, ctx);
            }
            Assign(System.Array.Empty<object?>());
            CheckCount(0);
            CollectionAssert.IsEmpty(client.GetUnorderedListEntryIds(ItemsListValueId));
            client.SetWritableValue(NeoValueOwnership.Session, new ObjectMemberValue
            {
                id = "replacement-item", classId = ItemClassId, value = new Dictionary<string, string>(),
            });
            Assign(new[] { primitive ? "replacement" : Eval(Entry("replacement-item")) });
            CheckCount(1);
            if (!sessionInline)
            {
                using var replaced = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: client.SerializeSaveData());
                Assert.AreEqual(1, ResolveItems(replaced).Count);
            }
            Mutate(CollectionMutationKind.Clear);
            CheckCount(0);
            CollectionAssert.IsEmpty(client.GetUnorderedListEntryIds(ItemsListValueId));
            CollectionAssert.IsEmpty(client.FindUnlinkedSaveValueIds());
            if (!sessionInline)
            {
                using var cleared = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: client.SerializeSaveData());
                Assert.AreEqual(0, ResolveItems(cleared).Count);
            }
        }

        /// <summary>
        /// A localized String entry stores its text id. Matching a script's
        /// value against entries compares the text a read returns, so
        /// <c>Remove("Alpha")</c> and a Lookup <c>Add("Beta")</c> find them.
        /// </summary>
        [Test]
        public void NeoScript_PayloadMatchesCompareLocalizedEntryText([Values(false, true)] bool unordered)
        {
            ProjectData data = BuildProjectData();
            var items = (ListMember)data.members["items-member"];
            items.ListKind = unordered ? NeoListKind.Unordered : NeoListKind.Ordered;
            data.members["item-entry-member"] = new StringMember
            {
                id = "item-entry-member", kind = MemberKind.String,
                Requirement = NeoMemberRequirementKind.Required,
            };
            foreach (string id in new[] { "item-a", "item-b" })
                data.values[id] = new StringMemberValue
                {
                    id = id, containerId = unordered ? ItemsListValueId : null, value = "text-" + id,
                };
            if (!unordered)
                ((ArrayMemberValue)data.values[ItemsListValueId]).value = new[] { "item-a", "item-b" };
            data.members["selected-member"] = new LookupMember
            {
                id = "selected-member", projectId = "project-a", name = "Selected", kind = MemberKind.Lookup,
                collectionMemberId = items.id, Selection = NeoMemberSelectionKind.Multi,
            };
            data.classes[BagClassId].schema["Selected"] = "selected-member";
            ((ObjectMemberValue)data.values["bag-value"]).value!["Selected"] = "selected-set";
            data.values["selected-set"] = new ArrayMemberValue { id = "selected-set", value = System.Array.Empty<string>() };
            var localization = NeoLocalization.CreateEmpty(null);
            Assert.IsTrue(localization.TryAddLoadedLocale(new ProjectLocalizationLocaleFile
            {
                schemaVersion = 1,
                projectId = data.project.id,
                versionId = "version-1",
                locale = "en-US",
                formattingSyntax = "smart-format",
                values = new Dictionary<string, string?> { ["text-item-a"] = "Alpha", ["text-item-b"] = "Beta" },
            }));
            using var client = NeoTestSaveStack.ClientFromSchema(data, localization: localization);
            var stringType = new PrimitiveTypeInfo { type = MemberKind.String, required = true };
            void Mutate(string valueId, TypeInfo typeInfo, string mutation, string text) =>
                NeoScriptExecutor.Execute(client, new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = System.Array.Empty<Variable>(),
                    typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                    instructions = new Instruction[]
                    {
                        new CollectionCallInstruction
                        {
                            type = InstructionKind.CollectionCall,
                            target = new WriteTarget
                            {
                                pointer = new ReferencePointer { type = PointerKind.Reference, valueId = valueId },
                                typeInfo = typeInfo,
                                writability = WritabilityKind.Save,
                            },
                            mutation = mutation,
                            args = new Pointer[]
                            {
                                new ValuePointer
                                {
                                    type = PointerKind.Value,
                                    value = new Value { typeInfo = stringType, value = Newtonsoft.Json.Linq.JToken.FromObject(text) },
                                },
                            },
                        },
                    },
                }, new Dictionary<string, object?>(), new NSGetterEvaluator.Context(client, null, null));

            Mutate("selected-set", new LookupTypeInfo
            {
                type = MemberKind.Lookup, required = true, entryTypeInfo = stringType,
                collectionMemberId = items.id, collectionValueId = ItemsListValueId,
            }, CollectionMutationKind.Add, "Beta");
            Mutate(ItemsListValueId, new CollectionTypeInfo
            {
                type = MemberKind.List, required = true, entryTypeInfo = stringType,
            }, CollectionMutationKind.Remove, "Alpha");

            Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Save, "selected-set", out ArrayMemberValue? selected));
            CollectionAssert.AreEqual(new[] { "item-b" }, selected!.value, "Lookup Add by text");
            CollectionAssert.AreEqual(new[] { "item-b" }, ResolveItems(client).ResolveEntryValueIds(), "Remove by text");
        }

        [Test]
        public void Evaluator_DoesNotRetainInvalidatedListViews()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var oldView = ReadAndInvalidateListView(ctx);
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            System.GC.Collect();
            Assert.IsFalse(oldView.IsAlive, "reverse provenance must not retain unused array views");
            System.GC.KeepAlive(ctx);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static System.WeakReference ReadAndInvalidateListView(NSGetterEvaluator.Context ctx)
        {
            var view = NSGetterEvaluator.EvaluatePointer(new ReferencePointer
            {
                type = PointerKind.Reference, valueId = ItemsListValueId,
            }, new Dictionary<string, object?>(), ctx);
            var weak = new System.WeakReference(view);
            NSGetterEvaluator.InvalidateCachedCollection(ItemsListValueId, NeoValueOwnership.Save, ctx);
            return weak;
        }

        // ------------------------------------------------------------------
        // Enumeration.
        // ------------------------------------------------------------------

        [Test]
        public void Enumeration_JoinsAuthoredMembersIdSorted()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var items = ResolveItems(client);

            Assert.IsTrue(items.IsUnordered);
            Assert.AreEqual(2, items.Count);
            // Deterministic enumeration: sorted by entry value id (ordinal).
            Assert.AreEqual("item-a", items[0].value!.id);
            Assert.AreEqual("item-b", items[1].value!.id);
        }

        [Test]
        public void Enumeration_NullDiscriminatorResolvesToNoEntries_EvenWithStampedMembers()
        {
            // Defense in depth (§1.5): value null wins unconditionally — the
            // join is only consulted when the value is [].
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var nullItems = ResolveList(client, "NullItems");

            Assert.IsTrue(nullItems.IsUnordered);
            Assert.AreEqual(0, nullItems.Count);
            // The stray member behind the null container exists as a row...
            Assert.IsTrue(client.values.ContainsKey("stray-item"));
            // ...but never resurfaces through the membership index.
            CollectionAssert.IsEmpty(client.GetUnorderedListEntryIds(NullItemsListValueId));
        }

        [Test]
        public void MembershipIndex_LayersOverlaysAndSubtractsTombstones()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());

            // Save overlay adds a member by join.
            client.AddSaveValue("item-c", new ObjectMemberValue
            {
                id = "item-c",
                classId = ItemClassId,
                containerId = ItemsListValueId,
                value = new Dictionary<string, string>(),
            });
            CollectionAssert.AreEqual(
                new[] { "item-a", "item-b", "item-c" },
                client.GetUnorderedListEntryIds(ItemsListValueId).ToArray());

            // A save tombstone at an authored member id subtracts it.
            client.AddSaveValue("item-a", new NullMemberValue
            {
                id = "item-a",
                createdAt = "2026-01-01T00:00:00.000Z",
                updatedAt = "2026-01-01T00:00:00.000Z",
                mark = NeoValueMarks.Removed,
            });
            CollectionAssert.AreEqual(
                new[] { "item-b", "item-c" },
                client.GetUnorderedListEntryIds(ItemsListValueId).ToArray());

            // Dropping the tombstone shadow resurfaces the authored member.
            client.RemoveWritableShadow(NeoValueOwnership.Save, "item-a");
            CollectionAssert.AreEqual(
                new[] { "item-a", "item-b", "item-c" },
                client.GetUnorderedListEntryIds(ItemsListValueId).ToArray());
        }

        // ------------------------------------------------------------------
        // Writable ops.
        // ------------------------------------------------------------------

        [Test]
        public void Add_CreatesTheEntryRowWithContainerId_WithoutRewritingTheContainer()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var items = ResolveItems(client);

            items.AddSerialized(NeoValueWritePayload.FromValue(new Dictionary<string, string>()));

            Assert.AreEqual(3, items.Count);
            string addedId = items
                .Select(child => child.value!.id)
                .Single(id => id != "item-a" && id != "item-b");
            Assert.IsTrue(client.TryGetWritableValue(
                NeoValueOwnership.Save, addedId, out MemberValue? addedRow));
            Assert.AreEqual(ItemsListValueId, addedRow!.containerId);

            // Membership lives on the entry row: the container's discriminator
            // was NOT shadowed into the save store by the add.
            Assert.IsFalse(client.saveValues.ContainsKey(ItemsListValueId));
            var authoredContainer = (ArrayMemberValue)client.values[ItemsListValueId];
            CollectionAssert.IsEmpty(authoredContainer.value!);
        }

        [Test]
        public void RemoveById_AuthoredMember_TombstonesItInTheOverlay()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var items = ResolveItems(client);

            items.RemoveById("item-a");

            Assert.AreEqual(1, items.Count);
            Assert.AreEqual("item-b", items[0].value!.id);
            Assert.IsTrue(client.TryGetWritableValue(
                NeoValueOwnership.Save, "item-a", out MemberValue? tombstone));
            Assert.IsTrue(tombstone!.IsRemoved);
            // The authored row itself is never touched.
            Assert.IsFalse(client.values["item-a"].IsRemoved);
            // The membership tombstone is containment state, not garbage.
            CollectionAssert.IsEmpty(client.FindUnlinkedSaveValueIds());
        }

        [Test]
        public void RemoveById_OverlayCreatedMember_DropsTheRow()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var items = ResolveItems(client);
            items.AddSerialized(NeoValueWritePayload.FromValue(new Dictionary<string, string>()));
            string addedId = items
                .Select(child => child.value!.id)
                .Single(id => id != "item-a" && id != "item-b");

            items.RemoveById(addedId);

            Assert.AreEqual(2, items.Count);
            // Dropped, not tombstoned: a row this overlay created has nothing
            // to shadow.
            Assert.IsFalse(client.saveValues.ContainsKey(addedId));
        }

        [Test]
        public void SetSerialized_Throws_EntriesAreUnordered()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var items = ResolveItems(client);

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                items.SetSerialized(0, NeoValueWritePayload.FromValue(
                    new Dictionary<string, string>())));
            StringAssert.Contains("unordered", error!.Message);
        }

        [Test]
        public void Clear_TombstonesEveryAuthoredMember()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var items = ResolveItems(client);

            items.ClearSerialized();

            Assert.AreEqual(0, items.Count);
            Assert.IsTrue(client.saveValues["item-a"].IsRemoved);
            Assert.IsTrue(client.saveValues["item-b"].IsRemoved);
            CollectionAssert.IsEmpty(client.GetUnorderedListEntryIds(ItemsListValueId));
            CollectionAssert.IsEmpty(client.FindUnlinkedSaveValueIds());
        }

        [Test]
        public void WholeListAssignment_NullClearsMembersAndSetsTheDiscriminatorNull()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var items = ResolveItems(client);

            items.AssignSerialized(null);

            // Members were tombstoned in the same write...
            Assert.IsTrue(client.saveValues["item-a"].IsRemoved);
            Assert.IsTrue(client.saveValues["item-b"].IsRemoved);
            // ...and the discriminator shadow is null: the list instance is gone.
            Assert.IsTrue(client.TryGetWritableValue(
                NeoValueOwnership.Save, ItemsListValueId, out ArrayMemberValue? shadow));
            Assert.IsNull(shadow!.value);
            Assert.AreEqual(0, items.Count);
            CollectionAssert.IsEmpty(client.FindUnlinkedSaveValueIds());
        }

        [Test]
        public void WholeListAssignment_ArrayTranslatesToClearPlusAddEach()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var items = ResolveItems(client);

            // Assign [item-b]: item-a is cleared (tombstone), item-b survives
            // as a re-added reference of the fresh instance.
            items.AssignSerialized(NeoValueWritePayload.FromValue(new[] { "item-b" }));

            var ids = items.Select(child => child.value!.id).ToArray();
            CollectionAssert.AreEqual(new[] { "item-b" }, ids);
            Assert.IsTrue(client.saveValues["item-a"].IsRemoved);
            // The discriminator is present ([]), never a member array.
            Assert.IsTrue(client.TryGetWritableValue(
                NeoValueOwnership.Save, ItemsListValueId, out ArrayMemberValue? shadow));
            CollectionAssert.IsEmpty(shadow!.value!);
        }

        // ------------------------------------------------------------------
        // GC / reachability containment edges.
        // ------------------------------------------------------------------

        [Test]
        public void Reachability_OverlayCreatedMembersAreReachableThroughTheContainerEdge()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var items = ResolveItems(client);

            items.AddSerialized(NeoValueWritePayload.FromValue(new Dictionary<string, string>()));

            // The created member row hangs off the container by join only; the
            // containment edge keeps it (and nothing else leaks).
            CollectionAssert.IsEmpty(client.FindUnlinkedSaveValueIds());
        }

        [Test]
        public void Reachability_LiveMembersBehindANullContainerAreCollectable()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var items = ResolveItems(client);
            items.AddSerialized(NeoValueWritePayload.FromValue(new Dictionary<string, string>()));
            string addedId = items
                .Select(child => child.value!.id)
                .Single(id => id != "item-a" && id != "item-b");

            // Null the container WITHOUT the cascading whole-list op —
            // simulating a mid-transaction/corrupted state. The stranded live
            // member is an anomaly and must be collectable.
            client.AddSaveValue(ItemsListValueId, new ArrayMemberValue
            {
                id = ItemsListValueId,
                createdAt = "2026-01-01T00:00:00.000Z",
                updatedAt = "2026-01-01T00:00:00.000Z",
                value = null,
            });

            CollectionAssert.Contains(client.FindUnlinkedSaveValueIds(), addedId);
        }

        [Test]
        public void Reachability_LiveOverlayMoveDoesNotRetainAuthoredContainerMembership()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            client.AddSaveValue("item-a", new ObjectMemberValue
            {
                id = "item-a",
                classId = ItemClassId,
                containerId = NullItemsListValueId,
                value = new Dictionary<string, string>(),
            });

            CollectionAssert.Contains(client.FindUnlinkedSaveValueIds(), "item-a",
                "the live overlay row replaces its authored container stamp; "
                + "its new null container cannot keep it reachable");
        }

        [Test]
        public void CloneValueReference_UsesIndexedMembersOfWritableOnlySessionContainer()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            client.SetWritableValue(NeoValueOwnership.Session, new ObjectMemberValue
            {
                id = "session-bag",
                classId = BagClassId,
                value = new Dictionary<string, string>
                {
                    ["Items"] = "session-items",
                },
            });
            client.SetWritableValue(NeoValueOwnership.Session, new ArrayMemberValue
            {
                id = "session-items",
                value = System.Array.Empty<string>(),
            });
            client.SetWritableValue(NeoValueOwnership.Session, new ObjectMemberValue
            {
                id = "session-item",
                classId = ItemClassId,
                containerId = "session-items",
                value = new Dictionary<string, string>(),
            });

            string clonedBagId = client.CloneValueReference(
                "session-bag",
                NeoValueOwnership.Session);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                clonedBagId,
                out ObjectMemberValue? clonedBag));
            string clonedItemsId = clonedBag!.value!["Items"];
            string clonedItemId = client.GetUnorderedListEntryIds(clonedItemsId).Single();
            Assert.AreNotEqual("session-items", clonedItemsId);
            Assert.AreNotEqual("session-item", clonedItemId);
        }

        [Test]
        public void CloneValueReference_ClonesNestedUnorderedMembersAcrossAuthoredAndSaveLayers()
        {
            ProjectData data = BuildProjectData();
            var members = data.members;
            members["nested-items-member"] = new ListMember
            {
                id = "nested-items-member",
                name = "Nested",
                kind = MemberKind.List,
                entryMemberId = "nested-entry-member",
                ListKind = NeoListKind.Unordered,
            };
            members["nested-entry-member"] = new StringMember
            {
                id = "nested-entry-member",
                name = "Nested entry",
                kind = MemberKind.String,
            };
            var classes = data.classes;
            classes[ItemClassId].schema!["Nested"] = "nested-items-member";
            const string nestedConstructorId = "nested-items-constructor";
            var nestedConstructor = new ConstructorRecord
            {
                id = nestedConstructorId,
                projectId = "project-a",
                classId = ItemClassId,
                argumentTypes = new[]
                {
                    new FunctionArgumentTypeInfo
                    {
                        name = "Nested",
                        type = MemberKind.List,
                        required = true,
                        entryTypeInfo = new PrimitiveTypeInfo
                        {
                            type = MemberKind.String,
                            required = true,
                        },
                    },
                },
            };
            string nestedParameterId = NeoClient.ConstructorParameterId(
                nestedConstructor,
                0);
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var parameters = new[]
            {
                new Variable { id = "__this__", typeInfo = new ClassTypeInfo { type = MemberKind.Class, classId = ItemClassId } },
                new Variable { id = "__root__", typeInfo = new ClassTypeInfo { type = MemberKind.Class, classId = "__root__" } },
                new Variable { id = nestedParameterId, typeInfo = nestedConstructor.argumentTypes[0] },
            };
            nestedConstructor.action = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = parameters, typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null },
                instructions = System.Array.Empty<Instruction>(),
            };
            ((ListMember)members["nested-items-member"]).defaultValue = new ArrayMemberValueBase
            {
                init = new InitializerBody { code = "Nested", compiled = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = parameters,
                    typeInfo = new CollectionTypeInfo { type = MemberKind.List, required = true,
                        entryTypeInfo = nestedConstructor.argumentTypes[0].entryTypeInfo },
                    instructions = new Instruction[] { new ReturnInstruction { type = InstructionKind.Return,
                        pointer = new VariablePointer { type = PointerKind.Variable, variableId = nestedParameterId } } },
                } },
            };
            NeoGeneratedTypesSupport.InvalidateConstructorSchemaCaches(client);
            data.constructors[nestedConstructorId] = nestedConstructor;
            var authored = data.values;
            ((ObjectMemberValue)authored["item-a"]).value!["Nested"] = "nested-a";
            var itemB = (ObjectMemberValue)authored["item-b"];
            itemB.instanceConstructorId = nestedConstructorId;
            itemB.constructorArgs = new Dictionary<string, Newtonsoft.Json.Linq.JToken?>
            {
                [nestedParameterId] = "nested-b",
            };
            client.AddSaveValue("nested-a", new ArrayMemberValue
            {
                id = "nested-a", value = System.Array.Empty<string>(),
            });
            client.AddSaveValue("nested-b", new ArrayMemberValue
            {
                id = "nested-b", value = System.Array.Empty<string>(),
            });
            client.AddSaveValue("nested-a-member", new StringMemberValue
            {
                id = "nested-a-member", containerId = "nested-a", value = "a",
            });
            client.AddSaveValue("nested-b-member", new StringMemberValue
            {
                id = "nested-b-member", containerId = "nested-b", value = "b",
            });

            // Overlay subtraction and addition must both be respected by the
            // exact Save graph clone.
            client.AddSaveValue("item-a", new NullMemberValue
            {
                id = "item-a", mark = NeoValueMarks.Removed,
            });
            client.SetWritableValues(NeoValueOwnership.Save, new MemberValue[]
            {
            new ObjectMemberValue
            {
                id = "item-c",
                classId = ItemClassId,
                containerId = ItemsListValueId,
                value = new Dictionary<string, string>(),
                instanceConstructorId = nestedConstructorId,
                constructorArgs = new Dictionary<string, Newtonsoft.Json.Linq.JToken?>
                {
                    [nestedParameterId] = "nested-c",
                },
            },
            new ArrayMemberValue
            {
                id = "nested-c", value = System.Array.Empty<string>(),
            },
            new StringMemberValue
            {
                id = "nested-c-member", containerId = "nested-c", value = "c",
            },
            });

            Assert.IsTrue(client.TryFindOwnedParent(
                NeoValueOwnership.Save,
                "nested-c",
                out string? constructorOwnedParent));
            Assert.AreEqual("item-c", constructorOwnedParent);
            Assert.IsTrue(client.StillHasOwnedChildReference(
                NeoValueOwnership.Save,
                "item-c",
                "nested-c"));

            string clonedBagId = client.CloneValueReference("bag-value", NeoValueOwnership.Save);
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Session,
                clonedBagId,
                out ObjectMemberValue? clonedBag));
            string clonedItemsId = clonedBag!.value!["Items"];
            var clonedItemIds = client.GetUnorderedListEntryIds(clonedItemsId).ToArray();
            Assert.AreEqual(2, clonedItemIds.Length,
                "the Save tombstone subtracts item-a while item-b and overlay item-c clone");
            CollectionAssert.DoesNotContain(clonedItemIds, "item-b");
            CollectionAssert.DoesNotContain(clonedItemIds, "item-c");

            var nestedValues = new List<string>();
            foreach (string clonedItemId in clonedItemIds)
            {
                Assert.IsTrue(client.TryGetValue(
                    NeoValueOwnership.Session,
                    clonedItemId,
                    out ObjectMemberValue? clonedItem));
                var clonedNested = (ArrayMemberValue)client.ResolveClassChildRow(
                    clonedItem!,
                    "Nested")!;
                string clonedNestedId = clonedNested.id;
                Assert.AreNotEqual("nested-b", clonedNestedId);
                Assert.AreNotEqual("nested-c", clonedNestedId);
                Assert.AreEqual(
                    clonedNestedId,
                    clonedItem.constructorArgs![nestedParameterId]!.ToObject<string>());
                string[] nestedIds = client.GetUnorderedListEntryIds(clonedNestedId).ToArray();
                Assert.AreEqual(1, nestedIds.Length, Newtonsoft.Json.JsonConvert.SerializeObject(
                    nestedIds.Select(id => client.TryGetValue(id, out MemberValue? row) ? row : null)));
                string clonedNestedMemberId = nestedIds.Single();
                Assert.IsTrue(client.TryGetValue(
                    NeoValueOwnership.Session,
                    clonedNestedMemberId,
                    out StringMemberValue? clonedNestedMember));
                nestedValues.Add(clonedNestedMember!.value!);
            }
            CollectionAssert.AreEquivalent(new[] { "b", "c" }, nestedValues);
        }

        [Test]
        public void Reachability_DeepNestedUnorderedContainersTraversesEachIndexedLevel()
        {
            ProjectData data = BuildProjectData();
            var members = data.members;
            members["deep-list-member"] = new ListMember
            {
                id = "deep-list-member",
                name = "Nested",
                kind = MemberKind.List,
                entryMemberId = "item-entry-member",
                ListKind = NeoListKind.Unordered,
            };
            data.classes[ItemClassId]
                .schema!["Nested"] = "deep-list-member";
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            const int depth = 64;
            string parentContainerId = ItemsListValueId;
            for (int i = 0; i < depth; i++)
            {
                string memberId = $"deep-member-{i}";
                string childContainerId = $"deep-container-{i}";
                client.AddSaveValue(memberId, new ObjectMemberValue
                {
                    id = memberId,
                    classId = ItemClassId,
                    containerId = parentContainerId,
                    value = new Dictionary<string, string>
                    {
                        ["Nested"] = childContainerId,
                    },
                });
                client.AddSaveValue(childContainerId, new ArrayMemberValue
                {
                    id = childContainerId,
                    value = System.Array.Empty<string>(),
                });
                parentContainerId = childContainerId;
            }

            // This scaling-shaped assertion exercises a long containment
            // chain without relying on machine-specific wall-clock timing.
            Assert.IsTrue(client.TryInferMemberForValueId(
                "deep-container-0",
                out NeoCompose.Runtime.Json.Member? inferredContainer));
            Assert.AreEqual("deep-list-member", inferredContainer!.id);
            CollectionAssert.IsEmpty(client.FindUnlinkedSaveValueIds());
        }

        // ------------------------------------------------------------------
        // Fixture.
        // ------------------------------------------------------------------

        private static NeoMemberListWritable ResolveItems(NeoClient client) =>
            ResolveList(client, "Items");

        private static NeoMemberListWritable ResolveList(NeoClient client, string key)
        {
            var bag = client.save.Get<NeoMemberClassWritable>("Bag");
            return bag.Get<NeoMemberListWritable>(key);
        }

        private static ProjectData BuildProjectData()
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
                    ["Bag"] = "bag-member",
                },
            };
            var bagClass = new NeoSchemaClass
            {
                id = BagClassId,
                projectId = "project-a",
                name = "Bag",
                schema = new Dictionary<string, string>
                {
                    ["Items"] = "items-member",
                    ["NullItems"] = "null-items-member",
                },
            };
            var itemClass = new NeoSchemaClass
            {
                id = ItemClassId,
                projectId = "project-a",
                name = "Item",
                schema = new Dictionary<string, string>(),
            };

            return new ProjectData
            {
                project = new Project
                {
                    id = "project-a",
                    _id = "project-a",
                    name = "Unordered Lists",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = new Dictionary<string, NeoCompose.Runtime.Json.Member>
                {
                    ["root-assets"] = RootMember("root-assets", "root-assets-value", rootClass.id),
                    ["root-save"] = RootMember("root-save", "root-save-value", saveRootClass.id),
                    ["root-session"] = RootMember("root-session", "root-session-value", rootClass.id),
                    ["bag-member"] = new ClassMember
                    {
                        id = "bag-member",
                        projectId = "project-a",
                        name = "Bag",
                        kind = MemberKind.Class,
                        classId = BagClassId,
                        Requirement = NeoMemberRequirementKind.Required,
                    },
                    ["items-member"] = new ListMember
                    {
                        id = "items-member",
                        projectId = "project-a",
                        name = "Items",
                        kind = MemberKind.List,
                        entryMemberId = "item-entry-member",
                        ListKind = NeoListKind.Unordered,
                        Requirement = NeoMemberRequirementKind.Required,
                    },
                    ["null-items-member"] = new ListMember
                    {
                        id = "null-items-member",
                        projectId = "project-a",
                        name = "NullItems",
                        kind = MemberKind.List,
                        entryMemberId = "item-entry-member",
                        ListKind = NeoListKind.Unordered,
                    },
                    ["item-entry-member"] = new ClassMember
                    {
                        id = "item-entry-member",
                        projectId = "project-a",
                        name = "Item",
                        kind = MemberKind.Class,
                        classId = ItemClassId,
                        Requirement = NeoMemberRequirementKind.Required,
                    },
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["root-assets-value"] = ObjectValue("root-assets-value", rootClass.id, new()),
                    ["root-save-value"] = ObjectValue(
                        "root-save-value",
                        saveRootClass.id,
                        new Dictionary<string, string> { ["Bag"] = "bag-value" }),
                    ["root-session-value"] = ObjectValue("root-session-value", rootClass.id, new()),
                    ["bag-value"] = ObjectValue(
                        "bag-value",
                        BagClassId,
                        new Dictionary<string, string>
                        {
                            ["Items"] = ItemsListValueId,
                            ["NullItems"] = NullItemsListValueId,
                        }),
                    // Present discriminator: [] — membership is joined.
                    [ItemsListValueId] = new ArrayMemberValue
                    {
                        id = ItemsListValueId,
                        value = System.Array.Empty<string>(),
                    },
                    // Null discriminator: the list resolves as null.
                    [NullItemsListValueId] = new ArrayMemberValue
                    {
                        id = NullItemsListValueId,
                        value = null,
                    },
                    // Authored members, intentionally declared out of id order
                    // to prove the id-sort.
                    ["item-b"] = MemberValue("item-b", ItemsListValueId),
                    ["item-a"] = MemberValue("item-a", ItemsListValueId),
                    // Anomalous member behind the null container.
                    ["stray-item"] = MemberValue("stray-item", NullItemsListValueId),
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [rootClass.id] = rootClass,
                    [saveRootClass.id] = saveRootClass,
                    [BagClassId] = bagClass,
                    [ItemClassId] = itemClass,
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
                Requirement = NeoMemberRequirementKind.Required,
                valueId = valueId,
                classId = classId,
            };
        }

        private static ObjectMemberValue ObjectValue(
            string id,
            string classId,
            Dictionary<string, string> record)
        {
            return new ObjectMemberValue
            {
                id = id,
                classId = classId,
                value = record,
            };
        }

        private static ObjectMemberValue MemberValue(string id, string containerId)
        {
            return new ObjectMemberValue
            {
                id = id,
                classId = ItemClassId,
                containerId = containerId,
                value = new Dictionary<string, string>(),
            };
        }
    }
}
