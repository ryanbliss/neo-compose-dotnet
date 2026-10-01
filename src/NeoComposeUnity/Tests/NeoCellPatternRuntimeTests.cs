// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace NeoCompose.Tests
{
    public class NeoCellPatternRuntimeTests
    {
        internal static NeoClient Client(Action<ProjectData>? configure = null)
        {
            ProjectData data = NeoGenericTestFixture.BuildProjectData();
            var seed = JObject.Parse(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/cell-pattern-seed.json"));
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
            foreach (var item in seed["enums"]!)
            {
                var value = item.ToObject<NeoCompose.Runtime.Json.Enum>()!;
                data.enums[value.id] = value;
            }
            data.constructors ??= new Dictionary<string, ConstructorRecord>();
            foreach (var item in seed["constructors"]!)
            {
                var value = item.ToObject<ConstructorRecord>()!;
                data.constructors[value.id] = value;
            }
            configure?.Invoke(data);
            return NeoTestSaveStack.ClientFromSchema(data);
        }

        [Test]
        public void NativeFactory_UsesCanonicalConstructorAndPreservesPatternOperations()
        {
            using NeoClient client = Client();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            Assert.IsTrue(NeoCellPatternRuntime.TryInvoke("system_aaad2df6-e31e-5f5d-95b9-e265211faed5", null,
                new object?[] { 2, 1, new[] { NeoCellPatternStorage.CenterId } }, ctx, out object? value));
            CollectionAssert.AreEqual(NeoCellPattern.Box(2, 1, NeoCellPatternExcluding.Center), NeoCellPatternStorage.ReadRuntime(value, ctx));
            string id = NSGetterEvaluator.FindRowIdByReference(value, ctx)!;
            Assert.IsTrue(client.TryGetValue(id, out ObjectMemberValue? row));
            Assert.AreEqual(NeoCellPatternStorage.ConstructorId, row!.instanceConstructorId);
            Assert.IsNotNull(row.constructorArgs);
            Assert.IsTrue(NeoCellPatternRuntime.TryInvoke("system_65a16908-05a6-52f9-a467-4e37e95ba0be", value,
                new object?[] { new Vector2Int(1, 0) }, ctx, out object? contains));
            Assert.AreEqual(true, contains);
        }

        [TestCase(0, 0)]
        [TestCase(2, 1)]
        public void DirectQueryPatternDoesNotConstructTemporaryRows(int x, int y)
        {
            using NeoClient client = Client();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            object source = NeoCellPatternStorage.Materialize(NeoCellPattern.EightNeighbors, ctx);
            var translate = new CallFunctionPointer
            {
                type = PointerKind.CallFunction,
                callSiteId = "translate-query-pattern",
                memberId = "system_efc67858-0c95-573f-a8a9-d7e07d0a1d55",
                receiver = CallReceiver.Instance(new VariablePointer { type = PointerKind.Variable, variableId = "source" }),
                args = new Pointer[] { new ValuePointer { type = PointerKind.Value, value = new Value
                {
                    typeInfo = new PrimitiveTypeInfo { type = MemberKind.Vector2Int, required = true },
                    value = JObject.FromObject(new { x, y }),
                } } },
            };
            var query = new CallFunctionPointer
            {
                type = PointerKind.CallFunction,
                callSiteId = "query",
                memberId = "system_f5ca386c-990c-54a1-8473-2d49d2cd887d",
                args = new Pointer[] { translate },
            };
            var scope = new NeoScriptScope(new Dictionary<string, object?> { ["source"] = source });
            int before = client.sessionValues.Count;
            object? result = NSGetterEvaluator.EvaluateFunctionArgument(query, 0, scope, ctx);
            Assert.That(client.sessionValues.Count, Is.EqualTo(before), "Query-only offsets must not become Session rows.");
            CollectionAssert.AreEqual(NeoCellPattern.EightNeighbors.Translate(new Vector2Int(x, y)), NeoCellPatternStorage.ReadRuntime(result, ctx));
            // The same expression outside a query still exposes a canonical
            // class identity and constructor provenance for storage/normal calls.
            object? stored = NSGetterEvaluator.EvaluatePointer(translate, scope, ctx);
            string id = NSGetterEvaluator.FindRowIdByReference(stored, ctx)!;
            Assert.That(client.TryGetValue(id, out ObjectMemberValue? row), Is.True);
            Assert.That(row!.instanceConstructorId, Is.EqualTo(NeoCellPatternStorage.ConstructorId));
        }

        [Test]
        public void NativePattern_BuildsNoRowsUntilItsRowIsNeeded()
        {
            using NeoClient client = Client();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            int before = client.sessionValues.Count;
            object source = NeoCellPatternStorage.Materialize(NeoCellPattern.EightNeighbors, ctx);
            CollectionAssert.AreEqual(NeoCellPattern.EightNeighbors, NeoCellPatternStorage.ReadRuntime(source, ctx));
            Assert.AreEqual(before, client.sessionValues.Count, "A pattern nothing stored must stay in slots.");
            // Needing the row materializes it with the constructor's recipe.
            string id = NSGetterEvaluator.FindRowIdByReference(source, ctx)!;
            Assert.IsTrue(client.TryGetValue(id, out ObjectMemberValue? row));
            Assert.AreEqual(NeoCellPatternStorage.ConstructorId, row!.instanceConstructorId);
            Assert.AreEqual(1, row.constructorArgs!.Count);
            CollectionAssert.AreEqual(NeoCellPattern.EightNeighbors, NeoCellPatternStorage.ReadRuntime(source, ctx));
        }

        [Test]
        public void StaticPatternGetter_ReadAsAnArgumentReceiverIsMemoizedUntilARowItReadChanges()
        {
            const string boxId = "system_aaad2df6-e31e-5f5d-95b9-e265211faed5";
            const string areaId = "member-area";
            var patternType = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = NeoCellPatternStorage.ClassId };
            static ValuePointer Literal(TypeInfo typeInfo, JToken value) => new()
            {
                type = PointerKind.Value,
                value = new Value { typeInfo = typeInfo, value = value },
            };
            // static NeoCellPattern Area => NeoCellPattern.Box(width, 0);
            var area = new NSPropertyMember
            {
                id = areaId,
                projectId = "project-a",
                name = "Area",
                kind = MemberKind.NSProperty,
                Modifier = NeoMemberModifierKind.Static,
                returnTypeInfo = patternType,
                getter = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = new[] { new Variable { id = "__this__" }, new Variable { id = "__root__" } },
                    typeInfo = patternType,
                    instructions = new Instruction[]
                    {
                        new ReturnInstruction
                        {
                            type = InstructionKind.Return,
                            pointer = new CallFunctionPointer
                            {
                                type = PointerKind.CallFunction,
                                callSiteId = "area-box",
                                memberId = boxId,
                                receiver = CallReceiver.Static(boxId),
                                args = new Pointer[]
                                {
                                    new ReferencePointer { type = PointerKind.Reference, valueId = "pattern-width" },
                                    Literal(new PrimitiveTypeInfo { type = MemberKind.Int, required = true }, 0),
                                    Literal(new EnumTypeInfo { type = MemberKind.Enum, required = true, enumId = NeoCellPatternStorage.ExcludingEnumId },
                                        new JArray(NeoCellPatternStorage.NoneId)),
                                },
                            },
                        },
                    },
                },
            };
            using NeoClient client = Client(data =>
            {
                data.values["pattern-width"] = new NumberMemberValue { id = "pattern-width", value = 1 };
                data.members[areaId] = area;
                data.classes["class-areas"] = new NeoSchemaClass
                {
                    id = "class-areas",
                    projectId = "project-a",
                    name = "Areas",
                    schema = new Dictionary<string, string> { ["Area"] = areaId },
                };
            });
            // GetObjects(Areas.Area.Translate((0, 0)))
            var query = new CallFunctionPointer
            {
                type = PointerKind.CallFunction,
                callSiteId = "query",
                memberId = "system_f5ca386c-990c-54a1-8473-2d49d2cd887d",
                args = new Pointer[]
                {
                    new CallFunctionPointer
                    {
                        type = PointerKind.CallFunction,
                        callSiteId = "translate-area",
                        memberId = "system_efc67858-0c95-573f-a8a9-d7e07d0a1d55",
                        receiver = CallReceiver.Instance(new CallGetterPointer
                        {
                            type = PointerKind.CallGetter,
                            memberId = areaId,
                            receiver = CallReceiver.Static(areaId),
                        }),
                        args = new Pointer[]
                        {
                            Literal(new PrimitiveTypeInfo { type = MemberKind.Vector2Int, required = true }, JObject.FromObject(new { x = 0, y = 0 })),
                        },
                    },
                },
            };
            var ctx = client.CreateGetterContext(NeoValueOwnership.Save);
            var scope = new NeoScriptScope(0);
            var key = new NeoClient.GetterMemoKey(NeoValueOwnership.Save, NeoClient.StaticGetterRowId, areaId, NeoValueOwnership.Save);
            NeoCellPattern Area() => NeoCellPatternStorage.ReadRuntime(NSGetterEvaluator.EvaluateFunctionArgument(query, 0, scope, ctx), ctx);

            CollectionAssert.AreEqual(NeoCellPattern.Box(1, 0, NeoCellPatternExcluding.None), Area());
            Assert.IsInstanceOf<NeoCellPattern>(client.FindMemoizedGetter(key)?.scalar, "The getter's pattern is kept as offsets.");
            CollectionAssert.AreEqual(NeoCellPattern.Box(1, 0, NeoCellPatternExcluding.None), Area());

            client.SetWritableValue(NeoValueOwnership.Save, new NumberMemberValue { id = "pattern-width", value = 2 });
            Assert.IsNull(client.FindMemoizedGetter(key), "A write to a row the getter read drops the entry.");
            CollectionAssert.AreEqual(NeoCellPattern.Box(2, 0, NeoCellPatternExcluding.None), Area());
        }

        [Test]
        public void OnlyQueriesGrid_HoldsWhenEveryReadIsAGridQueryCellsArgument()
        {
            static VariablePointer Cells() => new()
            {
                type = PointerKind.Variable,
                variableId = "cells"
            };
            static FunctionWithReturnType Body(params Instruction[] instructions) => new()
            {
                parameters = new[] { new Variable { id = "__this__" }, new Variable { id = "cells" } },
                instructions = instructions,
            };
            var query = new ReturnInstruction
            {
                type = InstructionKind.Return,
                pointer = new CallFunctionPointer
                {
                    type = PointerKind.CallFunction,
                    callSiteId = "query",
                    memberId = "system_f5ca386c-990c-54a1-8473-2d49d2cd887d",
                    receiver = CallReceiver.Instance(new VariablePointer { type = PointerKind.Variable, variableId = "__this__" }),
                    args = new Pointer[] { Cells() },
                },
            };

            FunctionWithReturnType queries = Body(query);
            Assert.IsTrue(NeoCellPatternRuntime.OnlyQueriesGrid(queries, 1));
            Assert.IsFalse(NeoCellPatternRuntime.OnlyQueriesGrid(queries, 0), "The receiver is read as a query receiver, not its cells.");
            FunctionWithReturnType escapes = Body(
                new ReturnInstruction { type = InstructionKind.Return, pointer = Cells() },
                query);
            Assert.IsFalse(NeoCellPatternRuntime.OnlyQueriesGrid(escapes, 1));
        }

        [Test]
        public void PatternArguments_AreConstructedForEveryTargetButAGridQuery()
        {
            using NeoClient client = Client();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var args = new object?[] { NeoCellPattern.EightNeighbors, 3 };
            NSGetterEvaluator.MaterializePatternArguments("system_f5ca386c-990c-54a1-8473-2d49d2cd887d", args, ctx);
            Assert.AreSame(NeoCellPattern.EightNeighbors, args[0], "A grid query reads the offsets as they are.");

            NSGetterEvaluator.MaterializePatternArguments("member-other", args, ctx);
            Assert.IsInstanceOf<NeoScriptObject>(args[0]);
            CollectionAssert.AreEqual(NeoCellPattern.EightNeighbors, NeoCellPatternStorage.ReadRuntime(args[0], ctx));
            Assert.AreEqual(3, args[1]);
        }

        [Test]
        public void DetachedPattern_IsReadFromItsSlotsOnce()
        {
            using NeoClient client = Client();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            object source = NeoCellPatternStorage.Materialize(NeoCellPattern.EightNeighbors, ctx);
            NeoCellPattern first = NeoCellPatternStorage.ReadRuntime(source, ctx);
            CollectionAssert.AreEqual(NeoCellPattern.EightNeighbors, first);
            Assert.AreSame(first, NeoCellPatternStorage.ReadRuntime(source, ctx));
        }

        [Test]
        public void NativePattern_RecordsTheArgumentsItWasBuiltWith()
        {
            // A vector read from a row is the row's cached payload, which a
            // later write patches in place. The recipe recorded when the
            // pattern attaches still holds the value it was built with.
            using NeoClient client = Client();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            NeoVector2Value offset = NeoVectorValues.FromVector2Int(new Vector2Int(1, 2));
            var resolved = NeoGeneratedTypesSupport.ResolveDeclaredConstructor(
                client,
                new ClassTypeInfo { type = MemberKind.Class, required = true, classId = NeoCellPatternStorage.ClassId },
                NeoCellPatternStorage.ConstructorId,
                new[] { "offsets" },
                Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>());
            object?[] arguments = resolved.NewArgumentValues();
            arguments[resolved.argumentPositions[0]] = new object?[] { offset };
            object source = NSGetterEvaluator.ConstructDeclared(
                resolved,
                arguments,
                Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>(),
                ctx,
                evaluateFieldValues: null,
                replayContext: false)!;

            offset.y = 9;

            string id = NSGetterEvaluator.FindRowIdByReference(source, ctx)!;
            Assert.IsTrue(client.TryGetValue(id, out ObjectMemberValue? row));
            JToken recorded = row!.constructorArgs!.Values.Single()!;
            Assert.AreEqual(2, recorded[0]!["y"]!.Value<int>(), recorded.ToString());
        }

        [Test]
        public void NativePattern_OffsetComponentWriteReachesItsRow()
        {
            using NeoClient client = Client();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            object source = NeoCellPatternStorage.Materialize(NeoCellPattern.EightNeighbors, ctx);
            var sourceType = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = NeoCellPatternStorage.ClassId };
            KeyOfPointer offset = KeyOf(
                KeyOf(new VariablePointer { type = PointerKind.Variable, variableId = "source" }, Literal("_offsets", MemberKind.String)),
                Literal(1, MemberKind.Int));
            var body = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = new[] { new Variable { id = "source", typeInfo = sourceType } },
                typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                instructions = new Instruction[]
                {
                    new AssignInstruction
                    {
                        type = InstructionKind.Assign,
                        operatorValue = "=",
                        pointer = Literal(7, MemberKind.Int),
                        target = new WriteTarget
                        {
                            pointer = KeyOf(offset, Literal("y", MemberKind.String)),
                            typeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                            writability = "runtime",
                        },
                    },
                },
            };
            NeoScriptExecutor.Execute(client, body, new Dictionary<string, object?> { ["source"] = source }, ctx);
            var expected = new List<Vector2Int>(NeoCellPattern.EightNeighbors);
            expected[1] = new Vector2Int(expected[1].x, 7);
            CollectionAssert.AreEqual(expected, NeoCellPatternStorage.ReadRuntime(source, ctx));
            Assert.IsNotNull(NSGetterEvaluator.FindRowIdByReference(source, ctx));
        }

        private static KeyOfPointer KeyOf(Pointer receiver, Pointer key) => new()
        {
            type = PointerKind.KeyOf,
            keyOf = new KeyOf { pointer = receiver, key = key },
        };

        private static ValuePointer Literal(object value, MemberKind kind) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = new PrimitiveTypeInfo { type = kind, required = true },
                value = JToken.FromObject(value),
            },
        };

        [Test]
        public void SnapshotRead_ReflectsChangedOffsetRowsWithoutChangingPreviousPattern()
        {
            using NeoClient client = Client();
            var payload = NeoCellPatternStorage.Serialize(client, new NeoCellPattern(new Vector2Int(3, 4)))!;
            string id = payload.valueId!;
            var member = new ClassMember { id = "pattern-field", name = "Pattern", kind = MemberKind.Class, classId = NeoCellPatternStorage.ClassId };
            var node = new NeoMemberClassWritable(client, member, id, NeoValueOwnership.Session);
            NeoCellPattern before = NeoCellPatternStorage.ReadRequired(client, node);
            var offsets = (ArrayMemberValue)client.ResolveClassChildRow(node.value!, "_offsets")!;
            string entryId = offsets.value![0];
            Assert.IsTrue(client.TryGetValue(entryId, out Vector2MemberValue? entry));
            entry!.value = NeoVectorValues.FromVector2Int(new Vector2Int(9, 8));
            client.SetWritableValue(NeoValueOwnership.Session, entry);
            NeoCellPattern after = NeoCellPatternStorage.ReadRequired(client, node);
            Assert.AreEqual(new Vector2Int(3, 4), before[0]);
            Assert.AreEqual(new Vector2Int(9, 8), after[0]);
            var env = NeoGenericBindings.Resolve<NeoCellPattern>(client, node);
            Assert.AreEqual(after[0], env.Read(node)[0]);
        }

        [Test]
        public void RowPatternRead_KeepsAnUnchangedReadAndSeesAChangedOffset()
        {
            using NeoClient client = Client();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            object source = NeoCellPatternStorage.Materialize(NeoCellPattern.EightNeighbors, ctx);
            string id = NSGetterEvaluator.FindRowIdByReference(source, ctx)!;
            NeoCellPattern first = NeoCellPatternStorage.ReadRuntime(source, ctx);
            CollectionAssert.AreEqual(NeoCellPattern.EightNeighbors, first);
            Assert.AreSame(first, NeoCellPatternStorage.ReadRuntime(source, ctx), "An unchanged row reads as the same pattern.");
            Assert.IsTrue(client.TryGetValue(id, out ObjectMemberValue? row));
            var offsets = (ArrayMemberValue)client.ResolveClassChildRow(row!, "_offsets")!;
            // The offset row changes in place, so its instance stays the same.
            Assert.IsTrue(client.TryGetValue(offsets.value![2], out Vector2MemberValue? entry));
            entry!.value = NeoVectorValues.FromVector2Int(new Vector2Int(9, 8));
            client.SetWritableValue(NeoValueOwnership.Session, entry);
            var expected = new List<Vector2Int>(NeoCellPattern.EightNeighbors);
            expected[2] = new Vector2Int(9, 8);
            CollectionAssert.AreEqual(expected, NeoCellPatternStorage.ReadRuntime(source, ctx));
            CollectionAssert.AreEqual(NeoCellPattern.EightNeighbors, first, "A read pattern never changes.");
        }

        [Test]
        public void EmptyOffsets_ReplayTwoStoredPatternsWithoutSharedArrayCollision()
        {
            // Empty constructor lists used to hand every row the shared
            // Array.Empty singleton as its origin key, so the second empty
            // pattern's replay threw "Key already in the list".
            using NeoClient client = Client();
            var first = NeoCellPatternStorage.Serialize(client, new NeoCellPattern(Array.Empty<Vector2Int>()))!;
            var second = NeoCellPatternStorage.Serialize(client, new NeoCellPattern(Array.Empty<Vector2Int>()))!;
            Assert.AreNotEqual(first.valueId, second.valueId);
            var member = new ClassMember { id = "pattern-field", name = "Pattern", kind = MemberKind.Class, classId = NeoCellPatternStorage.ClassId };
            var firstNode = new NeoMemberClassWritable(client, member, first.valueId!, NeoValueOwnership.Session);
            var secondNode = new NeoMemberClassWritable(client, member, second.valueId!, NeoValueOwnership.Session);
            Assert.AreEqual(0, NeoCellPatternStorage.ReadRequired(client, firstNode).Count);
            Assert.AreEqual(0, NeoCellPatternStorage.ReadRequired(client, secondNode).Count);
            Assert.AreEqual(0, NeoCellPatternStorage.ReadRequired(client, firstNode).Count);
        }

        [Test]
        public void NativeReturns_NormalizePatternAndPatternListForOrdinaryCountGetter()
        {
            var patternType = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = NeoCellPatternStorage.ClassId };
            using var client = Client(data =>
            {
                data.members["native-pattern"] = new FunctionMember
                {
                    id = "native-pattern",
                    name = "Pattern",
                    kind = MemberKind.Function,
                    Modifier = NeoMemberModifierKind.Static,
                    returnTypeInfo = patternType,
                    argumentTypes = Array.Empty<FunctionArgumentTypeInfo>()
                };
                data.members["native-patterns"] = new FunctionMember
                {
                    id = "native-patterns",
                    name = "Patterns",
                    kind = MemberKind.Function,
                    Modifier = NeoMemberModifierKind.Static,
                    returnTypeInfo = new CollectionTypeInfo { type = MemberKind.List, required = true, entryTypeInfo = patternType },
                    argumentTypes = Array.Empty<FunctionArgumentTypeInfo>()
                };
                data.classes[NeoCellPatternStorage.ClassId].schema["NativePattern"] = "native-pattern";
                data.classes[NeoCellPatternStorage.ClassId].schema["NativePatterns"] = "native-patterns";
                data.members["native-nested"] = new FunctionMember
                {
                    id = "native-nested",
                    name = "Nested",
                    kind = MemberKind.Function,
                    Modifier = NeoMemberModifierKind.Static,
                    returnTypeInfo = new CollectionTypeInfo
                    {
                        type = MemberKind.List,
                        required = true,
                        entryTypeInfo = new CollectionTypeInfo { type = MemberKind.List, required = true, entryTypeInfo = patternType }
                    },
                    argumentTypes = Array.Empty<FunctionArgumentTypeInfo>()
                };
                data.classes[NeoCellPatternStorage.ClassId].schema["NativeNested"] = "native-nested";
            });
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["native-pattern"] = (_, _, _) => NeoCellPattern.Box(1),
                ["native-patterns"] = (_, _, _) => new[] { NeoCellPattern.Cross(1) },
                ["native-nested"] = (_, _, _) => new[] { new[] { NeoCellPattern.Ring(1) } },
            });
            var ctx = client.CreateGetterContext(NeoValueOwnership.Session);
            Assert.IsTrue(client.TryGetMember("system_51d883c9-d451-5747-bcf0-e314e44bffa8", out NSPropertyMember? count));
            object? value = Invoke("native-pattern");
            Assert.AreEqual(9, Convert.ToInt32(NSGetterEvaluator.Evaluate(count!.getter!, ctx.WithThis(value))));
            var values = (object?[])Invoke("native-patterns")!;
            Assert.AreEqual(5, Convert.ToInt32(NSGetterEvaluator.Evaluate(count.getter!, ctx.WithThis(values[0]))));
            var dictionary = (IReadOnlyDictionary<string, object?>)NeoCellPatternStorage.NormalizeNativeResult(
                new ReadOnlyPatternResult(), ctx, new CollectionTypeInfo { type = MemberKind.Dictionary, required = true, entryTypeInfo = patternType })!;
            Assert.AreEqual(9, Convert.ToInt32(NSGetterEvaluator.Evaluate(count.getter!, ctx.WithThis(dictionary["pattern"]))));
            var nested = (object?[])Invoke("native-nested")!;
            Assert.AreEqual(8, Convert.ToInt32(NSGetterEvaluator.Evaluate(count.getter!, ctx.WithThis(((object?[])nested[0]!)[0]))));

            object? Invoke(string memberId)
            {
                Assert.IsTrue(client.TryGetMember(memberId, out FunctionMember? function));
                return NSGetterEvaluator.InvokeNativeFunction(memberId, null, function!.returnTypeInfo, null, Array.Empty<object?>(), ctx);
            }
        }

        private sealed class ReadOnlyPatternResult : IReadOnlyDictionary<string, object?>
        {
            private readonly Dictionary<string, object?> values = new() { ["pattern"] = NeoCellPattern.Box(1) };
            public object? this[string key] => values[key];
            public IEnumerable<string> Keys => values.Keys;
            public IEnumerable<object?> Values => values.Values;
            public int Count => values.Count;
            public bool ContainsKey(string key) => values.ContainsKey(key);
            public bool TryGetValue(string key, out object? value) => values.TryGetValue(key, out value);
            public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => values.GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        [Test]
        public void ExcludingCodec_PreservesNativeOptionalAndGenericEnumValues()
        {
            using var client = Client();
            Assert.AreEqual(NeoCellPatternExcluding.Center, NeoCellPatternStorage.ReadOptionalExcluding(NeoCellPatternExcluding.Center));
            var member = new EnumMember { id = "excluding", name = "Excluding", kind = MemberKind.Enum, enumId = NeoCellPatternStorage.ExcludingEnumId };
            var codec = NeoGenericBindings.ResolveForMember<NeoCellPatternExcluding?>(client, member);
            Assert.AreEqual(NeoCellPatternStorage.CenterId, ((string[])codec.Serialize(NeoCellPatternExcluding.Center)!.value!)[0]);
        }

        [Test]
        public void NativeFactory_RejectsOversizedOutputBeforeAllocation()
        {
            using NeoClient client = Client();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            Assert.Throws<NSGetterRuntimeError>(() => NeoCellPatternRuntime.TryInvoke(
                "system_82222600-f67f-5127-bb2d-278381b4ef2f", null, new object?[] { int.MaxValue }, ctx, out _));
        }
    }
}
