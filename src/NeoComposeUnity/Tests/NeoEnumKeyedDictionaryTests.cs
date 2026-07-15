// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using NUnit.Framework;
using Member = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    /// <summary>
    /// specs/dictionary-key-classes.md §9: two-arity enum-keyed dictionary
    /// wrappers + the <c>keyKind</c>/<c>keyEnumId</c> JSON read layer. The
    /// wrappers delegate to the same string-keyed storage as the
    /// single-arity pair; <see cref="ItemSlot"/> mirrors the generated enum
    /// wrapper shape (static per-option instances, interned
    /// <see cref="ItemSlot.FromOptionId"/>, implicit string conversions).
    /// </summary>
    public class NeoEnumKeyedDictionaryTests
    {
        private const string EnumId = "item-slot-enum";
        private const string InventoryValueId = "inventory-value";

        /// <summary>
        /// Hand-written stand-in for a codegen-emitted enum wrapper: sealed,
        /// interned per-option instances, <c>FromOptionId</c> factory that
        /// mints ad-hoc instances for unknown ids, implicit string
        /// conversions both ways, value equality on <c>optionId</c>.
        /// </summary>
        private sealed class ItemSlot : IEquatable<ItemSlot>
        {
            private static readonly Dictionary<string, ItemSlot> values = new();
            public string optionId { get; }

            private ItemSlot(string optionId)
            {
                this.optionId = optionId;
            }

            public static readonly ItemSlot Sword = FromOptionId("sword");
            public static readonly ItemSlot Shield = FromOptionId("shield");

            public static ItemSlot FromOptionId(string optionId)
            {
                if (values.TryGetValue(optionId, out var known)) return known;
                var created = new ItemSlot(optionId);
                values[optionId] = created;
                return created;
            }

            public static implicit operator string(ItemSlot value) => value.optionId;
            public static implicit operator ItemSlot(string optionId) => FromOptionId(optionId);
            public override string ToString() => optionId;
            public bool Equals(ItemSlot? other) => other is not null && optionId == other.optionId;
            public override bool Equals(object? obj) => Equals(obj as ItemSlot);
            public override int GetHashCode() => optionId.GetHashCode();
        }

        // ------------------------------------------------------------------
        // Fixture: a Save-stored enum-keyed Dictionary with an empty
        // authored map, so writes exercise the clone-on-write shadow path.
        // ------------------------------------------------------------------

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
                    ["Inventory"] = "inventory-member",
                },
            };

            return new ProjectData
            {
                project = new Project
                {
                    id = "project-a",
                    _id = "project-a",
                    name = "Enum-Keyed Dictionaries",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = new Dictionary<string, Member>
                {
                    ["root-assets"] = RootMember("root-assets", "root-assets-value", rootClass.id),
                    ["root-save"] = RootMember("root-save", "root-save-value", saveRootClass.id),
                    ["root-session"] = RootMember("root-session", "root-session-value", rootClass.id),
                    ["inventory-member"] = new DictionaryMember
                    {
                        id = "inventory-member",
                        projectId = "project-a",
                        name = "Inventory",
                        kind = MemberKind.Dictionary,
                        entryMemberId = "entry-member",
                        keyKind = NeoDictionaryKeyKinds.Enum,
                        keyEnumId = EnumId,
                        required = true,
                    },
                    ["entry-member"] = new StringMember
                    {
                        id = "entry-member",
                        projectId = "project-a",
                        name = "Item Name",
                        kind = MemberKind.String,
                        localizable = false,
                    },
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["root-assets-value"] = ObjectValue("root-assets-value", rootClass.id, new()),
                    ["root-save-value"] = ObjectValue(
                        "root-save-value",
                        saveRootClass.id,
                        new Dictionary<string, string> { ["Inventory"] = InventoryValueId }),
                    ["root-session-value"] = ObjectValue("root-session-value", rootClass.id, new()),
                    [InventoryValueId] = new ObjectMemberValue
                    {
                        id = InventoryValueId,
                        value = new Dictionary<string, string>(),
                    },
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [rootClass.id] = rootClass,
                    [saveRootClass.id] = saveRootClass,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>
                {
                    [EnumId] = new NeoCompose.Runtime.Json.Enum
                    {
                        id = EnumId,
                        projectId = "project-a",
                        name = "Item Slot",
                        options = new Dictionary<string, EnumOption>
                        {
                            ["sword"] = new EnumOption { text = "Sword" },
                            ["shield"] = new EnumOption { text = "Shield" },
                        },
                    },
                },
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
                required = true,
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

        private static NeoDictionary<ItemSlot, string> CreateWritableInventory(
            NeoClient client,
            out NeoMemberDictionaryWritable node)
        {
            node = client.save.Get<NeoMemberDictionaryWritable>("Inventory");
            return new NeoDictionary<ItemSlot, string>(
                client,
                node,
                (_, member) => ((NeoMemberString)member).value?.value ?? "",
                NeoGeneratedTypesSupport.Value,
                ItemSlot.FromOptionId,
                slot => slot.optionId);
        }

        [Test]
        public void KeyCodec_RoundTripsThroughWireStrings()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var inventory = CreateWritableInventory(client, out var node);

            inventory.Add(ItemSlot.Sword, "Excalibur");

            // Wire key is the option id; the typed key materializes back to
            // the interned wrapper instance.
            Assert.IsTrue(node.ContainsKey("sword"));
            var keys = new List<ItemSlot>(inventory.Keys);
            Assert.AreEqual(1, keys.Count);
            Assert.AreSame(ItemSlot.Sword, keys[0]);
        }

        [Test]
        public void TwoArity_IndexerTryGetAddRemove_TracksUnderlyingStorage()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            // Construct through the exact shape the web codegen emits for a
            // writable enum-keyed dictionary property (getOrCreate factory +
            // trailing beforeWrite/isReadOnly guards).
            var saveRoot = client.save;
            var inventory = new NeoDictionary<ItemSlot, string>(
                client,
                saveRoot.Get<NeoMemberDictionaryWritable>("Inventory"),
                () => saveRoot.GetOrCreateCollection<NeoMemberDictionaryWritable>("Inventory"),
                (_, member) => ((NeoMemberString)member).value?.value ?? "",
                item => NeoGeneratedTypesSupport.Value(item),
                ItemSlot.FromOptionId,
                key => key.optionId,
                () => { },
                () => false);

            int changed = 0;
            using var subscription = inventory.OnChanged((_, _) => changed++);

            inventory[ItemSlot.Sword] = "Excalibur";
            inventory.Add(ItemSlot.Shield, "Aegis");

            Assert.AreEqual(2, inventory.Count);
            Assert.IsTrue(inventory.ContainsKey(ItemSlot.Sword));
            Assert.AreEqual("Excalibur", inventory[ItemSlot.Sword]);
            Assert.AreEqual("Aegis", inventory[ItemSlot.Shield]);
            Assert.IsTrue(inventory.TryGetValue(ItemSlot.Shield, out string aegis));
            Assert.AreEqual("Aegis", aegis);

            inventory[ItemSlot.Sword] = "Caliburn";
            Assert.AreEqual("Caliburn", inventory[ItemSlot.Sword]);

            Assert.IsTrue(inventory.Remove(ItemSlot.Shield));
            Assert.IsFalse(inventory.ContainsKey(ItemSlot.Shield));
            Assert.IsFalse(inventory.TryGetValue(ItemSlot.Shield, out _));
            Assert.GreaterOrEqual(changed, 3);
        }

        [Test]
        public void TwoArity_SharesStorageWithSingleArityView()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var inventory = CreateWritableInventory(client, out var node);
            var stringView = new NeoReadOnlyDictionary<string>(
                client,
                node,
                (_, member) => ((NeoMemberString)member).value?.value ?? "");

            inventory.Add(ItemSlot.Sword, "Excalibur");

            // Same node, same rows: the string-keyed single-arity view reads
            // the entry under the raw option id — no forked storage.
            Assert.AreEqual(1, stringView.Count);
            Assert.AreEqual("Excalibur", stringView["sword"]);
        }

        [Test]
        public void TwoArity_Enumeration_YieldsTypedPairsInRecordOrder()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var inventory = CreateWritableInventory(client, out _);

            inventory.Add(ItemSlot.Sword, "Excalibur");
            inventory.Add(ItemSlot.Shield, "Aegis");

            var pairs = new List<KeyValuePair<ItemSlot, string>>(inventory);
            Assert.AreEqual(2, pairs.Count);
            Assert.AreSame(ItemSlot.Sword, pairs[0].Key);
            Assert.AreEqual("Excalibur", pairs[0].Value);
            Assert.AreSame(ItemSlot.Shield, pairs[1].Key);
            Assert.AreEqual("Aegis", pairs[1].Value);
        }

        [Test]
        public void TwoArity_StaleKey_DegradesToAdHocWrapperInstance()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var inventory = CreateWritableInventory(client, out var node);

            // Simulate an entry keyed by an option the enum no longer has by
            // writing it through the string-keyed layer, exactly as a stale
            // export would present it.
            var stringWriter = new NeoDictionary<string>(
                client,
                node,
                (_, member) => ((NeoMemberString)member).value?.value ?? "",
                NeoGeneratedTypesSupport.Value);
            stringWriter.Add("dagger", "Carnwennan");
            inventory.Add(ItemSlot.Sword, "Excalibur");

            var keys = new List<ItemSlot>(inventory.Keys);
            Assert.AreEqual(2, keys.Count);
            CollectionAssert.Contains(keys, ItemSlot.FromOptionId("dagger"));
            CollectionAssert.Contains(keys, ItemSlot.Sword);

            // The stale key is a first-class ad-hoc instance: interned,
            // round-trips, and reads its entry like any live option.
            var stale = ItemSlot.FromOptionId("dagger");
            Assert.AreSame(stale, ItemSlot.FromOptionId("dagger"));
            Assert.AreEqual("Carnwennan", inventory[stale]);
            Assert.IsTrue(inventory.Remove(stale));
            Assert.IsFalse(inventory.ContainsKey(stale));
        }

        [Test]
        public void TwoArity_NullKey_ThrowsBeforeReachingCodec()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var inventory = CreateWritableInventory(client, out _);

            Assert.Throws<ArgumentNullException>(() => inventory.Add(null!, "x"));
            Assert.Throws<ArgumentNullException>(() => inventory.ContainsKey(null!));
        }

        // ------------------------------------------------------------------
        // JSON layer: keyKind / keyEnumId read-compat.
        // ------------------------------------------------------------------

        [Test]
        public void DictionaryMember_KeyKindFields_DeserializeFromWire()
        {
            var member = (DictionaryMember)JsonConvert.DeserializeObject<Member>(
                @"{
                    ""id"": ""member-stats"",
                    ""projectId"": ""p"",
                    ""name"": ""Stats"",
                    ""kind"": 5,
                    ""locked"": false,
                    ""required"": false,
                    ""isStatic"": false,
                    ""createdAt"": 0,
                    ""updatedAt"": 0,
                    ""entryMemberId"": ""member-entry"",
                    ""keyKind"": ""enum"",
                    ""keyEnumId"": ""enum-item-slot""
                }")!;

            Assert.AreEqual(NeoDictionaryKeyKinds.Enum, member.keyKind);
            Assert.AreEqual("enum-item-slot", member.keyEnumId);
        }

        [Test]
        public void DictionaryMember_AbsentKeyKind_DefaultsToNullForReadCompat()
        {
            // Stale local exports predate the web-side backfill migration and
            // omit both fields; null keyKind reads as string-keyed.
            var member = (DictionaryMember)JsonConvert.DeserializeObject<Member>(
                @"{
                    ""id"": ""member-stats"",
                    ""projectId"": ""p"",
                    ""name"": ""Stats"",
                    ""kind"": 5,
                    ""locked"": false,
                    ""required"": false,
                    ""isStatic"": false,
                    ""createdAt"": 0,
                    ""updatedAt"": 0,
                    ""entryMemberId"": ""member-entry""
                }")!;

            Assert.IsNull(member.keyKind);
            Assert.IsNull(member.keyEnumId);
        }

        [Test]
        public void DictionaryMember_StringKeyKind_DeserializesWithoutKeyEnumId()
        {
            var member = (DictionaryMember)JsonConvert.DeserializeObject<Member>(
                @"{
                    ""id"": ""member-stats"",
                    ""projectId"": ""p"",
                    ""name"": ""Stats"",
                    ""kind"": 5,
                    ""locked"": false,
                    ""required"": false,
                    ""isStatic"": false,
                    ""createdAt"": 0,
                    ""updatedAt"": 0,
                    ""entryMemberId"": ""member-entry"",
                    ""keyKind"": ""string""
                }")!;

            Assert.AreEqual(NeoDictionaryKeyKinds.String, member.keyKind);
            Assert.IsNull(member.keyEnumId);
        }
    }
}
