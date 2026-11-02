// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;
using UnityEngine;

namespace NeoCompose.Tests
{
    public partial class NeoTileGridRendererTests
    {
        [Test]
        public void DetachedSessionWritesKeepGridIndexesAndTheirVersion()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildClassBackedTileGridProjectData());
            var primitive = NeoTileGridPrimitive.ResolveForSave(client, "town-grid",
                BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            var cache = primitive.LookupCache;
            var objects = cache.ObjectRecords(ObjectsLayerClassId);
            var tiles = cache.TileRecords(BackgroundLayerClassId);
            int version = cache.ObjectLayersVersion;
            client.SetWritableValue(NeoValueOwnership.Session, new NumberMemberValue { id = "temporary", value = 7 });
            Assert.AreEqual(version, cache.ObjectLayersVersion);
            Assert.AreSame(objects, cache.ObjectRecords(ObjectsLayerClassId));
            Assert.AreSame(tiles, cache.TileRecords(BackgroundLayerClassId));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SimpleObjectRemovalKeepsOtherPlacementsAndFreesItsCells(bool authored)
        {
            var data = BuildClassBackedTileGridProjectData();
            ConfigureObjectPlacementFootprint(data, Vector2Int.one, Vector2Int.zero);
            ((ObjectMemberValue)data.values["shop-1"]).value!.Remove("Children");
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoTileGridPrimitive.ResolveForSave(client, "town-grid",
                BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            var added = PlacedObject("second", 14, 20);
            client.SetWritableValues(NeoValueOwnership.Save, added);
            var objects = primitive.LookupCache.ObjectRecords(ObjectsLayerClassId);
            var tiles = primitive.LookupCache.TileRecords(BackgroundLayerClassId);
            var graph = primitive.LookupCache.Primitive.PlacementGraph;
            var changes = new List<NeoTileGridChangedArgs>();
            using var subscription = primitive.OnChanged(changes.Add);
            string removedId = authored ? "shop-1" : "second";
            var removal = new NeoWritePlan(client);
            if (authored)
                removal.Set(NeoValueOwnership.Save, new NullMemberValue { id = removedId, mark = NeoValueMarks.Removed });
            else
                foreach (var row in added)
                    removal.Remove(NeoValueOwnership.Save, row.id);
            removal.Commit();

            Assert.AreEqual("town-grid", removal.ValidatedObjectRemovalGrid);
            Assert.That(removal.PreparedObjectLayers, Is.Null.Or.Empty);
            Assert.That(removal.PreparedTileLayers, Is.Null.Or.Empty);
            Assert.AreEqual(2, objects.Count, "An existing enumeration retains its snapshot.");
            Assert.AreSame(tiles, primitive.LookupCache.TileRecords(BackgroundLayerClassId));
            Assert.AreSame(graph, primitive.LookupCache.Primitive.PlacementGraph);
            Assert.IsFalse(graph.Nodes.ContainsKey(removedId));
            Assert.IsNull(primitive.LookupCache.ObjectRecord(ObjectsLayerClassId, removedId));
            Assert.That(primitive.LookupCache.ObjectRecords(ObjectsLayerClassId).Select(record => record.InstanceId), Is.EquivalentTo(new[] { authored ? "second" : "shop-1" }));
            CollectionAssert.AreEqual(new NeoObjectInstanceId[] { removedId }, changes.Single().ObjectLayers.Single().RemovedInstances);
            Assert.That(changes.Single().TileLayers, Is.Empty);
            int x = authored ? 10 : 14;
            client.SetWritableValues(NeoValueOwnership.Save, PlacedObject("replacement", x, 20));
            Assert.That(primitive.LookupCache.ObjectRecord(ObjectsLayerClassId, "replacement")!.Cell, Is.EqualTo(new Vector2Int(x, 20)));
            Assert.Throws<NeoPlacementValidationException>(() => client.SetWritableValues(NeoValueOwnership.Save, PlacedObject("collision", x, 20)));
        }

        [Test]
        public void ObjectRemovalRetainsDetachedSessionAliasesWithoutRebuildingTheLayer()
        {
            var data = BuildClassBackedTileGridProjectData();
            ConfigureObjectPlacementFootprint(data, Vector2Int.one, Vector2Int.zero);
            ((ObjectMemberValue)data.values["shop-1"]).value!.Remove("Children");
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var rows = PlacedObject("second", 14, 20);
            client.SetWritableValues(NeoValueOwnership.Save, rows);
            var primitive = NeoTileGridPrimitive.ResolveForSave(client, "town-grid",
                BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            _ = primitive.LookupCache.ObjectRecords(ObjectsLayerClassId);
            Assert.IsTrue(client.TryInferMemberForValueId("second", out Member? member));
            var plan = new NeoWritePlan(client);
            var retained = new Dictionary<(NeoValueOwnership scope, string id), string>();
            string copy = client.StageClassRetention(plan, NeoValueOwnership.Save, "second", member!, retained);
            client.StageOwnedRemoval(plan, NeoValueOwnership.Save, "second", member);
            plan.Commit();
            Assert.AreEqual("town-grid", plan.ValidatedObjectRemovalGrid);
            Assert.IsNull(primitive.LookupCache.ObjectRecord(ObjectsLayerClassId, "second"));
            Assert.IsNull(primitive.LookupCache.ObjectRecord(ObjectsLayerClassId, copy));
            Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Session, copy, out ObjectMemberValue? alias));
            Assert.IsNull(alias!.containerId);
            Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Session, alias.value!["Position"], out Vector3MemberValue? position));
            Assert.AreEqual(14d, position!.value!.x);
            Assert.IsNotNull(primitive.LookupCache.ObjectRecord(ObjectsLayerClassId, "shop-1"));
        }

        [TestCase(35)]
        [TestCase(350)]
        [TestCase(1400)]
        public void BulkRemovalKeepsUnrelatedObjects(int objectCount)
        {
            var data = BuildClassBackedTileGridProjectData();
            ConfigureObjectPlacementFootprint(data, Vector2Int.one, Vector2Int.zero);
            ((ObjectMemberValue)data.values["shop-1"]).value!.Remove("Children");
            for (int i = 0; i < objectCount; i++)
                foreach (var row in PlacedObject("bulk-" + i, 100 + i, 20))
                    data.values[row.id] = row;
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoTileGridPrimitive.ResolveForSave(client, "town-grid",
                BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            _ = primitive.LookupCache.ObjectRecords(ObjectsLayerClassId);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 35; i++)
            {
                var plan = new NeoWritePlan(client);
                plan.Set(NeoValueOwnership.Save, new NullMemberValue { id = "bulk-" + i, mark = NeoValueMarks.Removed });
                plan.Commit();
                Assert.AreEqual("town-grid", plan.ValidatedObjectRemovalGrid);
            }
            timer.Stop();
            TestContext.WriteLine($"BULK_REMOVAL objects={objectCount} removed=35 ms={timer.Elapsed.TotalMilliseconds:F3}");
            Assert.AreEqual(objectCount - 35 + 1, primitive.LookupCache.ObjectRecords(ObjectsLayerClassId).Count);
            Assert.IsNotNull(primitive.LookupCache.ObjectRecord(ObjectsLayerClassId, "shop-1"));
            for (int i = 0; i < 35; i++)
                Assert.IsNull(primitive.LookupCache.ObjectRecord(ObjectsLayerClassId, "bulk-" + i));
        }

        [Test]
        public void RemovalWithSurvivingObjectMoveStillRejectsCollision()
        {
            var data = BuildClassBackedTileGridProjectData();
            ConfigureObjectPlacementFootprint(data, Vector2Int.one, Vector2Int.zero);
            ((ObjectMemberValue)data.values["shop-1"]).value!.Remove("Children");
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            client.SetWritableValues(NeoValueOwnership.Save, PlacedObject("second", 14, 20));
            var third = PlacedObject("third", 18, 20);
            client.SetWritableValues(NeoValueOwnership.Save, third);
            var primitive = NeoTileGridPrimitive.ResolveForSave(client, "town-grid",
                BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            _ = primitive.LookupCache.ObjectRecords(ObjectsLayerClassId);
            var plan = new NeoWritePlan(client);
            foreach (var row in third)
                plan.Remove(NeoValueOwnership.Save, row.id);
            plan.Set(NeoValueOwnership.Save, new Vector3MemberValue { id = "second-position", value = new NeoVector3Value { x = 10, y = 20 } });
            Assert.Throws<NeoPlacementValidationException>(plan.Commit);
            Assert.IsNull(plan.ValidatedObjectRemovalGrid);
            Assert.IsNotNull(primitive.LookupCache.ObjectRecord(ObjectsLayerClassId, "third"));
            Assert.AreEqual(new Vector2Int(14, 20), primitive.LookupCache.ObjectRecord(ObjectsLayerClassId, "second")!.Cell);
        }

        [Test]
        public void RemovalWithInsertionIntoRestagedSessionListRejectsCollision()
        {
            var data = BuildClassBackedTileGridProjectData();
            ConfigureObjectPlacementFootprint(data, Vector2Int.one, Vector2Int.zero);
            ((ObjectMemberValue)data.values["shop-1"]).value!.Remove("Children");
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var list = client.CloneRowForWrite(data.values["objects-link-objects"]);
            client.SetWritableValue(NeoValueOwnership.Session, list);
            var third = PlacedObject("third", 18, 20);
            client.SetWritableValues(NeoValueOwnership.Session, third);
            var primitive = NeoTileGridPrimitive.ResolveForSession(client, "town-grid",
                BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            _ = primitive.LookupCache.ObjectRecords(ObjectsLayerClassId);
            var plan = new NeoWritePlan(client);
            foreach (var row in third)
                plan.Remove(NeoValueOwnership.Session, row.id);
            plan.Set(NeoValueOwnership.Session, list);
            foreach (var row in PlacedObject("collision", 10, 20))
                plan.Set(NeoValueOwnership.Session, row);
            Assert.Throws<NeoPlacementValidationException>(plan.Commit);
            Assert.IsNull(plan.ValidatedObjectRemovalGrid);
            Assert.IsNotNull(primitive.LookupCache.ObjectRecord(ObjectsLayerClassId, "third"));
            Assert.IsNull(primitive.LookupCache.ObjectRecord(ObjectsLayerClassId, "collision"));
        }

        [Test]
        public void RemovalOfObjectCarryingTilesUsesFullValidation()
        {
            var data = BuildClassBackedTileGridProjectData();
            ConfigureObjectPlacementFootprint(data, Vector2Int.one, Vector2Int.zero);
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoTileGridPrimitive.ResolveForSave(client, "town-grid",
                BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            _ = primitive.LookupCache.ObjectRecords(ObjectsLayerClassId);
            var tiles = primitive.LookupCache.TileRecords(BackgroundLayerClassId);
            var plan = new NeoWritePlan(client);
            plan.Set(NeoValueOwnership.Save, new NullMemberValue { id = "shop-1", mark = NeoValueMarks.Removed });
            plan.Commit();
            Assert.IsNull(plan.ValidatedObjectRemovalGrid);
            Assert.AreNotSame(tiles, primitive.LookupCache.TileRecords(BackgroundLayerClassId));
            Assert.IsNull(primitive.LookupCache.ObjectRecord(ObjectsLayerClassId, "shop-1"));
        }
    }
}
