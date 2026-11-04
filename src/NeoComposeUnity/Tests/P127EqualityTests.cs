// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NeoCompose.Runtime.NeoScript.Hashing;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public sealed class P127EqualityTests
    {
        private sealed class NativeClassView : NeoGeneratedClassValue
        {
            internal NativeClassView(NeoClient owner, NeoMemberClass node)
                : base(owner, node, node.ClassId, node is not NeoMemberClassWritable, node.ownership) { }
        }

        private NeoClient client = null!;
        private NSGetterEvaluator.Context ctx = null!;
        private NeoScriptScope scope = null!;
        [SetUp]
        public void SetUp()
        {
            client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/p127-overloads-parity-export.json")), assumeCurrentSchema: false);
            ctx = new NSGetterEvaluator.Context(client, null, null);
            scope = new NeoScriptScope();
        }
        [TearDown] public void TearDown() => client.Dispose();
        private static TypeInfo Type(MemberKind kind) => new PrimitiveTypeInfo { type = kind, required = true };
        private static TypeBinding Binding(MemberKind kind) => NeoTypeBindings.FromTypeInfo(Type(kind));
        private static Pointer Value(object? value) => new ValuePointer { type = PointerKind.Value, primitiveResolved = true, primitive = value };
        private object? Eval(Pointer value) => NSGetterEvaluator.EvaluatePointer(value, scope, ctx);
        private object? Query(string op, object? source, MemberKind kind, object? comparer = null) => Eval(new FunctionPointer
        {
            type = PointerKind.Function,
            function = new CollectionQueryFunction
            {
                type = FunctionKind.CollectionQuery,
                info = new FunctionCollectionQueryInfo
                {
                    op = op,
                    collectionPointer = Value(source),
                    equality = new EqualitySite { type = Type(kind), comparer = comparer is null ? null : Value(comparer) }
                }
            }
        });
        private NSGetterEvaluator.NeoBuiltInComparer Comparer(Func<object?[], object?> equals, Func<object?[], object?>? hash, MemberKind kind = MemberKind.Int)
        {
            NeoDelegateValue Native(string id, MemberKind result, int arity, Func<object?[], object?> invoke)
            {
                client.ProjectDataForRuntime.members[id] = new FunctionMember
                {
                    id = id,
                    name = id,
                    kind = MemberKind.Function,
                    Modifier = NeoMemberModifierKind.Static,
                    returnTypeInfo = Type(result),
                    argumentTypes = Enumerable.Range(0, arity).Select(i => new FunctionArgumentTypeInfo { name = "p" + i, type = kind, required = true }).ToArray()
                };
                return new NeoDelegateValue { memberId = id };
            }
            var e = Native("b6-equals", MemberKind.Bool, 2, equals);
            var h = hash is null ? null : Native("b6-hash", MemberKind.Int, 1, hash);
            var invokers = new Dictionary<string, NeoClient.NeoNativeFunctionInvoker> { [e.memberId!] = (_, _, args, _) => equals(args) };
            if (h is not null)
                invokers[h.memberId!] = (_, _, args, _) => hash!(args);
            client.RegisterNativeFunctionInvokers(invokers);
            return new NSGetterEvaluator.NeoBuiltInComparer(NeoBuiltInComparerKind.Create, NeoComparerFamilyKind.Equality, Binding(kind), h is null ? new[] { e } : new[] { e, h });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SetGetterPreservesComparerAcrossRepeatedReads(bool nested)
        {
            var comparer = Comparer(args => Convert.ToDouble(args[0]) % 10 == Convert.ToDouble(args[1]) % 10,
                args => Convert.ToDouble(args[0]) % 10);
            var setType = new CollectionTypeInfo { type = MemberKind.Set, required = true, entryTypeInfo = Type(MemberKind.Int) };
            CollectionTypeInfo resultType = nested
                ? new CollectionTypeInfo { type = MemberKind.List, required = true, entryTypeInfo = setType }
                : setType;
            Pointer set = new FunctionPointer
            {
                type = PointerKind.Function,
                function = new CollectionQueryFunction
                {
                    type = FunctionKind.CollectionQuery,
                    info = new FunctionCollectionQueryInfo
                    {
                        op = "ToSet",
                        collectionPointer = Value(new object?[] { 1d, 2d, 11d }),
                        equality = new EqualitySite { type = Type(MemberKind.Int), comparer = Value(comparer) },
                    },
                },
            };
            var property = new NSPropertyMember
            {
                id = "set-getter",
                name = "SetGetter",
                kind = MemberKind.NSProperty,
                returnTypeInfo = resultType,
                getter = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = Array.Empty<Variable>(),
                    typeInfo = resultType,
                    instructions = new Instruction[]
                    {
                        new ReturnInstruction
                        {
                            type = InstructionKind.Return,
                            pointer = nested
                                ? new ListLiteralPointer { type = PointerKind.ListLiteral, typeInfo = resultType, entries = new[] { set } }
                                : set,
                        },
                    },
                },
            };
            client.ProjectDataForRuntime.members[property.id] = property;
            client.ProjectDataForRuntime.classes[client.session.ClassId].schema[property.name] = property.id;
            client.InvalidateSchemaResolutionCaches();
            var call = new CallGetterPointer
            {
                type = PointerKind.CallGetter,
                memberId = property.id,
                receiver = CallReceiver.Instance(new ReferencePointer { type = PointerKind.Reference, valueId = client.session.value!.id }),
            };
            RuntimeSet Read()
            {
                object? result = Eval(call);
                return (RuntimeSet)(nested ? ((object?[])result!)[0] : result)!;
            }
            for (int read = 0; read < 3; read++)
            {
                RuntimeSet result = Read();
                Assert.That(result.Count, Is.EqualTo(2));
                Assert.That(result.Contains(11d), Is.True, $"Read {read} must keep the authored comparer.");
            }
        }

        [Test]
        public void DefaultFloatAndDecimalEqualityUseValueSemantics()
        {
            var floats = NSGetterEvaluator.SelectEquality(Binding(MemberKind.Float), ctx);
            Assert.That(floats.Equals(double.NaN, double.NaN, ctx), Is.True);
            var decimals = NSGetterEvaluator.SelectEquality(Binding(MemberKind.Decimal), ctx);
            Assert.That(decimals.Equals("1.10", "1.1", ctx), Is.True);
            Assert.That(decimals.Hash("1.10", ctx), Is.EqualTo(decimals.Hash("1.1", ctx)));
            var set = (RuntimeSet)Query("ToSet", new object?[] { double.NaN, double.NaN, -0d, 0d }, MemberKind.Float)!;
            Assert.That(set.Count, Is.EqualTo(2));
        }
        [Test]
        public void HashingUsesOneHashPerInsertionAndEntryFirstEquality()
        {
            var log = new List<string>();
            var comparer = Comparer(args => { log.Add($"E{args[0]},{args[1]}"); return Convert.ToDouble(args[0]) % 10 == Convert.ToDouble(args[1]) % 10; },
                args => { log.Add($"H{args[0]}"); return 0d; });
            var set = (RuntimeSet)Query("ToSet", new object?[] { 1d, 2d, 11d }, MemberKind.Int, comparer)!;
            Assert.That(set.Count, Is.EqualTo(2));
            Assert.That(log, Is.EqualTo(new[] { "H1", "H2", "E1,2", "H11", "E2,11", "E1,11" }));
            log.Clear();
            Assert.That(set.Contains(12d), Is.True);
            Assert.That(log, Is.EqualTo(new[] { "H12", "E2,12" }));
        }
        [TestCase(false)]
        [TestCase(true)]
        public void CopyingASetWithAnEqualComparerReusesItsHashes(bool sparse)
        {
            int hashes = 0, equalities = 0;
            var comparer = Comparer(args => { equalities++; return Equals(args[0], args[1]); }, args => { hashes++; return args[0]; });
            object?[] values = sparse ? Enumerable.Range(0, 30).Select(i => (object?)(double)i).ToArray() : new object?[] { 1d, 2d, 3d };
            var first = (RuntimeSet)Query("ToSet", values, MemberKind.Int, comparer)!;
            if (sparse)
            {
                for (int i = 0; i < 28; i++)
                    first.Remove((double)i);
                first.Add(0d, 0d);
            }
            hashes = equalities = 0;
            var copy = (RuntimeSet)Query("ToSet", first, MemberKind.Int, comparer)!;
            Assert.That(copy, Is.Not.SameAs(first));
            Assert.That(copy.Count, Is.EqualTo(3));
            Assert.That(copy.ToArray(), Is.EqualTo(sparse ? new object?[] { 28d, 29d, 0d } : values));
            Assert.That(hashes, Is.Zero);
            Assert.That(equalities, Is.Zero);
        }

        [Test]
        public void SetEqualityUsesTheSetIndexWhenComparersMatch()
        {
            int hashes = 0, equals = 0;
            var comparer = Comparer(args => { equals++; return Equals(args[0], args[1]); }, args => { hashes++; return args[0]; });
            var a = Query("ToSet", new object?[] { 1d, 2d, 3d }, MemberKind.Int, comparer);
            var b = Query("ToSet", new object?[] { 3d, 2d, 1d }, MemberKind.Int, comparer);
            var setComparer = new NSGetterEvaluator.NeoBuiltInComparer(NeoBuiltInComparerKind.SetEquality, NeoComparerFamilyKind.Equality, Binding(MemberKind.Int));
            hashes = equals = 0;
            Assert.That(new NSGetterEvaluator.BoundEquality(Binding(MemberKind.Object), setComparer, ctx).Equals(a, b), Is.True);
            Assert.That(hashes, Is.EqualTo(3));
            Assert.That(equals, Is.EqualTo(3));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SetEqualityWithDifferentComparersCallsTheRightElementFirst(bool asymmetric)
        {
            const string classId = "b6-set-equality-item";
            const string equalsId = "b6-set-equality-override";
            client.ProjectDataForRuntime.classes[classId] = new NeoSchemaClass
            {
                id = classId,
                name = "Item",
                schema = new() { ["Equals#" + NeoObjectContract.EqualsMember] = equalsId },
            };
            client.ProjectDataForRuntime.members[equalsId] = new FunctionMember
            {
                id = equalsId,
                name = "Equals",
                kind = MemberKind.Function,
                extendsMemberId = NeoObjectContract.EqualsMember,
                returnTypeInfo = Type(MemberKind.Bool),
                argumentTypes = new[] { new FunctionArgumentTypeInfo { name = "other", type = MemberKind.Object, required = false } },
            };
            client.InvalidateSchemaResolutionCaches();
            client.RegisterGeneratedClassFactories(
                new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
                {
                    [classId] = (c, n) => NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(c, n,
                        static (owner, node) => new NativeClassView(owner, node)),
                },
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>
                {
                    [classId] = (c, n) => NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(c, n,
                        static (owner, node) => new NativeClassView(owner, node)),
                });
            using var first = NeoGeneratedTypesSupport.CreateWritableClassValue(client, classId, new Dictionary<string, string>(), Array.Empty<MemberValue>());
            using var second = NeoGeneratedTypesSupport.CreateWritableClassValue(client, classId, new Dictionary<string, string>(), Array.Empty<MemberValue>());
            object? a = NSGetterEvaluator.UnwrapRow(first.value!, ctx, first.ownership, first.member);
            object? b = NSGetterEvaluator.UnwrapRow(second.value!, ctx, second.ownership, second.member);
            var log = new List<string>();
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                [equalsId] = (_, receiver, args, _) =>
                {
                    Assert.That(args[0], Is.InstanceOf<NativeClassView>());
                    int left = NSGetterEvaluator.ReferenceEqual(receiver, a, ctx) ? 1 : 2;
                    int right = NSGetterEvaluator.ReferenceEqual(args[0], a, ctx) ? 1 : 2;
                    log.Add($"{left}>{right}");
                    return asymmetric ? left >= right : left == right;
                },
            });
            var reference = new NSGetterEvaluator.NeoBuiltInComparer(NeoBuiltInComparerKind.ReferenceEquality, NeoComparerFamilyKind.Equality, null);
            object? x = Query("ToSet", asymmetric ? new[] { a } : new[] { a, b }, MemberKind.Object, reference);
            object? y = Query("ToSet", asymmetric ? new[] { b } : new[] { b, a }, MemberKind.Object);
            var comparer = new NSGetterEvaluator.NeoBuiltInComparer(NeoBuiltInComparerKind.SetEquality, NeoComparerFamilyKind.Equality, Binding(MemberKind.Object));
            log.Clear();
            Assert.That(new NSGetterEvaluator.BoundEquality(Binding(MemberKind.Object), comparer, ctx).Equals(x, y), Is.True);
            Assert.That(log, Is.EqualTo(asymmetric ? new[] { "2>1" } : new[] { "2>1", "2>2", "1>1" }));
        }

        [Test]
        public void SetEqualitySkipsSelfComparisonAndProbesTheExistingTable()
        {
            var log = new List<string>();
            var comparer = Comparer(args =>
            {
                log.Add($"{args[0]}={args[1]}");
                return Equals(args[0], args[1]);
            }, args =>
            {
                log.Add($"h{args[0]}");
                return args[0];
            });
            object? x = Query("ToSet", new object?[] { 1d, 2d }, MemberKind.Int, comparer);
            object? y = Query("ToSet", new object?[] { 2d, 1d }, MemberKind.Int, comparer);
            var setComparer = new NSGetterEvaluator.NeoBuiltInComparer(NeoBuiltInComparerKind.SetEquality, NeoComparerFamilyKind.Equality, Binding(MemberKind.Int));
            var equality = new NSGetterEvaluator.BoundEquality(Binding(MemberKind.Object), setComparer, ctx);
            log.Clear();
            Assert.That(equality.Equals(x, y), Is.True);
            Assert.That(log, Is.EqualTo(new[] { "h2", "2=2", "h1", "1=1" }));
            log.Clear();
            Assert.That(equality.Equals(x, x), Is.True);
            Assert.That(log, Is.Empty);
        }

        [Test]
        public void MissingHashDelegateThrowsTheContractError()
        {
            var comparer = Comparer(_ => true, null);
            var error = Assert.Throws<NeoRuntimeError>(() => Query("ToSet", new object?[] { 1d }, MemberKind.Int, comparer));
            Assert.That(error!.ErrorClass, Is.EqualTo(NeoErrorClass.NeoNotSupportedError));
            Assert.That(error.Message, Is.EqualTo("Specified method is not supported."));
        }
        [Test]
        public void EqualityDoesNotSkipTheComparerForIdenticalArguments()
        {
            int calls = 0;
            var comparer = Comparer(_ => { calls++; throw new NeoRuntimeError(NeoErrorClass.NeoArgumentError, "equals marker"); }, _ => 0d);
            var equality = new NSGetterEvaluator.BoundEquality(Binding(MemberKind.Int), comparer, ctx);
            Assert.Throws<NeoRuntimeError>(() => equality.Equals(1d, 1d));
            Assert.That(calls, Is.EqualTo(1));
        }
        [Test]
        public void LastIndexOfCallsEqualityBackwards()
        {
            var log = new List<double>();
            var comparer = Comparer(args => { log.Add(Convert.ToDouble(args[0])); return Convert.ToDouble(args[0]) == Convert.ToDouble(args[1]); }, _ => 0d);
            var result = Eval(new FunctionPointer
            {
                type = PointerKind.Function,
                function = new IndexOfFunction
                {
                    type = FunctionKind.IndexOf,
                    info = new FunctionCollectionContainsInfo
                    {
                        collectionPointer = Value(new object?[] { 1d, 2d, 3d, 2d, 4d }),
                        valuePointer = Value(2d),
                        last = true,
                        equality = new EqualitySite { type = Type(MemberKind.Int), comparer = Value(comparer) }
                    }
                }
            });
            Assert.That(result, Is.EqualTo(3d));
            Assert.That(log, Is.EqualTo(new[] { 4d, 2d }));
        }
        [Test]
        public void SetLiteralsCollapseEqualValuesAndSetComparersHaveValueEquality()
        {
            var value = Eval(new ListLiteralPointer
            {
                type = PointerKind.ListLiteral,
                typeInfo = new CollectionTypeInfo { type = MemberKind.Set, required = true, entryTypeInfo = Type(MemberKind.Float) },
                entries = new[] { Value(double.NaN), Value(double.NaN), Value(1d) }
            });
            Assert.That(((RuntimeSet)value!).Count, Is.EqualTo(2));
            var pointer = new CollectionComparerPointer { type = PointerKind.CollectionComparer, op = "CreateSetComparer", elementType = Type(MemberKind.Int) };
            var first = Eval(pointer);
            var second = Eval(pointer);
            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(NSGetterEvaluator.SelectEquality(Binding(MemberKind.Object), ctx).Equals(first, second, ctx), Is.True);
        }

        [Test]
        public void GroupByKeepsItsComparerWithoutRehashingProjection()
        {
            int hashes = 0;
            var comparer = Comparer(args => StringComparer.OrdinalIgnoreCase.Equals((string?)args[0], (string?)args[1]), _ => { hashes++; return 0d; }, MemberKind.String);
            var item = new VariablePointer { type = PointerKind.Variable, variableId = "item" };
            var groups = (RuntimeDictionary)Eval(new FunctionPointer
            {
                type = PointerKind.Function,
                function = new CollectionQueryFunction
                {
                    type = FunctionKind.CollectionQuery,
                    info = new FunctionCollectionQueryInfo
                    {
                        op = "GroupBy",
                        collectionPointer = Value(new object?[] { "A", "a", "B" }),
                        equality = new EqualitySite { type = Type(MemberKind.String), comparer = Value(comparer) },
                        function = new FunctionWithReturnType
                        {
                            compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                            typeInfo = Type(MemberKind.String),
                            parameters = new[] { new Variable { id = "item", typeInfo = Type(MemberKind.String), pointer = item } },
                            instructions = new Instruction[] { new ReturnInstruction { type = InstructionKind.Return, pointer = item } },
                        },
                    },
                }
            })!;
            Assert.That(hashes, Is.EqualTo(3));
            Assert.That(groups.Keys, Is.EqualTo(new[] { "A", "B" }));
            Assert.That(groups["a"], Is.EqualTo(new object?[] { "A", "a" }));
        }

        [Test]
        public void DefaultSelectionIsCachedPerType()
        {
            Assert.That(NSGetterEvaluator.SelectEquality(Binding(MemberKind.Int), ctx), Is.SameAs(NSGetterEvaluator.SelectEquality(Binding(MemberKind.Int), ctx)));
            Assert.That(NSGetterEvaluator.SelectComparison(Binding(MemberKind.Int), ctx), Is.SameAs(NSGetterEvaluator.SelectComparison(Binding(MemberKind.Int), ctx)));
        }
        [Test]
        public void DefaultComparisonOrdersNullAndRejectsNonComparableObjects()
        {
            var comparison = NSGetterEvaluator.SelectComparison(Binding(MemberKind.Object), ctx);
            var a = NeoBox.NewObject();
            Assert.That(comparison.Compare(null, a, ctx), Is.EqualTo(-1));
            Assert.That(comparison.Compare(a, a, ctx), Is.Zero);
            var error = Assert.Throws<NeoRuntimeError>(() => comparison.Compare(a, NeoBox.NewObject(), ctx));
            Assert.That(error!.Message, Is.EqualTo("At least one object must implement IComparable."));
        }
        [Test]
        public void TypedSortUsesFloatNaNAndRangeRules()
        {
            object?[] input = { 100d, 3d, double.NaN, 1d, 200d };
            var sorted = NSGetterEvaluator.SortCollection(input, new CollectionCallInstruction
            {
                ordering = new OrderingSite { type = Type(MemberKind.Float) },
                index = Value(1d),
                count = Value(3d)
            }, scope, ctx);
            Assert.That(sorted[0], Is.EqualTo(100d));
            Assert.That(sorted[4], Is.EqualTo(200d));
            Assert.That(double.IsNaN(Convert.ToDouble(sorted[1])), Is.True);
            Assert.That(sorted[2], Is.EqualTo(1d));
            Assert.That(sorted[3], Is.EqualTo(3d));
        }
        [Test]
        public void TypedSortHandlesNullEntriesBeforeCallingCompareTo()
        {
            var sorted = NSGetterEvaluator.SortCollection(new object?[] { "c", null, "a", null, "b" }, new CollectionCallInstruction
            {
                ordering = new OrderingSite { type = Type(MemberKind.String) }
            }, scope, ctx);
            Assert.That(sorted, Is.EqualTo(new object?[] { null, null, "a", "b", "c" }));
        }
        [Test]
        public void MinMaxUsesReferenceAndValueEmptyRules()
        {
            object? Extreme(MemberKind kind, object?[] source) => Eval(new FunctionPointer
            {
                type = PointerKind.Function,
                function = new CollectionQueryFunction
                {
                    type = FunctionKind.CollectionQuery,
                    info = new FunctionCollectionQueryInfo { op = "Min", collectionPointer = Value(source), ordering = new OrderingSite { type = Type(kind) } }
                }
            });
            Assert.That(Extreme(MemberKind.String, Array.Empty<object?>()), Is.Null);
            Assert.That(Extreme(MemberKind.String, new object?[] { null, "z", "a", null }), Is.EqualTo("a"));
            var error = Assert.Throws<NeoRuntimeError>(() => Extreme(MemberKind.Int, Array.Empty<object?>()));
            Assert.That(error!.Message, Is.EqualTo("Sequence contains no elements"));
            Assert.That(double.IsNaN(Convert.ToDouble(Extreme(MemberKind.Float, new object?[] { 2d, double.NaN, 1d }))), Is.True);
        }
        [Test]
        public void StoredIndexRetainsFirstEqualEntryAndCachesHashes()
        {
            var log = new List<string>();
            var comparer = Comparer(args => { log.Add("E"); return Convert.ToDouble(args[0]) % 10 == Convert.ToDouble(args[1]) % 10; }, args => { log.Add("H"); return 0d; });
            var data = client.ProjectDataForRuntime;
            string interfaceId = NeoPrimitiveConformance.InterfaceId("IEqualityComparer`1");
            data.classes["b6-stored-comparer"] = new NeoSchemaClass
            {
                id = "b6-stored-comparer",
                name = "StoredComparer",
                schema = new() { ["Equals"] = "b6-equals", ["GetHashCode"] = "b6-hash" },
                implementsInterfaces = new List<InterfaceConstruction>
                {
                    new() { interfaceId = interfaceId, arguments = new() { [data.interfaces[interfaceId].genericParams![0].id] = Binding(MemberKind.Int) } },
                },
            };
            foreach (string id in new[] { "b6-equals", "b6-hash" })
            {
                var member = (FunctionMember)data.members[id];
                member.name = id == "b6-equals" ? "Equals" : "GetHashCode";
                member.Modifier = NeoMemberModifierKind.Sealed;
            }
            var write = new NeoWritePlan(client);
            write.Set(NeoValueOwnership.Session, new ObjectMemberValue { id = "b6-comparer-value", classId = "b6-stored-comparer", value = new() });
            write.Commit();
            var row = new ArrayMemberValue { id = "b6-set", value = new[] { "a", "b" }, comparer = new NeoCollectionComparer { kind = NeoCollectionComparerKind.Value, valueId = "b6-comparer-value" } };
            var index = NSGetterEvaluator.GetStoredEqualityIndex(row, Binding(MemberKind.Int), NeoValueOwnership.Session, ctx, row.value, id => id == "a" ? 1d : 11d);
            Assert.That(log, Is.EqualTo(new[] { "H", "H" }));
            log.Clear();
            Assert.That(index.Find(21d), Is.EqualTo("a"));
            Assert.That(log, Is.EqualTo(new[] { "H", "E" }));
            Assert.That(NSGetterEvaluator.GetStoredEqualityIndex(row, Binding(MemberKind.Int), NeoValueOwnership.Session, ctx, row.value, _ => throw new Exception("must not rebuild")), Is.SameAs(index));
            NSGetterEvaluator.InvalidateStoredEquality(client, NeoValueOwnership.Session, row.id);
            Assert.That(index.Valid, Is.False);
        }

        [Test]
        public void NativeMethodGroupRetainsItsClosedTypeArguments()
        {
            const string id = "b6-generic-native";
            client.ProjectDataForRuntime.members[id] = new FunctionMember
            {
                id = id,
                name = "Kind",
                kind = MemberKind.Function,
                Modifier = NeoMemberModifierKind.Static,
                genericParams = new[] { new FunctionGenericParam { name = "T" } },
                argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                returnTypeInfo = Type(MemberKind.String),
            };
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                [id] = (_, _, _, arguments) => arguments![0].type.ToString(),
            });
            NeoDelegateValue Target(MemberKind kind) => (NeoDelegateValue)Eval(new MemberTargetPointer
            {
                type = PointerKind.MemberTarget,
                memberId = id,
                receiver = new CallReceiver { kind = CallReceiverKind.Static, memberId = id },
                typeArguments = new[] { Type(kind) },
                delegateType = new DelegateTypeInfo
                {
                    type = MemberKind.NSDelegate,
                    required = true,
                    returnTypeInfo = Type(MemberKind.String),
                    argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                },
            })!;
            var first = Target(MemberKind.Int);
            var same = Target(MemberKind.Int);
            var other = Target(MemberKind.String);
            Assert.That(NSGetterEvaluator.DelegateEquals(first, same, ctx), Is.True);
            Assert.That(NSGetterEvaluator.DelegateHash(first, ctx), Is.EqualTo(NSGetterEvaluator.DelegateHash(same, ctx)));
            Assert.That(NSGetterEvaluator.DelegateEquals(first, other, ctx), Is.False);
            Assert.That(NSGetterEvaluator.InvokeDelegate(first, Array.Empty<object?>(), ctx), Is.EqualTo("Int"));
            Assert.That(NSGetterEvaluator.InvokeDelegate(other, Array.Empty<object?>(), ctx), Is.EqualTo("String"));
        }

        [Test]
        public void ClosureEqualityUsesEnvironmentIdentityAndCode()
        {
            object environment = NeoBox.NewObject();
            NeoDelegateValue Closure(object target, string code = "body") => new()
            {
                site = "site",
                code = code,
                placement = "environment",
                identityTarget = target,
                identityTargetBound = true,
                action = new FunctionWithReturnType { instructions = Array.Empty<Instruction>() }
            };
            var a = Closure(environment);
            var b = Closure(environment);
            Assert.That(NSGetterEvaluator.DelegateEquals(a, b, ctx), Is.True);
            Assert.That(NSGetterEvaluator.DelegateEquals(a, Closure(NeoBox.NewObject()), ctx), Is.False);
            Assert.That(NSGetterEvaluator.DelegateEquals(a, Closure(environment, "edited"), ctx), Is.False);
        }
        [TestCase("ToSet")]
        [TestCase("Distinct")]
        public void BoxedValueQueriesUseHeldTypeAndPayloadEquality(string operation)
        {
            var tupleType = new TupleTypeInfo
            {
                type = MemberKind.Tuple,
                required = true,
                elements = new[] { new TupleElementTypeInfo { typeInfo = Type(MemberKind.Int) }, new TupleElementTypeInfo { typeInfo = Type(MemberKind.String) } },
            };
            var pairs = new (TypeBinding Type, object Left, object Right)[]
            {
                (Binding(MemberKind.Int), 3d, 3d),
                (Binding(MemberKind.Decimal), "1.10", "1.1"),
                (Binding(MemberKind.Float), double.NaN, double.NaN),
                (Binding(MemberKind.Float), -0d, 0d),
                (Binding(MemberKind.String), "same", "same"),
                (NeoTypeBindings.FromTypeInfo(tupleType), new RuntimeTuple(new object?[] { 3d, "x" }, tupleType), new RuntimeTuple(new object?[] { 3d, "x" }, tupleType)),
            };
            foreach (var pair in pairs)
            {
                object a = NeoBox.Of(pair.Type, pair.Left), b = NeoBox.Of(pair.Type, pair.Right);
                Assert.That(a, Is.Not.SameAs(b));
                var values = (System.Collections.IEnumerable)Query(operation, new[] { a, b }, MemberKind.Object)!;
                Assert.That(values.Cast<object?>().Count(), Is.EqualTo(1), pair.Type.ToString());
            }
            var differentTypes = (System.Collections.IEnumerable)Query(operation, new[] { NeoBox.Of(Binding(MemberKind.Int), 3d), NeoBox.Of(Binding(MemberKind.Float), 3d) }, MemberKind.Object)!;
            Assert.That(differentTypes.Cast<object?>().Count(), Is.EqualTo(2));
        }

        [TestCase("ToSet")]
        [TestCase("Distinct")]
        public void ObjectQueriesRetainReferenceIdentityAndUseComparerEquality(string operation)
        {
            var listType = NeoTypeBindings.FromTypeInfo(new CollectionTypeInfo { type = MemberKind.List, required = true, entryTypeInfo = Type(MemberKind.Int) });
            object?[] payload = { 1d };
            object a = NeoBox.Of(listType, payload), same = NeoBox.Of(listType, payload), other = NeoBox.Of(listType, new object?[] { 1d });
            Assert.That(same, Is.SameAs(a));
            var values = (System.Collections.IEnumerable)Query(operation, new[] { a, same, other }, MemberKind.Object)!;
            Assert.That(values.Cast<object?>().Count(), Is.EqualTo(2));
            var first = Comparer(args => Equals(args[0], args[1]), _ => 0d);
            var second = new NSGetterEvaluator.NeoBuiltInComparer(NeoBuiltInComparerKind.Create, NeoComparerFamilyKind.Equality, Binding(MemberKind.Int), first.Delegates);
            values = (System.Collections.IEnumerable)Query(operation, new[] { first, second }, MemberKind.Object)!;
            Assert.That(values.Cast<object?>().Count(), Is.EqualTo(1));
        }

        [Test]
        public void ObjectAndValueTypeAlwaysSelectTheComparerPath()
        {
            Assert.That(NSGetterEvaluator.SelectEquality(Binding(MemberKind.Object), ctx).KeyEquivalent, Is.False);
            var valueType = NeoTypeBindings.FromTypeInfo(new ObjectTypeInfo { type = MemberKind.Object, required = true, objectType = NeoObjectTypeKind.ValueType });
            Assert.That(NSGetterEvaluator.SelectEquality(valueType, ctx).KeyEquivalent, Is.False);
            var vector = Binding(MemberKind.Vector2);
            object a = NeoBox.Of(vector, new NeoVector2Value { x = double.NaN, y = 0 });
            object b = NeoBox.Of(vector, new NeoVector2Value { x = double.NaN, y = 0 });
            var set = (RuntimeSet)Query("ToSet", new[] { a, b }, MemberKind.Object)!;
            Assert.That(set.Count, Is.EqualTo(2), "NaN vector equality cannot be replaced by its payload key.");
        }
    }
}
