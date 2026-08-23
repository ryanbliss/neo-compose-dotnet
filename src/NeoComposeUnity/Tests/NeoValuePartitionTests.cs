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
    /// Storage partitions (specs/list-member-and-tilegrid-scaling.md §6):
    /// project.json ships non-main value rows under
    /// <c>valuePartitions[mapKey]</c>, lazily parsed; a world grid's
    /// <c>world:&lt;gridClassId&gt;</c> placement partition auto-loads on grid
    /// content access (the grid root + light metadata stay in main, so the
    /// type id the key derives from is resolvable before the placement subtree
    /// loads); overlay writes inherit the partition stamp; commits split the
    /// overlay by partition.
    /// </summary>
    public class NeoValuePartitionTests
    {
        private const string GridClassId = "grid-class";
        private const string TileClassId = "tile-class";
        private const string TileLayerBaseClassId = "tile-layer-base-class";
        private const string TileLayerClassId = "background-layer";
        private const string TileInstanceClassId = "tile-instance-class";
        private const string TileLayerLinkBaseClassId = "tile-layer-link-base-class";
        private const string TileLayerLinkClassId = "tile-layer-link-class";
        private const string WorldPartitionKey = "world:" + GridClassId;
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
            var tiles = primitive.GetTiles("background-layer", TileClassId);

            Assert.IsTrue(client.IsValuePartitionLoaded(WorldPartitionKey));
            Assert.AreEqual(1, tiles.Count);
            Assert.AreEqual(new Vector2Int(3, 4), tiles[0].Cell);
        }

        [Test]
        public void GridContentAccess_AfterUnload_ReloadsTransparently()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildPartitionedProjectData());
            var primitive = ResolvePrimitive(client);
            Assert.AreEqual(1, primitive.GetTiles("background-layer", TileClassId).Count);

            client.UnloadValuePartition(WorldPartitionKey);
            Assert.IsFalse(client.IsValuePartitionLoaded(WorldPartitionKey));

            // The same primitive keeps working: the query path re-ensures the
            // partition and rebuilds the spatial index.
            Assert.AreEqual(1, primitive.GetTiles("background-layer", TileClassId).Count);
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
        public void PartitionLoadAndUnloadBuildAndTearDownVirtualPlacementRows()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildPartitionedProjectData());
            client.LoadValuePartition(WorldPartitionKey);
            var placementMember = (ClassMember)client.ProjectDataForRuntime.members[
                "tile-layer-link-tile-entry-member"];
            using var placement = new NeoMemberClass(
                client,
                placementMember,
                "floor-1");
            NeoMemberBool enabled = placement.Get<NeoMemberBool>("Enabled");
            string virtualId = enabled.value!.id;

            Assert.AreEqual(true, enabled.value.value);
            Assert.AreEqual(
                WorldPartitionKey,
                client.ResolveEffectiveRow(virtualId)!.mapKey);

            client.UnloadValuePartition(WorldPartitionKey);
            Assert.IsNull(client.ResolveEffectiveRow(virtualId));

            client.LoadValuePartition(WorldPartitionKey);
            Assert.AreEqual(
                true,
                ((BoolMemberValue)client.ResolveEffectiveRow(virtualId)!).value);
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

            client.SetSaveValue(new ObjectMemberValue
            {
                id = "painted-1",
                classId = TileInstanceClassId,
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

            client.SetSaveValue(new StringMemberValue
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
            var merged = NeoSaveValues.FromTypedValues(new Dictionary<string, MemberValue>
            {
                ["row-a"] = new StringMemberValue { id = "row-a", value = "x" },
            });

            var (mainValues, partitions) = NeoSaveValuePartitions.Split(merged);

            Assert.IsNull(partitions);
            Assert.AreSame(merged, mainValues);
        }

        [Test]
        public void Split_RoutesRowsByStamp()
        {
            var merged = NeoSaveValues.FromTypedValues(new Dictionary<string, MemberValue>
            {
                ["row-a"] = new StringMemberValue { id = "row-a", value = "x" },
                ["row-b"] = new ObjectMemberValue
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
                NeoCommitResult.Committed(NeoSaveTestSupport.Remote("save-1", "head-1")));

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
            var readOnlyFactories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                [TileClassId] = (resolvedClient, node) => new TestTile(resolvedClient, node),
            };
            return NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                readOnlyFactories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>());
        }

        private sealed class TestTile : NeoGeneratedClassValue
        {
            public TestTile(NeoClient client, NeoMemberClass node)
                : base(client, node, TileClassId)
            {
            }
        }

        /// <summary>
        /// A grid whose <c>Children</c> placement subtree ships in
        /// <c>valuePartitions["world:grid-class"]</c> (keyed on the grid's
        /// concrete class id): Children list → tile layer link → unordered
        /// Tiles list ← one placement (Cell + Tile lookup). The grid root row
        /// itself lives in the MAIN partition (unstamped), so its type id — the
        /// partition key — is resolvable before the placement subtree loads.
        /// The tile asset the placement references also stays in the main
        /// partition (lookup targets are references, not owned rows).
        /// </summary>
        private static ProjectData BuildPartitionedProjectData()
        {
            var rootClass = new NeoSchemaClass
            {
                id = "root-class",
                projectId = "project-a",
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            var gridClass = new NeoSchemaClass
            {
                id = GridClassId,
                projectId = "project-a",
                name = "Grid",
                schema = new Dictionary<string, string>
                {
                    ["Children"] = "grid-children-member",
                },
            };
            var tileClass = new NeoSchemaClass
            {
                id = TileClassId,
                projectId = "project-a",
                name = "Tile",
                schema = new Dictionary<string, string>(),
            };
            var tileLayerClass = new NeoSchemaClass
            {
                id = TileLayerClassId,
                projectId = "project-a",
                name = "Background",
                extendsClassId = TileLayerBaseClassId,
                schema = new Dictionary<string, string>(),
            };
            var tileLayerBaseClass = new NeoSchemaClass
            {
                id = TileLayerBaseClassId,
                projectId = "project-a",
                name = "Neo Tile Layer",
                schema = new Dictionary<string, string>(),
                isAbstract = true,
                system = JObject.FromObject(new { worldKind = "tileLayer" }),
            };
            var tileInstanceClass = new NeoSchemaClass
            {
                id = TileInstanceClassId,
                projectId = "project-a",
                name = "Tile Instance",
                schema = new Dictionary<string, string>
                {
                    ["Cell"] = "tile-instance-cell-member",
                    ["Enabled"] = "tile-instance-enabled-member",
                },
            };
            var tileLayerLinkBaseClass = new NeoSchemaClass
            {
                id = TileLayerLinkBaseClassId,
                projectId = "project-a",
                name = "Neo Tile Layer Link",
                isAbstract = true,
                schema = new Dictionary<string, string>
                {
                    ["Tiles"] = "tile-layer-link-tiles-member",
                },
                system = JObject.FromObject(new { worldKind = "tileLayerLink" }),
            };
            var tileLayerLinkClass = new NeoSchemaClass
            {
                id = TileLayerLinkClassId,
                projectId = "project-a",
                name = "Tile Layer Link",
                extendsClassId = TileLayerLinkBaseClassId,
                schema = new Dictionary<string, string>(),
            };

            JObject floorPlacement = PartitionRow(
                "floor-1",
                TileInstanceClassId,
                new JObject
                {
                    ["Cell"] = "floor-1-cell",
                    ["assetClassId"] = TileClassId,
                },
                containerId: TilesListValueId);
            floorPlacement["instanceConstructorId"] = JValue.CreateNull();
            floorPlacement["constructorArgs"] = new JObject();

            var partition = new JObject
            {
                ["town-grid-children"] = PartitionRow(
                    "town-grid-children", null, new JArray("background-link")),
                ["background-link"] = PartitionRow(
                    "background-link",
                    TileLayerLinkClassId,
                    new JObject
                    {
                        ["Tiles"] = TilesListValueId,
                    }),
                [TilesListValueId] = PartitionRow(TilesListValueId, null, new JArray()),
                ["floor-1"] = floorPlacement,
                ["floor-1-cell"] = PartitionRow(
                    "floor-1-cell", null, new JObject { ["x"] = 3, ["y"] = 4 }),
            };

            return new ProjectData
            {
                project = new Project
                {
                    id = "project-a",
                    _id = "project-a",
                    name = "Partition Test",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = new Dictionary<string, NeoCompose.Runtime.Json.Member>
                {
                    ["root-assets"] = RootMember("root-assets", "root-assets-value", rootClass.id),
                    ["root-save"] = RootMember("root-save", "root-save-value", rootClass.id),
                    ["root-session"] = RootMember("root-session", "root-session-value", rootClass.id),
                    ["grid-children-member"] = new ListMember
                    {
                        id = "grid-children-member",
                        projectId = "project-a",
                        name = "Children",
                        kind = MemberKind.List,
                        entryMemberId = "grid-child-entry-member",
                    },
                    ["grid-child-entry-member"] = new ClassMember
                    {
                        id = "grid-child-entry-member",
                        projectId = "project-a",
                        name = "Child",
                        kind = MemberKind.Class,
                        classId = TileLayerLinkClassId,
                    },
                    ["tile-instance-cell-member"] = new Vector2IntMember
                    {
                        id = "tile-instance-cell-member",
                        projectId = "project-a",
                        name = "Cell",
                        kind = MemberKind.Vector2Int,
                        required = true,
                        defaultValue = new Vector2MemberValueBase
                        {
                            value = new NeoVector2Value { x = 0, y = 0 },
                        },
                    },
                    ["tile-instance-enabled-member"] = new BoolMember
                    {
                        id = "tile-instance-enabled-member",
                        projectId = "project-a",
                        name = "Enabled",
                        kind = MemberKind.Bool,
                        required = true,
                        defaultValue = new BoolMemberValueBase { value = true },
                    },
                    ["tile-layer-link-tiles-member"] = new ListMember
                    {
                        id = "tile-layer-link-tiles-member",
                        projectId = "project-a",
                        name = "Tiles",
                        kind = MemberKind.List,
                        entryMemberId = "tile-layer-link-tile-entry-member",
                        listKind = NeoListKinds.Unordered,
                        storageKey = WorldPartitionKey,
                    },
                    ["tile-layer-link-tile-entry-member"] = new ClassMember
                    {
                        id = "tile-layer-link-tile-entry-member",
                        projectId = "project-a",
                        name = "Tile",
                        kind = MemberKind.Class,
                        classId = TileInstanceClassId,
                    },
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["root-assets-value"] = ObjectValue("root-assets-value", rootClass.id),
                    ["root-save-value"] = ObjectValue("root-save-value", rootClass.id),
                    ["root-session-value"] = ObjectValue("root-session-value", rootClass.id),
                    // The grid ROOT + its light metadata live in the main
                    // partition (unstamped): only the Children placement subtree
                    // is partitioned, and the partition key derives from this
                    // row's classId.
                    ["town-grid"] = new ObjectMemberValue
                    {
                        id = "town-grid",
                        classId = GridClassId,
                        value = new Dictionary<string, string>
                        {
                            ["Children"] = "town-grid-children",
                        },
                    },
                    ["floor-tile"] = ObjectValue("floor-tile", TileClassId),
                },
                valuePartitions = new Dictionary<string, JToken>
                {
                    [WorldPartitionKey] = partition,
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [rootClass.id] = rootClass,
                    [GridClassId] = gridClass,
                    [TileClassId] = tileClass,
                    [TileLayerBaseClassId] = tileLayerBaseClass,
                    [TileLayerClassId] = tileLayerClass,
                    [TileInstanceClassId] = tileInstanceClass,
                    [TileLayerLinkBaseClassId] = tileLayerLinkBaseClass,
                    [TileLayerLinkClassId] = tileLayerLinkClass,
                },
                internalRecordRelations = new Dictionary<string, InternalRecordRelation>
                {
                    ["grid-background-layer"] = ClassRelation(
                        "grid-background-layer",
                        InternalRecordRelationKinds.WorldGridTileLayer,
                        GridClassId,
                        TileLayerClassId,
                        "a0"),
                    ["background-link-target"] = ClassRelation(
                        "background-link-target",
                        InternalRecordRelationKinds.WorldTileLayerLinkTarget,
                        TileLayerLinkClassId,
                        TileLayerClassId),
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
        }

        private static InternalRecordRelation ClassRelation(
            string id,
            string relationKind,
            string sourceClassId,
            string targetClassId,
            string? orderKey = null)
        {
            return new InternalRecordRelation
            {
                id = id,
                projectId = "project-a",
                relationKind = relationKind,
                sourceRecordKind = "class",
                sourceRecordId = sourceClassId,
                targetRecordKind = "class",
                targetRecordId = targetClassId,
                orderKey = orderKey,
                createdAt = "2026-07-17T00:00:00.000Z",
                updatedAt = "2026-07-17T00:00:00.000Z",
            };
        }

        private static JObject PartitionRow(
            string id,
            string? classId,
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
            if (classId is not null) row["classId"] = classId;
            if (containerId is not null) row["containerId"] = containerId;
            return row;
        }

        private static ClassMember RootMember(
            string id,
            string valueId,
            string classId)
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

        private static ObjectMemberValue ObjectValue(string id, string classId)
        {
            return new ObjectMemberValue
            {
                id = id,
                classId = classId,
                value = new Dictionary<string, string>(),
            };
        }
    }
}
