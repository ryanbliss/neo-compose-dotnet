// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeoCompose.Tests
{
    public partial class NeoTileGridRendererTests
    {
        private const string DepartingGeneratorClassId = "departing-generator";
        private const string DepartureHookId = "generator-on-unload";

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void GridDeparture_UnloadQueriesSurvivingNeighborsAndUnregisters(bool warm, bool throwCleanup)
        {
            ProjectData data = DepartureGridData();
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            RegisterDepartureGrid(client);
            var generator = PlacedObject("generator", warm ? 30 : 11, 20);
            ((ObjectMemberValue)generator[^1]).classId = DepartingGeneratorClassId;
            client.SetWritableValues(NeoValueOwnership.Save, generator);
            client.SetWritableValues(NeoValueOwnership.Save, PlacedObject("receiver", 14, 20));
            var registrations = new Dictionary<string, HashSet<string>>
            {
                ["shop-1"] = new() { "generator" },
                ["receiver"] = new() { "generator" },
            };
            string[]? seen = null;
            Vector2Int? origin = null;
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["departure-cleanup"] = (c, receiver, args, _) =>
                {
                    string id = NeoGeneratedTypesSupport.ValueId(receiver)!;
                    var ctx = c.CreateGetterContext(NeoValueOwnership.Save);
                    seen = ((object?[])args[0]!).Select(value => NSGetterEvaluator.FindRowIdByReference(value, ctx)!).ToArray();
                    Assert.IsTrue(c.ScriptGridQueries.TryInvoke("system_df1c2d06-eeec-5340-addc-740f3668c9e4", receiver,
                        Array.Empty<object?>(), null, ctx, out object? cell));
                    origin = NeoGeneratedTypesSupport.ReadVector2IntValue(cell);
                    foreach (string neighbor in seen)
                        registrations[neighbor].Remove(id);
                    if (throwCleanup)
                        throw new InvalidOperationException("cleanup failed after unregistering");
                    return null;
                },
            });
            client.StartScriptRuntime();
            if (warm)
            {
                CollectionAssert.AreEqual(new[] { "generator" }, DepartureQuery(client, "generator"));
                client.SetWritableValue(NeoValueOwnership.Save, new Vector3MemberValue
                {
                    id = "generator-position",
                    value = new NeoVector3Value { x = 11, y = 20 },
                });
            }
            if (throwCleanup)
                LogAssert.Expect(LogType.Exception, new Regex("cleanup failed after unregistering"));
            var removal = new NeoWritePlan(client);
            // Neighbors come from the committed grid, not a pre-removal snapshot.
            removal.Set(NeoValueOwnership.Save, new Vector3MemberValue
            {
                id = "receiver-position",
                value = new NeoVector3Value { x = 12, y = 20 },
            });
            // Remove position and footprint before the root in plan order.
            foreach (MemberValue row in generator)
                removal.Remove(NeoValueOwnership.Save, row.id);
            removal.Commit();

            CollectionAssert.AreEquivalent(new[] { "shop-1", "receiver" }, seen);
            Assert.AreEqual(new Vector2Int(11, 20), origin);
            Assert.IsTrue(registrations.Values.All(ids => ids.Count == 0));
            CollectionAssert.DoesNotContain(DepartureQuery(client, "shop-1"), "generator");
            Assert.Throws<NSGetterRuntimeError>(() => DepartureQuery(client, "generator"));
            // Reusing the id later must use its new origin, never the old departure.
            client.SetWritableValues(NeoValueOwnership.Save, PlacedObject("generator", 80, 90));
            CollectionAssert.AreEqual(new[] { "generator" }, DepartureQuery(client, "generator"));
        }

        [Test]
        public void GridDeparture_GridReplacementDoesNotBecomeTheCleanupTarget()
        {
            ProjectData data = DepartureGridData();
            data.classes["replacement-grid"] = new NeoSchemaClass
            {
                id = "replacement-grid",
                projectId = "project-a",
                name = "ReplacementGrid",
                extendsClassId = GridClassId,
                schema = new Dictionary<string, string>(),
            };
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            RegisterDepartureGrid(client);
            var generator = PlacedObject("generator", 11, 20);
            ((ObjectMemberValue)generator[^1]).classId = DepartingGeneratorClassId;
            client.SetWritableValues(NeoValueOwnership.Save, generator);
            int? count = null;
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["departure-cleanup"] = (_, _, args, _) =>
                {
                    count = ((object?[])args[0]!).Length;
                    return null;
                },
            });
            client.StartScriptRuntime();
            var replacement = new NeoWritePlan(client);
            var grid = (ObjectMemberValue)data.values["town-grid"];
            replacement.Set(NeoValueOwnership.Save, new ObjectMemberValue
            {
                id = grid.id,
                classId = "replacement-grid",
                value = new Dictionary<string, string>(grid.value!),
            });
            foreach (MemberValue row in generator)
                replacement.Remove(NeoValueOwnership.Save, row.id);
            replacement.Commit();
            Assert.AreEqual(0, count);
            Assert.Throws<NSGetterRuntimeError>(() => DepartureQuery(client, "generator"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GridDeparture_PartitionUnloadDoesNotReloadTheGrid(bool includeGrid)
        {
            ProjectData data = DepartureGridData();
            string partition = NeoClient.MakeWorldPartitionKey(GridClassId);
            var rows = new Dictionary<string, MemberValue>();
            // A real world partition can leave the grid root loaded.
            foreach (string id in data.values.Keys.Where(id => id != "root-save-value" && (includeGrid || id != "town-grid")).ToArray())
            {
                rows[id] = data.values[id];
                data.values.Remove(id);
            }
            foreach (MemberValue row in PlacedObject("generator", 11, 20))
                rows[row.id] = row;
            ((ObjectMemberValue)rows["generator"]).classId = DepartingGeneratorClassId;
            data.valuePartitions = NeoTestExport.Partitions(new Dictionary<string, JToken> { [partition] = JObject.FromObject(rows) });
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            client.LoadValuePartition(partition);
            RegisterDepartureGrid(client);
            int? count = null;
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["departure-cleanup"] = (_, _, args, _) =>
                {
                    count = ((object?[])args[0]!).Length;
                    return null;
                },
            });
            client.StartScriptRuntime();
            client.UnloadValuePartition(partition);
            Assert.AreEqual(0, count);
            Assert.IsFalse(client.IsValuePartitionLoaded(partition));
            Assert.Throws<NSGetterRuntimeError>(() => DepartureQuery(client, "generator"));
            Assert.IsFalse(client.IsValuePartitionLoaded(partition));
        }

        private static ProjectData DepartureGridData()
        {
            ProjectData data = BuildGridGetterProjectData();
            data.classes[DepartingGeneratorClassId] = new NeoSchemaClass
            {
                id = DepartingGeneratorClassId,
                projectId = "project-a",
                name = "Generator",
                extendsClassId = ObjectClassId,
                implementsInterfaceIds = new List<string> { "system_c64e99f9-e8be-4d0a-927f-de20bdf9cdeb" },
                schema = new Dictionary<string, string> { ["OnUnload"] = DepartureHookId, ["Cleanup"] = "departure-cleanup" },
            };
            data.members["departure-cleanup"] = new FunctionMember
            {
                id = "departure-cleanup",
                projectId = "project-a",
                name = "Cleanup",
                kind = MemberKind.Function,
                Dispatch = NeoFunctionDispatchKind.Synchronous,
                argumentTypes = new[] { new FunctionArgumentTypeInfo { name = "neighbors", type = MemberKind.List, required = true,
                    entryTypeInfo = new ClassTypeInfo { type = MemberKind.Class, classId = ObjectClassId, required = true } } },
                returnTypeInfo = new VoidTypeInfo { type = MemberKind.Void },
            };
            var receiver = new VariablePointer { type = PointerKind.Variable, variableId = "__this__" };
            data.members[DepartureHookId] = new NSFunctionMember
            {
                id = DepartureHookId,
                code = "compiled cleanup function",
                projectId = "project-a",
                name = "OnUnload",
                kind = MemberKind.NSFunction,
                argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                returnTypeInfo = new VoidTypeInfo { type = MemberKind.Void, required = true },
                Dispatch = NeoFunctionDispatchKind.Synchronous,
                action = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = new[]
                    {
                        new Variable { id = "__this__", pointer = new VariablePointer { type = PointerKind.Variable, variableId = "__this__" }, typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true } },
                        new Variable { id = "__root__", pointer = new VariablePointer { type = PointerKind.Variable, variableId = "__root__" }, typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true } },
                    },
                    typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                    instructions = new Instruction[]
                    {
                        new FunctionCallInstruction
                        {
                            type = InstructionKind.FunctionCall,
                            call = new CallFunctionPointer
                            {
                                type = PointerKind.CallFunction, callSiteId = "cleanup", memberId = "departure-cleanup",
                                receiver = CallReceiver.Instance(receiver),
                                args = new Pointer[]
                                {
                                    new CallFunctionPointer
                                    {
                                        type = PointerKind.CallFunction, callSiteId = "neighbors", memberId = GetObjectsId,
                                        receiver = CallReceiver.Instance(receiver),
                                        typeArguments = new TypeInfo[] { new ClassTypeInfo { type = MemberKind.Class, classId = ObjectClassId, required = true } },
                                        args = new Pointer[] { NeoCellPatternRuntimeTests.StaticCall("range", NeoCellPatternRuntimeTests.BoxId,
                                            NeoCellPatternRuntimeTests.Int(1), NeoCellPatternRuntimeTests.Int(0),
                                            NeoCellPatternRuntimeTests.Excluding(NeoCellPatternRuntimeTests.NoneOptionId)) },
                                    },
                                },
                            },
                        },
                    },
                },
            };
            return data;
        }

        private static void RegisterDepartureGrid(NeoClient client)
        {
            client.ScriptGridQueries.RegisterFactories(new Dictionary<string, Func<NeoClient, string, INeoTileGridContent>>
            {
                [GridClassId] = (c, id) =>
                {
                    var primitive = NeoReadOnlyTileGridPrimitive.Resolve(c, id,
                        BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
                    return new TestTileGridContent(primitive, Array.Empty<IReadOnlyNeoTileLayerRuntime>(),
                        new[] { primitive.BindReadOnlyObjectLayer<TestAuthoredObjectLayer>(ObjectsLayerClassId, new[] { ObjectClassId }) });
                },
            });
        }

        private static string[] DepartureQuery(NeoClient client, string id)
        {
            var ctx = client.CreateGetterContext(NeoValueOwnership.Save);
            client.TryGetValue(id, out MemberValue? row);
            object? receiver = row is null ? null : NSGetterEvaluator.UnwrapRow(row, ctx, NeoValueOwnership.Save);
            client.ScriptGridQueries.TryInvoke(GetObjectsId, receiver,
                new object?[] { NeoCellPatternRuntimeTests.Construct(client, Vector2Int.left, Vector2Int.zero, Vector2Int.right) }, null, ctx, out object? result);
            return ((object?[])result!).Select(value => NSGetterEvaluator.FindRowIdByReference(value, ctx)!).ToArray();
        }
    }
}
