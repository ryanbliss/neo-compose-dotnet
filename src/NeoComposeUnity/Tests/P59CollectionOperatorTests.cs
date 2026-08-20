// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.Profiling;

namespace NeoCompose.Tests
{
    public class P59CollectionOperatorTests
    {
        private const int MeasurementCount = 5;
        private static readonly int[] SourceCounts =
            { 0, 10, 1_000, 10_000, 100_000 };

        [Test]
        public void ResultCapacity_UsesRemainingP54OutputBudget()
        {
            var tracker = new NeoScriptAllocationTracker(
                new NeoScriptExecutionBudgetLimits(
                    producedCollectionEntries: 3));
            Assert.AreEqual(3, tracker.SafeResultCapacity(100));
            tracker.ConsumeProducedCollectionEntry(2);
            Assert.AreEqual(1, tracker.SafeResultCapacity(100));
            tracker.ConsumeProducedCollectionEntry();
            Assert.AreEqual(0, tracker.SafeResultCapacity(100));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void EmptyCollection_ReturnsEmptyResult(
            bool dictionary,
            bool select)
        {
            object? result = Evaluate(
                BuildOperator(
                    Collection(dictionary),
                    select ? CallbackKind.Select : CallbackKind.MatchAll));

            Assert.AreEqual(0, ResultCount(result));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SparseWhere_ContainsOnlyMatchesAndNoDefaultEntries(
            bool dictionary)
        {
            Pointer source = dictionary
                ? Collection(
                    true,
                    ("first", "drop"),
                    ("second", "keep"),
                    ("third", "drop"))
                : Collection(
                    false,
                    ("0", "drop"),
                    ("1", "keep"),
                    ("2", "drop"));

            object? result = Evaluate(
                BuildOperator(source, CallbackKind.MatchKeep));

            if (dictionary)
            {
                var filtered = (IDictionary<string, object?>)result!;
                CollectionAssert.AreEqual(new[] { "second" }, filtered.Keys);
                CollectionAssert.AreEqual(new object?[] { "keep" }, filtered.Values);
            }
            else
            {
                CollectionAssert.AreEqual(
                    new object?[] { "keep" },
                    (object?[])result!);
            }
        }

        [Test]
        public void Where_ReemitsStoredValueIdsInSourceOrder()
        {
            NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);

            var list = (object?[])Evaluate(
                BuildOperator(Reference("v-path"), CallbackKind.MatchAll),
                ctx)!;
            CollectionAssert.AreEqual(new object?[] { "v-position" }, list);

            var dictionary = (IDictionary<string, object?>)Evaluate(
                BuildOperator(Reference("v-dict"), CallbackKind.MatchAll),
                ctx)!;
            CollectionAssert.AreEqual(
                new[] { "Name", "Health", "Position", "GridCell", "Path" },
                dictionary.Keys);
            CollectionAssert.AreEqual(
                new object?[]
                {
                    "v-name",
                    "v-level",
                    "v-position",
                    "v-grid-cell",
                    "v-path",
                },
                dictionary.Values);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Select_PreservesListAndDictionaryValueOrder(bool dictionary)
        {
            Pointer source = dictionary
                ? Collection(
                    true,
                    ("10", "ten"),
                    ("2", "two"),
                    ("alpha", "alpha"))
                : Collection(
                    false,
                    ("0", "ten"),
                    ("1", "two"),
                    ("2", "alpha"));

            object? result = Evaluate(
                BuildOperator(source, CallbackKind.Select));

            CollectionAssert.AreEqual(
                dictionary
                    ? new object?[] { "two", "ten", "alpha" }
                    : new object?[] { "ten", "two", "alpha" },
                (object?[])result!);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Count_WithoutPredicateReturnsCollectionLength(bool dictionary)
        {
            Pointer source = Collection(
                dictionary,
                ("0", "drop"),
                ("1", "keep"),
                ("2", "keep"));

            Assert.AreEqual(3, Evaluate(Count(source)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Count_WithPredicateCountsOnlyMatches(bool dictionary)
        {
            Pointer source = Collection(
                dictionary,
                ("0", "drop"),
                ("1", "keep"),
                ("2", "keep"));

            Assert.AreEqual(
                2,
                Evaluate(Count(source, Callback(CallbackKind.MatchKeep))));
        }

        [Test]
        public void IndexOf_ReturnsFirstMatchingIndexOrMinusOne()
        {
            Pointer source = Collection(
                false,
                ("0", "drop"),
                ("1", "keep"),
                ("2", "keep"));

            Assert.AreEqual(1, Evaluate(IndexOf(source, Text("keep"))));
            Assert.AreEqual(-1, Evaluate(IndexOf(source, Text("missing"))));
        }

        [Test]
        public void IndexOf_RejectsADictionaryReceiver()
        {
            Pointer source = Collection(true, ("entry", "keep"));

            var error = Assert.Throws<NSGetterRuntimeError>(() =>
                Evaluate(IndexOf(source, Text("keep"))));
            Assert.AreEqual("IndexOf receiver must be a List value.", error!.Message);
        }

        [Test]
        public void Json_CountPredicateAndIndexOfRoundTripThroughFunctionConverter()
        {
            Function count = JsonConvert.DeserializeObject<Function>(@"{
                'type':'count',
                'info':{
                    'collectionPointer':{'type':'variable','variableId':'items'},
                    'function':{
                        'parameters':[],
                        'instructions':[],
                        'typeInfo':{'type':1,'required':true}
                    }
                }
            }")!;
            Function indexOf = JsonConvert.DeserializeObject<Function>(@"{
                'type':'indexOf',
                'info':{
                    'collectionPointer':{'type':'variable','variableId':'items'},
                    'valuePointer':{'type':'variable','variableId':'target'}
                }
            }")!;

            Assert.IsInstanceOf<CountFunction>(count);
            Assert.IsNotNull(((CountFunction)count).info.function);
            Assert.IsInstanceOf<IndexOfFunction>(indexOf);
            StringAssert.Contains(
                "\"type\":\"indexOf\"",
                JsonConvert.SerializeObject(indexOf));
        }

        [Test]
        public void RevisionGateRequiresThirteenForIndexOfAndPredicateCountOnly()
        {
            Pointer source = Collection(false, ("0", "keep"));
            Assert.DoesNotThrow(() => EvaluateBody(Count(source), 12));

            var countError = Assert.Throws<NeoScriptPreExecutionValidationError>(
                () => EvaluateBody(
                    Count(source, Callback(CallbackKind.MatchKeep)),
                    12));
            StringAssert.Contains(
                "predicate-Count IR requires compiler revision 13",
                countError!.Message);

            var indexError = Assert.Throws<NeoScriptPreExecutionValidationError>(
                () => EvaluateBody(IndexOf(source, Text("keep")), 12));
            StringAssert.Contains(
                "IndexOf IR requires compiler revision 13",
                indexError!.Message);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ThrowingCallback_PreservesError(
            bool dictionary,
            bool select)
        {
            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                Evaluate(
                    BuildOperator(
                        Collection(dictionary, ("entry", "value")),
                        select ? CallbackKind.ThrowSelect : CallbackKind.Throw)))!;

            Assert.AreEqual("p59 callback failure", error.Message);
        }

        [Test, Explicit("P59 allocation and elapsed-time benchmark")]
        public void CollectionOperators_BenchmarkSourceDerivedCapacity()
        {
            foreach (int sourceCount in SourceCounts)
            {
                Benchmark(sourceCount, false, CallbackKind.MatchAll, "where-list-all");
                Benchmark(sourceCount, true, CallbackKind.MatchAll, "where-dictionary-all");
                Benchmark(sourceCount, false, CallbackKind.MatchNone, "where-list-sparse");
                Benchmark(sourceCount, true, CallbackKind.MatchNone, "where-dictionary-sparse");
                Benchmark(sourceCount, false, CallbackKind.Select, "select-list");
                Benchmark(sourceCount, true, CallbackKind.Select, "select-dictionary");
            }
        }

        private static void Benchmark(
            int sourceCount,
            bool dictionary,
            CallbackKind callbackKind,
            string scenario)
        {
            object source = BenchmarkCollection(dictionary, sourceCount);
            Pointer pointer = BuildOperator(
                Variable("__context__"),
                callbackKind);
            var getter = new FunctionWithReturnType
            {
                parameters = Array.Empty<Variable>(),
                typeInfo = dictionary
                    && callbackKind != CallbackKind.Select
                    && callbackKind != CallbackKind.ThrowSelect
                    ? DictionaryType()
                    : ListType(),
                instructions = new Instruction[]
                {
                    Return(pointer),
                },
            };
            NeoClient client = BuildClient();

            MeasureOnce(getter, client, source);
            MeasureOnce(getter, client, source);

            Measurement[] measurements = Enumerable.Range(0, MeasurementCount)
                .Select(_ => MeasureOnce(getter, client, source))
                .ToArray();
            bool expectedLimit = sourceCount >
                NeoScriptExecutionBudgetLimits.DefaultProducedCollectionEntries;
            Assert.IsTrue(
                measurements.All(measurement =>
                    measurement.HitResourceLimit == expectedLimit),
                $"Unexpected P54 safety-limit outcome for {scenario} at {sourceCount} entries.");
            long allocatedBytes = Median(
                measurements.Select(measurement => measurement.AllocatedBytes)
                    .ToArray());
            double durationMs = Median(
                measurements.Select(measurement => measurement.DurationMs)
                    .ToArray());
            string outcome = expectedLimit ? "resource-limit" : "completed";

            TestContext.WriteLine(
                $"scenario={scenario} sourceCount={sourceCount} " +
                $"outcome={outcome} allocatedBytes={allocatedBytes} " +
                $"medianDurationMs={durationMs:F3}");
        }

        private static Measurement MeasureOnce(
            FunctionWithReturnType getter,
            NeoClient client,
            object source)
        {
            using var recorder = new ProfilerRecorder(
                ProfilerCategory.Memory,
                "GC Allocated In Frame",
                1,
                ProfilerRecorderOptions.WrapAroundWhenCapacityReached
                    | ProfilerRecorderOptions.SumAllSamplesInFrame);
            Assert.IsTrue(recorder.Valid, "Unity GC allocation counter is unavailable.");
            Assert.AreEqual(
                ProfilerMarkerDataUnit.Bytes,
                recorder.UnitType,
                "Unity GC allocation counter did not report byte values.");

            var ctx = new NSGetterEvaluator.Context(client, null, null, source);
            recorder.Start();
            long allocatedBefore = recorder.CurrentValue;
            var stopwatch = Stopwatch.StartNew();
            object? result = null;
            bool hitResourceLimit = false;
            try
            {
                result = NSGetterEvaluator.Evaluate(getter, ctx);
            }
            catch (NeoScriptResourceLimitError)
            {
                hitResourceLimit = true;
            }
            stopwatch.Stop();
            long allocatedAfter = recorder.CurrentValue;
            recorder.Stop();

            GC.KeepAlive(result);
            return new Measurement(
                allocatedAfter - allocatedBefore,
                stopwatch.Elapsed.TotalMilliseconds,
                hitResourceLimit);
        }

        private static object BenchmarkCollection(bool dictionary, int count)
        {
            if (!dictionary)
            {
                return Enumerable.Repeat<object?>("entry", count)
                    .ToArray();
            }

            var entries = new Dictionary<string, object?>(count);
            for (int index = 0; index < count; index++)
            {
                entries[index.ToString(CultureInfo.InvariantCulture)] = "entry";
            }
            return entries;
        }

        private static object? Evaluate(
            Pointer pointer,
            NSGetterEvaluator.Context? context = null)
        {
            context ??= new NSGetterEvaluator.Context(BuildClient(), null, null);
            return NSGetterEvaluator.EvaluatePointer(
                pointer,
                new Dictionary<string, object?>(),
                context);
        }

        private static object? EvaluateBody(Pointer pointer, int compilerRevision)
        {
            return NSGetterEvaluator.Evaluate(
                new FunctionWithReturnType
                {
                    compilerRevision = compilerRevision,
                    parameters = Array.Empty<Variable>(),
                    typeInfo = IntType(),
                    instructions = new Instruction[] { Return(pointer) },
                },
                new NSGetterEvaluator.Context(BuildClient(), null, null));
        }

        private static Pointer BuildOperator(
            Pointer collection,
            CallbackKind callbackKind)
        {
            FunctionWithReturnType callback = Callback(callbackKind);
            Function function = callbackKind is CallbackKind.Select
                or CallbackKind.ThrowSelect
                ? new SelectFunction
                {
                    type = FunctionKind.Select,
                    info = new FunctionCollectionSelectInfo
                    {
                        collectionPointer = collection,
                        function = callback,
                    },
                }
                : new WhereFunction
                {
                    type = FunctionKind.Where,
                    info = new FunctionCollectionBoolInfo
                    {
                        collectionPointer = collection,
                        function = callback,
                    },
                };
            return new FunctionPointer
            {
                type = PointerKind.Function,
                function = function,
            };
        }

        private static Pointer Count(
            Pointer collection,
            FunctionWithReturnType? predicate = null)
        {
            return new FunctionPointer
            {
                type = PointerKind.Function,
                function = new CountFunction
                {
                    type = FunctionKind.Count,
                    info = new FunctionCollectionOptionalBoolInfo
                    {
                        collectionPointer = collection,
                        function = predicate,
                    },
                },
            };
        }

        private static Pointer IndexOf(Pointer collection, Pointer value)
        {
            return new FunctionPointer
            {
                type = PointerKind.Function,
                function = new IndexOfFunction
                {
                    type = FunctionKind.IndexOf,
                    info = new FunctionCollectionContainsInfo
                    {
                        collectionPointer = collection,
                        valuePointer = value,
                    },
                },
            };
        }

        private static FunctionWithReturnType Callback(CallbackKind kind)
        {
            var entry = new Variable
            {
                id = "entry",
                typeInfo = StringType(),
                pointer = Variable("entry"),
            };
            Instruction[] instructions = kind switch
            {
                CallbackKind.Select => new Instruction[]
                {
                    Return(entry.pointer),
                },
                CallbackKind.MatchAll => new Instruction[]
                {
                    Return(Boolean(true)),
                },
                CallbackKind.MatchNone => new Instruction[]
                {
                    Return(Boolean(false)),
                },
                CallbackKind.MatchKeep => new Instruction[]
                {
                    Return(Equal(entry.pointer, Text("keep"))),
                },
                CallbackKind.Throw => new Instruction[]
                {
                    new ThrowInstruction
                    {
                        type = InstructionKind.Throw,
                        pointer = Text("p59 callback failure"),
                    },
                },
                CallbackKind.ThrowSelect => new Instruction[]
                {
                    new ThrowInstruction
                    {
                        type = InstructionKind.Throw,
                        pointer = Text("p59 callback failure"),
                    },
                },
                _ => throw new AssertionException(
                    $"Unknown callback kind '{kind}'."),
            };
            return new FunctionWithReturnType
            {
                parameters = new[] { entry },
                typeInfo = kind is CallbackKind.Select
                    or CallbackKind.ThrowSelect
                    ? StringType()
                    : BoolType(),
                instructions = instructions,
            };
        }

        private static PrimitiveTypeInfo IntType() => new()
        {
            type = MemberKind.Int,
            required = true,
        };

        private static Pointer Collection(
            bool dictionary,
            params (string Key, string Value)[] entries)
        {
            if (!dictionary)
            {
                return new ListLiteralPointer
                {
                    type = PointerKind.ListLiteral,
                    typeInfo = ListType(),
                    entries = entries.Select(entry => Text(entry.Value)).ToArray(),
                };
            }
            return new DictLiteralPointer
            {
                type = PointerKind.DictLiteral,
                typeInfo = DictionaryType(),
                entries = entries.Select(entry => new DictLiteralPair
                {
                    key = Text(entry.Key),
                    value = Text(entry.Value),
                }).ToArray(),
            };
        }

        private static int ResultCount(object? result) => result switch
        {
            object?[] list => list.Length,
            IDictionary<string, object?> dictionary => dictionary.Count,
            _ => throw new AssertionException(
                $"Unexpected result type '{result?.GetType().Name ?? "null"}'."),
        };

        private static ReturnInstruction Return(Pointer pointer) => new()
        {
            type = InstructionKind.Return,
            pointer = pointer,
        };

        private static OperationPointer Equal(Pointer left, Pointer right) => new()
        {
            type = PointerKind.Operation,
            operation = new BooleanOperation
            {
                type = OperationKind.Boolean,
                expression = new BooleanExpression
                {
                    condition = new Condition
                    {
                        type = OperatorKind.EqualTo,
                        operand1 = left,
                        operand2 = right,
                    },
                },
            },
        };

        private static VariablePointer Variable(string id) => new()
        {
            type = PointerKind.Variable,
            variableId = id,
        };

        private static ReferencePointer Reference(string valueId) => new()
        {
            type = PointerKind.Reference,
            valueId = valueId,
        };

        private static ValuePointer Text(string value) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = StringType(),
                value = JToken.FromObject(value),
            },
        };

        private static ValuePointer Boolean(bool value) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = BoolType(),
                value = JToken.FromObject(value),
            },
        };

        private static PrimitiveTypeInfo StringType() => new()
        {
            type = MemberKind.String,
            required = true,
        };

        private static PrimitiveTypeInfo BoolType() => new()
        {
            type = MemberKind.Bool,
            required = true,
        };

        private static CollectionTypeInfo ListType() => new()
        {
            type = MemberKind.List,
            required = true,
            entryTypeInfo = StringType(),
        };

        private static CollectionTypeInfo DictionaryType() => new()
        {
            type = MemberKind.Dictionary,
            required = true,
            entryTypeInfo = StringType(),
        };

        private static NeoClient BuildClient()
        {
            const string packageRoot =
                "Packages/com.ryanbliss.neocompose/Tests";
            return NeoTestSaveStack.LoadClient(
                File.ReadAllText(Path.Combine(packageRoot, "synth-example.json")));
        }

        private static long Median(long[] values)
        {
            Array.Sort(values);
            return values[values.Length / 2];
        }

        private static double Median(double[] values)
        {
            Array.Sort(values);
            return values[values.Length / 2];
        }

        private enum CallbackKind
        {
            MatchAll,
            MatchNone,
            MatchKeep,
            Select,
            Throw,
            ThrowSelect,
        }

        private readonly struct Measurement
        {
            internal Measurement(
                long allocatedBytes,
                double durationMs,
                bool hitResourceLimit)
            {
                AllocatedBytes = allocatedBytes;
                DurationMs = durationMs;
                HitResourceLimit = hitResourceLimit;
            }

            internal long AllocatedBytes { get; }
            internal double DurationMs { get; }
            internal bool HitResourceLimit { get; }
        }
    }
}
