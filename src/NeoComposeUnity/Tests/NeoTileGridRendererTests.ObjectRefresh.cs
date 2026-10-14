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
    /// <summary>A write to a placed object that keeps its placement validates the object, not its grid.</summary>
    public partial class NeoTileGridRendererTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void UnmovedObjectWriteKeepsGridIndexesAndReportsTheObject(bool carriesTiles)
        {
            var data = BuildClassBackedTileGridProjectData();
            ConfigureObjectPlacementFootprint(data, Vector2Int.one, Vector2Int.zero, new Vector2Int(1, 0));
            if (!carriesTiles)
                ((ObjectMemberValue)data.values["shop-1"]).value!.Remove("Children");
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoTileGridPrimitive.ResolveForSave(client, "town-grid",
                BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            var objects = primitive.LookupCache.ObjectRecords(ObjectsLayerClassId);
            var tiles = primitive.LookupCache.TileRecords(BackgroundLayerClassId);
            var changes = new List<NeoTileGridChangedArgs>();
            using var subscription = primitive.OnChanged(changes.Add);
            NeoWritePlan? published = null;
            client.OnWritableValuesPublished += (_, plan) => published = plan;

            // Repoint Position at a new row in the same cell: the object still
            // places as before, but now reads a row its record never read.
            CommitResized(client, "shop-1", variantId: null, new NeoVector3Value { x = 10, y = 20, z = 0 });

            CollectionAssert.AreEqual(new NeoObjectInstanceId[] { "shop-1" },
                changes.Single().ObjectLayers.Single().AddedOrChangedInstances);
            CollectionAssert.AreEquivalent(new[] { new Vector2Int(10, 20), new Vector2Int(11, 20) },
                changes.Single().ObjectLayers.Single().ChangedCells);
            if (carriesTiles)
            {
                // A carried tile layer is proven only by rebuilding the grid's layers.
                Assert.IsNull(published!.ValidatedObjectRefreshes);
                Assert.AreNotSame(objects, primitive.LookupCache.ObjectRecords(ObjectsLayerClassId));
                return;
            }
            Assert.AreEqual("shop-1", published!.ValidatedObjectRefreshes!.Single().ObjectId);
            Assert.That(published.PreparedObjectLayers, Is.Null.Or.Empty);
            Assert.That(published.PreparedTileLayers, Is.Null.Or.Empty);
            Assert.AreSame(objects, primitive.LookupCache.ObjectRecords(ObjectsLayerClassId));
            Assert.AreSame(tiles, primitive.LookupCache.TileRecords(BackgroundLayerClassId));
            // The kept index now depends on the row the object's placement reads.
            client.SetWritableValue(NeoValueOwnership.Save, new Vector3MemberValue
            {
                id = "shop-1-repointed",
                value = new NeoVector3Value { x = 30, y = 20, z = 0 },
            });
            Assert.AreEqual(new Vector2Int(30, 20), primitive.LookupCache.ObjectRecord(ObjectsLayerClassId, "shop-1")!.Cell);
        }

        [Test]
        public void UnmovedObjectWriteStillValidatesTheObject()
        {
            var data = BuildClassBackedTileGridProjectData();
            ConfigureObjectPlacementFootprint(data, Vector2Int.one, Vector2Int.zero);
            ((ObjectMemberValue)data.values["shop-1"]).value!.Remove("Children");
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoTileGridPrimitive.ResolveForSave(client, "town-grid",
                BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            var objects = primitive.LookupCache.ObjectRecords(ObjectsLayerClassId);
            long revision = client.WriteRevision;

            // A non-finite depth keeps the object's cell.
            var error = Assert.Throws<NeoPlacementValidationException>(() => CommitResized(client, "shop-1", null,
                new NeoVector3Value { x = 10, y = 20, z = float.NaN }));

            Assert.AreEqual("object-position-invalid", error!.ErrorCode);
            Assert.AreEqual(revision, client.WriteRevision);
            Assert.AreSame(objects, primitive.LookupCache.ObjectRecords(ObjectsLayerClassId));
        }

        [Test]
        public void ObjectWriteThatMovesItValidatesItsGrid()
        {
            var data = BuildClassBackedTileGridProjectData();
            ConfigureObjectPlacementFootprint(data, Vector2Int.one, Vector2Int.zero, new Vector2Int(1, 0));
            ((ObjectMemberValue)data.values["shop-1"]).value!.Remove("Children");
            data.values["shop-2"] = new ObjectMemberValue
            {
                id = "shop-2",
                classId = ObjectClassId,
                containerId = "objects-link-objects",
                value = new Dictionary<string, string> { ["Position"] = "shop-2-position" },
            };
            data.values["shop-2-position"] = new Vector3MemberValue
            {
                id = "shop-2-position",
                value = new NeoVector3Value { x = 13, y = 20, z = 0 },
            };
            SetPlacementTiles(data, "shop-2", Vector2Int.zero);
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoTileGridPrimitive.ResolveForSave(client, "town-grid",
                BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            Assert.AreEqual(2, primitive.LookupCache.ObjectRecords(ObjectsLayerClassId).Count);
            var row = (ObjectMemberValue)client.ResolveValueRow("shop-1")!;
            var plan = new NeoWritePlan(client);
            plan.Set(NeoValueOwnership.Save, new Vector3MemberValue
            {
                id = "shop-1-moved",
                value = new NeoVector3Value { x = 12, y = 20, z = 0 },
            });
            plan.Set(NeoValueOwnership.Save, new ObjectMemberValue
            {
                id = row.id,
                classId = row.classId,
                containerId = row.containerId,
                value = new Dictionary<string, string>(row.value!) { ["Position"] = "shop-1-moved" },
            });

            var error = Assert.Throws<NeoPlacementValidationException>(plan.Commit);

            Assert.AreEqual("tile-grid-object-cell-occupied", error!.ErrorCode);
            Assert.IsNull(plan.ValidatedObjectRefreshes);
        }

        private static void CommitResized(NeoClient client, string objectId, string? variantId,
            NeoVector3Value? repointedPosition = null)
        {
            var row = (ObjectMemberValue)client.ResolveValueRow(objectId)!;
            var plan = new NeoWritePlan(client);
            var fields = new Dictionary<string, string>(row.value!) { ["Size"] = objectId + "-resized" };
            plan.Set(NeoValueOwnership.Save, new Vector3MemberValue
            {
                id = objectId + "-resized",
                value = new NeoVector3Value { x = 3, y = 3, z = 0 },
            });
            if (repointedPosition is not null)
            {
                fields["Position"] = objectId + "-repointed";
                plan.Set(NeoValueOwnership.Save, new Vector3MemberValue
                {
                    id = objectId + "-repointed",
                    value = repointedPosition,
                });
            }
            plan.Set(NeoValueOwnership.Save, new ObjectMemberValue
            {
                id = row.id,
                classId = row.classId,
                containerId = row.containerId,
                instanceVariantId = variantId,
                value = fields,
            });
            plan.Commit();
        }
    }
}
