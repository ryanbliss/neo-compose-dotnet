// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;
using UnityEngine;

namespace NeoCompose.Tests
{
    public partial class NeoTileGridRendererTests
    {
        private sealed class AcceptLinkSpawn : NeoTileGridLifecycle
        {
            internal int Calls;
            public override void BeforeSpawnObject(NeoObjectSpawnContext context)
            {
                Calls++;
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void RemovedObjectCanMoveWithoutCloningAndRetryAfterConflict(bool otherGrid, bool rejectFirst)
        {
            var data = BuildClassBackedTileGridProjectData();
            AddPlacementResultSchema(data);
            data.values["transfer-link"] = new ObjectMemberValue
            {
                id = "transfer-link",
                classId = ObjectLayerLinkClassId,
                value = new Dictionary<string, string> { ["Objects"] = "transfer-objects" }
            };
            data.values["transfer-objects"] = new ArrayMemberValue { id = "transfer-objects", value = Array.Empty<string>() };
            if (otherGrid)
            {
                data.values["transfer-grid"] = new ObjectMemberValue
                {
                    id = "transfer-grid",
                    classId = GridClassId,
                    value = new Dictionary<string, string> { ["Children"] = "transfer-children" }
                };
                data.values["transfer-children"] = new ArrayMemberValue { id = "transfer-children", value = new[] { "transfer-link" } };
            }
            else
            {
                var children = (ArrayMemberValue)data.values["town-grid-children"];
                children.value = children.value!.Append("transfer-link").ToArray();
            }
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var targetGrid = NeoTileGridPrimitive.ResolveForSave(client, otherGrid ? "transfer-grid" : "town-grid",
                BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            var lifecycle = new AcceptLinkSpawn();
            targetGrid.Lifecycle = lifecycle;
            var source = WritableObjectLink(client, "objects-link");
            var target = WritableObjectLink(client, "transfer-link");
            var original = ClonePlacementObject(client);
            var blocker = ClonePlacementObject(client);
            Assert.IsTrue(source.TrySpawn(new Vector2Int(80, 90), original).Ok);
            Assert.IsTrue(target.TrySpawn(new Vector2Int(100, 110), blocker).Ok);
            string id = original.valueId!;
            var grid = NeoReadOnlyTileGridPrimitive.Resolve(client, otherGrid ? "transfer-grid" : "town-grid",
                BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            grid.BuildObjectLayerRecords(ObjectsLayerClassId, null);
            var list = NeoGeneratedTypesSupport.AsWritable(source.BackingNode, source.StorageOwnership).Get<NeoMemberListWritable>("Objects");
            var ctx = client.CreateGetterContext(NeoValueOwnership.Save);
            client.EnterScriptWrites();
            try
            {
                var batch = client.ScriptWriteBatch(list.value!.id, unordered: true)!;
                batch.Stage(_ => batch.RemoveUnordered(list.ownership, original.ValueOwnership, list.value!.id,
                    client.ResolveValueRow(id)!, (ListMember)list.member!,
                    client.TryResolveCollectionEntryMember(list.member!)!, ctx));
                if (rejectFirst)
                {
                    Assert.IsFalse(Spawn(new Vector2Int(100, 110)));
                    Assert.IsTrue(batch.TryGetEntryParent(id, out var parent));
                    Assert.IsNull(parent, "The rejected attempt must leave the preceding removal pending.");
                }
                Assert.IsTrue(Spawn(new Vector2Int(120, 130)));
            }
            finally { client.ExitScriptWrites(); }
            Assert.That(lifecycle.Calls, Is.EqualTo(otherGrid ? 2 : 3), "Rejected placement does not call the successful placement hook.");
            Assert.AreEqual(id, original.valueId);
            Assert.AreEqual("transfer-objects", client.ResolveValueRow(id)!.containerId);
            Assert.IsFalse(list.ResolveEntryValueIds().Contains(id));
            Assert.That(grid.BuildObjectLayerRecords(ObjectsLayerClassId, null).Single(item => item.InstanceId == id).Cell,
                Is.EqualTo(new Vector2Int(120, 130)));
            using var reloaded = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: client.SerializeSaveData());
            Assert.AreEqual("transfer-objects", reloaded.ResolveValueRow(id)!.containerId);

            bool Spawn(Vector2Int cell)
            {
                Assert.IsTrue(NeoLayerLinkRuntime.TryInvoke(NeoLayerLinkRuntime.TrySpawnId, target,
                    new object?[] { cell, original }, ctx, out object? result));
                var scope = new NeoScriptScope(0);
                scope.SetLocal("result", result);
                return (bool)NSGetterEvaluator.EvaluatePointer(new KeyOfPointer
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
                                value = "Ok"
                            }
                        }
                    }
                }, scope, ctx)!;
            }
        }
    }
}
