// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;
using UnityEngine;

namespace NeoCompose.Tests
{
    public partial class NeoTileGridRendererTests
    {
        [Test]
        public void LayerLinkPlacementUsesEffectiveCollectionStorage(
            [Values(false, true)] bool tile, [Values(false, true)] bool native,
            [Values(NeoMemberStorage.Save, NeoMemberStorage.Session, NeoMemberStorage.Immutable, NeoMemberStorage.Inherit)] NeoMemberStorage storage)
        {
            ProjectData data = BuildClassBackedTileGridProjectData();
            if (native)
                AddPlacementResultSchema(data);
            string classId = tile ? TileLayerLinkClassId : ObjectLayerLinkClassId;
            string memberId = tile ? "tile-layer-link-tiles-member" : "object-layer-link-objects-member";
            string key = tile ? "Tiles" : "Objects";
            string linkId = tile ? "background-link" : "objects-link";
            var inherited = (ListMember)data.members[memberId];
            inherited.Storage = NeoMemberStorage.Immutable;
            var field = (ListMember)inherited.ShallowClone();
            field.id = "placement-storage-override";
            field.extendsMemberId = inherited.id;
            field.Storage = storage;
            field.ListKind = NeoListKind.Unordered;
            data.members[field.id] = field;
            data.classes[classId].schema[key] = field.id;
            data.classes[classId].allowedStorage = NeoMemberStorage.Immutable;
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var grid = NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid",
                BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            var node = (NeoMemberClass)NeoMember.Create(client,
                new ClassMember { id = "asset-link-view", kind = MemberKind.Class, DeclaredClassId = classId }, linkId);
            NeoGeneratedClassValue entry = tile
                ? new TestTile(client, NeoGeneratedTypesSupport.CreateWritableClassValue(client, TileClassId, Array.Empty<NeoGeneratedConstructorValue>()), false)
                : ClonePlacementObject(client);
            string entryId = entry.valueId!;
            var cell = new Vector2Int(80, 90);
            var tileLink = tile ? new TestTileLayerLink(client, node) : null;
            var objectLink = tile ? null : new TestObjectLayerLink(client, node);
            NeoGeneratedClassValue link = tile ? tileLink! : objectLink!;
            Assert.IsTrue(link.IsReadOnly, "The immutable link wrapper does not determine its collection's storage.");
            Assert.AreEqual(NeoValueOwnership.Asset, link.ValueOwnership);
            void Place()
            {
                if (native)
                {
                    var context = client.CreateGetterContext(NeoValueOwnership.Asset);
                    object? receiver = NSGetterEvaluator.EvaluatePointer(new ReferencePointer
                    {
                        type = PointerKind.Reference,
                        valueId = linkId,
                    }, new NeoScriptScope(0), context);
                    Assert.IsTrue(NeoLayerLinkRuntime.TryInvoke(tile ? NeoLayerLinkRuntime.TrySetId : NeoLayerLinkRuntime.TrySpawnId,
                        receiver, new object?[] { cell, entry }, context, out object? result));
                    Assert.IsNotNull(result);
                }
                else
                {
                    var result = tile ? tileLink!.TrySet(cell, entry) : objectLink!.TrySpawn(cell, entry);
                    Assert.IsTrue(result.Ok, result.Message);
                }
            }

            if (storage == NeoMemberStorage.Immutable || storage == NeoMemberStorage.Inherit)
            {
                Assert.Throws<InvalidOperationException>(() => Place());
                Assert.IsNull(client.ResolveValueRow(entryId)!.containerId);
                Assert.AreEqual(NeoValueOwnership.Session, entry.ValueOwnership);
                Assert.IsEmpty(client.saveValues);
                return;
            }

            Place();
            NeoValueOwnership expected = storage == NeoMemberStorage.Save ? NeoValueOwnership.Save : NeoValueOwnership.Session;
            Assert.AreEqual(expected, entry.ValueOwnership);
            Assert.IsTrue(client.TryGetValue(expected, entryId, out ObjectMemberValue? placed));
            Assert.AreEqual(tile ? "background-link-tiles" : "objects-link-objects", placed!.containerId);
            Assert.IsFalse(data.values.ContainsKey(entryId), "Placement must preserve authored values.");
            if (tile)
                Assert.IsTrue(grid.BuildTileLayerRecords(BackgroundLayerClassId).Records.Any(row => row.PlacementValueId == entryId));
            else
                Assert.IsTrue(grid.BuildObjectLayerRecords(ObjectsLayerClassId, null).Any(row => row.InstanceId == entryId));
        }

