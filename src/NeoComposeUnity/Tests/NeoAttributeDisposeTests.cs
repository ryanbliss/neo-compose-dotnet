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
    ///   - A writable <c>Set</c> shadows the value at its stable id and a
    ///     bound <see cref="NeoAttribute"/> refreshes its resolved <c>value</c>
    ///     via <see cref="NeoClient.OnSaveValueChanged"/>; <c>ClearOverride</c>
    ///     drops the shadow back to the authored default.
        ///   - <c>*Writable.Remove</c>/<c>RemoveAt</c> on collection types
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
            return NeoTestSaveStack.LoadClient(LoadFixture("synth-example.json"));
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
            Assert.IsTrue(client.nodes.ContainsKey("asset:attr-name"));

            node.Dispose();
            Assert.IsTrue(node.isDisposed);
            Assert.IsFalse(client.nodes.ContainsKey("asset:attr-name"));
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
            Assert.AreSame(second, client.nodes["asset:attr-name"], "Direct new is last-write-wins");

            first.Dispose();
            // First's Dispose tried to unregister "attr-name" but the
            // registry's instance was second, so the entry stays.
            Assert.IsTrue(client.nodes.ContainsKey("asset:attr-name"));
            Assert.AreSame(second, client.nodes["asset:attr-name"]);
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
            Assert.IsTrue(client.nodes.ContainsKey("asset:attr-name_v-name"),
                "Pre-condition: child registered");
            var child = client.nodes["asset:attr-name_v-name"];

            hero.Dispose();
            Assert.IsTrue(hero.isDisposed);
            Assert.IsTrue(child.isDisposed, "Child should be disposed by parent's recursive Dispose");
            Assert.IsFalse(client.nodes.ContainsKey("asset:attr-hero_v-dict"));
            Assert.IsFalse(client.nodes.ContainsKey("asset:attr-name_v-name"));
        }

        // -----------------------------------------------------------------
        // Save value change events.
        // -----------------------------------------------------------------

        [Test]
        public void AddSaveValue_FiresOnSaveValueChanged()
        {
            var client = LoadClient();
            string? observedValueId = null;
            client.OnSaveValueChanged += vid => observedValueId = vid;

            client.AddSaveValue("attr-name", new StringAttributeValue
            {
                id = "v-new",
                createdAt = "now",
                updatedAt = "now",
                value = "fresh",
            });

            Assert.AreEqual("v-new", observedValueId);
        }

        [Test]
        public void NeoAttribute_TracksValue_AfterWritableSet()
        {
            var client = LoadClient();
            var nameAttr = RequireAttribute<StringAttribute>(client, "attr-name");
            // attr-name has an authored default in the fixture, so a
            // freshly-constructed standalone node resolves through that default
            // until a writable row is set.
            var node = (NeoAttributeStringWritable)NeoAttribute.CreateWritable(
                client,
                nameAttr,
                null,
                NeoValueOwnership.Save);
            Assert.IsNotNull(node.value);
            Assert.AreEqual("Hero", node.value!.value);

            // Stable-id overlay: a write mints a value bound to this
            // (parentless) node and tracks it directly — no override-map hop.
            node.SetLiteralOverride("after-set");

            Assert.IsNotNull(node.value);
            Assert.AreEqual("after-set", node.value!.value);
        }

        [Test]
        public void NeoAttribute_ValueRevertsToDefault_WhenWritableShadowCleared()
        {
            var client = LoadClient();
            var nameAttr = RequireAttribute<StringAttribute>(client, "attr-name");
            var node = (NeoAttributeStringWritable)NeoAttribute.CreateWritable(
                client,
                nameAttr,
                null,
                NeoValueOwnership.Save);
            node.SetLiteralOverride("seeded");
            Assert.IsNotNull(node.value);

            node.ClearOverride();

            // attr-name has no authored default → clearing the shadow leaves
            // the resolved value null.
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
            var inv = (NeoAttributeDictionaryWritable)NeoAttribute.CreateWritable(
                client, inventoryAttr, null);
            NeoGeneratedTypesSupport.SetValue(
                inv,
                "sword",
                NeoGeneratedTypesSupport.Value("Excalibur"));

            // Capture the registered child + its valueId before removal.
            Assert.IsTrue(inv.TryGet<NeoAttributeString>("sword", out NeoAttributeString? childBefore));
            string entryValueId = childBefore!.overrideValueId!;
            Assert.IsTrue(client.nodes.ContainsKey($"session:attr-name_{entryValueId}"));
            // Round-trip through the client to confirm the entry value
            // is sitting in saveData.values.
            Assert.IsTrue(client.TryGetValue<StringAttributeValue>(entryValueId, out _));

            inv.Remove("sword");

            Assert.IsTrue(childBefore.isDisposed);
            Assert.IsFalse(client.nodes.ContainsKey($"session:attr-name_{entryValueId}"));
            Assert.IsFalse(client.TryGetValue<StringAttributeValue>(entryValueId, out _),
                "Removed entry's value row should be GC'd from saveData");
        }

        [Test]
        public void ListRemoveAt_DisposesChild_AndCascadesValueDelete()
        {
            var client = LoadClient();
            var tagsAttr = RequireAttribute<ListAttribute>(client, "attr-tags");
            var tags = (NeoAttributeListWritable)NeoAttribute.CreateWritable(client, tagsAttr, null);
            NeoGeneratedTypesSupport.AddValue(
                tags,
                NeoGeneratedTypesSupport.Value("first"));
            NeoGeneratedTypesSupport.AddValue(
                tags,
                NeoGeneratedTypesSupport.Value("second"));

            var firstChild = (NeoAttributeString)tags[0];
            string firstValueId = firstChild.overrideValueId!;
            Assert.IsTrue(client.nodes.ContainsKey($"session:attr-name_{firstValueId}"));
            Assert.IsTrue(client.TryGetValue<StringAttributeValue>(firstValueId, out _));

            tags.RemoveAt(0);

            Assert.IsTrue(firstChild.isDisposed);
            Assert.IsFalse(client.nodes.ContainsKey($"session:attr-name_{firstValueId}"));
            Assert.IsFalse(client.TryGetValue<StringAttributeValue>(firstValueId, out _),
                "Removed entry's value row should be GC'd from writable data");
            Assert.AreEqual(1, tags.Count);
        }

        [Test]
        public void CustomRemove_DisposesChild_AndCascadesValueDelete()
        {
            var client = LoadClient();
            var heroAttr = RequireAttribute<CustomAttribute>(client, "attr-hero");
            var hero = (NeoAttributeCustomWritable)NeoAttribute.CreateWritable(client, heroAttr, null);
            NeoGeneratedTypesSupport.SetValue(
                hero,
                "Name",
                NeoGeneratedTypesSupport.Value("Aragorn"));

            var nameChild = (NeoAttributeString)hero["Name"];
            string nameValueId = nameChild.overrideValueId!;
            Assert.IsTrue(client.nodes.ContainsKey($"session:attr-name_{nameValueId}"));
            Assert.IsTrue(client.TryGetValue<StringAttributeValue>(nameValueId, out _));

            hero.Remove("Name");

            Assert.IsTrue(nameChild.isDisposed);
            Assert.IsFalse(client.nodes.ContainsKey($"session:attr-name_{nameValueId}"));
            Assert.IsFalse(client.TryGetValue<StringAttributeValue>(nameValueId, out _));
        }

        [Test]
        public void RemoveSaveValueAndDescendants_RecursesIntoNestedValues()
        {
            // Builds a small typed tree directly in saveData and verifies
            // schema-authoritative Custom + List ownership is followed.
            var client = LoadClient();
            ((System.Collections.Generic.Dictionary<string, CustomType>)client.types)["test-gc-type"] =
                new CustomType
                {
                    id = "test-gc-type",
                    name = "GcRoot",
                    schema = new System.Collections.Generic.Dictionary<string, string>
                    {
                        ["Tags"] = "attr-tags",
                    },
                };
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
                id = "test-root", typeId = "test-gc-type", createdAt = "x", updatedAt = "x",
                value = new System.Collections.Generic.Dictionary<string, string>
                {
                    { "Tags", "test-list" },
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

        [Test]
        public void RemoveSaveValueAndDescendants_PreservesLookupTargets()
        {
            var client = LoadClient();
            ((System.Collections.Generic.Dictionary<string, CustomType>)client.types)["test-lookup-owner-type"] =
                new CustomType
                {
                    id = "test-lookup-owner-type",
                    name = "LookupOwner",
                    schema = new System.Collections.Generic.Dictionary<string, string>
                    {
                        ["Choice"] = "attr-choice",
                    },
                };
            client.SetSaveValue(new StringAttributeValue
            {
                id = "test-lookup-target", value = "referenced", createdAt = "x", updatedAt = "x",
            });
            client.SetSaveValue(new ArrayAttributeValue
            {
                id = "test-lookup-row", value = new[] { "test-lookup-target" },
                createdAt = "x", updatedAt = "x",
            });
            client.SetSaveValue(new ObjectAttributeValue
            {
                id = "test-lookup-owner", typeId = "test-lookup-owner-type",
                value = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["Choice"] = "test-lookup-row",
                },
                createdAt = "x", updatedAt = "x",
            });

            client.RemoveSaveValueAndDescendants("test-lookup-owner");

            Assert.IsFalse(client.saveValues.ContainsKey("test-lookup-owner"));
            Assert.IsFalse(client.saveValues.ContainsKey("test-lookup-row"));
            Assert.IsTrue(client.saveValues.ContainsKey("test-lookup-target"),
                "Lookup selections are references and must not be recursively deleted");
        }

        [Test]
        public void RemoveSaveValueAndDescendants_DefensivelyHandlesOwnedCycles()
        {
            var client = LoadClient();
            var attributes =
                (System.Collections.Generic.Dictionary<string, NeoCompose.Runtime.Json.Attribute>)client.attributes;
            attributes["test-cycle-child"] = new CustomAttribute
            {
                id = "test-cycle-child", name = "Child", type = AttributeType.Custom,
                customTypeId = "test-cycle-type",
            };
            ((System.Collections.Generic.Dictionary<string, CustomType>)client.types)["test-cycle-type"] =
                new CustomType
                {
                    id = "test-cycle-type",
                    name = "Cycle",
                    schema = new System.Collections.Generic.Dictionary<string, string>
                    {
                        ["Child"] = "test-cycle-child",
                    },
                };
            client.SetSaveValue(new ObjectAttributeValue
            {
                id = "test-cycle-a", typeId = "test-cycle-type",
                value = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["Child"] = "test-cycle-b",
                },
            });
            client.SetSaveValue(new ObjectAttributeValue
            {
                id = "test-cycle-b", typeId = "test-cycle-type",
                value = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["Child"] = "test-cycle-a",
                },
            });

            Assert.DoesNotThrow(() => client.RemoveSaveValueAndDescendants("test-cycle-a"));
            Assert.IsFalse(client.saveValues.ContainsKey("test-cycle-a"));
            Assert.IsFalse(client.saveValues.ContainsKey("test-cycle-b"));
        }

        [Test]
        public void TypedUnlinkedRemoval_CollectsDetachedDictionaryAndListRows()
        {
            var client = LoadClient();
            var dictionaryAttribute = new DictionaryAttribute
            {
                id = "test-detached-dictionary-attribute",
                name = "Detached dictionary",
                type = AttributeType.Dictionary,
                keyKind = "string",
                entryAttributeId = "attr-tags",
            };
            client.SetSaveValue(new StringAttributeValue
            {
                id = "test-detached-leaf", value = "leaf",
            });
            client.SetSaveValue(new ArrayAttributeValue
            {
                id = "test-detached-list", value = new[] { "test-detached-leaf" },
            });
            client.SetSaveValue(new ObjectAttributeValue
            {
                id = "test-detached-dictionary",
                value = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["entry"] = "test-detached-list",
                },
            });

            client.RemoveWritableValueAndDescendantsIfUnlinked(
                NeoValueOwnership.Save,
                "test-detached-dictionary",
                dictionaryAttribute);

            Assert.IsFalse(client.saveValues.ContainsKey("test-detached-dictionary"));
            Assert.IsFalse(client.saveValues.ContainsKey("test-detached-list"));
            Assert.IsFalse(client.saveValues.ContainsKey("test-detached-leaf"));
        }

        [Test]
        public void TypedRemoval_DoesNotCrossDeclaredStorageOwnership()
        {
            var client = LoadClient();
            var attributes =
                (System.Collections.Generic.Dictionary<string, NeoCompose.Runtime.Json.Attribute>)client.attributes;
            attributes["test-session-entry"] = new StringAttribute
            {
                id = "test-session-entry",
                name = "Session entry",
                type = AttributeType.String,
                storage = "session",
            };
            var dictionaryAttribute = new DictionaryAttribute
            {
                id = "test-cross-storage-dictionary",
                name = "Cross-storage dictionary",
                type = AttributeType.Dictionary,
                keyKind = "string",
                entryAttributeId = "test-session-entry",
            };
            client.SetWritableValue(NeoValueOwnership.Session, new StringAttributeValue
            {
                id = "test-session-owned-leaf", value = "keep",
            });
            client.SetSaveValue(new ObjectAttributeValue
            {
                id = "test-save-dictionary",
                value = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["entry"] = "test-session-owned-leaf",
                },
            });

            client.RemoveWritableValueAndDescendants(
                NeoValueOwnership.Save,
                "test-save-dictionary",
                dictionaryAttribute);

            Assert.IsFalse(client.saveValues.ContainsKey("test-save-dictionary"));
            Assert.IsTrue(client.sessionValues.ContainsKey("test-session-owned-leaf"));
        }

        // -----------------------------------------------------------------
        // Tombstone removal (mark: "removed").
        // -----------------------------------------------------------------

        [Test]
        public void CustomUnset_TombstonesFieldSparsely_ResolvesUnset()
        {
            var client = LoadClient();
            var heroAttr = RequireAttribute<CustomAttribute>(client, "attr-hero");
            var hero = (NeoAttributeCustomWritable)NeoAttribute.CreateWritable(client, heroAttr, null);
            NeoGeneratedTypesSupport.SetValue(
                hero, "Name", NeoGeneratedTypesSupport.Value("Aragorn"));
            string nameId = hero.Get<NeoAttributeString>("Name").overrideValueId!;

            hero.Unset("Name");

            // Sparse: the record still references the key (it is not dropped), but
            // the child resolves as unset through the tombstone at its stable id.
            Assert.IsTrue(hero.value!.value!.ContainsKey("Name"));
            Assert.AreEqual(nameId, hero.value.value["Name"]);
            Assert.IsTrue(client.sessionValues.TryGetValue(nameId, out AttributeValue? row));
            Assert.IsTrue(row!.IsRemoved);
            Assert.IsTrue(hero.TryGet("Name", out NeoAttributeString? refetched));
            Assert.IsNull(refetched!.value);
        }

        [Test]
        public void CustomUnset_RequiredField_Throws()
        {
            var client = LoadClient();
            var heroAttr = RequireAttribute<CustomAttribute>(client, "attr-hero");
            var hero = (NeoAttributeCustomWritable)NeoAttribute.CreateWritable(client, heroAttr, null);
            NeoGeneratedTypesSupport.SetValue(
                hero, "Name", NeoGeneratedTypesSupport.Value("Aragorn"));
            RequireAttribute<StringAttribute>(client, "attr-name").required = true;

            Assert.Throws<System.InvalidOperationException>(() => hero.Unset("Name"));
        }

        [Test]
        public void CustomUnset_HardRemovesField_ReclaimsOrphanedSubtree()
        {
            var client = LoadClient();
            // Shadow the save root referencing a Heroes list → one hero → a Name
            // leaf, all written into the save store.
            client.SetSaveValue(new ObjectAttributeValue
            {
                id = "v-root-save", typeId = "type-root", createdAt = "x", updatedAt = "x",
                value = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["Heroes"] = "heroes-list",
                },
            });
            client.SetSaveValue(new ArrayAttributeValue
            {
                id = "heroes-list", createdAt = "x", updatedAt = "x",
                value = new[] { "hero-1" },
            });
            client.SetSaveValue(new ObjectAttributeValue
            {
                id = "hero-1", typeId = "type-hero", createdAt = "x", updatedAt = "x",
                value = new System.Collections.Generic.Dictionary<string, string>
                {
                    ["Name"] = "hero-1-name",
                },
            });
            client.SetSaveValue(new StringAttributeValue
            {
                id = "hero-1-name", createdAt = "x", updatedAt = "x", value = "Aragorn",
            });
            Assert.IsTrue(client.saveValues.ContainsKey("hero-1"));
            Assert.IsTrue(client.saveValues.ContainsKey("hero-1-name"));

            client.save.Unset("Heroes");

            // The Heroes list is tombstoned in place; the orphaned hero + its Name
            // leaf are reclaimed from the save store (hard remove).
            Assert.IsTrue(client.saveValues.TryGetValue("heroes-list", out AttributeValue? listRow));
            Assert.IsTrue(listRow!.IsRemoved);
            Assert.IsFalse(client.saveValues.ContainsKey("hero-1"));
            Assert.IsFalse(client.saveValues.ContainsKey("hero-1-name"));
            // Sparse: the root still references the Heroes slot (record untouched).
            var rootRow = (ObjectAttributeValue)client.saveValues["v-root-save"];
            Assert.AreEqual("heroes-list", rootRow.value!["Heroes"]);
        }
    }
}
