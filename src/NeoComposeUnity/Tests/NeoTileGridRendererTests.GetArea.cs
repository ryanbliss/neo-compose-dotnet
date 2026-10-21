// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>P114. <c>GetArea&lt;T&gt;()</c> on objects and tiles.</summary>
    public partial class NeoTileGridRendererTests
    {
        private const string GetObjectAreaId = "system_7ad477e2-be5b-4892-b9ba-2a2f2476988d";
        private const string GetTileAreaId = "system_a92b0911-8e7e-48ef-a12e-f3c55c33f64b";
        private const string GardenGridClassId = "garden-grid-class";

        [TestCase("shop-1", GetObjectAreaId, TestName = "GetArea_PlacedObject")]
        [TestCase("bare", GetObjectAreaId, TestName = "GetArea_PlacedObjectWithNoPlacementTiles")]
        [TestCase("child-2", GetObjectAreaId, TestName = "GetArea_ChildTwoLevelsDeep")]
        [TestCase("held-1", GetObjectAreaId, TestName = "GetArea_ObjectHeldInAPlacedObjectsField")]
        [TestCase("inner-link", GetObjectAreaId, TestName = "GetArea_ObjectLayerLinkInAnObjectsChildren")]
        [TestCase("inner-object", GetObjectAreaId, TestName = "GetArea_ObjectInAChildObjectLayerLink")]
        [TestCase("objects-link", GetObjectAreaId, TestName = "GetArea_LayerLink")]
        [TestCase("class-backed-placement", GetTileAreaId, TestName = "GetArea_GridTile")]
        [TestCase("blocked-path-placement", GetTileAreaId, TestName = "GetArea_TileInAnOrderedTileList")]
        [TestCase("floor-local", GetTileAreaId, TestName = "GetArea_TileInAnObjectsChildTileLayerLink")]
        [TestCase("deep-tile", GetTileAreaId, TestName = "GetArea_TileInATileLayerLinkUnderADepthTwoChild")]
        public void GetArea_AnswersTheNearestOwningGrid(string rowId, string memberId)
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildGetAreaProjectData());
            Assert.AreEqual("town-grid", AreaId(client, rowId, memberId));
            // A delegate or method group has no call-site type arguments and binds the default.
            var ctx = client.CreateGetterContext(NeoValueOwnership.Asset);
            object? viaDelegate = NSGetterEvaluator.InvokeDelegate(
                new NeoDelegateValue { memberId = memberId, valueId = rowId }, Array.Empty<object?>(), ctx);
            Assert.AreEqual("town-grid", NSGetterEvaluator.FindRowIdByReference(viaDelegate, ctx));
        }

        [Test]
        public void GetArea_IsNullOutsideAGridWithNoRowAndForAnotherT()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildGetAreaProjectData());
            client.SetWritableValue(NeoValueOwnership.Save, LooseObject("loose"));
            Assert.IsNull(AreaId(client, "loose"), "A detached object is in no area.");
            var ctx = client.CreateGetterContext(NeoValueOwnership.Asset);
            Assert.IsTrue(client.ScriptGridQueries.TryInvoke(GetObjectAreaId, null, Array.Empty<object?>(), null, ctx, out object? none));
            Assert.IsNull(none);
            Assert.IsNull(client.ScriptGridQueries.Invoke(GetObjectAreaId, new RowlessValue(), Array.Empty<object?>()),
                "A value with no row is in no area, not an error.");
            var garden = new[] { new ClassTypeInfo { type = MemberKind.Class, classId = GardenGridClassId, required = true } };
            Assert.IsNull(AreaId(client, "shop-1", typeArguments: garden), "The town is not a Garden.");
            client.SetWritableValues(NeoValueOwnership.Save, SpawnedObject("rock", "garden-objects"));
            Assert.AreEqual("garden", AreaId(client, "rock", typeArguments: garden));
        }

        [Test]
        public void GetArea_GetterKeepsItsMemoUntilTheReceiversOwnersChange()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildGetAreaProjectData());
            client.SetWritableValues(NeoValueOwnership.Save, SpawnedObject("rock", "objects-link-objects"));
            NeoClient.GetterMemoKey key = MemoizeArea(client, "rock", "town-grid");

            client.SetWritableValues(NeoValueOwnership.Save, SpawnedObject("pebble", "objects-link-objects"));
            client.SetWritableValues(NeoValueOwnership.Save, SpawnedObject("stone", "garden-objects"));
            Despawn(client, "pebble");
            client.ScriptGridQueries.NotifyChanged(new NeoTileGridChangedArgs("town-grid"));
            Assert.IsNotNull(client.FindMemoizedGetter(key), "Neighbors and grid changes leave the receiver's area alone.");

            client.SetWritableValues(NeoValueOwnership.Save, SpawnedObject("rock", "garden-objects"));
            Assert.IsNull(client.FindMemoizedGetter(key), "Respawning in the other grid drops it.");
            key = MemoizeArea(client, "rock", "garden");
            Despawn(client, "rock");
            Assert.IsNull(client.FindMemoizedGetter(key), "Despawning drops it.");

            // child-1.Children drops child-2.
            key = MemoizeArea(client, "child-2", "town-grid");
            client.SetWritableValue(NeoValueOwnership.Save, new ArrayMemberValue { id = "child-1-children", value = Array.Empty<string>() });
            Assert.IsNull(client.FindMemoizedGetter(key), "Removing a nested receiver from its parent drops it.");
            Assert.IsNull(AreaId(client, "child-2"));
        }

        [Test]
        public void GetArea_GetterHearsADetachedReceiverPlaced()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildGetAreaProjectData());
            client.SetWritableValue(NeoValueOwnership.Save, LooseObject("spawned"));
            NeoClient.GetterMemoKey key = MemoizeArea(client, "spawned", null);
            client.SetWritableValues(NeoValueOwnership.Save, SpawnedObject("spawned", "objects-link-objects"));
            Assert.IsNull(client.FindMemoizedGetter(key), "By spawn.");
            Assert.AreEqual("town-grid", AreaId(client, "spawned"));

            // A C# field write commits a plan no script batch holds.
            client.SetWritableValues(NeoValueOwnership.Save, SpawnedObject("chest", "objects-link-objects"));
            client.SetWritableValue(NeoValueOwnership.Save, LooseObject("held"));
            key = MemoizeArea(client, "held", null);
            WritableObject(client, "chest").SetSerializedValue("Held", NeoValueWritePayload.FromValueReference("held"));
            Assert.IsNull(client.FindMemoizedGetter(key), "By assigning it to an owned field.");
            Assert.AreEqual("town-grid", AreaId(client, "held"));

            client.SetWritableValue(NeoValueOwnership.Save, new ArrayMemberValue { id = "shop-1-children", value = new[] { "shop-floor-link", "child-1" } });
            client.SetWritableValue(NeoValueOwnership.Save, LooseObject("adopted"));
            key = MemoizeArea(client, "adopted", null);
            client.EnterScriptWrites();
            try
            {
                Execute(client, ChildrenCall("shop-1", CollectionMutationKind.Add, Reference("adopted")));
                Assert.AreEqual("town-grid", AreaId(client, "adopted"), "The pending add answers inside the evaluation.");
            }
            finally
            {
                client.ExitScriptWrites();
            }
            Assert.IsNull(client.FindMemoizedGetter(key), "By adding it to a Children list.");
        }

        [Test]
        public void GetArea_AnswersAnInventoryItemOnceItMovesIntoAPlacedChest()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildGetAreaProjectData());
            client.SetWritableValue(NeoValueOwnership.Save, LooseObject("item"));
            client.SetWritableValue(NeoValueOwnership.Save, new ArrayMemberValue { id = "inventory", value = new[] { "item" } });
            NeoClient.GetterMemoKey key = MemoizeArea(client, "item", null);

            client.SetWritableValues(NeoValueOwnership.Save, SpawnedObject("chest", "objects-link-objects"));
            client.SetWritableValue(NeoValueOwnership.Save, new ArrayMemberValue { id = "inventory", value = Array.Empty<string>() });
            WritableObject(client, "chest").SetSerializedValue("Held", NeoValueWritePayload.FromValueReference("item"));
            Assert.IsNull(client.FindMemoizedGetter(key), "Moving the item into the chest drops it.");
            Assert.AreEqual("town-grid", AreaId(client, "item"));
        }

        [Test]
        public void GetArea_AnswersWritesPendingInTheSameEvaluation()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(BuildGetAreaProjectData());
            client.SetWritableValue(NeoValueOwnership.Save, new ArrayMemberValue { id = "child-1-children", value = new[] { "child-2" } });
            client.SetWritableValues(NeoValueOwnership.Save, SpawnedObject("rock", "objects-link-objects"));
            var ctx = client.CreateGetterContext(NeoValueOwnership.Asset);
            object? child = NSGetterEvaluator.UnwrapRow(client.ResolveValueRow("child-2")!, ctx, NeoValueOwnership.Asset);
            client.EnterScriptWrites();
            try
            {
                Execute(client, ChildrenCall("child-1", CollectionMutationKind.RemoveAt, Int(0)));
                Assert.IsTrue(client.PendingScriptWrites?.HasCollection("child-1-children"), "The removal is still pending.");
                Assert.IsTrue(client.ScriptGridQueries.TryInvoke(GetObjectAreaId, child, Array.Empty<object?>(), null, ctx, out object? area));
                Assert.IsNull(area, "A pending removal leaves the child no owner.");
                Assert.IsTrue(client.PendingScriptWrites?.HasCollection("child-1-children"), "Answering did not commit it.");

                client.SetWritableValues(NeoValueOwnership.Save, SpawnedObject("rock", "garden-objects"));
                Assert.AreEqual("garden", AreaId(client, "rock"), "A move between grids answers the new grid.");
                client.SetWritableValues(NeoValueOwnership.Save, SpawnedObject("fresh", "objects-link-objects"));
                Assert.AreEqual("town-grid", AreaId(client, "fresh"), "So does a spawn.");
            }
            finally
            {
                client.ExitScriptWrites();
            }
        }

        [Test]
        public void GetArea_EverySystemNativeInTheHelloWorldSchemaHasARuntime()
        {
            // Package tests run inside the HelloWorld sample, whose export carries the seed schema.
            const string projectJsonPath = "Assets/Resources/Neo/project.json";
            var members = (JObject)JObject.Parse(File.ReadAllText(projectJsonPath))["members"]!;
            var unhandled = new List<string>();
            int natives = 0;
            foreach (var pair in members)
            {
                if (!pair.Key.StartsWith("system_", StringComparison.Ordinal) || pair.Value?["kind"]?.Value<int>() != (int)MemberKind.Function)
                    continue;
                natives++;
                // NeoTile.TryConvert compiles to tileConvert.
                if (pair.Key == "system_54ecc5a7-874d-54ed-92f4-38100a4b7465")
                    continue;
                if (!NeoCellPatternRuntime.Handles(pair.Key)
                    && !NeoScriptGridQueries.Handles(pair.Key)
                    && !NeoRandomContract.Handles(pair.Key))
                    unhandled.Add($"{pair.Value!["name"]} ({pair.Key})");
            }
            Assert.Greater(natives, 0, "The sample export has no system natives; the kind filter is wrong.");
            CollectionAssert.IsEmpty(unhandled, "System natives with no SDK runtime.");
        }

        /// <summary>
        /// The class-backed grid with both GetArea natives and every receiver
        /// shape the climb visits: a Held field on objects, two levels of
        /// nested children under shop-1 with layer links under them, an object
        /// with no placement tiles, a Save root inventory, and a Garden grid
        /// only its own row names.
        /// </summary>
        private static ProjectData BuildGetAreaProjectData()
        {
            ProjectData data = BuildClassBackedTileGridProjectData();
            var grid = new ClassTypeInfo { type = MemberKind.Class, classId = GridClassId, required = true };
            foreach (string id in new[] { GetObjectAreaId, GetTileAreaId })
            {
                data.members[id] = new FunctionMember
                {
                    id = id,
                    projectId = "project-a",
                    name = "GetArea",
                    kind = MemberKind.Function,
                    argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                    returnTypeInfo = new ClassTypeInfo { type = MemberKind.Class, classId = GridClassId, required = false },
                    genericParams = new[] { new FunctionGenericParam { name = "T", constraint = grid, defaultType = grid } },
                };
            }
            data.classes[GardenGridClassId] = new NeoSchemaClass
            {
                id = GardenGridClassId,
                projectId = "project-a",
                name = "Garden",
                extendsClassId = GridClassId,
                schema = new Dictionary<string, string> { ["Children"] = "grid-children-member" },
            };
            data.members["object-held-member"] = new ClassMember
            {
                id = "object-held-member",
                projectId = "project-a",
                name = "Held",
                kind = MemberKind.Class,
                DeclaredClassId = ObjectClassId,
            };
            data.classes[ObjectClassId].schema["Held"] = "object-held-member";
            data.members["inventory-member"] = new ListMember
            {
                id = "inventory-member",
                projectId = "project-a",
                name = "Inventory",
                kind = MemberKind.List,
                entryMemberId = "inventory-entry-member",
            };
            data.members["inventory-entry-member"] = new ClassMember
            {
                id = "inventory-entry-member",
                projectId = "project-a",
                name = "Item",
                kind = MemberKind.Class,
                DeclaredClassId = ObjectClassId,
            };
            data.classes["root-class"].schema["Inventory"] = "inventory-member";
            ((ObjectMemberValue)data.values["root-save-value"]).value!["Inventory"] = "inventory";
            ((ObjectMemberValue)data.values["shop-1"]).value!["Held"] = "held-1";
            ((ArrayMemberValue)data.values["shop-1-children"]).value = new[] { "shop-floor-link", "child-1" };
            foreach (MemberValue row in new MemberValue[]
            {
                // Unstamped: its class comes from Object.Held.
                Row("held-1", null, new Dictionary<string, string>()),
                Row("child-1", ObjectClassId, new Dictionary<string, string> { ["Children"] = "child-1-children" }),
                new ArrayMemberValue { id = "child-1-children", value = new[] { "child-2", "inner-link" } },
                Row("child-2", ObjectClassId, new Dictionary<string, string> { ["Children"] = "child-2-children" }),
                new ArrayMemberValue { id = "child-2-children", value = new[] { "deep-tile-link" } },
                Row("deep-tile-link", TileLayerLinkClassId, new Dictionary<string, string>
                {
                    ["layerClassId"] = BackgroundLayerClassId,
                    ["Tiles"] = "deep-tiles",
                }),
                new ArrayMemberValue { id = "deep-tiles", value = new[] { "deep-tile" } },
                Row("deep-tile", TileClassId, new Dictionary<string, string> { ["Cell"] = "deep-tile-cell" }),
                new Vector2MemberValue { id = "deep-tile-cell", value = new NeoVector2Value { x = 40, y = 40 } },
                Row("inner-link", ObjectLayerLinkClassId, new Dictionary<string, string>
                {
                    ["layerClassId"] = ObjectsLayerClassId,
                    ["Objects"] = "inner-objects",
                }),
                new ArrayMemberValue { id = "inner-objects", value = Array.Empty<string>() },
                Placed(Row("inner-object", ObjectClassId, new Dictionary<string, string>()), "inner-objects"),
                Placed(Row("bare", ObjectClassId, new Dictionary<string, string> { ["PlacementTiles"] = "bare-tiles" }), "objects-link-objects"),
                new ArrayMemberValue { id = "bare-tiles", value = Array.Empty<string>() },
                Row("garden", GardenGridClassId, new Dictionary<string, string> { ["Children"] = "garden-children" }),
                new ArrayMemberValue { id = "garden-children", value = new[] { "garden-link" } },
                Row("garden-link", ObjectLayerLinkClassId, new Dictionary<string, string> { ["Objects"] = "garden-objects" }),
                new ArrayMemberValue { id = "garden-objects", value = Array.Empty<string>() },
            })
            {
                data.values[row.id] = row;
            }
            return data;

            static ObjectMemberValue Row(string id, string? classId, Dictionary<string, string> value) => new()
            {
                id = id,
                classId = classId,
                value = value,
            };

            static ObjectMemberValue Placed(ObjectMemberValue row, string objectsListId)
            {
                row.containerId = objectsListId;
                return row;
            }
        }

        private static ObjectMemberValue LooseObject(string id) => new()
        {
            id = id,
            classId = ObjectClassId,
            value = new Dictionary<string, string>(),
        };

        private static MemberValue[] SpawnedObject(string id, string objectsListId)
        {
            ObjectMemberValue row = LooseObject(id);
            row.containerId = objectsListId;
            return new MemberValue[] { row };
        }

        /// <summary>The id of <paramref name="rowId"/>'s area, called directly.</summary>
        private static string? AreaId(NeoClient client, string rowId, string memberId = GetObjectAreaId, TypeInfo[]? typeArguments = null)
        {
            var ownership = client.TryGetValueOwnership(rowId, out NeoValueOwnership found) ? found : NeoValueOwnership.Asset;
            var ctx = client.CreateGetterContext(ownership);
            object? receiver = NSGetterEvaluator.UnwrapRow(client.ResolveValueRow(rowId)!, ctx, ownership);
            Assert.IsTrue(client.ScriptGridQueries.TryInvoke(memberId, receiver, Array.Empty<object?>(), typeArguments, ctx, out object? area));
            return NSGetterEvaluator.FindRowIdByReference(area, ctx);
        }

        /// <summary>Memoizes a getter that answered <paramref name="expected"/> for <paramref name="rowId"/>'s area.</summary>
        private static NeoClient.GetterMemoKey MemoizeArea(NeoClient client, string rowId, string? expected)
        {
            var key = new NeoClient.GetterMemoKey(NeoValueOwnership.Asset, rowId, "area-getter", NeoValueOwnership.Asset);
            NeoClient.GetterCaptureFrame enclosing = client.BeginGetterReadCapture();
            Assert.AreEqual(expected, AreaId(client, rowId));
            client.MemoizeGetter(key, 0, null, client.EndGetterReadCapture(enclosing));
            Assert.IsNotNull(client.FindMemoizedGetter(key));
            return key;
        }

        /// <summary>Despawns a Save object the way the grid does, through a committed plan.</summary>
        private static void Despawn(NeoClient client, string id)
        {
            var plan = new NeoWritePlan(client);
            client.StageOwnedRemoval(plan, NeoValueOwnership.Save, id, client.TryInferMemberForValueId(id, out Member? member) ? member : null);
            plan.Commit();
        }

        private static void Execute(NeoClient client, Instruction instruction) => NeoScriptExecutor.Execute(client, new FunctionWithReturnType
        {
            compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
            parameters = Array.Empty<Variable>(),
            typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
            instructions = new[] { instruction },
        }, new Dictionary<string, object?>(), client.CreateGetterContext(NeoValueOwnership.Asset));

        /// <summary><c>owner.Children.Mutation(args)</c>, written to the Save.</summary>
        private static CollectionCallInstruction ChildrenCall(string ownerId, string mutation, params Pointer[] args) => new()
        {
            type = InstructionKind.CollectionCall,
            target = new WriteTarget
            {
                pointer = new KeyOfPointer
                {
                    type = PointerKind.KeyOf,
                    keyOf = new KeyOf
                    {
                        pointer = Reference(ownerId),
                        key = new ValuePointer
                        {
                            type = PointerKind.Value,
                            value = new Value { typeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true }, value = new JValue("Children") },
                        },
                    },
                },
                typeInfo = new CollectionTypeInfo
                {
                    type = MemberKind.List,
                    required = true,
                    entryTypeInfo = new ClassTypeInfo { type = MemberKind.Class, classId = ObjectClassId, required = true },
                },
                writability = WritabilityKind.Save,
            },
            mutation = mutation,
            args = args,
        };

        private static ReferencePointer Reference(string valueId) => new()
        {
            type = PointerKind.Reference,
            valueId = valueId,
        };

        private static ValuePointer Int(int value) => new()
        {
            type = PointerKind.Value,
            value = new Value { typeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true }, value = new JValue(value) },
        };

        private sealed class RowlessValue : INeoValueReference
        {
            public string? valueId => null;
        }
    }
}