        [Test]
        public void LayerLinkSpawnValidatesSiblingContentAndWritesToTheCalledLink()
        {
            ProjectData data = BuildClassBackedTileGridProjectData();
            data.values["second-link"] = new ObjectMemberValue
            {
                id = "second-link",
                classId = ObjectLayerLinkClassId,
                value = new Dictionary<string, string> { ["Objects"] = "second-objects" }
            };
            data.values["second-objects"] = new ArrayMemberValue { id = "second-objects", value = Array.Empty<string>() };
            var children = (ArrayMemberValue)data.values["town-grid-children"];
            children.value = children.value!.Concat(new[] { "second-link" }).ToArray();
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid",
                BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            var link = WritableObjectLink(client, "second-link");
            NeoGeneratedClassValue entry = ClonePlacementObject(client);
            string id = entry.valueId!;
            var before = (ObjectMemberValue)client.CloneRowForWrite(client.ResolveValueRow(id)!);
            Vector2Int occupied = primitive.BuildObjectLayerRecords(ObjectsLayerClassId, null)[0].Footprint[0];

            NeoPlacementOutcome blocked = link.TrySpawn(occupied, entry);

            Assert.That(blocked.Ok, Is.False);
            Assert.That(blocked.ErrorCode, Is.EqualTo("tile-grid-object-cell-occupied"));
            Assert.That(((ObjectMemberValue)client.ResolveValueRow(id)!).value, Is.EquivalentTo(before.value));
            Assert.That(client.ResolveValueRow(id)!.containerId, Is.Null);
            NeoPlacementOutcome placed = link.TrySpawn(new Vector2Int(50, 60), entry);
            Assert.That(placed.Ok, Is.True, placed.Message);
            Assert.That(client.ResolveValueRow(id)!.containerId, Is.EqualTo("second-objects"));
            Assert.That(primitive.BuildObjectLayerRecords(ObjectsLayerClassId, null).Single(item => item.InstanceId == id).Cell,
                Is.EqualTo(new Vector2Int(50, 60)));
        }

