// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace NeoCompose.Tests
{
    /// <summary>
    /// Storage partitions (specs/list-attribute-and-tilegrid-scaling.md §6):
    /// project.json ships non-main value rows under
    /// <c>valuePartitions[mapKey]</c>, lazily parsed; a world grid's
    /// <c>world:&lt;gridTypeId&gt;</c> placement partition auto-loads on grid
    /// content access (the grid root + light metadata stay in main, so the
    /// type id the key derives from is resolvable before the placement subtree
    /// loads); overlay writes inherit the partition stamp; commits split the
    /// overlay by partition.
    /// </summary>
    public class NeoValuePartitionTests
    {
        private const string GridTypeId = "grid-type";
        private const string TileTypeId = "tile-type";
        private const string TileInstanceTypeId = "tile-instance-type";
        private const string TileLayerLinkTypeId = "tile-layer-link-type";
        private const string WorldPartitionKey = "world:" + GridTypeId;
        private const string TilesListValueId = "background-link-tiles";

        // ------------------------------------------------------------------
        // Lazy parse + load.
        // ------------------------------------------------------------------

        [Test]
        public void PartitionRows_AreAbsentBeforeLoad()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildPartitionedProjectData());

            CollectionAssert.IsEmpty(client.LoadedValuePartitions);
            // The grid root lives in main — enumerable/nameable without the
            // placement partition loaded.
            Assert.IsTrue(client.values.ContainsKey("town-grid"));
            // The Children placement subtree is not merged yet.
            Assert.IsFalse(client.values.ContainsKey("town-grid-children"));
            Assert.IsFalse(client.values.ContainsKey("floor-1"));
            // Membership joins of not-loaded rows resolve to nothing — same
            // as before the level existed. No throw on lookup.
            CollectionAssert.IsEmpty(client.GetUnorderedListEntryIds(TilesListValueId));
        }

        [Test]
        public void LoadValuePartition_MergesRowsAndMembershipIndex()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildPartitionedProjectData());

            client.LoadValuePartition(WorldPartitionKey);

            Assert.IsTrue(client.IsValuePartitionLoaded(WorldPartitionKey));
            CollectionAssert.AreEquivalent(
                new[] { WorldPartitionKey }, client.LoadedValuePartitions);
            // The Children placement subtree merged in from the partition.
            Assert.IsTrue(client.values.ContainsKey("town-grid-children"));
            Assert.IsTrue(client.values.ContainsKey("floor-1"));
            Assert.AreEqual(WorldPartitionKey, client.values["floor-1"].mapKey);
            CollectionAssert.AreEqual(
                new[] { "floor-1" },
                client.GetUnorderedListEntryIds(TilesListValueId).ToArray());

            // Idempotent: a second load is a no-op, not a duplicate merge.
            Assert.DoesNotThrow(() => client.LoadValuePartition(WorldPartitionKey));
            CollectionAssert.AreEqual(
                new[] { "floor-1" },
                client.GetUnorderedListEntryIds(TilesListValueId).ToArray());
        }

        [Test]
        public void LoadValuePartition_UnknownKeyThrowsWithAvailableKeys()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildPartitionedProjectData());

            var exception = Assert.Throws<System.ArgumentOutOfRangeException>(
                () => client.LoadValuePartition("world:missing-grid"));
            StringAssert.Contains("world:missing-grid", exception!.Message);
            StringAssert.Contains(WorldPartitionKey, exception.Message);
        }

        [Test]
        public void MainValuesRow_WithPartitionStamp_IsRejectedLoudly()
        {
            var data = BuildPartitionedProjectData();
            data.values["floor-tile"].mapKey = WorldPartitionKey;

            var exception = Assert.Throws<System.InvalidOperationException>(
                () => NeoTestSaveStack.ClientFromSchema(data));
            StringAssert.Contains("floor-tile", exception!.Message);
            StringAssert.Contains(WorldPartitionKey, exception.Message);
        }

        // ------------------------------------------------------------------
        // Auto-load on grid content access.
        // ------------------------------------------------------------------

        [Test]
        public void GridPrimitiveResolution_AutoLoadsTheWorldPartition()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildPartitionedProjectData());

            var primitive = ResolvePrimitive(client);
            var tiles = primitive.GetTiles("background-layer", TileTypeId);

            Assert.IsTrue(client.IsValuePartitionLoaded(WorldPartitionKey));
            Assert.AreEqual(1, tiles.Count);
            Assert.AreEqual(new Vector2Int(3, 4), tiles[0].Cell);
        }

        [Test]
        public void GridContentAccess_AfterUnload_ReloadsTransparently()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildPartitionedProjectData());
            var primitive = ResolvePrimitive(client);
            Assert.AreEqual(1, primitive.GetTiles("background-layer", TileTypeId).Count);

            client.UnloadValuePartition(WorldPartitionKey);
            Assert.IsFalse(client.IsValuePartitionLoaded(WorldPartitionKey));

            // The same primitive keeps working: the query path re-ensures the
            // partition and rebuilds the spatial index.
            Assert.AreEqual(1, primitive.GetTiles("background-layer", TileTypeId).Count);
            Assert.IsTrue(client.IsValuePartitionLoaded(WorldPartitionKey));
        }

        // ------------------------------------------------------------------
        // Unload.
        // ------------------------------------------------------------------

        [Test]
        public void UnloadValuePartition_DropsRowsAndMembershipIndex()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildPartitionedProjectData());
            client.LoadValuePartition(WorldPartitionKey);

            client.UnloadValuePartition(WorldPartitionKey);

            CollectionAssert.IsEmpty(client.LoadedValuePartitions);
            Assert.IsFalse(client.values.ContainsKey("town-grid-children"));
            Assert.IsFalse(client.values.ContainsKey("floor-1"));
            CollectionAssert.IsEmpty(client.GetUnorderedListEntryIds(TilesListValueId));
            // Main-partition rows are untouched: the grid root (nameable
            // without the placement subtree) and the referenced tile asset.
            Assert.IsTrue(client.values.ContainsKey("town-grid"));
            Assert.IsTrue(client.values.ContainsKey("floor-tile"));
        }

        [Test]
        public void UnloadValuePartition_NotLoadedThrows()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildPartitionedProjectData());

            var exception = Assert.Throws<System.InvalidOperationException>(
                () => client.UnloadValuePartition(WorldPartitionKey));
            StringAssert.Contains("not loaded", exception!.Message);
        }

        [Test]
        public void UnloadValuePartition_WithOverlayShadowThrows()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildPartitionedProjectData());
            client.LoadValuePartition(WorldPartitionKey);
            // Shadow an authored partition row into the save overlay — a
            // pending write the unload must refuse to strand.
            Assert.IsTrue(client.EnsureWritableShadow(NeoValueOwnership.Save, "floor-1"));

            var exception = Assert.Throws<System.InvalidOperationException>(
                () => client.UnloadValuePartition(WorldPartitionKey));
            StringAssert.Contains("floor-1", exception!.Message);
            StringAssert.Contains(WorldPartitionKey, exception.Message);
        }

        // ------------------------------------------------------------------
        // Overlay writes inherit the partition stamp.
        // ------------------------------------------------------------------

        [Test]
        public void SaveShadow_OfAuthoredPartitionRow_InheritsTheStamp()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildPartitionedProjectData());
            client.LoadValuePartition(WorldPartitionKey);

            Assert.IsTrue(client.EnsureWritableShadow(NeoValueOwnership.Save, "floor-1-cell"));

            Assert.IsTrue(client.saveValues.TryGetValue("floor-1-cell", out var shadow));
            Assert.AreEqual(WorldPartitionKey, shadow!.mapKey);
        }

        [Test]
        public void CreatedMemberRow_InheritsTheContainerStamp()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildPartitionedProjectData());
            client.LoadValuePartition(WorldPartitionKey);

            client.SetSaveValue(new ObjectAttributeValue
            {
                id = "painted-1",
                typeId = TileInstanceTypeId,
                containerId = TilesListValueId,
                value = new Dictionary<string, string>(),
            });

            Assert.AreEqual(WorldPartitionKey, client.saveValues["painted-1"].mapKey);
            CollectionAssert.AreEqual(
                new[] { "floor-1", "painted-1" },
                client.GetUnorderedListEntryIds(TilesListValueId).ToArray());
        }

        [Test]
        public void RemovalTombstone_OfPartitionRow_InheritsTheStamp()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildPartitionedProjectData());
            client.LoadValuePartition(WorldPartitionKey);

            client.WriteRemovalTombstone(NeoValueOwnership.Save, "floor-1");

            Assert.IsTrue(client.saveValues.TryGetValue("floor-1", out var tombstone));
            Assert.IsTrue(tombstone!.IsRemoved);
            Assert.AreEqual(WorldPartitionKey, tombstone.mapKey);
        }

        [Test]
        public void MainPartitionWrites_StayUnstamped()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildPartitionedProjectData());

            client.SetSaveValue(new StringAttributeValue
            {
                id = "plain-save-row",
                value = "hello",
            });

            Assert.IsNull(client.saveValues["plain-save-row"].mapKey);
        }

        [Test]
        public void SaveRoundTrip_PreservesTheStamp()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildPartitionedProjectData());
            client.LoadValuePartition(WorldPartitionKey);
            client.WriteRemovalTombstone(NeoValueOwnership.Save, "floor-1");

            string serialized = client.SerializeSaveData();
            var save = LocalGameSaveLoader.Load(serialized);
            Assert.IsTrue(save.TryDeserializeValues(out var rows));
            Assert.AreEqual(WorldPartitionKey, rows["floor-1"].mapKey);
        }

        // ------------------------------------------------------------------
        // Commit split.
        // ------------------------------------------------------------------

        [Test]
        public void Split_UnstampedOverlay_PassesThroughZeroCopy()
        {
            var merged = NeoSaveValues.FromTypedValues(new Dictionary<string, AttributeValue>
            {
                ["row-a"] = new StringAttributeValue { id = "row-a", value = "x" },
            });

            var (mainValues, partitions) = NeoSaveValuePartitions.Split(merged);

            Assert.IsNull(partitions);
            Assert.AreSame(merged, mainValues);
        }

        [Test]
        public void Split_RoutesRowsByStamp()
        {
            var merged = NeoSaveValues.FromTypedValues(new Dictionary<string, AttributeValue>
            {
                ["row-a"] = new StringAttributeValue { id = "row-a", value = "x" },
                ["row-b"] = new ObjectAttributeValue
                {
                    id = "row-b",
                    mapKey = WorldPartitionKey,
                    value = new Dictionary<string, string>(),
                },
            });

            var (mainValues, partitions) = NeoSaveValuePartitions.Split(merged);

            var mainObject = (JObject)mainValues.Raw;
            Assert.IsNotNull(mainObject["row-a"]);
            Assert.IsNull(mainObject["row-b"]);
            Assert.IsNotNull(partitions);
            CollectionAssert.AreEquivalent(new[] { WorldPartitionKey }, partitions!.Keys);
            var partitionObject = (JObject)partitions[WorldPartitionKey].Raw;
            Assert.IsNotNull(partitionObject["row-b"]);
        }

        [Test]
        public async Task CommitRequest_SplitsTheOverlayByPartition()
        {
            var api = new FakeApiClient();
            var local = new NeoInMemoryLocalSaveStore();
            var store = new NeoProjectStore(
                dataSource: new NeoJsonProjectDataSource(NeoSaveTestSupport.ProjectJson),
                localStore: local,
                apiClient: api,
                targetReleaseChannelId: NeoSaveTestSupport.TargetChannel);
            await store.LoadAsync();
            api.commitResults.Enqueue(
                NeoCommitResult.Committed(NeoSaveTestSupport.Remote("save-1", "head-1", "h1")));

            var sync = store.CreateNew("save-1");
            string values =
                "{\"main-row\":{\"id\":\"main-row\",\"createdAt\":1,\"updatedAt\":1,\"value\":\"x\"}," +
                "\"world-row\":{\"id\":\"world-row\",\"createdAt\":1,\"updatedAt\":1," +
                "\"mapKey\":\"" + WorldPartitionKey + "\",\"value\":\"y\"}}";
            await sync.CommitSaveContentAsync(
                NeoSaveTestSupport.SaveContent("Local", values), replaceSnapshot: false);

            Assert.That(api.commits, Has.Count.EqualTo(1));
            var request = api.commits[0].request;
            var mainObject = (JObject)request.values.Raw;
            Assert.IsNotNull(mainObject["main-row"]);
            Assert.IsNull(mainObject["world-row"], "Stamped rows leave the main overlay.");
            Assert.IsNotNull(request.valuePartitions);
            CollectionAssert.AreEquivalent(
                new[] { WorldPartitionKey }, request.valuePartitions!.Keys);
            Assert.IsNotNull(((JObject)request.valuePartitions[WorldPartitionKey].Raw)["world-row"]);
        }

        // ------------------------------------------------------------------
        // Fixture.
        // ------------------------------------------------------------------

        /// <summary>Tile resolution requires a generated-value factory for
        /// the tile type; queries otherwise mirror production wiring.</summary>
        private static NeoReadOnlyTileGridPrimitive ResolvePrimitive(NeoClient client)
        {
            var readOnlyFactories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyCustomFactory>
            {
                [TileTypeId] = (resolvedClient, node) => new TestTile(resolvedClient, node),
            };
            return NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                readOnlyFactories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>());
        }

        private sealed class TestTile : NeoGeneratedCustomValue
        {
            public TestTile(NeoClient client, NeoAttributeCustom node)
                : base(client, node, TileTypeId)
            {
            }
        }

        /// <summary>
        /// A grid whose <c>Children</c> placement subtree ships in
        /// <c>valuePartitions["world:grid-type"]</c> (keyed on the grid's
        /// concrete type id): Children list → tile layer link → unordered
        /// Tiles list ← one placement (Cell + Tile lookup). The grid root row
        /// itself lives in the MAIN partition (unstamped), so its type id — the
        /// partition key — is resolvable before the placement subtree loads.
        /// The tile asset the placement references also stays in the main
        /// partition (lookup targets are references, not owned rows).
        /// </summary>
        private static ProjectData BuildPartitionedProjectData()
        {
            var rootType = new CustomType
            {
                id = "root-type",
                projectId = "project-a",
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            var gridType = new CustomType
            {
                id = GridTypeId,
                projectId = "project-a",
                name = "Grid",
                schema = new Dictionary<string, string>
                {
                    ["Children"] = "grid-children-attribute",
                },
            };
            var tileType = new CustomType
            {
                id = TileTypeId,
                projectId = "project-a",
                name = "Tile",
                schema = new Dictionary<string, string>(),
            };
            var tileInstanceType = new CustomType
            {
                id = TileInstanceTypeId,
                projectId = "project-a",
                name = "Tile Instance",
                schema = new Dictionary<string, string>
                {
                    ["Cell"] = "tile-instance-cell-attribute",
                    ["Tile"] = "tile-instance-tile-attribute",
                },
            };
            var tileLayerLinkType = new CustomType
            {
                id = TileLayerLinkTypeId,
                projectId = "project-a",
                name = "Tile Layer Link",
                schema = new Dictionary<string, string>
                {
                    ["TileLayer"] = "tile-layer-link-layer-attribute",
                    ["Tiles"] = "tile-layer-link-tiles-attribute",
                },
            };

            var partition = new JObject
            {
                ["town-grid-children"] = PartitionRow(
                    "town-grid-children", null, new JArray("background-link")),
                ["background-link"] = PartitionRow(
                    "background-link",
                    TileLayerLinkTypeId,
                    new JObject
                    {
                        ["TileLayer"] = "background-link-layer",
                        ["Tiles"] = TilesListValueId,
                    }),
                ["background-link-layer"] = PartitionRow(
                    "background-link-layer", null, new JArray("background-layer")),
                [TilesListValueId] = PartitionRow(TilesListValueId, null, new JArray()),
                ["floor-1"] = PartitionRow(
                    "floor-1",
                    TileInstanceTypeId,
                    new JObject
                    {
                        ["Cell"] = "floor-1-cell",
                        ["Tile"] = "floor-1-tile",
                    },
                    containerId: TilesListValueId),
                ["floor-1-cell"] = PartitionRow(
                    "floor-1-cell", null, new JObject { ["x"] = 3, ["y"] = 4 }),
                ["floor-1-tile"] = PartitionRow(
                    "floor-1-tile", null, new JArray("floor-tile")),
            };

            return new ProjectData
            {
                project = new Project
                {
                    id = "project-a",
                    _id = "project-a",
                    name = "Partition Test",
                    rootAssetsAttributeId = "root-assets",
                    rootSaveFileAttributeId = "root-save",
                    rootSessionAttributeId = "root-session",
                },
                attributes = new Dictionary<string, NeoCompose.Runtime.Json.Attribute>
                {
                    ["root-assets"] = RootAttribute("root-assets", "root-assets-value", rootType.id),
                    ["root-save"] = RootAttribute("root-save", "root-save-value", rootType.id),
                    ["root-session"] = RootAttribute("root-session", "root-session-value", rootType.id),
                    ["grid-children-attribute"] = new ListAttribute
                    {
                        id = "grid-children-attribute",
                        projectId = "project-a",
                        name = "Children",
                        type = AttributeType.List,
                        entryAttributeId = "grid-child-entry-attribute",
                    },
                    ["grid-child-entry-attribute"] = new CustomAttribute
                    {
                        id = "grid-child-entry-attribute",
                        projectId = "project-a",
                        name = "Child",
                        type = AttributeType.Custom,
                        customTypeId = TileLayerLinkTypeId,
                    },
                    ["tile-instance-cell-attribute"] = new Vector2IntAttribute
                    {
                        id = "tile-instance-cell-attribute",
                        projectId = "project-a",
                        name = "Cell",
                        type = AttributeType.Vector2Int,
                    },
                    ["tile-instance-tile-attribute"] = new LookupAttribute
                    {
                        id = "tile-instance-tile-attribute",
                        projectId = "project-a",
                        name = "Tile",
                        type = AttributeType.Lookup,
                        collectionAttributeId = "tile-layer-link-tiles-attribute",
                    },
                    ["tile-layer-link-layer-attribute"] = new LookupAttribute
                    {
                        id = "tile-layer-link-layer-attribute",
                        projectId = "project-a",
                        name = "TileLayer",
                        type = AttributeType.Lookup,
                        collectionAttributeId = "grid-children-attribute",
                    },
                    ["tile-layer-link-tiles-attribute"] = new ListAttribute
                    {
                        id = "tile-layer-link-tiles-attribute",
                        projectId = "project-a",
                        name = "Tiles",
                        type = AttributeType.List,
                        entryAttributeId = "tile-layer-link-tile-entry-attribute",
                        listKind = NeoListKinds.Unordered,
                        storageKey = WorldPartitionKey,
                    },
                    ["tile-layer-link-tile-entry-attribute"] = new CustomAttribute
                    {
                        id = "tile-layer-link-tile-entry-attribute",
                        projectId = "project-a",
                        name = "Tile",
                        type = AttributeType.Custom,
                        customTypeId = TileInstanceTypeId,
                    },
                },
                values = new Dictionary<string, AttributeValue>
                {
                    ["root-assets-value"] = ObjectValue("root-assets-value", rootType.id),
                    ["root-save-value"] = ObjectValue("root-save-value", rootType.id),
                    ["root-session-value"] = ObjectValue("root-session-value", rootType.id),
                    // The grid ROOT + its light metadata live in the main
                    // partition (unstamped): only the Children placement subtree
                    // is partitioned, and the partition key derives from this
                    // row's typeId.
                    ["town-grid"] = new ObjectAttributeValue
                    {
                        id = "town-grid",
                        typeId = GridTypeId,
                        value = new Dictionary<string, string>
                        {
                            ["Children"] = "town-grid-children",
                        },
                    },
                    ["floor-tile"] = ObjectValue("floor-tile", TileTypeId),
                },
                valuePartitions = new Dictionary<string, JToken>
                {
                    [WorldPartitionKey] = partition,
                },
                types = new Dictionary<string, CustomType>
                {
                    [rootType.id] = rootType,
                    [GridTypeId] = gridType,
                    [TileTypeId] = tileType,
                    [TileInstanceTypeId] = tileInstanceType,
                    [TileLayerLinkTypeId] = tileLayerLinkType,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
        }

        private static JObject PartitionRow(
            string id,
            string? typeId,
            JToken value,
            string? containerId = null)
        {
            var row = new JObject
            {
                ["id"] = id,
                ["createdAt"] = 1,
                ["updatedAt"] = 1,
                ["mapKey"] = WorldPartitionKey,
                ["value"] = value,
            };
            if (typeId is not null) row["typeId"] = typeId;
            if (containerId is not null) row["containerId"] = containerId;
            return row;
        }

        private static CustomAttribute RootAttribute(
            string id,
            string valueId,
            string customTypeId)
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

        private static ObjectAttributeValue ObjectValue(string id, string typeId)
        {
            return new ObjectAttributeValue
            {
                id = id,
                typeId = typeId,
                value = new Dictionary<string, string>(),
            };
        }
    }
}
