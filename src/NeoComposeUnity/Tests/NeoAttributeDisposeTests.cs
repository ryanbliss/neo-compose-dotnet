// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.IO;
using NUnit.Framework;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Tests
{
    /// <summary>
    /// Coverage for the disposal + valueId-tracking surface added to
    /// <see cref="NeoAttribute"/> / <see cref="NeoClient"/>:
    ///
    ///   - <see cref="NeoAttribute.Dispose"/> unregisters from
    ///     <see cref="NeoClient.nodes"/>.
    ///   - Disposing a collection-type parent recursively disposes all
    ///     descendants in <c>childAttributes</c>.
    ///   - <see cref="NeoClient.AddSaveValue"/> +
    ///     <see cref="NeoClient.RemoveSaveOverride"/> fire
    ///     <see cref="NeoClient.OnSaveOverrideChanged"/> with the new
    ///     value-id (or null on removal); subscribed
    ///     <see cref="NeoAttribute"/> nodes refresh their resolved
    ///     <c>value</c> via the chain.
    ///   - <c>*Saved.Remove</c>/<c>RemoveAt</c> on collection types
    ///     dispose the orphaned child node AND cascade-delete the
    ///     orphaned value graph from
    ///     <see cref="ProjectSaveData.values"/>.
    /// </summary>
    public class NeoAttributeDisposeTests
    {
        private const string PackageRoot =
            "Packages/com.ryanbliss.neocompose/Tests";

        private static string LoadFixture(string fileName)
        {
            return File.ReadAllText(Path.Combine(PackageRoot, fileName));
        }

        private static NeoClient LoadClient()
        {
            var loader = new NeoLoader();
            string saveBuffer = "";
            string loadSave() => saveBuffer;
            void handleSave(string file) => saveBuffer = file;
            return loader.Load(LoadFixture("synth-example.json"), loadSave, handleSave);
        }

        private static T RequireAttribute<T>(NeoClient client, string id) where T : Attribute
        {
            if (!client.TryGetAttribute(id, out T? attr))
            {
                Assert.Fail($"Fixture is missing attribute '{id}'");
                throw new System.InvalidOperationException("unreachable");
            }
            return attr;
        }

        // -----------------------------------------------------------------
        // Single-node disposal.
        // -----------------------------------------------------------------

        [Test]
        public void Dispose_UnregistersFromClientNodes()
        {
            var client = LoadClient();
            var nameAttr = RequireAttribute<StringAttribute>(client, "attr-name");

            var node = NeoAttribute.Create(client, nameAttr, null);
            Assert.IsTrue(client.nodes.ContainsKey("attr-name"));

            node.Dispose();
            Assert.IsTrue(node.isDisposed);
            Assert.IsFalse(client.nodes.ContainsKey("attr-name"));
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            var client = LoadClient();
            var nameAttr = RequireAttribute<StringAttribute>(client, "attr-name");
            var node = NeoAttribute.Create(client, nameAttr, null);

            node.Dispose();
            // Calling Dispose twice shouldn't throw or otherwise misbehave.
            Assert.DoesNotThrow(() => node.Dispose());
            Assert.IsTrue(node.isDisposed);
        }

        [Test]
        public void UnregisterNode_DoesntDropDifferentInstanceUnderSameKey()
        {
            // If a second node replaced the first in the registry (via
            // direct `new`, last-write-wins), disposing the first
            // shouldn't yank the second out.
            var client = LoadClient();
            var nameAttr = RequireAttribute<StringAttribute>(client, "attr-name");

            var first = new NeoAttributeString(client, nameAttr, null);
            var second = new NeoAttributeString(client, nameAttr, null);
            Assert.AreSame(second, client.nodes["attr-name"], "Direct new is last-write-wins");

            first.Dispose();
            // First's Dispose tried to unregister "attr-name" but the
            // registry's instance was second, so the entry stays.
            Assert.IsTrue(client.nodes.ContainsKey("attr-name"));
            Assert.AreSame(second, client.nodes["attr-name"]);
        }

        // -----------------------------------------------------------------
        // Recursive disposal on collection types.
        // -----------------------------------------------------------------

        [Test]
        public void DisposingParentCustom_DisposesChildren()
        {
            var client = LoadClient();
            var heroAttr = RequireAttribute<CustomAttribute>(client, "attr-hero");
            // Bind to v-dict so children get walked.
            var hero = (NeoAttributeCustom)NeoAttribute.Create(client, heroAttr, "v-dict");
            Assert.IsTrue(client.nodes.ContainsKey("attr-name_v-name"),
                "Pre-condition: child registered");
            var child = client.nodes["attr-name_v-name"];

            hero.Dispose();
            Assert.IsTrue(hero.isDisposed);
            Assert.IsTrue(child.isDisposed, "Child should be disposed by parent's recursive Dispose");
            Assert.IsFalse(client.nodes.ContainsKey("attr-hero_v-dict"));
            Assert.IsFalse(client.nodes.ContainsKey("attr-name_v-name"));
        }

        // -----------------------------------------------------------------
        // OnSaveOverrideChanged event.
        // -----------------------------------------------------------------

        [Test]
        public void AddSaveValue_FiresOnSaveOverrideChanged()
        {
            var client = LoadClient();
            string? observedAttrId = null;
            string? observedValueId = null;
            client.OnSaveOverrideChanged += (attrId, vid) =>
            {
                observedAttrId = attrId;
                observedValueId = vid;
            };

            var newRow = new StringAttributeValue
            {
                id = "v-new",

                createdAt = "now",
                updatedAt = "now",
                value = "fresh",
            };
            client.AddSaveValue("attr-name", newRow);

            Assert.AreEqual("attr-name", observedAttrId);
            Assert.AreEqual("v-new", observedValueId);
        }

        [Test]
        public void RemoveSaveOverride_FiresOnSaveOverrideChangedWithNullId()
        {
            var client = LoadClient();
            // Seed an override.
            client.AddSaveValue("attr-name", new StringAttributeValue
            {
                id = "v-seed",
                createdAt = "now", updatedAt = "now", value = "seeded",
            });

            string? observedValueId = "sentinel";
            client.OnSaveOverrideChanged += (attrId, vid) =>
            {
                if (attrId == "attr-name") observedValueId = vid;
            };

            bool removed = client.RemoveSaveOverride("attr-name");
            Assert.IsTrue(removed);
            Assert.IsNull(observedValueId, "Removal fires the event with newValueId == null");
        }

        [Test]
        public void NeoAttribute_RefreshesValue_OnSaveOverrideChanged()
        {
            var client = LoadClient();
            var nameAttr = RequireAttribute<StringAttribute>(client, "attr-name");
            // attr-name has no static valueId in the fixture, so the
            // freshly-constructed node has value == null.
            var node = (NeoAttributeString)NeoAttribute.Create(client, nameAttr, null);
            Assert.IsNull(node.value);

            // Add a save override → event fires → node refreshes from
            // the resolved chain → value tracks the new row.
            var newRow = new StringAttributeValue
            {
                id = "v-new",
                createdAt = "now", updatedAt = "now", value = "after-set",
            };
            client.AddSaveValue("attr-name", newRow);

            Assert.IsNotNull(node.value);
            Assert.AreEqual("after-set", node.value!.value);
        }

        [Test]
        public void NeoAttribute_ValueBecomesNull_WhenSaveOverrideRemoved()
        {
            var client = LoadClient();
            var nameAttr = RequireAttribute<StringAttribute>(client, "attr-name");
            client.AddSaveValue("attr-name", new StringAttributeValue
            {
                id = "v-seed",
                createdAt = "now", updatedAt = "now", value = "seeded",
            });

            var node = (NeoAttributeString)NeoAttribute.Create(client, nameAttr, null);
            Assert.IsNotNull(node.value);

            client.RemoveSaveOverride("attr-name");

            // attr-name has no static valueId, no override → value chain
            // resolves to null → cached value cleared.
            Assert.IsNull(node.value);
        }

        // -----------------------------------------------------------------
        // Cascade-delete from saveData.values on collection Removes.
        // -----------------------------------------------------------------

        [Test]
        public void DictionaryRemove_DisposesChild_AndCascadesValueDelete()
        {
            var client = LoadClient();
            var inventoryAttr = RequireAttribute<DictionaryAttribute>(client, "attr-inventory");
            // Materialize a Dict by adding a key — DictionarySaved.Set
            // creates the parent + child rows in saveData.values.
            var inv = (NeoAttributeDictionarySaved)NeoAttribute.CreateSaved(
                client, inventoryAttr, null);
            NeoGeneratedTypesSupport.SetValue(
                inv,
                "sword",
                NeoGeneratedTypesSupport.Value("Excalibur"));

            // Capture the registered child + its valueId before removal.
            Assert.IsTrue(inv.TryGet<NeoAttributeString>("sword", out NeoAttributeString? childBefore));
            string entryValueId = childBefore!.overrideValueId!;
            Assert.IsTrue(client.nodes.ContainsKey($"attr-name_{entryValueId}"));
            // Round-trip through the client to confirm the entry value
            // is sitting in saveData.values.
            Assert.IsTrue(client.TryGetValue<StringAttributeValue>(entryValueId, out _));

            inv.Remove("sword");

            Assert.IsTrue(childBefore.isDisposed);
            Assert.IsFalse(client.nodes.ContainsKey($"attr-name_{entryValueId}"));
            Assert.IsFalse(client.TryGetValue<StringAttributeValue>(entryValueId, out _),
                "Removed entry's value row should be GC'd from saveData");
        }

        [Test]
        public void ListRemoveAt_DisposesChild_AndCascadesValueDelete()
        {
            var client = LoadClient();
            var tagsAttr = RequireAttribute<ListAttribute>(client, "attr-tags");
            var tags = (NeoAttributeListSaved)NeoAttribute.CreateSaved(client, tagsAttr, null);
            NeoGeneratedTypesSupport.AddValue(
                tags,
                NeoGeneratedTypesSupport.Value("first"));
            NeoGeneratedTypesSupport.AddValue(
                tags,
                NeoGeneratedTypesSupport.Value("second"));

            var firstChild = (NeoAttributeString)tags[0];
            string firstValueId = firstChild.overrideValueId!;
            Assert.IsTrue(client.nodes.ContainsKey($"attr-name_{firstValueId}"));
            Assert.IsTrue(client.TryGetValue<StringAttributeValue>(firstValueId, out _));

            tags.RemoveAt(0);

            Assert.IsTrue(firstChild.isDisposed);
            Assert.IsFalse(client.nodes.ContainsKey($"attr-name_{firstValueId}"));
            Assert.IsFalse(client.TryGetValue<StringAttributeValue>(firstValueId, out _),
                "Removed entry's value row should be GC'd from saveData");
            Assert.AreEqual(1, tags.Count);
        }

        [Test]
        public void CustomRemove_DisposesChild_AndCascadesValueDelete()
        {
            var client = LoadClient();
            var heroAttr = RequireAttribute<CustomAttribute>(client, "attr-hero");
            var hero = (NeoAttributeCustomSaved)NeoAttribute.CreateSaved(client, heroAttr, null);
            NeoGeneratedTypesSupport.SetValue(
                hero,
                "Name",
                NeoGeneratedTypesSupport.Value("Aragorn"));

            var nameChild = (NeoAttributeString)hero["Name"];
            string nameValueId = nameChild.overrideValueId!;
            Assert.IsTrue(client.nodes.ContainsKey($"attr-name_{nameValueId}"));
            Assert.IsTrue(client.TryGetValue<StringAttributeValue>(nameValueId, out _));

            hero.Remove("Name");

            Assert.IsTrue(nameChild.isDisposed);
            Assert.IsFalse(client.nodes.ContainsKey($"attr-name_{nameValueId}"));
            Assert.IsFalse(client.TryGetValue<StringAttributeValue>(nameValueId, out _));
        }

        [Test]
        public void RemoveSaveValueAndDescendants_RecursesIntoNestedValues()
        {
            // Builds a small tree directly in saveData and verifies the
            // recursion walks ObjectAttributeValue + ArrayAttributeValue
            // children.
            var client = LoadClient();
            // Use prefixed ids so we don't collide with the synth
            // fixture's authored values map (which already has v-list,
            // v-num, etc. — the cascade test would falsely fail if a
            // removed save id was also present in `data.values`).
            var grandchildA = new StringAttributeValue
            {
                id = "test-gca", createdAt = "x", updatedAt = "x",
                value = "a",
            };
            var grandchildB = new StringAttributeValue
            {
                id = "test-gcb", createdAt = "x", updatedAt = "x",
                value = "b",
            };
            var listChild = new ArrayAttributeValue
            {
                id = "test-list", createdAt = "x", updatedAt = "x",
                value = new[] { "test-gca", "test-gcb" },
            };
            var rootObject = new ObjectAttributeValue
            {
                id = "test-root", createdAt = "x", updatedAt = "x",
                value = new System.Collections.Generic.Dictionary<string, string>
                {
                    { "tags", "test-list" },
                },
            };
            client.SetSaveValue(grandchildA);
            client.SetSaveValue(grandchildB);
            client.SetSaveValue(listChild);
            client.SetSaveValue(rootObject);

            client.RemoveSaveValueAndDescendants("test-root");

            Assert.IsFalse(client.TryGetValue<AttributeValue>("test-root", out _));
            Assert.IsFalse(client.TryGetValue<AttributeValue>("test-list", out _));
            Assert.IsFalse(client.TryGetValue<AttributeValue>("test-gca", out _));
            Assert.IsFalse(client.TryGetValue<AttributeValue>("test-gcb", out _));
        }
    }
}
