// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// Unordered List attributes (listKind: "unordered"): the stored value is
    /// only the null-vs-present discriminator; membership is the set of live
    /// rows carrying the list value's id as their containerId, layered like
    /// the value overlay (authored + save/session joins, tombstones subtract).
    /// </summary>
    public class NeoUnorderedListTests
    {
        private const string BagTypeId = "bag-type";
        private const string ItemTypeId = "item-type";
        private const string ItemsListValueId = "bag-items-list";
        private const string NullItemsListValueId = "bag-null-items-list";

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
            client.AddSaveValue("item-c", new ObjectAttributeValue
            {
                id = "item-c",
                typeId = ItemTypeId,
                containerId = ItemsListValueId,
                value = new Dictionary<string, string>(),
            });
            CollectionAssert.AreEqual(
                new[] { "item-a", "item-b", "item-c" },
                client.GetUnorderedListEntryIds(ItemsListValueId).ToArray());

            // A save tombstone at an authored member id subtracts it.
            client.AddSaveValue("item-a", new NullAttributeValue
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
                NeoValueOwnership.Save, addedId, out AttributeValue? addedRow));
            Assert.AreEqual(ItemsListValueId, addedRow!.containerId);

            // Membership lives on the entry row: the container's discriminator
            // was NOT shadowed into the save store by the add.
            Assert.IsFalse(client.saveValues.ContainsKey(ItemsListValueId));
            var authoredContainer = (ArrayAttributeValue)client.values[ItemsListValueId];
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
                NeoValueOwnership.Save, "item-a", out AttributeValue? tombstone));
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
                NeoValueOwnership.Save, ItemsListValueId, out ArrayAttributeValue? shadow));
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
                NeoValueOwnership.Save, ItemsListValueId, out ArrayAttributeValue? shadow));
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
            client.AddSaveValue(ItemsListValueId, new ArrayAttributeValue
            {
                id = ItemsListValueId,
                createdAt = "2026-01-01T00:00:00.000Z",
                updatedAt = "2026-01-01T00:00:00.000Z",
                value = null,
            });

            CollectionAssert.Contains(client.FindUnlinkedSaveValueIds(), addedId);
        }

        // ------------------------------------------------------------------
        // Fixture.
        // ------------------------------------------------------------------

        private static NeoAttributeListWritable ResolveItems(NeoClient client) =>
            ResolveList(client, "Items");

        private static NeoAttributeListWritable ResolveList(NeoClient client, string key)
        {
            var bag = client.save.Get<NeoAttributeCustomWritable>("Bag");
            return bag.Get<NeoAttributeListWritable>(key);
        }

        private static ProjectData BuildProjectData()
        {
            var rootType = new CustomType
            {
                id = "root-type",
                projectId = "project-a",
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            var saveRootType = new CustomType
            {
                id = "save-root-type",
                projectId = "project-a",
                name = "Save Root",
                schema = new Dictionary<string, string>
                {
                    ["Bag"] = "bag-attribute",
                },
            };
            var bagType = new CustomType
            {
                id = BagTypeId,
                projectId = "project-a",
                name = "Bag",
                schema = new Dictionary<string, string>
                {
                    ["Items"] = "items-attribute",
                    ["NullItems"] = "null-items-attribute",
                },
            };
            var itemType = new CustomType
            {
                id = ItemTypeId,
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
                    rootAssetsAttributeId = "root-assets",
                    rootSaveFileAttributeId = "root-save",
                    rootSessionAttributeId = "root-session",
                },
                attributes = new Dictionary<string, NeoCompose.Runtime.Json.Attribute>
                {
                    ["root-assets"] = RootAttribute("root-assets", "root-assets-value", rootType.id),
                    ["root-save"] = RootAttribute("root-save", "root-save-value", saveRootType.id),
                    ["root-session"] = RootAttribute("root-session", "root-session-value", rootType.id),
                    ["bag-attribute"] = new CustomAttribute
                    {
                        id = "bag-attribute",
                        projectId = "project-a",
                        name = "Bag",
                        type = AttributeType.Custom,
                        customTypeId = BagTypeId,
                        required = true,
                    },
                    ["items-attribute"] = new ListAttribute
                    {
                        id = "items-attribute",
                        projectId = "project-a",
                        name = "Items",
                        type = AttributeType.List,
                        entryAttributeId = "item-entry-attribute",
                        listKind = NeoListKinds.Unordered,
                        required = true,
                    },
                    ["null-items-attribute"] = new ListAttribute
                    {
                        id = "null-items-attribute",
                        projectId = "project-a",
                        name = "NullItems",
                        type = AttributeType.List,
                        entryAttributeId = "item-entry-attribute",
                        listKind = NeoListKinds.Unordered,
                    },
                    ["item-entry-attribute"] = new CustomAttribute
                    {
                        id = "item-entry-attribute",
                        projectId = "project-a",
                        name = "Item",
                        type = AttributeType.Custom,
                        customTypeId = ItemTypeId,
                        required = true,
                    },
                },
                values = new Dictionary<string, AttributeValue>
                {
                    ["root-assets-value"] = ObjectValue("root-assets-value", rootType.id, new()),
                    ["root-save-value"] = ObjectValue(
                        "root-save-value",
                        saveRootType.id,
                        new Dictionary<string, string> { ["Bag"] = "bag-value" }),
                    ["root-session-value"] = ObjectValue("root-session-value", rootType.id, new()),
                    ["bag-value"] = ObjectValue(
                        "bag-value",
                        BagTypeId,
                        new Dictionary<string, string>
                        {
                            ["Items"] = ItemsListValueId,
                            ["NullItems"] = NullItemsListValueId,
                        }),
                    // Present discriminator: [] — membership is joined.
                    [ItemsListValueId] = new ArrayAttributeValue
                    {
                        id = ItemsListValueId,
                        value = System.Array.Empty<string>(),
                    },
                    // Null discriminator: the list resolves as null.
                    [NullItemsListValueId] = new ArrayAttributeValue
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
                types = new Dictionary<string, CustomType>
                {
                    [rootType.id] = rootType,
                    [saveRootType.id] = saveRootType,
                    [BagTypeId] = bagType,
                    [ItemTypeId] = itemType,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
        }

        private static CustomAttribute RootAttribute(string id, string valueId, string customTypeId)
        {
            return new CustomAttribute
            {
                id = id,
                projectId = "project-a",
                name = id,
                type = AttributeType.Custom,
                required = true,
                valueId = valueId,
                customTypeId = customTypeId,
            };
        }

        private static ObjectAttributeValue ObjectValue(
            string id,
            string typeId,
            Dictionary<string, string> record)
        {
            return new ObjectAttributeValue
            {
                id = id,
                typeId = typeId,
                value = record,
            };
        }

        private static ObjectAttributeValue MemberValue(string id, string containerId)
        {
            return new ObjectAttributeValue
            {
                id = id,
                typeId = ItemTypeId,
                containerId = containerId,
                value = new Dictionary<string, string>(),
            };
        }
    }
}
