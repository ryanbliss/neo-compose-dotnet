// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public sealed class NeoTileConversionTests
    {
        [TestCase(null)]
        [TestCase("object-placement-tiles")]
        public void ConversionPreservesPlacementAndDiscardsOldConstruction(string? containerId)
        {
            using var client = LoadClient();
            var source = new ObjectMemberValue
            {
                id = "tile-row", classId = "test-tile-a", containerId = containerId,
                mapKey = "world:test", sourceValueId = "authored-tile",
                createdAt = 12, updatedAt = 13,
                instanceConstructorId = null,
                constructorArgs = new Dictionary<string, JToken?>(),
                value = new Dictionary<string, string> { ["Cell"] = "tile-cell" },
            };
            client.SetSaveValue(new Vector2MemberValue
            {
                id = "tile-cell", value = new NeoVector2Value { x = 3, y = 5 },
            });
            client.SetSaveValue(source);
            var notifications = new List<string>();
            client.OnWritableValueChanged += (_, id) => notifications.Add(id);

            client.ConvertTile(NeoValueOwnership.Save, source.id, "test-tile-b");

            var converted = (ObjectMemberValue)client.saveValues[source.id];
            Assert.AreEqual("test-tile-b", converted.classId);
            Assert.AreEqual(source.id, converted.id);
            Assert.AreEqual(source.containerId, converted.containerId);
            Assert.AreEqual(source.mapKey, converted.mapKey);
            Assert.AreEqual(source.createdAt, converted.createdAt);
            Assert.AreEqual(source.sourceValueId, converted.sourceValueId);
            Assert.AreEqual("tile-cell", converted.value!["Cell"]);
            Assert.AreEqual(3, ((Vector2MemberValue)client.saveValues["tile-cell"]).value!.x);
            Assert.AreEqual(5, ((Vector2MemberValue)client.saveValues["tile-cell"]).value!.y);
            Assert.IsFalse(converted.hasInstanceConstructorId);
            Assert.IsNull(converted.constructorArgs);
            Assert.IsNull(converted.instanceVariantId);
            CollectionAssert.Contains(notifications, source.id);
            if (containerId is not null) CollectionAssert.Contains(notifications, containerId);
        }

        [TestCase("test-tile-base")]
        [TestCase("class-hero")]
        public void InvalidTargetDoesNotWriteOrNotify(string targetClassId)
        {
            using var client = LoadClient();
            var source = new ObjectMemberValue
            {
                id = "tile-row", classId = "test-tile-a",
                value = new Dictionary<string, string> { ["Cell"] = "tile-cell" },
            };
            client.SetSaveValue(new Vector2MemberValue { id = "tile-cell", value = new NeoVector2Value { x = 0, y = 0 } });
            client.SetSaveValue(source);
            int notifications = 0;
            client.OnWritableValueChanged += (_, __) => notifications++;

            Assert.Throws<ArgumentException>(() =>
                client.ConvertTile(NeoValueOwnership.Save, source.id, targetClassId));

            Assert.AreSame(source, client.saveValues[source.id]);
            Assert.AreEqual(0, notifications);
        }

        [Test]
        public void ConversionKeepsAnExistingPhysicalCellWithoutPublishingItAgain()
        {
            using var client = LoadClient(grid: true);
            MemberValue cell = client.values["existing-cell"];
            var changed = new List<string>();
            client.OnWritableValueChanged += (_, id) => changed.Add(id);

            client.ConvertTile(NeoValueOwnership.Save, "existing-tile", "test-tile-b");

            Assert.AreSame(cell, client.values["existing-cell"]);
            Assert.IsFalse(client.saveValues.ContainsKey("existing-cell"));
            Assert.AreEqual("existing-cell", ((ObjectMemberValue)client.saveValues["existing-tile"]).value!["Cell"]);
            CollectionAssert.DoesNotContain(changed, "existing-cell");
            CollectionAssert.Contains(changed, "existing-tile");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SessionConversionPreservesTheEffectiveSavedCell(bool savedPlacement)
        {
            using var client = LoadClient(grid: true);
            string tileId = savedPlacement ? "saved-tile" : "existing-tile";
            string cellId = savedPlacement ? "saved-cell" : "existing-cell";
            var cell = new Vector2MemberValue { id = cellId, value = new NeoVector2Value { x = 20, y = 21 } };
            if (savedPlacement)
                client.SetWritableValues(NeoValueOwnership.Save, new MemberValue[]
                {
                    cell,
                    new ObjectMemberValue { id = tileId, classId = "test-tile-a", containerId = "test-tiles", value = new() { ["Cell"] = cellId } },
                });
            else client.SetSaveValue(cell);
            string before = client.SerializeSaveData();

            client.ConvertTile(NeoValueOwnership.Session, tileId, "test-tile-b");

            var converted = (ObjectMemberValue)client.sessionValues[tileId];
            var retained = (Vector2MemberValue)client.sessionValues[converted.value!["Cell"]];
            Assert.AreEqual(20, retained.value!.x);
            Assert.AreEqual(21, retained.value.y);
            Assert.AreEqual("test-tile-b", converted.classId);
            Assert.AreEqual(before, client.SerializeSaveData());
        }

        [Test]
        public void SparseCellIsMaterializedBeforeClassChangeNotification()
        {
            using var client = LoadClient();
            client.SetSaveValue(new ObjectMemberValue
            {
                id = "tile-row", classId = "test-tile-a",
                value = new Dictionary<string, string>(),
            });
            bool observedCompleteGraph = false;
            client.OnWritableValueChanged += (_, id) =>
            {
                if (id != "tile-row") return;
                var row = (ObjectMemberValue)client.saveValues[id];
                observedCompleteGraph = row.classId == "test-tile-b"
                    && row.value!.TryGetValue("Cell", out string childId)
                    && client.saveValues[childId] is Vector2MemberValue { value: not null };
            };

            client.ConvertTile(NeoValueOwnership.Save, "tile-row", "test-tile-b");

            Assert.IsTrue(observedCompleteGraph);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ScriptConversionPreservesReceiverAliasAndRefreshesClassCheck(bool dynamicTarget)
        {
            using var client = LoadClient();
            var source = new ObjectMemberValue
            {
                id = "tile-row", classId = "test-tile-a",
                value = new Dictionary<string, string>(),
            };
            var target = new ObjectMemberValue
            {
                id = "target-row", classId = "test-tile-b",
                value = new Dictionary<string, string>(),
            };
            client.SetSaveValue(source);
            client.SetSaveValue(target);
            var context = new NSGetterEvaluator.Context(client, null, null);
            object receiver = NSGetterEvaluator.UnwrapRow(source, context, NeoValueOwnership.Save)!;
            var scope = new Dictionary<string, object?>
            {
                ["tile"] = receiver,
                ["next"] = NSGetterEvaluator.UnwrapRow(target, context, NeoValueOwnership.Save),
            };
            var pointer = new TileConvertPointer
            {
                type = PointerKind.TileConvert,
                receiverPointer = new VariablePointer { type = PointerKind.Variable, variableId = "tile" },
                callSiteId = "convert-test",
                targetClassId = dynamicTarget ? null : "test-tile-b",
                targetPointer = dynamicTarget
                    ? new VariablePointer { type = PointerKind.Variable, variableId = "next" } : null,
            };
            // Exercise the exported wire discriminator and arm validation too.
            Pointer parsed = JsonConvert.DeserializeObject<Pointer>(JsonConvert.SerializeObject(pointer))!;
            Assert.AreEqual(true, NSGetterEvaluator.EvaluatePointer(parsed, scope, context));
            Assert.AreSame(receiver, scope["tile"]);
            Assert.AreEqual(true, NSGetterEvaluator.EvaluatePointer(new IsCheckPointer
            {
                type = PointerKind.IsCheck,
                pointer = pointer.receiverPointer,
                checkType = new ClassTypeInfo
                {
                    type = MemberKind.Class, classId = "test-tile-b", required = true,
                },
            }, scope, context));
            Assert.AreSame(target, client.saveValues["target-row"]);
        }

        [Test]
        public void ConversionPointerRejectsBothTargetArms()
        {
            Assert.Throws<JsonSerializationException>(() => JsonConvert.DeserializeObject<Pointer>(
                "{\"type\":\"tileConvert\",\"callSiteId\":\"test\","
                + "\"receiverPointer\":{\"type\":\"variable\",\"variableId\":\"tile\"},"
                + "\"targetClassId\":\"test-tile-b\","
                + "\"targetPointer\":{\"type\":\"variable\",\"variableId\":\"next\"}}"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RejectedListAdoptionLeavesBothStoresAndMembershipUnchanged(bool wholeAssignment)
        {
            using var client = LoadClient(grid: true);
            client.SetWritableValues(NeoValueOwnership.Session, new MemberValue[]
            {
                new ObjectMemberValue { id = "candidate", classId = "test-tile-a", value = new() { ["Cell"] = "candidate-cell" } },
                new Vector2MemberValue { id = "candidate-cell", value = new NeoVector2Value { x = 0, y = 0 } },
            });
            using var list = new NeoMemberListWritable(client,
                (ListMember)client.members["test-tiles-member"], "test-tiles", NeoValueOwnership.Save);
            string beforeSave = JsonConvert.SerializeObject(client.saveValues);
            string beforeSession = JsonConvert.SerializeObject(client.sessionValues);
            int changed = 0;
            client.OnWritableValueChanged += (_, __) => changed++;
            var error = Assert.Throws<NeoPlacementValidationException>(() =>
            {
                if (wholeAssignment)
                    list.AssignSerialized(NeoValueWritePayload.FromValue(new[] { "existing-tile", "candidate" }));
                else list.AddSerialized(NeoValueWritePayload.FromValueReference("candidate", null));
            });
            Assert.AreEqual("tile-cell-occupied", error!.ErrorCode);
            Assert.AreEqual(beforeSave, JsonConvert.SerializeObject(client.saveValues));
            Assert.AreEqual(beforeSession, JsonConvert.SerializeObject(client.sessionValues));
            CollectionAssert.AreEquivalent(new[] { "existing-tile", "second-tile" }, client.GetUnorderedListEntryIds("test-tiles"));
            Assert.AreEqual(0, changed);
        }

        [Test]
        public void RemoteBatchValidatesCompletedCellSwapAndRejectsOverlapAtomically()
        {
            using var client = LoadClient(grid: true);
            JObject incoming = JObject.Parse(client.SerializeSaveData());
            incoming["values"]!["existing-cell"] = JObject.FromObject(new Vector2MemberValue
            { id = "existing-cell", value = new NeoVector2Value { x = 1, y = 0 } });
            incoming["values"]!["second-cell"] = JObject.FromObject(new Vector2MemberValue
            { id = "second-cell", value = new NeoVector2Value { x = 0, y = 0 } });
            client.ApplyExternalSaveContent(incoming.ToString());
            Assert.AreEqual(1, ((Vector2MemberValue)client.saveValues["existing-cell"]).value!.x);
            Assert.AreEqual(0, ((Vector2MemberValue)client.saveValues["second-cell"]).value!.x);
            string before = client.SerializeSaveData();
            incoming["values"]!["second-cell"]!["value"]!["x"] = 1;
            int changed = 0;
            client.OnWritableValueChanged += (_, __) => changed++;
            Assert.Throws<NeoPlacementValidationException>(() => client.ApplyExternalSaveContent(incoming.ToString()));
            Assert.AreEqual(before, client.SerializeSaveData());
            Assert.AreEqual(0, changed);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TileResetRestoresClassAndCellInOneValidatedPublication(bool authoredCellOccupied)
        {
            using var client = LoadClient(grid: true);
            client.ConvertTile(NeoValueOwnership.Save, "existing-tile", "test-tile-b");
            var cells = new List<MemberValue>
            {
                new Vector2MemberValue { id = "existing-cell", value = new NeoVector2Value { x = 2, y = 0 } },
            };
            if (authoredCellOccupied)
                cells.Add(new Vector2MemberValue { id = "second-cell", value = new NeoVector2Value { x = 0, y = 0 } });
            client.SetWritableValues(NeoValueOwnership.Save, cells);
            var grid = NeoTileGridPrimitive.ResolveForSave(client, "test-grid");
            int publications = 0;
            client.OnWritableValuesPublished += (_, __) => publications++;
            string before = client.SerializeSaveData();

            var result = grid.TryResetTile(new NeoTileInstanceId("existing-tile"));

            Assert.AreEqual(!authoredCellOccupied, result.Ok, result.Message);
            if (authoredCellOccupied)
            {
                Assert.AreEqual("tile-cell-occupied", result.ErrorCode);
                Assert.AreEqual(before, client.SerializeSaveData());
                Assert.AreEqual(0, publications);
            }
            else
            {
                Assert.IsFalse(client.saveValues.ContainsKey("existing-tile"));
                Assert.IsFalse(client.saveValues.ContainsKey("existing-cell"));
                Assert.AreEqual(1, publications);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PreparedVariantReadsItsWritesAndPublishesOnlyAValidFinalGraph(bool reject)
        {
            using var client = LoadClient(grid: true);
            using var receiver = new NeoMemberClassWritable(client,
                (ClassMember)client.members["test-tile-entry"], "existing-tile", NeoValueOwnership.Save);
            var held = receiver.Get<NeoMemberVector2IntWritable>("Cell");
            client.SetStaticBinding("test-variant-state", NeoValueOwnership.Save, "state-a");
            string beforeSave = client.SerializeSaveData();
            string beforeSession = JsonConvert.SerializeObject(client.sessionValues);
            int changes = 0;
            client.OnWritableValueChanged += (_, __) => changes++;
            void Apply() => client.PrepareVariantApply(receiver, scoped =>
            {
                var cell = scoped.Get<NeoMemberVector2IntWritable>("Cell");
                cell.Set(new UnityEngine.Vector2Int(1, 0)); // Temporarily overlaps the second tile.
                Assert.AreEqual(1, cell.value!.value!.x);
                Assert.AreEqual(0, held.value!.value!.x);
                Assert.AreEqual(0, changes);
                client.SetStaticBinding("test-variant-state", NeoValueOwnership.Save, "state-b");
                client.SetStaticBinding("test-variant-state", NeoValueOwnership.Save, "state-a");
                client.TryResolveStaticBinding("test-variant-state", out _, out _, out string? state);
                Assert.AreEqual("state-a", state);
                client.CloneValueReference("second-tile", NeoValueOwnership.Save);
                if (!reject) cell.Set(new UnityEngine.Vector2Int((int)cell.value.value.x + 1, 0));
            });
            if (reject)
            {
                Assert.Throws<NeoPlacementValidationException>(Apply);
                Assert.AreEqual(beforeSave, client.SerializeSaveData());
                Assert.AreEqual(beforeSession, JsonConvert.SerializeObject(client.sessionValues));
                Assert.AreEqual(0, changes);
                Assert.AreEqual(0, held.value!.value!.x);
            }
            else
            {
                Apply();
                Assert.AreEqual(2, held.value!.value!.x);
                Assert.Greater(changes, 0);
                Assert.AreNotEqual(beforeSession, JsonConvert.SerializeObject(client.sessionValues));
            }
            Assert.IsFalse(held.isDisposed);
        }

        private static void AddGrid(JObject document)
        {
            void Member(Member member) => document["members"]![member.id] = JObject.FromObject(member);
            void Class(string id, string worldKind, Dictionary<string, string> schema) =>
                document["classes"]![id] = JObject.FromObject(new NeoSchemaClass
                {
                    id = id, name = id, schema = schema,
                    system = new JObject { ["kind"] = "worldAuthoring", ["worldKind"] = worldKind },
                });
            void Row(MemberValue row) => document["values"]![row.id] = JObject.FromObject(row);
            Class("test-grid-class", "tileGrid", new() { ["Children"] = "test-children-member", ["VariantState"] = "test-variant-state" });
            Member(new IntMember { id = "test-variant-state", name = "VariantState", kind = MemberKind.Int,
                Modifier = NeoMemberModifierKind.Static, Storage = NeoMemberStorage.Save, valueId = "state-a" });
            Row(new NumberMemberValue { id = "state-a", value = 1 });
            Row(new NumberMemberValue { id = "state-b", value = 2 });
            Class("test-link-class", "tileLayerLink", new() { ["Tiles"] = "test-tiles-member" });
            Class("test-layer-class", "tileLayer", new());
            document["classes"]!["test-link-base"] = JObject.FromObject(new NeoSchemaClass
            {
                id = "test-link-base", name = "LinkBase", Modifier = NeoClassModifierKind.Abstract,
                schema = new Dictionary<string, string>(),
                system = new JObject { ["kind"] = "worldAuthoring", ["worldKind"] = "tileLayerLink" },
            });
            ((JObject)document["classes"]!["test-link-class"]!).Remove("system");
            document["classes"]!["test-link-class"]!["extendsClassId"] = "test-link-base";

            Member(new ClassMember { id = "test-link-entry", name = "Link", kind = MemberKind.Class, classId = "test-link-class" });
            Member(new ClassMember { id = "test-tile-entry", name = "Tile", kind = MemberKind.Class, classId = "test-tile-base" });
            Member(new ListMember { id = "test-children-member", name = "Children", kind = MemberKind.List, entryMemberId = "test-link-entry" });
            Member(new ListMember { id = "test-tiles-member", name = "Tiles", kind = MemberKind.List, entryMemberId = "test-tile-entry", ListKind = NeoListKind.Unordered });
            Row(new ObjectMemberValue { id = "test-grid", classId = "test-grid-class", value = new() { ["Children"] = "test-children" } });
            Row(new ArrayMemberValue { id = "test-children", value = new[] { "test-link" } });
            Row(new ObjectMemberValue { id = "test-link", classId = "test-link-class", value = new() { ["Tiles"] = "test-tiles" } });
            Row(new ArrayMemberValue { id = "test-tiles", value = Array.Empty<string>() });
            foreach (var (id, x) in new[] { ("existing", 0), ("second", 1) })
            {
                Row(new ObjectMemberValue { id = id + "-tile", classId = "test-tile-a", containerId = "test-tiles", value = new() { ["Cell"] = id + "-cell" } });
                Row(new Vector2MemberValue { id = id + "-cell", value = new NeoVector2Value { x = x, y = 0 } });
            }
            var relations = (JObject)(document["internalRecordRelations"] ??= new JObject());
            void Relation(string kind, string source, string target)
            {
                string id = kind + source + target;
                relations[id] = JObject.FromObject(new InternalRecordRelation
                { id = id, relationKind = kind, orderKey = kind == InternalRecordRelationKinds.WorldGridTileLayer ? "a0" : null, sourceRecordKind = "class", sourceRecordId = source, targetRecordKind = "class", targetRecordId = target });
            }
            Relation(InternalRecordRelationKinds.WorldGridTileLayer, "test-grid-class", "test-layer-class");
            Relation(InternalRecordRelationKinds.WorldTileLayerLinkTarget, "test-link-class", "test-layer-class");
            foreach (string tile in new[] { "test-tile-a", "test-tile-b" })
            {
                Relation(InternalRecordRelationKinds.WorldGridTileImport, "test-grid-class", tile);
                Relation(InternalRecordRelationKinds.WorldTileCompatibleLayer, tile, "test-layer-class");
            }
        }

        private static NeoClient LoadClient(bool grid = false)
        {
            var document = JObject.Parse(File.ReadAllText(
                "Packages/com.ryanbliss.neocompose/Tests/synth-example.json"));
            document["members"]!["test-tile-cell"] = JObject.FromObject(new Vector2IntMember
            {
                id = "test-tile-cell", name = "Cell", kind = MemberKind.Vector2Int,
                defaultValue = new Vector2MemberValueBase
                {
                    value = new NeoVector2Value { x = 0, y = 0 },
                },
            });
            document["classes"]!["test-tile-base"] = JObject.FromObject(new NeoSchemaClass
            {
                id = "test-tile-base", name = "TileBase",
                Modifier = NeoClassModifierKind.Abstract,
                schema = new Dictionary<string, string> { ["Cell"] = "test-tile-cell" },
                system = new JObject { ["kind"] = "world", ["worldKind"] = "tile" },
            });
            foreach (string suffix in new[] { "a", "b" })
            {
                string id = "test-tile-" + suffix;
                document["classes"]![id] = JObject.FromObject(new NeoSchemaClass
                {
                    id = id, name = "Tile" + suffix, extendsClassId = "test-tile-base",
                    schema = new Dictionary<string, string>(),
                });
            }
            if (grid) AddGrid(document);
            return NeoTestSaveStack.LoadClient(document.ToString());
        }
    }
}