        [TestCase(16777217)]
        [TestCase(int.MaxValue - 10)]
        public void LayerLinkSpawnPreservesIntegerCellPrecision(int x)
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildClassBackedTileGridProjectData());
            var grid = NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid",
                BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            var link = WritableObjectLink(client, "objects-link");
            var entry = ClonePlacementObject(client);
            var cell = new Vector2Int(x, 1000);
            var result = link.TrySpawn(cell, entry);
            Assert.That(result.Ok, Is.True, result.Message);
            Assert.That(grid.BuildObjectLayerRecords(ObjectsLayerClassId, null).Single(row => row.InstanceId == entry.valueId).Cell, Is.EqualTo(cell));
            Assert.That(link.TrySpawn(cell, ClonePlacementObject(client)).Ok, Is.False);
        }

        [Test]
        public void DetachedLayerLinkSpawnChecksOnlyItsOwnFootprints()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildClassBackedTileGridProjectData());
            client.RegisterGeneratedClassFactories(BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            var node = NeoGeneratedTypesSupport.CreateWritableClassValue(client, ObjectLayerLinkClassId, Array.Empty<NeoGeneratedConstructorValue>());
            var link = new TestObjectLayerLink(client, node, false);
            NeoGeneratedClassValue first = ClonePlacementObject(client);
            NeoGeneratedClassValue second = ClonePlacementObject(client);
            NeoPlacementOutcome placed = link.TrySpawn(new Vector2Int(10, 20), first);
            Assert.That(placed.Ok, Is.True, placed.Message);
            NeoPlacementOutcome blocked = link.TrySpawn(new Vector2Int(10, 20), second);
            Assert.That(blocked.Ok, Is.False);
            Assert.That(blocked.ErrorCode, Is.EqualTo("tile-grid-object-cell-occupied"));
            Assert.That(second.ValueOwnership, Is.EqualTo(NeoValueOwnership.Session));
            Assert.That(client.ResolveValueRow(second.valueId!)!.containerId, Is.Null);
            Assert.That(link.TrySpawn(new Vector2Int(70, 80), second).Ok, Is.True);
        }

        [Test]
        public void CarriedTileLinkTrySetUsesGridCoordinatesAndReplacesItsOwnTile()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildClassBackedTileGridProjectData());
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid",
                BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            var linkNode = (NeoMemberClass)NeoMember.CreateWritable(client,
                new ClassMember { id = "test-tile-link-member", kind = MemberKind.Class, DeclaredClassId = TileLayerLinkClassId },
                "shop-floor-link", NeoValueOwnership.Save);
            var link = new TestTileLayerLink(client, linkNode, false);
            var cell = new Vector2Int(9, 22);
            var tileNode = NeoGeneratedTypesSupport.CreateWritableClassValue(client, TileClassId, Array.Empty<NeoGeneratedConstructorValue>());
            var tile = new TestTile(client, tileNode, false);
            NeoPlacementOutcome result = link.TrySet(cell, tile);
            Assert.That(result.Ok, Is.True, result.Message);
            var placed = primitive.BuildTileLayerRecords(BackgroundLayerClassId).Records.Single(row => row.PlacementValueId == tile.valueId);
            Assert.That(placed.Cell, Is.EqualTo(cell));
            Assert.That(placed.SourceTileLayerLinkId, Is.EqualTo("shop-floor-link"));
            Assert.That(link.GetTile(cell)?.valueId, Is.EqualTo(tile.valueId), "A carried link query uses the same grid cell as TrySet.");
            Assert.That(primitive.BuildTileLayerRecords(BackgroundLayerClassId).Records.Any(row => row.PlacementValueId == "floor-local"), Is.False);
            var value = (ObjectMemberValue)client.ResolveValueRow(tile.valueId!)!;
            var local = (Vector2MemberValue)client.ResolveClassChildRow(value, "Cell")!;
            Assert.That(local.value!.x, Is.EqualTo(-1));
            Assert.That(local.value.y, Is.EqualTo(2));
        }

        [Test]
        public void LayerLinkNativeUsesTheSdkPlacementAndReturnsTheSchemaResult()
        {
            ProjectData data = BuildClassBackedTileGridProjectData();
            AddPlacementResultSchema(data);
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            client.RegisterGeneratedClassFactories(BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            var link = WritableObjectLink(client, "objects-link");
            var entry = ClonePlacementObject(client);
            var ctx = client.CreateGetterContext(NeoValueOwnership.Save);
            Assert.That(NeoLayerLinkRuntime.TryInvoke(NeoLayerLinkRuntime.TrySpawnId, link,
                new object?[] { new Vector2Int(80, 90), entry }, ctx, out object? result), Is.True);
            var scope = new NeoScriptScope(0);
            scope.SetLocal("result", result);
            object? ReadResult(string key) => NSGetterEvaluator.EvaluatePointer(new KeyOfPointer
            {
                type = PointerKind.KeyOf,
                keyOf = new KeyOf
                {
                    pointer = new VariablePointer { type = PointerKind.Variable, variableId = "result" },
                    key = new ValuePointer
                    {
                        type = PointerKind.Value,
                        value = new Value
                        {
                            typeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true },
                            value = key,
                        }
                    },
                },
            }, scope, ctx);
            Assert.That(ReadResult("Ok"), Is.EqualTo(true));
            Assert.That(ReadResult("ErrorCode"), Is.Null);
            Assert.That(client.ResolveValueRow(entry.valueId!)!.containerId, Is.EqualTo("objects-link-objects"));
        }

        [Test]
        public void LayerLinkUsesTheOwningGridLifecycleBeforeAnyWrite()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildClassBackedTileGridProjectData());
            var grid = NeoTileGridPrimitive.ResolveForSave(client, "town-grid",
                BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            var hook = new RejectLinkSpawn();
            grid.Lifecycle = hook;
            var link = WritableObjectLink(client, "objects-link");
            var entry = ClonePlacementObject(client);
            Assert.Throws<InvalidOperationException>(() => link.TrySpawn(new Vector2Int(80, 90), entry));
            Assert.That(hook.Calls, Is.EqualTo(1));
            Assert.That(client.ResolveValueRow(entry.valueId!)!.containerId, Is.Null);
            grid.Lifecycle = null;
            Assert.That(link.TrySpawn(new Vector2Int(80, 90), entry).Ok, Is.True);
        }

        private static void AddPlacementResultSchema(ProjectData data)
        {
            var seed = JObject.Parse(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/layer-link-result-seed.json"));
            foreach (var item in seed["classes"]!)
            {
                var value = item.ToObject<NeoSchemaClass>()!;
                data.classes[value.id] = value;
            }
            foreach (var item in seed["members"]!)
            {
                var value = item.ToObject<Member>()!;
                data.members[value.id] = value;
            }
            data.constructors ??= new Dictionary<string, ConstructorRecord>();
            foreach (var item in seed["constructors"]!)
            {
                var value = item.ToObject<ConstructorRecord>()!;
                data.constructors[value.id] = value;
            }
        }

        private sealed class RejectLinkSpawn : NeoTileGridLifecycle
        {
            internal int Calls;
            public override void BeforeSpawnObject(NeoObjectSpawnContext context)
            {
                Calls++;
                throw new InvalidOperationException("Rejected by gameplay.");
            }
        }

        private static TestObjectLayerLink WritableObjectLink(NeoClient client, string id)
        {
            var node = (NeoMemberClass)NeoMember.CreateWritable(client,
                new ClassMember { id = "test-link-member", kind = MemberKind.Class, DeclaredClassId = ObjectLayerLinkClassId }, id, NeoValueOwnership.Save);
            return new TestObjectLayerLink(client, node, false);
        }

        private static NeoGeneratedClassValue ClonePlacementObject(NeoClient client)
        {
            string id = client.CloneValueReference("shop-object");
            return (NeoGeneratedClassValue)NeoGeneratedTypesSupport.ResolveClassValue(client, id,
                BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories())!;
        }
    }
}
