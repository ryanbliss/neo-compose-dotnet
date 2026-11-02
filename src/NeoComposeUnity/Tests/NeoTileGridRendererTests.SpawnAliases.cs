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
        [TestCase("shared")]
        [TestCase("private")]
        [TestCase("retired")]
        [TestCase("cross")]
        [TestCase("foreign")]
        public void SpawnKeepsScriptLocalsWritableAfterSessionAdoption(string contextKind)
        {
            var data = BuildClassBackedTileGridProjectData();
            AddPlacementResultSchema(data);
            data.members["spawn-counter"] = new IntMember
            {
                id = "spawn-counter",
                kind = MemberKind.Int,
                name = "Counter",
                Storage = NeoMemberStorage.Inherit,
                defaultValue = new NumberMemberValueBase { value = 0 },
            };
            data.classes[ObjectClassId].schema["Counter"] = "spawn-counter";
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            client.RegisterGeneratedClassFactories(BuildClassBackedReadOnlyFactories(), BuildClassBackedWritableFactories());
            var entry = ClonePlacementObject(client);
            string id = entry.valueId!;
            var ctx = contextKind is "private" or "foreign" ? new NSGetterEvaluator.Context(client, null, null, valueOwnership: NeoValueOwnership.Session)
                : client.CreateGetterContext(NeoValueOwnership.Session);
            var local = NSGetterEvaluator.EvaluatePointer(Reference(id), new NeoScriptScope(0), ctx);
            Assert.AreEqual(NeoValueOwnership.Session, NSGetterEvaluator.FindRowOwnershipByReference(local, ctx));
            var grid = NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid");
            var occupied = grid.BuildObjectLayerRecords(ObjectsLayerClassId, null)[0].Cell;
            var link = WritableObjectLink(client, "objects-link");
            Assert.IsFalse(link.TrySpawn(occupied, entry).Ok);
            Assert.AreEqual(NeoValueOwnership.Session, NSGetterEvaluator.FindRowOwnershipByReference(local, ctx));
            if (contextKind == "retired")
            {
                typeof(NeoClient).GetMethod("InvalidateSharedEvaluationContext",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(client, null);
                _ = client.CreateGetterContext(NeoValueOwnership.Session);
            }
            var originContext = ctx;
            if (contextKind == "foreign")
                ctx = new NSGetterEvaluator.Context(client, null, null);
            Assert.IsTrue(NeoLayerLinkRuntime.TryInvoke(NeoLayerLinkRuntime.TrySpawnId, link,
                new object?[] { new Vector2Int(80, 90), local }, ctx, out _));
            Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Save, id, out ObjectMemberValue? placed));
            Assert.IsFalse(client.HasWritableValue(NeoValueOwnership.Session, id));
            Assert.That(grid.BuildObjectLayerRecords(ObjectsLayerClassId, null).Any(row => row.InstanceId == id), Is.True);
            if (contextKind == "cross")
                ctx = new NSGetterEvaluator.Context(client, null, null);
            Assert.AreEqual(NeoValueOwnership.Save, NSGetterEvaluator.FindRowOwnershipByReference(local, ctx));
            if (contextKind == "foreign")
                ctx = originContext;
            var scope = new NeoScriptScope(1);
            scope.SetLocal("local", local);
            Assert.AreEqual(0d, NSGetterEvaluator.EvaluatePointer(new KeyOfPointer
            {
                type = PointerKind.KeyOf,
                keyOf = new KeyOf
                {
                    pointer = new VariablePointer { type = PointerKind.Variable, variableId = "local" },
                    key = new ValuePointer
                    {
                        type = PointerKind.Value,
                        value = new Value
                        {
                            typeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true },
                            value = "Counter",
                        }
                    },
                },
            }, scope, ctx));
            WriteSpawnCounter(client, ctx, local!, WritabilityKind.Session);
            placed = (ObjectMemberValue)client.ResolveValueRow(id)!;
            Assert.AreEqual(17, ((NumberMemberValue)client.ResolveClassChildRow(placed, "Counter")!).value);
            Assert.That(client.SerializeSaveData(), Does.Contain(id));
        }

        [Test]
        public void AuthoredOccurrenceUsesDeclaredSaveFieldStorage()
        {
            var data = BuildClassBackedTileGridProjectData();
            data.members["spawn-counter"] = new IntMember
            {
                id = "spawn-counter",
                kind = MemberKind.Int,
                name = "Counter",
                Storage = NeoMemberStorage.Save,
                defaultValue = new NumberMemberValueBase { value = 0 },
            };
            data.classes["occurrence-class"] = new NeoSchemaClass
            {
                id = "occurrence-class",
                name = "Occurrence",
                allowedStorage = NeoMemberStorage.Immutable,
                schema = new Dictionary<string, string> { ["Counter"] = "spawn-counter" },
            };
            data.values["occurrence"] = new ObjectMemberValue
            {
                id = "occurrence",
                classId = "occurrence-class",
                value = new Dictionary<string, string>(),
            };
            data.members["occurrence-member"] = new ClassMember
            {
                id = "occurrence-member",
                name = "Occurrence",
                kind = MemberKind.Class,
                DeclaredClassId = "occurrence-class",
                Storage = NeoMemberStorage.Immutable,
            };
            data.classes["root-class"].schema["Occurrence"] = "occurrence-member";
            ((ObjectMemberValue)data.values["root-assets-value"]).value!["Occurrence"] = "occurrence";
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var ctx = client.CreateGetterContext(NeoValueOwnership.Asset);
            object local = NSGetterEvaluator.EvaluatePointer(Reference("occurrence"), new NeoScriptScope(0), ctx)!;
            Assert.AreEqual(NeoValueOwnership.Asset, NSGetterEvaluator.FindRowOwnershipByReference(local, ctx));
            WriteSpawnCounter(client, ctx, local, WritabilityKind.Runtime);
            var row = (ObjectMemberValue)client.ResolveValueRow("occurrence")!;
            var field = client.ResolveClassChildRow(row, "Counter")!;
            Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Save, field.id, out NumberMemberValue? saved));
            Assert.AreEqual(17, saved!.value);
        }

        private static void WriteSpawnCounter(NeoClient client, NSGetterEvaluator.Context ctx, object local, string writability)
        {
            var integer = new PrimitiveTypeInfo { type = MemberKind.Int, required = true };
            var action = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = Array.Empty<Variable>(),
                typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                instructions = new Instruction[] { new AssignInstruction
                {
                    type = InstructionKind.Assign, operatorValue = "=",
                    target = new WriteTarget
                    {
                        writability = writability, typeInfo = integer,
                        pointer = new KeyOfPointer
                        {
                            type = PointerKind.KeyOf,
                            keyOf = new KeyOf
                            {
                                pointer = new VariablePointer { type = PointerKind.Variable, variableId = "local" },
                                key = new ValuePointer { type = PointerKind.Value, value = new Value
                                {
                                    typeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true }, value = "Counter",
                                } },
                            },
                        },
                    },
                    pointer = new ValuePointer { type = PointerKind.Value, value = new Value { typeInfo = integer, value = 17 } },
                } },
            };
            NeoScriptExecutor.Execute(client, action, new Dictionary<string, object?> { ["local"] = local }, ctx);
        }
    }
}
