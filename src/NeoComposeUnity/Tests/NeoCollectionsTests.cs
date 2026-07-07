// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.IO;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;
using UnityEngine.TestTools;

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

        private static T RequireAttribute<T>(NeoClient client, string id) where T : Attribute
        {
            if (!client.TryGetAttribute(id, out T? attr))
            {
                Assert.Fail($"Fixture is missing attribute '{id}' of type {typeof(T).Name}");
                throw new System.InvalidOperationException("unreachable");
            }
            return attr;
        }

        [Test]
        public void NeoList_AddSetRemove_TracksUnderlyingSavedList()
        {
            var client = LoadClient(out _);
            var tagsAttr = RequireAttribute<ListAttribute>(client, "attr-tags");
            var tagsNode = (NeoAttributeListWritable)NeoAttribute.CreateWritable(client, tagsAttr, null);
            var tags = new NeoList<string>(
                client,
                tagsNode,
                (_, attr) => ((NeoAttributeString)attr).value?.value ?? "",
                NeoGeneratedTypesSupport.Value);

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
            var tagsAttr = RequireAttribute<ListAttribute>(client, "attr-tags");
            var tagsNode = (NeoAttributeListWritable)NeoAttribute.CreateWritable(client, tagsAttr, null);
            var tags = new NeoList<string>(
                client,
                tagsNode,
                (_, attr) => ((NeoAttributeString)attr).value?.value ?? "",
                NeoGeneratedTypesSupport.Value);

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
            var inventoryAttr = RequireAttribute<DictionaryAttribute>(client, "attr-inventory");
            var inventoryNode = (NeoAttributeDictionaryWritable)NeoAttribute.CreateWritable(
                client,
                inventoryAttr,
                null);
            var inventory = new NeoDictionary<string>(
                client,
                inventoryNode,
                (_, attr) => ((NeoAttributeString)attr).value?.value ?? "",
                NeoGeneratedTypesSupport.Value);

            int changed = 0;
            using var inventorySubscription = inventory.OnChanged((_, _) => changed++);

            inventory["sword"] = "Excalibur";
            inventory.Add("shield", "Aegis");

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
        // ONLY from the authored default (`attr-inventory` = { sword, shield })
        // with no bound value row yet. Save ownership routes writes to the save
        // store so persistence is observable via SerializeSaveData. Regression
        // surface for the clone-on-write seed: the first mutation must clone the
        // default map, not start empty.
        private static NeoDictionary<string> LoadDefaultOnlyInventory(NeoClient client)
        {
            var inventoryAttr = RequireAttribute<DictionaryAttribute>(client, "attr-inventory");
            var inventoryNode = (NeoAttributeDictionaryWritable)NeoAttribute.CreateWritable(
                client,
                inventoryAttr,
                null,
                NeoValueOwnership.Save);
            return new NeoDictionary<string>(
                client,
                inventoryNode,
                (_, attr) => ((NeoAttributeString)attr).value?.value ?? "",
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
            var choiceAttr = RequireAttribute<LookupAttribute>(client, "attr-choice");
            // Stable-id overlay: pin the lookup to its target collection value by
            // id and shadow that value in the save store (the old override-map
            // rebind of attr-tags is gone).
            choiceAttr.collectionValueId = "v-tags-target";
            client.SetSaveValue(new ArrayAttributeValue
            {
                id = "v-tags-target",
                createdAt = "now",
                updatedAt = "now",
                value = new[] { "v-a", "v-b" },
            });

            var choiceNode = (NeoAttributeLookupWritable)NeoAttribute.CreateWritable(
                client,
                choiceAttr,
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

            Assert.IsTrue(choices.Remove("v-a"));
            Assert.IsFalse(choices.Contains("v-a"));
            Assert.AreEqual(0, choices.Count);

            choices.Add("v-b");
            choices.Clear();
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
        public void NeoClient_INeoClientSurface_CommitsAndSerializesSaveData()
        {
            var client = LoadClient(out string initialSave);
            INeoClient host = client;
            Assert.IsNotEmpty(initialSave);

            int changed = 0;
            client.OnSaveValueChanged += _ => changed++;
            client.SetSaveValue(new StringAttributeValue
            {
                id = "manual-save-value",
                createdAt = "now",
                updatedAt = "now",
                value = "stored",
            });

            string json = host.SerializeSaveData();
            StringAssert.Contains("manual-save-value", json);
            Assert.AreEqual(1, changed);

            LogAssert.Expect(
                UnityEngine.LogType.Warning,
                new System.Text.RegularExpressions.Regex(
                    "NeoCompose save contains 1 unlinked value"));
            Assert.DoesNotThrow(() => host.CommitAsync().GetAwaiter().GetResult());
        }
    }
}
