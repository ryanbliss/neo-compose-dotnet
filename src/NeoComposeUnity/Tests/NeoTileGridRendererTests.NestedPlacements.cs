// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeoCompose.Tests
{
    public partial class NeoTileGridRendererTests
    {
        private static ProjectData BuildNestedObjectPlacementData()
        {
            var data = BuildNestedCarriedLinkProjectData();
            data.classes[ObjectLayerLinkClassId].schema["Position"] = "object-position-member";
            ((ObjectMemberValue)data.values["objects-link"]).value!["Position"] = "root-link-position";
            data.values["root-link-position"] = new Vector3MemberValue
            {
                id = "root-link-position",
                value = new NeoVector3Value { x = 0, y = 0, z = 0 }
            };
            ((ArrayMemberValue)data.values["shop-part-children"]).value = new[] { "part-link", "nested-objects-link" };
            data.values["nested-objects-link"] = new ObjectMemberValue
            {
                id = "nested-objects-link",
                classId = ObjectLayerLinkClassId,
                value = new Dictionary<string, string> { ["Objects"] = "nested-objects", ["Position"] = "nested-link-position" }
            };
            data.values["nested-link-position"] = new Vector3MemberValue
            {
                id = "nested-link-position",
                value = new NeoVector3Value { x = 2, y = 0, z = 0 }
            };
            data.values["nested-objects"] = new ArrayMemberValue { id = "nested-objects", value = new[] { "door" } };
            data.values["door"] = new ObjectMemberValue
            {
                id = "door",
                classId = ObjectClassId,
                value = new Dictionary<string, string> { ["Position"] = "door-position" }
            };
            data.values["door-position"] = new Vector3MemberValue
            {
                id = "door-position",
                value = new NeoVector3Value { x = 1, y = 0, z = 0 }
            };
            SetPlacementTiles(data, "door", Vector2Int.zero, Vector2Int.right);
            return data;
        }

        [Test]
        public void NestedLinkMembersOccupyTheirOwningGridAndMoveWithAncestors()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildNestedObjectPlacementData());
            var grid = NeoTileGridPrimitive.ResolveForSave(client, "town-grid",
                BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            var cache = grid.LookupCache;
            var door = cache.ObjectRecord(ObjectsLayerClassId, "door")!;
            Assert.That(door.Cell, Is.EqualTo(new Vector2Int(16, 21)));
            Assert.That(door.SourceLayerLinkId, Is.EqualTo("nested-objects-link"));
            Assert.That(cache.ObjectCandidatesAt(ObjectsLayerClassId, new Vector2Int(17, 21)).Single().InstanceId, Is.EqualTo("door"));
            var records = cache.ObjectRecords(ObjectsLayerClassId);
            var node = NeoGeneratedTypesSupport.AsWritable(((NeoGeneratedClassValue)NeoGeneratedTypesSupport.ResolveClassValue(
                client, "shop-part", BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories())!).BackingNode);
            NeoGeneratedTypesSupport.SetValue(node, "Position", NeoValueWritePayload.FromValue(new Vector3(5, 1, 0)));
            Assert.That(cache.ObjectRecord(ObjectsLayerClassId, "door")!.Cell, Is.EqualTo(new Vector2Int(18, 21)));
            Assert.That(cache.ObjectRecords(ObjectsLayerClassId), Is.SameAs(records), "Movement patches the existing index.");
            Assert.That(cache.ObjectCandidatesAt(ObjectsLayerClassId, new Vector2Int(16, 21)), Is.Empty);
        }

        [Test]
        public void NestedPlacementCollisionRejectsTheWholeAncestorMove()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildNestedObjectPlacementData());
            var grid = NeoTileGridPrimitive.ResolveForSave(client, "town-grid",
                BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            var link = WritableObjectLink(client, "objects-link");
            var obstacle = ClonePlacementObject(client);
            Assert.That(link.TrySpawn(new Vector2Int(18, 21), obstacle).Ok, Is.True);
            var before = grid.LookupCache.ObjectRecord(ObjectsLayerClassId, "door");
            var node = NeoGeneratedTypesSupport.AsWritable(((NeoGeneratedClassValue)NeoGeneratedTypesSupport.ResolveClassValue(
                client, "shop-part", BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories())!).BackingNode);
            Assert.Throws<NeoPlacementValidationException>(() => NeoGeneratedTypesSupport.SetValue(node, "Position",
                NeoValueWritePayload.FromValue(new Vector3(5, 1, 0))));
            Assert.That(grid.LookupCache.ObjectRecord(ObjectsLayerClassId, "door"), Is.SameAs(before));
            Assert.That(((Vector3MemberValue)client.ResolveClassChildRow((ObjectMemberValue)client.ResolveValueRow("shop-part")!, "Position")!).value!.x,
                Is.EqualTo(3));
        }

        [Test]
        public void NestedLinkSpawnChecksOtherLinksAndRetainsLocalPosition()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildNestedObjectPlacementData());
            var grid = NeoTileGridPrimitive.ResolveForSave(client, "town-grid",
                BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            var link = WritableObjectLink(client, "nested-objects-link");
            var entry = ClonePlacementObject(client);
            Assert.That(link.TrySpawn(new Vector2Int(17, 21), entry).Ok, Is.False);
            var result = link.TrySpawn(new Vector2Int(40, 50), entry);
            Assert.That(result.Ok, Is.True, result.Message);
            Assert.That(grid.LookupCache.ObjectRecord(ObjectsLayerClassId, entry.valueId!)!.Cell, Is.EqualTo(new Vector2Int(40, 50)));
            var row = (ObjectMemberValue)client.ResolveValueRow(entry.valueId!)!;
            var position = ((Vector3MemberValue)client.ResolveClassChildRow(row, "Position")!).value!;
            Assert.That(position.x, Is.EqualTo(25));
            Assert.That(position.y, Is.EqualTo(29));
        }
        [Test]
        public void NestedSubcellMoveUpdatesTheNextAncestorCollisionCheck()
        {
            var data = BuildNestedObjectPlacementData();
            ((Vector3MemberValue)data.values["door-position"]).value!.x = 0.1;
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var grid = NeoTileGridPrimitive.ResolveForSave(client, "town-grid", BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            Assert.That(grid.LookupCache.ObjectRecord(ObjectsLayerClassId, "door")!.Cell.x, Is.EqualTo(15));
            NeoGeneratedTypesSupport.SetPlacementVector3(WritableObject(client, "door"), "Position", new NeoReadOnlyVector3(0.4f, 0, 0));
            NeoGeneratedTypesSupport.SetPlacementVector3(WritableObject(client, "shop-part"), "Position", new NeoReadOnlyVector3(3.2f, 1, 0));
            Assert.That(grid.LookupCache.ObjectRecord(ObjectsLayerClassId, "door")!.Cell.x, Is.EqualTo(16));
            Assert.That(grid.LookupCache.ObjectCandidatesAt(ObjectsLayerClassId, new Vector2Int(15, 21)), Is.Empty);
        }

        [Test]
        public void RemovingNestedObjectLinksRemovesTheirIndexedOccupants()
        {
            var data = BuildNestedObjectPlacementData();
            ((ArrayMemberValue)data.values["shop-1-children"]).value = new[] { "shop-part" };
            ((ArrayMemberValue)data.values["shop-part-children"]).value = new[] { "nested-objects-link" };
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var grid = NeoTileGridPrimitive.ResolveForSave(client, "town-grid", BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            Assert.That(grid.LookupCache.ObjectRecord(ObjectsLayerClassId, "door"), Is.Not.Null);
            client.SetWritableValue(NeoValueOwnership.Save, new ArrayMemberValue { id = "shop-1-children", value = Array.Empty<string>() });
            Assert.That(grid.LookupCache.ObjectRecord(ObjectsLayerClassId, "door"), Is.Null);
            Assert.That(grid.LookupCache.ObjectCandidatesAt(ObjectsLayerClassId, new Vector2Int(16, 21)), Is.Empty);
        }

        [Test]
        public void FastInsertionRetainsSourceLinkForNativeQueries()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildCellPatternTileGridProjectData());
            RegisterGridGetterContent(client);
            var grid = NeoTileGridPrimitive.ResolveForSave(client, "town-grid", BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            _ = grid.LookupCache.ObjectRecords(ObjectsLayerClassId);
            var link = WritableObjectLink(client, "objects-link");
            var entry = ClonePlacementObject(client);
            Assert.That(link.TrySpawn(new Vector2Int(40, 50), entry).Ok, Is.True);
            Assert.That(grid.LookupCache.ObjectRecord(ObjectsLayerClassId, entry.valueId!)!.SourceLayerLinkId, Is.EqualTo("objects-link"));
            var ctx = client.CreateGetterContext(NeoValueOwnership.Save);
            Assert.That(NeoWorldLayerRuntime.TryInvoke("system_cce85e15-bede-5e77-8463-63805445eeeb", link,
                new object?[] { new Vector2Int(40, 50) }, null, ctx, out var sequence), Is.True);
            Assert.That(QueryValues(sequence, ctx).Select(value => NeoCompose.Runtime.NeoScript.NSGetterEvaluator.FindRowIdByReference(value, ctx)), Is.EqualTo(new[] { entry.valueId }));
        }

        [Test]
        public void NestedRenderingFollowsAncestorFractionsHeightAndVisibility()
        {
            var data = BuildNestedObjectPlacementData();
            data.classes[ObjectLayerLinkClassId].schema["Enabled"] = "object-enabled-member";
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var factories = BuildClassBackedReadOnlyFactories();
            factories[ObjectClassId] = (c, n) => NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(c, n,
                () => new TestComposedObject(c, n) { PositionFromNode = true, EnabledFromNode = true });
            factories[ObjectLayerLinkClassId] = (c, n) => NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(c, n,
                () => new TestObjectLayerLink(c, n) { WorldFieldsFromNode = true });
            var grid = NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid", factories, BuildClassBackedWritableFactories());
            var go = new GameObject("nested-rendering");
            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(new TestTileGridContent(grid, Array.Empty<IReadOnlyNeoTileLayerRuntime>(),
                    new[] { grid.BindReadOnlyObjectLayer<TestAuthoredObjectLayer>(ObjectsLayerClassId, new[] { ObjectClassId }) }));
                Assert.That(renderer.TryGetObjectRoot("door", out var door), Is.True);
                Assert.That(door.transform.localPosition, Is.EqualTo(new Vector3(16, 21, 0)));
                NeoGeneratedTypesSupport.SetPlacementVector3(WritableObject(client, "shop-part"), "Position", new NeoReadOnlyVector3(3.2f, 1, 4));
                Assert.That(door.transform.localPosition.x, Is.EqualTo(16.2f).Within(0.001));
                Assert.That(door.transform.localPosition.z, Is.EqualTo(4));
                NeoGeneratedTypesSupport.SetValue(WritableObject(client, "shop-part"), "Enabled", NeoValueWritePayload.FromValue(false));
                Assert.That(door.activeSelf, Is.False);
                NeoGeneratedTypesSupport.SetValue(WritableObject(client, "shop-part"), "Enabled", NeoValueWritePayload.FromValue(true));
                Assert.That(door.activeSelf, Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void NestedObjectLayerRenderingKeepsAncestorSortingGroup(bool reverseLayers, bool sortAtRoot)
        {
            const string supportLayerId = "support-object-layer";
            const string supportLinkId = "support-object-link";
            var data = BuildNestedObjectPlacementData();
            data.classes[ObjectLayerLinkClassId].schema["Enabled"] = "object-enabled-member";
            data.classes[ObjectsLayerClassId].schema["SortingOrder"] = "primary-order";
            data.members["primary-order"] = SortingOrderMember("primary-order", 7);
            data.members["support-order"] = SortingOrderMember("support-order", 19);
            data.classes[supportLayerId] = new NeoSchemaClass
            {
                id = supportLayerId,
                projectId = "project-a",
                name = "Support",
                extendsClassId = ObjectsLayerClassId,
                schema = new Dictionary<string, string> { ["SortingOrder"] = "support-order" },
            };
            data.classes[supportLinkId] = new NeoSchemaClass
            {
                id = supportLinkId,
                projectId = "project-a",
                name = "SupportLink",
                extendsClassId = ObjectLayerLinkClassId,
                schema = new Dictionary<string, string>(),
            };
            data.internalRecordRelations!["support-grid"] = Relation("support-grid",
                InternalRecordRelationKinds.WorldGridObjectLayer, GridClassId, supportLayerId, "c0");
            data.internalRecordRelations["support-compatible"] = Relation("support-compatible",
                InternalRecordRelationKinds.WorldObjectCompatibleLayer, ObjectClassId, supportLayerId);
            data.internalRecordRelations["support-target"] = Relation("support-target",
                InternalRecordRelationKinds.WorldObjectLayerLinkTarget, supportLinkId, supportLayerId);
            data.values["nested-objects-link"].classId = supportLinkId;
            data.values["door-art"] = new ObjectMemberValue
            {
                id = "door-art",
                classId = ObjectClassId,
                value = new Dictionary<string, string>()
            };
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var factories = BuildClassBackedReadOnlyFactories();
            factories[ObjectClassId] = (c, n) => NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(c, n,
                () => new TestComposedObject(c, n) { PositionFromNode = true });
            factories[supportLinkId] = (c, n) => NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(c, n,
                () => new TestObjectLayerLink(c, n) { WorldFieldsFromNode = true });
            factories[supportLayerId] = (c, n) => NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(c, n,
                () => new TestAuthoredObjectLayer(c, n));
            var grid = NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid", factories, BuildClassBackedWritableFactories());
            TestComposedObject Object(string id) => (TestComposedObject)grid.ResolveGeneratedClassValue(id)!;
            var location = Object("shop-1");
            var part = Object("shop-part");
            var door = Object("door");
            location.Children = new[] { part };
            part.Children = new[] { (INeoWorldObjectValue)grid.ResolveGeneratedClassValue("nested-objects-link")! };
            location.SortingGroup = new TestSortingGroup();
            door.SortingGroup = new TestSortingGroup { SortAtRoot = sortAtRoot };
            var sprite = CreateTestSprite("Door art");
            door.Children = new[] { new TestSpriteObject(client, Object("door-art").BackingNode)
                { Name = "Door art", Sprite = sprite, SortingOrder = 4 } };
            int doorSpawns = 0, doorDespawns = 0;
            door.Spawned = _ => doorSpawns++;
            door.Despawned = _ => doorDespawns++;
            var primary = grid.BindReadOnlyObjectLayer<TestAuthoredObjectLayer>(ObjectsLayerClassId, new[] { ObjectClassId });
            var support = grid.BindReadOnlyObjectLayer<TestAuthoredObjectLayer>(supportLayerId, new[] { ObjectClassId });
            var go = new GameObject("nested-sorting");
            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(new TestTileGridContent(grid, Array.Empty<IReadOnlyNeoTileLayerRuntime>(),
                    reverseLayers ? new[] { support, primary } : new[] { primary, support }));
                Assert.That(renderer.TryGetObjectRoot("shop-1", out var locationRoot), Is.True);
                Assert.That(renderer.TryGetObjectRoot("door", out var doorRoot), Is.True);
                Assert.That(doorRoot.transform.parent, Is.SameAs(locationRoot.transform.Find("Content")));
                var doorGroup = doorRoot.GetComponent<UnityEngine.Rendering.SortingGroup>();
                Assert.That(doorGroup.sortAtRoot, Is.EqualTo(sortAtRoot));
                Assert.That(doorGroup.sortingOrder, Is.EqualTo(19));
                Assert.That(locationRoot.GetComponent<UnityEngine.Rendering.SortingGroup>().sortingOrder, Is.EqualTo(7));
                Assert.That(doorRoot.GetComponentInChildren<SpriteRenderer>().sortingOrder, Is.EqualTo(23));
                AssertSamePosition(new Vector3(16.5f, 21, 0), doorRoot.transform.position);
                Assert.That(go.GetComponentsInChildren<SpriteRenderer>(true).Length, Is.EqualTo(1));

                NeoGeneratedTypesSupport.SetPlacementVector3(WritableObject(client, "shop-part"), "Position", new NeoReadOnlyVector3(3.25f, 1, 2));
                AssertSamePosition(new Vector3(16.75f, 21, 2), doorRoot.transform.position);
                NeoGeneratedTypesSupport.SetPlacementVector3(WritableObject(client, "shop-1"), "Position", new NeoReadOnlyVector3(11, 20, 1));
                AssertSamePosition(new Vector3(17.75f, 21, 3), doorRoot.transform.position);

                // A parent's shape rebuild must neither destroy nor respawn its
                // separately indexed nested placement, in either layer order.
                location.SortingGroup = null;
                ApplyNestedRenderDelta(renderer, primary, "shop-1", removed: false);
                Assert.That(renderer.TryGetObjectRoot("door", out var afterRebuild), Is.True);
                Assert.That(afterRebuild, Is.SameAs(doorRoot));
                AssertSamePosition(new Vector3(17.75f, 21, 3), doorRoot.transform.position);
                location.SortingGroup = new TestSortingGroup();
                ApplyNestedRenderDelta(renderer, primary, "shop-1", removed: false);
                Assert.That(renderer.TryGetObjectRoot("shop-1", out locationRoot), Is.True);
                Assert.That(doorRoot.transform.parent, Is.SameAs(locationRoot.transform.Find("Content")));
                Assert.That(doorSpawns, Is.EqualTo(Application.isPlaying ? 1 : 0));
                Assert.That(doorDespawns, Is.Zero);
                ApplyNestedRenderDelta(renderer, primary, "shop-1", removed: true);
                Assert.That(renderer.TryGetObjectRoot("door", out afterRebuild), Is.True);
                Assert.That(afterRebuild, Is.SameAs(doorRoot));
                ApplyNestedRenderDelta(renderer, support, "door", removed: true);
                Assert.That(doorDespawns, Is.EqualTo(Application.isPlaying ? 1 : 0));
                Assert.That(renderer.TryGetObjectRoot("door", out _), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(sprite.texture);
                UnityEngine.Object.DestroyImmediate(sprite);
            }
        }

        [UnityTest]
        public IEnumerator NestedSortingRebuildKeepsPlacedChildLifecycle()
        {
            yield return new EnterPlayMode();
            NestedObjectLayerRenderingKeepsAncestorSortingGroup(reverseLayers: true, sortAtRoot: true);
            yield return new ExitPlayMode();
        }

        private static void ApplyNestedRenderDelta(NeoTileGridRenderer renderer, IReadOnlyNeoObjectLayerRuntime layer,
            string id, bool removed)
        {
            var empty = Array.Empty<NeoObjectInstanceId>();
            var ids = new NeoObjectInstanceId[] { id };
            var change = new NeoObjectLayerChangedArgs(layer.LayerId, removed ? ids : empty, removed ? empty : ids,
                Array.Empty<Vector2Int>(), NeoTileGridChangeSourceKind.Direct, null);
            typeof(NeoTileGridRenderer).GetMethod("ApplyObjectLayerDelta",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(renderer, new object[] { layer, change });
        }

        [Test]
        public void NestedNativeLayerQueriesSeparateFlattenedContentFromDirectMembers()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildNestedObjectPlacementData());
            RegisterGridGetterContent(client);
            var grid = NeoTileGridPrimitive.ResolveForSave(client, "town-grid", BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            var layer = grid.BindReadOnlyObjectLayer<TestAuthoredObjectLayer>(ObjectsLayerClassId, new[] { ObjectClassId });
            var context = client.CreateGetterContext(NeoValueOwnership.Save);
            Assert.That(NeoWorldLayerRuntime.TryInvoke("system_1362a56e-3d3d-56e6-b115-24a78f216b1b", layer,
                new object?[] { new Vector2Int(16, 21) }, null, context, out var found), Is.True);
            Assert.That(NeoCompose.Runtime.NeoScript.NSGetterEvaluator.FindRowIdByReference(found, context), Is.EqualTo("door"));
            var root = WritableObjectLink(client, "objects-link");
            NeoWorldLayerRuntime.TryInvoke("system_855ebeef-50d6-563a-9518-56472de73ce4", root,
                new object?[] { new Vector2Int(16, 21) }, null, context, out found);
            Assert.That(found, Is.Null, "Root link queries must exclude nested link members.");
            var nested = WritableObjectLink(client, "nested-objects-link");
            NeoWorldLayerRuntime.TryInvoke("system_855ebeef-50d6-563a-9518-56472de73ce4", nested,
                new object?[] { new Vector2Int(16, 21) }, null, context, out found);
            Assert.That(NeoCompose.Runtime.NeoScript.NSGetterEvaluator.FindRowIdByReference(found, context), Is.EqualTo("door"));
            var door = NeoGeneratedTypesSupport.ResolveClassValue(client, "door", BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            client.ScriptGridQueries.TryInvoke("system_df1c2d06-eeec-5340-addc-740f3668c9e4", door, Array.Empty<object?>(), null, context, out var local);
            client.ScriptGridQueries.TryInvoke("system_a7c3197e-e672-5f58-a82e-39db2d864a78", door, Array.Empty<object?>(), null, context, out var global);
            Assert.That(NeoGeneratedTypesSupport.ReadVector2IntValue(local), Is.EqualTo(Vector2Int.right));
            Assert.That(NeoGeneratedTypesSupport.ReadVector2IntValue(global), Is.EqualTo(new Vector2Int(16, 21)));
        }

        [Test]
        public void NativeLayerClearRemovesNestedMembersInOneCommit()
        {
            var data = BuildNestedObjectPlacementData();
            data.members["object-layer-link-objects-member"].DeclaredStorage = NeoMemberStorage.Save;
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            RegisterGridGetterContent(client);
            var grid = NeoTileGridPrimitive.ResolveForSave(client, "town-grid", BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            var layer = grid.BindReadOnlyObjectLayer<TestAuthoredObjectLayer>(ObjectsLayerClassId, new[] { ObjectClassId });
            Assert.That(grid.LookupCache.ObjectRecord(ObjectsLayerClassId, "door"), Is.Not.Null);
            NeoWorldLayerRuntime.TryInvoke("system_0a1422c9-62b4-59d9-aafc-dadabfc710d3", layer,
                Array.Empty<object?>(), null, client.CreateGetterContext(NeoValueOwnership.Save), out _);
            Assert.That(grid.LookupCache.ObjectRecords(ObjectsLayerClassId), Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BuildingSpawnRejectsNestedCollisionBeforeLifecycleOrAdoption(bool unstamped)
        {
            var data = BuildNestedObjectPlacementData();
            if (unstamped)
            {
                data.values["town-grid"].classId = null;
                data.members["query-grid"] = RootMember("query-grid", "town-grid", GridClassId);
            }
            data.members["object-layer-link-objects-member"].DeclaredStorage = NeoMemberStorage.Save;
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var grid = NeoTileGridPrimitive.ResolveForSave(client, "town-grid", BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            Assert.That(grid.TrySpawn(ObjectsLayerClassId, new Vector2Int(50, 50), ClonePlacementObject(client)).Ok, Is.True);
            string cloneId = client.CloneValueReference("shop-1");
            var building = (NeoGeneratedClassValue)NeoGeneratedTypesSupport.ResolveClassValue(client, cloneId,
                BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories())!;
            var lifecycle = new CountingSpawnLifecycle();
            grid.Lifecycle = lifecycle;
            var before = client.ResolveClassChildRow((ObjectMemberValue)client.ResolveValueRow(cloneId)!, "Position");
            var outcome = grid.TrySpawn(ObjectsLayerClassId, new Vector2Int(44, 49), building);
            Assert.That(outcome.Ok, Is.False);
            Assert.That(outcome.ErrorCode, Is.EqualTo("tile-grid-object-cell-occupied"));
            Assert.That(lifecycle.Spawns, Is.Zero);
            Assert.That(grid.LookupCache.ObjectRecord(ObjectsLayerClassId, cloneId), Is.Null);
            Assert.That(client.ResolveClassChildRow((ObjectMemberValue)client.ResolveValueRow(cloneId)!, "Position"), Is.SameAs(before));
            var retry = grid.TrySpawn(ObjectsLayerClassId, new Vector2Int(60, 60), building);
            Assert.That(retry.Ok, Is.True, retry.Message);
            Assert.That(lifecycle.Spawns, Is.EqualTo(1));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void ContentIrReadsLayerFields(bool useOverride, bool unstamped)
        {
            var data = BuildClassBackedTileGridProjectData();
            if (unstamped)
            {
                data.values["town-grid"].classId = null;
                data.members["query-grid"] = RootMember("query-grid", "town-grid", GridClassId);
            }
            if (!useOverride)
                ((ObjectMemberValue)data.values["background-link"]).value!.Remove("layerOverrideValueId");
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            RegisterGridGetterContent(client);
            var context = client.CreateGetterContext(NeoValueOwnership.Save);
            var layer = new FunctionPointer
            {
                type = PointerKind.Function,
                function = new WorldLayerFunction
                {
                    type = FunctionKind.WorldLayer,
                    info = new WorldLayerInfo
                    {
                        receiverPointer = new ReferencePointer { type = PointerKind.Reference, valueId = "town-grid" },
                        layerClassId = BackgroundLayerClassId,
                        source = "grid"
                    }
                }
            };
            var read = new KeyOfPointer
            {
                type = PointerKind.KeyOf,
                keyOf = new KeyOf
                {
                    pointer = layer,
                    key = new ValuePointer
                    {
                        type = PointerKind.Value,
                        value = new Value
                        {
                            typeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true },
                            value = "Name"
                        }
                    }
                }
            };
            Assert.That(NeoScriptIrWalker.AnyPointer(new Instruction[] { new ReturnInstruction { type = InstructionKind.Return, pointer = read } },
                pointer => pointer is ReferencePointer reference && reference.valueId == "town-grid"), Is.True);
            var expected = NeoWorldLayerRuntime.ResolveLayer(NSGetterEvaluator.EvaluatePointer(layer.function is WorldLayerFunction function ? function.info.receiverPointer : throw new Exception(), new NeoScriptScope(0), context), BackgroundLayerClassId, "grid", context);
            Assert.That(NSGetterEvaluator.EvaluatePointer(read, new NeoScriptScope(0), context),
                Is.EqualTo(((TestAuthoredTileLayer)expected).Name));
        }

        [Test]
        public void ContentSpawnUsesSessionCollectionEvenInSaveContext()
        {
            var data = BuildClassBackedTileGridProjectData();
            data.members["object-layer-link-objects-member"].DeclaredStorage = NeoMemberStorage.Session;
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            RegisterGridGetterContent(client);
            var grid = NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid", BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            var outcome = grid.TryPlaceConstructedLayerEntry(ObjectsLayerClassId, new Vector2Int(60, 60), ClonePlacementObject(client), false);
            Assert.That(outcome.Ok, Is.True, outcome.Message);
            var record = grid.LookupCache.ObjectCandidatesAt(ObjectsLayerClassId, new Vector2Int(60, 60)).Single();
            Assert.That(client.TryGetValueOwnership(record.InstanceId, out var ownership), Is.True);
            Assert.That(ownership, Is.EqualTo(NeoValueOwnership.Session));
        }

        [Test]
        public void ContentSetPreservesConstructedTileOnImmutableSourceOverlay()
        {
            var data = BuildClassBackedTileGridProjectData();
            data.classes[TileLayerLinkClassId].allowedStorage = NeoMemberStorage.Immutable;
            data.members["tile-layer-link-tiles-member"].DeclaredStorage = null;
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var tile = new TestTile(client, NeoGeneratedTypesSupport.CreateWritableClassValue(client, TileClassId, Array.Empty<NeoGeneratedConstructorValue>()), false);
            string cloneId = tile.valueId!;
            var grid = NeoTileGridPrimitive.ResolveForSave(client, "town-grid", BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            var outcome = grid.TryPlaceConstructedLayerEntry(BackgroundLayerClassId, new Vector2Int(60, 60), tile, true);
            Assert.That(outcome.Ok, Is.True, outcome.Message);
            Assert.That(grid.LookupCache.TileCandidatesAt(BackgroundLayerClassId, new Vector2Int(60, 60)).Last().PlacementValueId, Is.EqualTo(cloneId));
        }

        [Test]
        public void SessionTileReplacementAndRemovalHideLowerSaveOverlay()
        {
            var data = BuildClassBackedTileGridProjectData();
            data.classes[TileLayerLinkClassId].allowedStorage = NeoMemberStorage.Immutable;
            data.members["tile-layer-link-tiles-member"].DeclaredStorage = null;
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            RegisterGridGetterContent(client);
            var grid = NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid", BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            var cell = new Vector2Int(60, 60);
            TestTile NewTile() => new TestTile(client, NeoGeneratedTypesSupport.CreateWritableClassValue(client,
                TileClassId, Array.Empty<NeoGeneratedConstructorValue>()), false);
            var saved = NewTile();
            Assert.That(grid.TryPlaceConstructedLayerEntry(BackgroundLayerClassId, cell, saved, true).Ok, Is.True);
            var session = NewTile();
            var result = grid.TryPlaceConstructedLayerEntry(BackgroundLayerClassId, cell, session, true, NeoValueOwnership.Session);
            Assert.That(result.Ok, Is.True, result.Message);
            Assert.That(grid.LookupCache.TileCandidatesAt(BackgroundLayerClassId, cell).Single().PlacementValueId, Is.EqualTo(session.valueId));
            var layer = grid.BindReadOnlyTileLayer<TestAuthoredTileLayer>(BackgroundLayerClassId, new[] { TileClassId });
            var context = client.CreateGetterContext(NeoValueOwnership.Session);
            Assert.That(NeoWorldLayerRuntime.TryInvoke("system_c9a358ad-96ee-5c93-8321-73d5a546e0fe", layer,
                new object?[] { session }, null, context, out var removed), Is.True);
            Assert.That(removed, Is.True);
            Assert.That(grid.LookupCache.TileCandidatesAt(BackgroundLayerClassId, cell), Is.Empty);
            var fresh = NewTile();
            Assert.That(grid.TryPlaceConstructedLayerEntry(BackgroundLayerClassId, cell, fresh, true, NeoValueOwnership.Session).Ok, Is.True);
            Assert.That(NeoWorldLayerRuntime.TryInvoke("system_f77c4cbb-2df3-5350-b0bc-9bf99ade5082", layer,
                Array.Empty<object?>(), null, context, out _), Is.True);
            Assert.That(grid.LookupCache.TileRecords(BackgroundLayerClassId), Is.Empty);
        }

        [Test]
        public void GeneratedLinkLayerPreservesSessionTileOwnership()
        {
            var data = BuildClassBackedTileGridProjectData();
            data.classes[TileLayerLinkClassId].allowedStorage = NeoMemberStorage.Immutable;
            data.members["tile-layer-link-tiles-member"].DeclaredStorage = null;
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            RegisterGridGetterContent(client);
            var node = new NeoMemberClass(client, new ClassMember { id = "session-link-view", kind = MemberKind.Class, DeclaredClassId = TileLayerLinkClassId },
                "background-link", NeoValueOwnership.Session);
            var link = new TestTileLayerLink(client, node);
            Assert.That(link.StorageOwnership, Is.EqualTo(NeoValueOwnership.Session), "link");
            var layer = client.ScriptGridQueries.Layer<TestAuthoredTileLayer>(link, BackgroundLayerClassId);
            Assert.That(layer.StorageOwnership, Is.EqualTo(NeoValueOwnership.Session), "layer");
            var tile = new TestTile(client, NeoGeneratedTypesSupport.CreateWritableClassValue(client,
                TileClassId, Array.Empty<NeoGeneratedConstructorValue>()), false);
            var cell = new Vector2Int(60, 60);
            var placed = layer.SetConstructed(cell, tile);
            Assert.That(placed.Ok, Is.True, placed.Message);
            Assert.That(client.TryGetValueOwnership(tile.valueId!, out var ownership), Is.True);
            Assert.That(ownership, Is.EqualTo(NeoValueOwnership.Session));
            Assert.That(client.ScriptGridQueries.Invoke("system_c9a358ad-96ee-5c93-8321-73d5a546e0fe", layer, new object?[] { tile }), Is.True);
            Assert.That(layer.GetTile(cell), Is.Null);
        }

        [Test]
        public void FractionalLeafMovesReuseTheirStagingStorage()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildNestedObjectPlacementData());
            var grid = NeoTileGridPrimitive.ResolveForSave(client, "town-grid", BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            var cache = grid.LookupCache;
            cache.ObjectRecord(ObjectsLayerClassId, "door");
            void Move(double x)
            {
                var move = NeoTileGridLookupCache.ObjectMove.Prepare(cache, "door", (x, 0))!;
                move.Apply();
                move.Release();
            }
            Move(1.1);
            Move(1.2);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
                Move(i % 2 == 0 ? 1.1 : 1.2);
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
        }

        [Test]
        public void RootLinkPositionContributesToGridCellAndPlacement()
        {
            var data = BuildNestedObjectPlacementData();
            var root = (ObjectMemberValue)data.values["objects-link"];
            root.value!["Position"] = "root-link-position";
            data.values["root-link-position"] = new Vector3MemberValue
            {
                id = "root-link-position",
                value = new NeoVector3Value { x = 10.4, y = 0, z = 0 }
            };
            ((Vector3MemberValue)data.values["door-position"]).value!.x = 1.2;
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var grid = NeoTileGridPrimitive.ResolveForSave(client, "town-grid", BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            Assert.That(grid.LookupCache.ObjectRecord(ObjectsLayerClassId, "door")!.Cell, Is.EqualTo(new Vector2Int(27, 21)));
            var spawned = ClonePlacementObject(client);
            Assert.That(WritableObjectLink(client, "objects-link").TrySpawn(new Vector2Int(60, 60), spawned).Ok, Is.True);
            var row = (ObjectMemberValue)client.ResolveValueRow(spawned.valueId!)!;
            Assert.That(((Vector3MemberValue)client.ResolveClassChildRow(row, "Position")!).value!.x, Is.EqualTo(49.6).Within(0.0001));
            Assert.That(grid.LookupCache.ObjectRecord(ObjectsLayerClassId, spawned.valueId!)!.Cell, Is.EqualTo(new Vector2Int(60, 60)));
            NeoGeneratedTypesSupport.SetPlacementVector3(NeoGeneratedTypesSupport.AsWritable(WritableObjectLink(client, "objects-link").BackingNode), "Position", new NeoReadOnlyVector3(11.4f, 0, 0));
            Assert.That(grid.LookupCache.ObjectRecord(ObjectsLayerClassId, "door")!.Cell, Is.EqualTo(new Vector2Int(28, 21)));
        }

        [TestCase(10.4)]
        [TestCase(10.5)]
        public void BoundWritableLayerUsesGridCoordinatesWithOffsetRootLink(double offset)
        {
            var data = BuildNestedObjectPlacementData();
            ((Vector3MemberValue)data.values["root-link-position"]).value!.x = offset;
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var grid = NeoTileGridPrimitive.ResolveForSave(client, "town-grid", BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            var layer = grid.BindWritableObjectLayer<TestAuthoredObjectLayer>(ObjectsLayerClassId, new[] { ObjectClassId });
            var spawned = ClonePlacementObject(client);
            Assert.That(layer.Spawn(new Vector2Int(60, 60), spawned).Ok, Is.True);
            Assert.That(grid.LookupCache.ObjectRecord(ObjectsLayerClassId, spawned.valueId!)!.Cell, Is.EqualTo(new Vector2Int(60, 60)));
            Assert.That(layer.Spawn(new Vector2Int(60, 60), ClonePlacementObject(client)).Ok, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NestedVariantMayPreserveItsGridFootprint(bool unstamped)
        {
            var data = BuildNestedObjectPlacementData();
            if (unstamped)
            {
                data.values["town-grid"].classId = null;
                data.members["query-grid"] = RootMember("query-grid", "town-grid", GridClassId);
            }
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var grid = NeoTileGridPrimitive.ResolveForSave(client, "town-grid", BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            var before = grid.LookupCache.ObjectRecord(ObjectsLayerClassId, "door")!.Footprint.ToArray();
            var door = WritableObject(client, "door");
            Assert.DoesNotThrow(() => client.PrepareVariantApply(door, _ => { }));
            Assert.That(grid.LookupCache.ObjectRecord(ObjectsLayerClassId, "door")!.Footprint, Is.EqualTo(before));
            var error = Assert.Throws<NeoPlacementValidationException>(() => client.PrepareVariantApply(door, _ =>
                client.SetWritableValue(NeoValueOwnership.Save, new Vector2MemberValue
                {
                    id = "door-placement-cell-0",
                    value = new NeoVector2Value { x = 4, y = 0 },
                })));
            Assert.That(error!.ErrorCode, Is.EqualTo("object-variant-footprint-changed"));
            Assert.That(grid.LookupCache.ObjectRecord(ObjectsLayerClassId, "door")!.Footprint, Is.EqualTo(before));
        }

        [Test]
        public void CarriedTileInstanceIdResolvesThroughLayerAndOwningLink()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildNestedObjectPlacementData());
            RegisterGridGetterContent(client);
            var grid = NeoTileGridPrimitive.ResolveForSave(client, "town-grid", BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            var record = grid.LookupCache.TileRecords(BackgroundLayerClassId).First(tile => tile.SourceObjectInstanceId is not null);
            var context = client.CreateGetterContext(NeoValueOwnership.Save);
            var layer = grid.BindReadOnlyTileLayer<TestAuthoredTileLayer>(BackgroundLayerClassId, new[] { TileClassId });
            NeoWorldLayerRuntime.TryInvoke("system_92bac216-5060-5043-90fa-6666cc37f868", layer, new object?[] { record.InstanceId }, null, context, out var tile);
            Assert.That(NSGetterEvaluator.FindRowIdByReference(tile, context), Is.EqualTo(record.PlacementValueId));
            var link = NeoGeneratedTypesSupport.ResolveClassValue(client, record.SourceTileLayerLinkId, BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            NeoWorldLayerRuntime.TryInvoke("system_ed52c435-3d4a-555c-bc63-9db796f471fe", link, new object?[] { record.InstanceId }, null, context, out tile);
            Assert.That(NSGetterEvaluator.FindRowIdByReference(tile, context), Is.EqualTo(record.PlacementValueId));
        }

        [Test]
        public void ProtectedChildrenMutationRejectsDynamicPlacementFootprints()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildNestedObjectPlacementData());
            var parent = WritableObject(client, "shop-part");
            var children = parent.Get<NeoMemberList>("Children");
            var child = ClonePlacementObject(client);
            var list = (NeoMemberListWritable)children;
            var plan = new NeoWritePlan(client);
            list.PrepareAddSerialized(plan, NeoValueWritePayload.FromValueReference(child.valueId!, child));
            Assert.Throws<NeoPlacementValidationException>(() => plan.Commit());
            Assert.That(children.ResolveEntryValueIds(), Does.Not.Contain(child.valueId));
        }

        [Test]
        public void LayerMembershipMemoAndLazyQueriesObserveInsertionWithoutUnrelatedGraphRebuilds()
        {
            var data = BuildNestedObjectPlacementData();
            data.members["object-layer-link-objects-member"].DeclaredStorage = NeoMemberStorage.Save;
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            RegisterGridGetterContent(client);
            var grid = NeoTileGridPrimitive.ResolveForSave(client, "town-grid", BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            var layer = grid.BindReadOnlyObjectLayer<TestAuthoredObjectLayer>(ObjectsLayerClassId, new[] { ObjectClassId });
            var context = client.CreateGetterContext(NeoValueOwnership.Save);
            var key = new NeoClient.GetterMemoKey(NeoValueOwnership.Save, "shop-1", "layer-count", NeoValueOwnership.Save);
            var capture = client.BeginGetterReadCapture();
            NeoWorldLayerRuntime.TryInvoke("system_e2575ec5-04b2-5f6b-8c8e-0ce04387308c", layer, Array.Empty<object?>(), null, context, out _);
            client.MemoizeGetter(key, 0, null, client.EndGetterReadCapture(capture));
            Assert.That(client.FindMemoizedGetter(key), Is.Not.Null);
            var graph = grid.LookupCache.Primitive.PlacementGraph;
            client.SetWritableValue(NeoValueOwnership.Save, new NumberMemberValue { id = "unrelated-inventory-state", value = 1 });
            Assert.That(grid.LookupCache.Primitive.PlacementGraph, Is.SameAs(graph));
            Assert.That(client.FindMemoizedGetter(key), Is.Not.Null);
            NeoWorldLayerRuntime.TryInvoke("system_2efd48d3-ab2e-52e6-a6ed-8a67513ea63d", layer,
                new object?[] { new Vector2Int(60, 60) }, null, context, out var sequence);
            Assert.That(QueryValues(sequence, context), Is.Empty);
            var entry = ClonePlacementObject(client);
            Assert.That(WritableObjectLink(client, "objects-link").TrySpawn(new Vector2Int(60, 60), entry).Ok, Is.True);
            Assert.That(client.FindMemoizedGetter(key), Is.Null);
            Assert.That(QueryValues(sequence, context).Select(value => NSGetterEvaluator.FindRowIdByReference(value, context)), Is.EqualTo(new[] { entry.valueId }));
        }

        private sealed class CountingSpawnLifecycle : NeoTileGridLifecycle
        {
            internal int Spawns;
            public override void BeforeSpawnObject(NeoObjectSpawnContext context) => Spawns++;
        }
    }
}
