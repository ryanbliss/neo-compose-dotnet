// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.IO;
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
            var loader = new NeoLoader();
            string buffer = "";
            string loadSave() => buffer;
            void handleSave(string file) => buffer = file;
            var client = loader.Load(LoadFixture("synth-example.json"), loadSave, handleSave);
            saveBuffer = buffer;
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
            var tagsNode = (NeoAttributeListSaved)NeoAttribute.CreateSaved(client, tagsAttr, null);
            var tags = new NeoList<string>(
                client,
                tagsNode,
                (_, attr) => ((NeoAttributeString)attr).value?.value ?? "",
                value => value);

            int changed = 0;
            tags.OnChanged += () => changed++;

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
        public void NeoDictionary_SetRemove_TracksUnderlyingSavedDictionary()
        {
            var client = LoadClient(out _);
            var inventoryAttr = RequireAttribute<DictionaryAttribute>(client, "attr-inventory");
            var inventoryNode = (NeoAttributeDictionarySaved)NeoAttribute.CreateSaved(
                client,
                inventoryAttr,
                null);
            var inventory = new NeoDictionary<string>(
                client,
                inventoryNode,
                (_, attr) => ((NeoAttributeString)attr).value?.value ?? "",
                value => value);

            int changed = 0;
            inventory.OnChanged += () => changed++;

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

        [Test]
        public void NeoValuePayload_CarriesTypeIdIntoCreatedRows()
        {
            var client = LoadClient(out _);
            var tagsAttr = RequireAttribute<ListAttribute>(client, "attr-tags");
            var tags = (NeoAttributeListSaved)NeoAttribute.CreateSaved(client, tagsAttr, null);

            tags.Add((object)new NeoValuePayload("typed-tag", "type-special"));

            var child = (NeoAttributeString)tags[0];
            Assert.IsNotNull(child.overrideValueId);
            Assert.IsTrue(client.TryGetValue<StringAttributeValue>(
                child.overrideValueId!,
                out StringAttributeValue? row));
            Assert.AreEqual("typed-tag", row!.value);
            Assert.AreEqual("type-special", row.typeId);
        }

        [Test]
        public void NeoClient_SaveAndSerializeSaveData_ArePublicHostSurface()
        {
            var client = LoadClient(out string initialSave);
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

            string json = client.SerializeSaveData();
            StringAssert.Contains("manual-save-value", json);
            Assert.AreEqual(1, changed);

            Assert.DoesNotThrow(() => client.Save());
        }
    }
}
