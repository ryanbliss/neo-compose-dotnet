// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class NeoCollectionsTests
    {
        private const string PackageRoot =
            "Packages/com.ryanbliss.neocompose/Tests";

        private static string LoadFixture(string fileName)
        {
            return File.ReadAllText(Path.Combine(PackageRoot, fileName));
        }

        private static NeoClient LoadClient(out string saveBuffer)
        {
            var client = NeoTestSaveStack.LoadClient(LoadFixture("synth-example.json"));
            saveBuffer = client.SerializeSaveData();
            return client;
        }

        private static T RequireMember<T>(NeoClient client, string id) where T : Member
        {
            if (!client.TryGetMember(id, out T? member))
            {
                Assert.Fail($"Fixture is missing member '{id}' of type {typeof(T).Name}");
                throw new System.InvalidOperationException("unreachable");
            }
            return member;
        }

        [Test]
        public void ListSearchUsesClassValueIdentityAcrossViews()
        {
            using var client = LoadClient(out _);
            var data = client.ProjectDataForRuntime;
            data.classes["audit-entry-class"] = new NeoSchemaClass
            {
                id = "audit-entry-class",
                name = "Entry",
                schema = new Dictionary<string, string>(),
            };
            data.members["audit-entry"] = new ClassMember
            {
                id = "audit-entry",
                name = "Entry",
                kind = MemberKind.Class,
                DeclaredClassId = "audit-entry-class",
            };
            data.members["audit-list"] = new ListMember
            {
                id = "audit-list",
                name = "Entries",
                kind = MemberKind.List,
                entryMemberId = "audit-entry",
                defaultValue = new ArrayMemberValueBase { value = System.Array.Empty<string>() },
            };
            var entry = NeoGeneratedTypesSupport.CreateWritableClassValue(client, "audit-entry-class");
            using var listNode = new NeoMemberListWritable(client, (ListMember)data.members["audit-list"], null, NeoValueOwnership.Session);
            var list = new NeoList<IdentityView>(client, listNode,
                (c, n) => new IdentityView(c, (NeoMemberClass)n),
                value => NeoValueWritePayload.FromValueReference(value.valueId!));
            list.Add(new IdentityView(client, entry));
            var first = list[0];
            Assert.AreNotSame(first, list[0], "Different views of the same row are valid.");
            Assert.That(list.IndexOf(first), Is.EqualTo(0));
            Assert.That(((NeoReadOnlyList<IdentityView>)list).Contains(first), Is.True);
            using var foreignClient = NeoTestSaveStack.ClientFromSchema(data);
            foreignClient.SetWritableValue(NeoValueOwnership.Session, new ObjectMemberValue
            {
                id = first.valueId!,
                classId = "audit-entry-class",
                value = new Dictionary<string, string>(),
            });
            using var foreignNode = new NeoMemberClassWritable(foreignClient, (ClassMember)data.members["audit-entry"], first.valueId, NeoValueOwnership.Session);
            Assert.That(list.Contains(new IdentityView(foreignClient, foreignNode)), Is.False, "Matching ids in another client are different objects.");
            Assert.That(list.Contains(first), Is.True);
            Assert.That(list.Remove(first), Is.True);
            Assert.That(list.Contains(first), Is.False);
            data.members["audit-map"] = new DictionaryMember
            {
                id = "audit-map",
                name = "Entries",
                kind = MemberKind.Dictionary,
                entryMemberId = "audit-entry",
                defaultValue = new ObjectMemberValueBase { value = new Dictionary<string, string>() },
            };
            using var mapNode = new NeoMemberDictionaryWritable(client, (DictionaryMember)data.members["audit-map"], null, NeoValueOwnership.Session);
            var map = new NeoDictionary<IdentityView>(client, mapNode,
                (c, n) => new IdentityView(c, (NeoMemberClass)n),
                value => NeoValueWritePayload.FromValueReference(value.valueId!));
            map.Add("a", new IdentityView(client, NeoGeneratedTypesSupport.CreateWritableClassValue(client, "audit-entry-class")));
            var pair = new KeyValuePair<string, IdentityView>("a", map["a"]);
            Assert.That(map.Contains(pair), Is.True);
            Assert.That(map.Remove(pair), Is.True);
            Assert.That(map.Count, Is.Zero);
        }

        private sealed class IdentityView : NeoGeneratedClassValue
        {
            internal IdentityView(NeoClient client, NeoMemberClass node)
                : base(client, node, "audit-entry-class", false, node.ownership) { }
        }

        [Test]
        public void DictionaryClassEntryEditBubblesToItsOwningClass()
        {
            using var client = LoadClient(out _);
            var data = client.ProjectDataForRuntime;
            data.classes["audit-entry-class"] = new NeoSchemaClass
            {
                id = "audit-entry-class",
                name = "Entry",
                schema = new Dictionary<string, string> { ["Name"] = "member-name" },
            };
            data.members["audit-entry"] = new ClassMember
            {
                id = "audit-entry",
                name = "Entry",
                kind = MemberKind.Class,
                DeclaredClassId = "audit-entry-class",
            };
            data.members["audit-entries"] = new DictionaryMember
            {
                id = "audit-entries",
                name = "Entries",
                kind = MemberKind.Dictionary,
                entryMemberId = "audit-entry",
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new ObjectMemberValueBase { value = new Dictionary<string, string>() },
            };
            data.classes["audit-owner"] = new NeoSchemaClass
            {
                id = "audit-owner",
                name = "Owner",
                schema = new Dictionary<string, string> { ["Entries"] = "audit-entries" },
            };
            using var owner = NeoGeneratedTypesSupport.CreateWritableClassValue(client, "audit-owner");
            var entry = NeoGeneratedTypesSupport.CreateWritableClassValue(client, "audit-entry-class",
                new NeoGeneratedConstructorValue("Name", "member-name", "before"));
            var dictionary = owner.Get<NeoMemberDictionaryWritable>("Entries");
            dictionary.SetSerialized("entry", NeoValueWritePayload.FromValueReference(entry.value!.id));
            int dictionaryChanges = 0, ownerChanges = 0;
            dictionary.OnChanged += _ => dictionaryChanges++;
            owner.OnChanged += _ => ownerChanges++;
            ((NeoMemberClassWritable)dictionary["entry"]).Get<NeoMemberStringWritable>("Name").Set("after");
            Assert.That(dictionaryChanges, Is.EqualTo(1));
            Assert.That(ownerChanges, Is.EqualTo(1));
        }

        [Test]
        public void DictionaryDirectEntryEditNotifiesItsSubscribers()
        {
            using var client = LoadClient(out _);
            var member = RequireMember<DictionaryMember>(client, "member-inventory");
            using var node = (NeoMemberDictionaryWritable)NeoMember.CreateWritable(client, member, null, NeoValueOwnership.Save);
            node.SetSerialized("runtime", NeoGeneratedTypesSupport.Value("before"));
            var dictionary = new NeoDictionary<string>(client, node,
                (_, child) => ((NeoMemberString)child).value!.value!, NeoGeneratedTypesSupport.Value);
            int changes = 0;
            using var subscription = dictionary.OnChanged((_, _) => changes++);
            ((NeoMemberStringWritable)node["runtime"]).Set("after");
            Assert.That(dictionary["runtime"], Is.EqualTo("after"));
            Assert.That(changes, Is.EqualTo(1));
        }

        [TestCase("Add")]
        [TestCase("Remove")]
        [TestCase("Clear")]
        public void UnboundListFirstMutationPreservesVisibleDefaults(string operation)
        {
            using var client = LoadClient(out _);
            client.ProjectDataForRuntime.values["v-a"] = new StringMemberValue { id = "v-a", value = "first" };
            client.ProjectDataForRuntime.values["v-b"] = new StringMemberValue { id = "v-b", value = "second" };
            var member = RequireMember<ListMember>(client, "member-tags");
            using var node = (NeoMemberListWritable)NeoMember.CreateWritable(client, member, null, NeoValueOwnership.Save);
            var list = new NeoList<string>(client, node,
                (_, child) => ((NeoMemberString)child).value!.value!, NeoGeneratedTypesSupport.Value);
            var first = list[0];
            var second = list[1];
            Assert.That(list.Count, Is.EqualTo(2));
            if (operation == "Add")
            {
                list.Add("third");
                CollectionAssert.AreEqual(new[] { first, second, "third" }, list);
            }
            else if (operation == "Remove")
            {
                list.RemoveAt(0);
                CollectionAssert.AreEqual(new[] { second }, list);
            }
            else
            {
                list.Clear();
                Assert.That(list, Is.Empty);
            }
        }

        [Test]
        public void DictionaryAddRejectsDuplicateWithoutWriting()
        {
            using var client = LoadClient(out _);
            var dictionary = LoadDefaultOnlyInventory(client);
            dictionary.Add("new", "original");
            var before = client.SerializeSaveData();
            var notifications = 0;
            using var subscription = dictionary.OnChanged((_, _) => notifications++);
            Assert.Throws<System.ArgumentException>(() => dictionary.Add("new", "replacement"));
            Assert.That(dictionary["new"], Is.EqualTo("original"));
            Assert.That(client.SerializeSaveData(), Is.EqualTo(before));
            Assert.That(notifications, Is.Zero);
        }

        [TestCase(NeoValueOwnership.Save)]
        [TestCase(NeoValueOwnership.Session)]
        public void ReplacingRuntimeListEntryPreservesPartition(NeoValueOwnership ownership)
        {
            using var client = LoadClient(out _);
            var member = RequireMember<ListMember>(client, "member-tags");
            using var node = (NeoMemberListWritable)NeoMember.CreateWritable(client, member, null, ownership);
            node.AddSerialized(NeoGeneratedTypesSupport.Value("original"));
            string id = node[node.Count - 1].value!.id;
            Assert.That(client.TryGetWritableValue(ownership, id, out MemberValue? row), Is.True);
            row!.mapKey = "map-a";
            node.SetSerialized(node.Count - 1, NeoGeneratedTypesSupport.Value("replacement"));
            Assert.That(client.TryGetWritableValue(ownership, id, out row), Is.True);
            Assert.That(row!.mapKey, Is.EqualTo("map-a"));
            Assert.That(((StringMemberValue)row).value, Is.EqualTo("replacement"));
        }

        [Test]
        public void NeoList_AddSetRemove_TracksUnderlyingSavedList()
        {
            var client = LoadClient(out _);
            var tagsMember = RequireMember<ListMember>(client, "member-tags");
            var tagsNode = (NeoMemberListWritable)NeoMember.CreateWritable(client, tagsMember, null);
            var tags = new NeoList<string>(
                client,
                tagsNode,
                (_, member) => ((NeoMemberString)member).value?.value ?? "",
                NeoGeneratedTypesSupport.Value);

            tags.Clear();
            int changed = 0;
            using var tagsSubscription = tags.OnChanged((_, _) => changed++);

            tags.Add("first");
            tags.Add("second");
            Assert.AreEqual(2, tags.Count);
            Assert.AreEqual("first", tags[0]);
            Assert.AreEqual("second", tags[1]);

            tags[1] = "updated";
            Assert.AreEqual("updated", tags[1]);

            tags.RemoveAt(0);
            Assert.AreEqual(1, tags.Count);
            Assert.AreEqual("updated", tags[0]);
            Assert.GreaterOrEqual(changed, 3);
        }

        [Test]
        public void NeoList_Clear_ClearsBackingArrayOnceAndFiresOneBulkChange()
        {
            var client = LoadClient(out _);
            var tagsMember = RequireMember<ListMember>(client, "member-tags");
            var tagsNode = (NeoMemberListWritable)NeoMember.CreateWritable(client, tagsMember, null);
            var tags = new NeoList<string>(
                client,
                tagsNode,
                (_, member) => ((NeoMemberString)member).value?.value ?? "",
                NeoGeneratedTypesSupport.Value);

            tags.Clear();
            tags.Add("first");
            tags.Add("second");
            var removedIds = new[]
            {
                tagsNode[0].value!.id,
                tagsNode[1].value!.id,
            };
            var changed = 0;
            NeoListChangedArgs? observed = null;
            using var tagsSubscription = tags.OnChanged((_, args, __) =>
            {
                changed += 1;
                observed = args;
            });

            tags.Clear();

            Assert.AreEqual(0, tags.Count);
            Assert.AreEqual(1, changed);
            Assert.IsNotNull(observed);
            Assert.AreEqual(NeoListChangeKind.Clear, observed!.Kind);
            CollectionAssert.AreEqual(removedIds, observed.RemovedValueIds);
            Assert.AreEqual(0, observed.AddedValueIds.Count);
            Assert.AreEqual(0, observed.ReplacedValueIds.Count);
        }

        [Test]
        public void NeoDictionary_SetRemove_TracksUnderlyingSavedDictionary()
        {
            var client = LoadClient(out _);
            var inventoryMember = RequireMember<DictionaryMember>(client, "member-inventory");
            var inventoryNode = (NeoMemberDictionaryWritable)NeoMember.CreateWritable(
                client,
                inventoryMember,
                null);
            var inventory = new NeoDictionary<string>(
                client,
                inventoryNode,
                (_, member) => ((NeoMemberString)member).value?.value ?? "",
                NeoGeneratedTypesSupport.Value);

            int changed = 0;
            using var inventorySubscription = inventory.OnChanged((_, _) => changed++);

            inventory["sword"] = "Excalibur";
            inventory["shield"] = "Aegis";

            Assert.AreEqual(2, inventory.Count);
            Assert.IsTrue(inventory.ContainsKey("sword"));
            Assert.AreEqual("Excalibur", inventory["sword"]);
            Assert.AreEqual("Aegis", inventory["shield"]);

            inventory["sword"] = "Caliburn";
            Assert.AreEqual("Caliburn", inventory["sword"]);

            Assert.IsTrue(inventory.Remove("shield"));
            Assert.IsFalse(inventory.ContainsKey("shield"));
            Assert.GreaterOrEqual(changed, 3);
        }

        // Builds a Save-owned writable inventory dictionary whose entries come
        // ONLY from the authored default (`member-inventory` = { sword, shield })
        // with no bound value row yet. Save ownership routes writes to the save
        // store so persistence is observable via SerializeSaveData. Regression
        // surface for the clone-on-write seed: the first mutation must clone the
        // default map, not start empty.
        private static NeoDictionary<string> LoadDefaultOnlyInventory(NeoClient client)
        {
            var inventoryMember = RequireMember<DictionaryMember>(client, "member-inventory");
            var inventoryNode = (NeoMemberDictionaryWritable)NeoMember.CreateWritable(
                client,
                inventoryMember,
                null,
                NeoValueOwnership.Save);
            return new NeoDictionary<string>(
                client,
                inventoryNode,
                (_, member) => ((NeoMemberString)member).value?.value ?? "",
                NeoGeneratedTypesSupport.Value);
        }

        [Test]
        public void NeoDictionary_RemoveFirstOnDefaultOnly_DropsKeyWithoutThrowing()
        {
            var client = LoadClient(out _);
            var inventory = LoadDefaultOnlyInventory(client);

            // Remove is the FIRST mutation: nothing is bound, so the visible
            // keys come purely from the default. Previously `EnsureWritableObject`
            // minted an empty map and `Remove` threw KeyNotFoundException indexing
            // a key it could see but the fresh map lacked.
            Assert.DoesNotThrow(() => inventory.Remove("sword"));
            Assert.IsFalse(inventory.ContainsKey("sword"));
            // The sibling default entry survives the clone-on-write.
            Assert.IsTrue(inventory.ContainsKey("shield"));
            Assert.AreEqual(1, inventory.Count);
        }

        [Test]
        public void NeoDictionary_ClearFirstOnDefaultOnly_EmptiesWithoutThrowing()
        {
            var client = LoadClient(out _);
            var inventory = LoadDefaultOnlyInventory(client);

            // Clear loops Remove over the visible (default) keys as the first
            // mutation — same seed bug, hit once per key.
            Assert.DoesNotThrow(() => inventory.Clear());
            Assert.AreEqual(0, inventory.Count);
            Assert.IsFalse(inventory.ContainsKey("sword"));
            Assert.IsFalse(inventory.ContainsKey("shield"));
        }

        [Test]
        public void NeoDictionary_SetNewKeyFirstOnDefaultOnly_PreservesDefaultSiblings()
        {
            var client = LoadClient(out _);
            var inventory = LoadDefaultOnlyInventory(client);

            // Adding a brand-new key as the first mutation must not wipe the
            // authored default entries — a fresh empty seed used to silently
            // drop them (only the new key survived).
            inventory["potion"] = "Elixir";

            Assert.AreEqual("Elixir", inventory["potion"]);
            Assert.IsTrue(inventory.ContainsKey("sword"));
            Assert.IsTrue(inventory.ContainsKey("shield"));
            Assert.AreEqual(3, inventory.Count);
        }

        [Test]
        public void NeoDictionary_RemoveFirstOnDefaultOnly_PersistsThroughSerialize()
        {
            var client = LoadClient(out _);
            var inventory = LoadDefaultOnlyInventory(client);

            inventory.Remove("sword");

            // The clone-on-write parent row is persisted with the surviving
            // sibling only — the serialized save must not carry the removed key.
            string saved = client.SerializeSaveData();
            StringAssert.Contains("shield", saved);
            StringAssert.DoesNotContain("sword", saved);
        }

        [Test]
        public void NeoLookupSet_AddRemoveClear_TracksUniqueLookupSelections()
        {
            var client = LoadClient(out _);
            var choiceMember = RequireMember<LookupMember>(client, "member-choice");
            // Stable-id overlay: pin the lookup to its target collection value by
            // id and shadow that value in the save store (the old override-map
            // rebind of member-tags is gone).
            choiceMember.collectionValueId = "v-tags-target";
            client.SetSaveValue(new ArrayMemberValue
            {
                id = "v-tags-target",
                createdAt = "now",
                updatedAt = "now",
                value = new[] { "v-a", "v-b" },
            });

            var choiceNode = (NeoMemberLookupWritable)NeoMember.CreateWritable(
                client,
                choiceMember,
                null);
            var choices = new NeoLookupSet<NeoLookupSelection>(
                client,
                choiceNode,
                child => new NeoLookupSelection(child.value?.id ?? ""));

            int changed = 0;
            using var choicesSubscription = choices.OnChanged((_, _) => changed++);

            Assert.IsTrue(choices.Add("v-a"));
            Assert.IsFalse(choices.Add("v-a"));
            Assert.IsTrue(choices.Contains("v-a"));
            Assert.AreEqual(1, choices.Count);
            var firstSelected = choiceNode.GetFirstSelected();
            Assert.AreSame(choiceNode.GetSelected()[0], firstSelected);
            Assert.AreSame(firstSelected, choiceNode.GetFirstSelected());
            var replacement = new NeoMemberStringWritable(client,
                (StringMember)firstSelected!.member, firstSelected.overrideValueId,
                firstSelected.ownership);
            Assert.AreSame(replacement, choiceNode.GetFirstSelected());
            var readOnlyReplacement = new NeoMemberString(client,
                (StringMember)firstSelected.member, firstSelected.overrideValueId,
                firstSelected.ownership);
            Assert.IsInstanceOf<NeoMemberStringWritable>(choiceNode.GetFirstSelected());
            Assert.AreNotSame(readOnlyReplacement, choiceNode.GetFirstSelected());

            Assert.IsTrue(choices.Remove("v-a"));
            Assert.IsFalse(choices.Contains("v-a"));
            Assert.AreEqual(0, choices.Count);

            choices.Add("v-b");
            Assert.AreEqual("v-b", choiceNode.GetFirstSelected()!.overrideValueId);
            Assert.AreNotSame(firstSelected, choiceNode.GetFirstSelected());
            choices.Clear();
            Assert.IsNull(choiceNode.GetFirstSelected());
            Assert.AreEqual(0, choices.Count);
            Assert.GreaterOrEqual(changed, 3);
        }

        [Test]
        public void NeoList_DoesNotExposeUntypedAddOverload()
        {
            Assert.IsNull(typeof(NeoList<string>).GetMethod(
                "Add",
                new[] { typeof(object) }));
            Assert.IsNull(typeof(NeoList<string>).GetMethod(
                "SetValue",
                new[] { typeof(int), typeof(object) }));
        }

        [Test]
        public async Task NeoClient_INeoClientSurface_CommitsAndSerializesSaveData()
        {
            var client = LoadClient(out string initialSave);
            INeoClient host = client;
            Assert.IsNotEmpty(initialSave);

            int changed = 0;
            client.OnSaveValueChanged += _ => changed++;
            client.SetSaveValue(new StringMemberValue
            {
                id = "manual-save-value",
                createdAt = "now",
                updatedAt = "now",
                value = "stored",
            });

            string json = host.SerializeSaveData();
            StringAssert.Contains("manual-save-value", json);
            Assert.AreEqual(1, changed);

            await host.CommitAsync();
        }
    }
}
