// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Member = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    /// <summary>
    /// specs/dictionary-key-classes.md §9: two-arity enum-keyed dictionary
    /// wrappers + the <c>keyKind</c>/<c>keyEnumId</c> JSON read layer. The
    /// wrappers delegate to the same string-keyed storage as the
    /// single-arity pair; <see cref="ItemSlot"/> mirrors the generated enum
    /// shape (a C# enum converted by <see cref="ItemSlotOptions"/>).
    /// </summary>
    public class NeoEnumKeyedDictionaryTests
    {
        private const string EnumId = "item-slot-enum";
        private const string InventoryValueId = "inventory-value";

        /// <summary>
        /// Hand-written stand-in for a codegen-emitted enum: a real C# enum
        /// plus an options class converting to and from option ids.
        /// </summary>
        private enum ItemSlot
        {
            Sword = 1,
            Shield = 2,
        }

        private static class ItemSlotOptions
        {
            public static ItemSlot FromOptionId(string optionId)
            {
                return optionId switch
                {
                    "sword" => ItemSlot.Sword,
                    "shield" => ItemSlot.Shield,
                    _ => NeoUndeclaredEnumOptions<ItemSlot>.FromOptionId(optionId),
                };
            }

            public static string OptionId(ItemSlot value)
            {
                return value switch
                {
                    ItemSlot.Sword => "sword",
                    ItemSlot.Shield => "shield",
                    _ => NeoUndeclaredEnumOptions<ItemSlot>.OptionId(value),
                };
            }
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
                        KeyKind = NeoDictionaryKeyKind.Enum,
                        keyEnumId = EnumId,
                        Requirement = NeoMemberRequirementKind.Required,
                    },
                    ["entry-member"] = new StringMember
                    {
                        id = "entry-member",
                        projectId = "project-a",
                        name = "Item Name",
                        kind = MemberKind.String,
                        Format = NeoStringFormatKind.Plain,
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
            }.WithUserRoot();
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
                ItemSlotOptions.FromOptionId,
                ItemSlotOptions.OptionId);
        }

        [Test]
        public void KeyCodec_RoundTripsThroughWireStrings()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var inventory = CreateWritableInventory(client, out var node);

            inventory.Add(ItemSlot.Sword, "Excalibur");

            // Wire key is the option id; the typed key materializes back to
            // the enum member.
            Assert.IsTrue(node.ContainsKey("sword"));
            var keys = new List<ItemSlot>(inventory.Keys);
            Assert.AreEqual(1, keys.Count);
            Assert.AreEqual(ItemSlot.Sword, keys[0]);
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
                ItemSlotOptions.FromOptionId,
                ItemSlotOptions.OptionId,
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
            Assert.AreEqual(ItemSlot.Sword, pairs[0].Key);
            Assert.AreEqual("Excalibur", pairs[0].Value);
            Assert.AreEqual(ItemSlot.Shield, pairs[1].Key);
            Assert.AreEqual("Aegis", pairs[1].Value);
        }

        [Test]
        public void TwoArity_StaleKey_RoundTripsAsUndeclaredOption()
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
            CollectionAssert.Contains(keys, ItemSlotOptions.FromOptionId("dagger"));
            CollectionAssert.Contains(keys, ItemSlot.Sword);

            // The stale key is an undeclared value: negative, stable,
            // round-trips, and reads its entry like any live option.
            var stale = ItemSlotOptions.FromOptionId("dagger");
            Assert.Less((int)stale, 0);
            Assert.AreEqual(stale, ItemSlotOptions.FromOptionId("dagger"));
            Assert.AreEqual("dagger", ItemSlotOptions.OptionId(stale));
            Assert.AreEqual("Carnwennan", inventory[stale]);
            Assert.IsTrue(inventory.Remove(stale));
            Assert.IsFalse(inventory.ContainsKey(stale));
        }

        [Test]
        public void TwoArity_DefaultKey_Throws()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var inventory = CreateWritableInventory(client, out _);

            Assert.Throws<ArgumentOutOfRangeException>(() => inventory.Add(default, "x"));
        }

        // ------------------------------------------------------------------
        // JSON layer: keyKind / keyEnumId contract.
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
                    ""createdAt"": 0,
                    ""updatedAt"": 0,
                    ""entryMemberId"": ""member-entry"",
                    ""keyKind"": 1,
                    ""keyEnumId"": ""enum-item-slot""
                }")!;

            Assert.AreEqual(NeoDictionaryKeyKind.Enum, member.DeclaredKeyKind);
            Assert.AreEqual("enum-item-slot", member.keyEnumId);
        }

        [Test]
        public void DictionaryMember_AbsentKeyKind_DefaultsToNullForReadCompat()
        {
            // the canonical format omits the zero ordinal; null keyKind reads as string-keyed.
            var member = (DictionaryMember)JsonConvert.DeserializeObject<Member>(
                @"{
                    ""id"": ""member-stats"",
                    ""projectId"": ""p"",
                    ""name"": ""Stats"",
                    ""kind"": 5,
                    ""createdAt"": 0,
                    ""updatedAt"": 0,
                    ""entryMemberId"": ""member-entry""
                }")!;

            Assert.IsNull(member.DeclaredKeyKind);
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
                    ""createdAt"": 0,
                    ""updatedAt"": 0,
                    ""entryMemberId"": ""member-entry"",
                    ""keyKind"": 0
                }")!;

            Assert.AreEqual(NeoDictionaryKeyKind.String, member.DeclaredKeyKind);
            Assert.IsNull(member.keyEnumId);
        }

        [TestCase(99, null, "keyKind")]
        [TestCase(1, null, "keyEnumId")]
        [TestCase(1, "", "keyEnumId")]
        [TestCase(0, "enum-item-slot", "keyEnumId")]
        [TestCase(null, "enum-item-slot", "keyEnumId")]
        public void DictionaryMember_InvalidKeyContractIsRejectedByJsonAndClient(
            int? keyKind,
            string? keyEnumId,
            string expectedField)
        {
            var json = JObject.Parse(@"{
                ""id"": ""member-stats"",
                ""projectId"": ""p"",
                ""name"": ""Stats"",
                ""kind"": 5,
                ""createdAt"": 0,
                ""updatedAt"": 0,
                ""entryMemberId"": ""member-entry""
            }");
            if (keyKind is not null)
            {
                json["keyKind"] = keyKind.Value;
            }
            if (keyEnumId is not null)
            {
                json["keyEnumId"] = keyEnumId;
            }

            var jsonError = Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<Member>(json.ToString()));
            StringAssert.Contains(expectedField, jsonError!.Message);

            ProjectData data = BuildProjectData();
            var dictionary = (DictionaryMember)data.members["inventory-member"];
            dictionary.DeclaredKeyKind = keyKind is null
                ? null
                : (NeoDictionaryKeyKind)keyKind.Value;
            dictionary.keyEnumId = keyEnumId;

            var clientError = Assert.Throws<InvalidOperationException>(() =>
                NeoTestSaveStack.ClientFromSchema(data));
            StringAssert.Contains("inventory-member", clientError!.Message);
            StringAssert.Contains(expectedField, clientError.Message);
        }
    }
}
