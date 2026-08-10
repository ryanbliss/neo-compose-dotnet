// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class P58CallbackPerformanceTests
    {
        private const int EntryCount = 10_000;
        private const int MeasurementCount = 30;

        [Test]
        public void PreparedCallback_ValidatesAndBuildsBindingPlanOnce()
        {
            CallbackProfile profile = BuildProfile(dictionary: false);
            var metrics = new CollectionCallbackPreparationMetrics();
            var ctx = new NSGetterEvaluator.Context(
                profile.Client,
                null,
                null)
            {
                collectionCallbackPreparationMetrics = metrics,
            };

            object? result = NSGetterEvaluator.Evaluate(profile.Getter, ctx);

            Assert.AreEqual(0, ((object?[])result!).Length);
            Assert.AreEqual(1, metrics.BodyValidations);
            Assert.AreEqual(1, metrics.BindingPlanCreations);
        }

        [Test]
        public void PreparedCallback_PreservesListAndDictionaryBindings()
        {
            NeoClient client = LoadClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);

            CollectionAssert.AreEqual(
                new object?[] { "first", "second" },
                (object?[])NSGetterEvaluator.Evaluate(
                    ListEntryProjection(),
                    ctx)!);
            CollectionAssert.AreEqual(
                new object?[] { "first", "second" },
                (object?[])NSGetterEvaluator.Evaluate(
                    DictionaryProjection(selectKey: true),
                    new NSGetterEvaluator.Context(client, null, null))!);
            CollectionAssert.AreEqual(
                new object?[] { true, false },
                (object?[])NSGetterEvaluator.Evaluate(
                    DictionaryProjection(selectKey: false),
                    new NSGetterEvaluator.Context(client, null, null))!);
        }

        [Test]
        public void PreparedCallback_RejectsCorruptMetadataBeforeCallbackBody()
        {
            CallbackProfile profile = BuildProfile(dictionary: false);
            FunctionWithReturnType callback = WhereCallback(profile.Getter);
            callback.compilerRevision =
                FunctionWithReturnType.CurrentCompilerRevision + 1;
            callback.instructions = new Instruction[]
            {
                new ThrowInstruction
                {
                    type = InstructionKind.Throw,
                    pointer = StringValue("callback body executed"),
                },
            };

            NeoScriptPreExecutionValidationError error = Assert.Throws<
                NeoScriptPreExecutionValidationError>(() =>
                    NSGetterEvaluator.Evaluate(
                        profile.Getter,
                        new NSGetterEvaluator.Context(
                            profile.Client,
                            null,
                            null)))!;

            StringAssert.Contains("Unsupported NeoScript compiler revision", error.Message);
            StringAssert.DoesNotContain("callback body executed", error.Message);
        }

        [Test]
        public void PreparedCallback_ValidatesActualReturnAtProducingEntry()
        {
            CallbackProfile profile = BuildProfile(dictionary: false);
            FunctionWithReturnType callback = WhereCallback(profile.Getter);
            callback.instructions = new Instruction[]
            {
                new ReturnInstruction
                {
                    type = InstructionKind.Return,
                    pointer = VariableValue(callback.parameters[0].id),
                },
            };

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.Evaluate(
                    profile.Getter,
                    new NSGetterEvaluator.Context(
                        profile.Client,
                        null,
                        null)))!;

            StringAssert.Contains(
                "does not match its required Bool contract",
                error.Message);
        }

        [Test]
        public void CollectionOperators_ProfileHoistedInvariantsSeparatelyFromP57()
        {
            CallbackProfile[] profiles =
            {
                BuildProfile(dictionary: false),
                BuildProfile(dictionary: true),
            };

            foreach (CallbackProfile profile in profiles)
            {
                for (int warmup = 0; warmup < 5; warmup++)
                {
                    Measure(profile);
                }
                var samples = new double[MeasurementCount];
                for (int sample = 0; sample < MeasurementCount; sample++)
                {
                    samples[sample] = Measure(profile);
                }

                double medianDurationMs = Median(samples);
                string durationSamples = string.Join(",", samples.Select(
                    sample => sample.ToString("F3")));
                TestContext.WriteLine(
                    "P58_DOTNET_PROFILE " +
                    $"profile={profile.Name} entries={EntryCount} " +
                    $"medianDurationMs={medianDurationMs:F3} " +
                    $"durationSamplesMs=[{durationSamples}]");
            }
        }

        private static double Measure(CallbackProfile profile)
        {
            var ctx = new NSGetterEvaluator.Context(
                profile.Client,
                null,
                null);
            var stopwatch = Stopwatch.StartNew();
            object? result = NSGetterEvaluator.Evaluate(profile.Getter, ctx);
            stopwatch.Stop();
            int count = result switch
            {
                object?[] array => array.Length,
                IDictionary<string, object?> dictionary => dictionary.Count,
                _ => -1,
            };
            Assert.AreEqual(0, count);
            GC.KeepAlive(result);
            return stopwatch.Elapsed.TotalMilliseconds;
        }

        private static double Median(double[] values)
        {
            double[] ordered = (double[])values.Clone();
            Array.Sort(ordered);
            return ordered[ordered.Length / 2];
        }

        private static CallbackProfile BuildProfile(bool dictionary)
        {
            NeoClient client = LoadClient();
            PrimitiveTypeInfo stringType = RequiredType(MemberKind.String);
            PrimitiveTypeInfo boolType = RequiredType(MemberKind.Bool);
            Variable[] parameters = dictionary
                ? new[]
                {
                    Variable("key", stringType),
                    Variable("item", boolType),
                }
                : new[] { Variable("item", stringType) };
            var callback = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = parameters,
                typeInfo = boolType,
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = BoolValue(false),
                    },
                },
            };
            CollectionTypeInfo collectionType = new()
            {
                type = dictionary ? MemberKind.Dictionary : MemberKind.List,
                required = true,
                entryTypeInfo = dictionary ? boolType : stringType,
            };
            Pointer collection = dictionary
                ? DictionaryLiteral(collectionType)
                : ListLiteral(collectionType);
            var where = new FunctionPointer
            {
                type = PointerKind.Function,
                function = new WhereFunction
                {
                    type = FunctionKind.Where,
                    info = new FunctionCollectionBoolInfo
                    {
                        collectionPointer = collection,
                        function = callback,
                    },
                },
            };
            var getter = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = Array.Empty<Variable>(),
                typeInfo = collectionType,
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = where,
                    },
                },
            };
            return new CallbackProfile(
                dictionary
                    ? "dictionary-two-parameters"
                    : "list-one-parameter",
                client,
                getter);
        }

        private static FunctionWithReturnType ListEntryProjection()
        {
            PrimitiveTypeInfo stringType = RequiredType(MemberKind.String);
            var callback = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = new[] { Variable("item", stringType) },
                typeInfo = stringType,
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = VariableValue("item"),
                    },
                },
            };
            CollectionTypeInfo inputType = new()
            {
                type = MemberKind.List,
                required = true,
                entryTypeInfo = stringType,
            };
            return ProjectionGetter(
                new ListLiteralPointer
                {
                    type = PointerKind.ListLiteral,
                    typeInfo = inputType,
                    entries = new Pointer[]
                    {
                        StringValue("first"),
                        StringValue("second"),
                    },
                },
                callback,
                stringType);
        }

        private static FunctionWithReturnType DictionaryProjection(
            bool selectKey)
        {
            PrimitiveTypeInfo stringType = RequiredType(MemberKind.String);
            PrimitiveTypeInfo boolType = RequiredType(MemberKind.Bool);
            var callback = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = new[]
                {
                    Variable("key", stringType),
                    Variable("item", boolType),
                },
                typeInfo = selectKey ? stringType : boolType,
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = VariableValue(selectKey ? "key" : "item"),
                    },
                },
            };
            CollectionTypeInfo inputType = new()
            {
                type = MemberKind.Dictionary,
                required = true,
                entryTypeInfo = boolType,
            };
            return ProjectionGetter(
                new DictLiteralPointer
                {
                    type = PointerKind.DictLiteral,
                    typeInfo = inputType,
                    entries = new[]
                    {
                        new DictLiteralPair
                        {
                            key = StringValue("first"),
                            value = BoolValue(true),
                        },
                        new DictLiteralPair
                        {
                            key = StringValue("second"),
                            value = BoolValue(false),
                        },
                    },
                },
                callback,
                callback.typeInfo);
        }

        private static FunctionWithReturnType ProjectionGetter(
            Pointer collection,
            FunctionWithReturnType callback,
            TypeInfo projectedEntryType)
        {
            var projection = new FunctionPointer
            {
                type = PointerKind.Function,
                function = new SelectFunction
                {
                    type = FunctionKind.Select,
                    info = new FunctionCollectionSelectInfo
                    {
                        collectionPointer = collection,
                        function = callback,
                    },
                },
            };
            return new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = Array.Empty<Variable>(),
                typeInfo = new CollectionTypeInfo
                {
                    type = MemberKind.List,
                    required = true,
                    entryTypeInfo = projectedEntryType,
                },
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = projection,
                    },
                },
            };
        }

        private static FunctionWithReturnType WhereCallback(
            FunctionWithReturnType getter)
        {
            var outerReturn = (ReturnInstruction)getter.instructions[0];
            var pointer = (FunctionPointer)outerReturn.pointer!;
            return ((WhereFunction)pointer.function).info.function;
        }

        private static ListLiteralPointer ListLiteral(
            CollectionTypeInfo typeInfo) => new()
        {
            type = PointerKind.ListLiteral,
            typeInfo = typeInfo,
            entries = Enumerable.Repeat<Pointer>(
                StringValue("entry"),
                EntryCount).ToArray(),
        };

        private static DictLiteralPointer DictionaryLiteral(
            CollectionTypeInfo typeInfo) => new()
        {
            type = PointerKind.DictLiteral,
            typeInfo = typeInfo,
            entries = Enumerable.Range(0, EntryCount)
                .Select(index => new DictLiteralPair
                {
                    key = StringValue($"entry-{index}"),
                    value = BoolValue(true),
                })
                .ToArray(),
        };

        private static Variable Variable(string id, TypeInfo typeInfo) => new()
        {
            id = id,
            typeInfo = typeInfo,
            pointer = typeInfo.type == MemberKind.Bool
                ? BoolValue(false)
                : StringValue(""),
        };

        private static PrimitiveTypeInfo RequiredType(MemberKind kind) => new()
        {
            type = kind,
            required = true,
        };

        private static ValuePointer StringValue(string value) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = RequiredType(MemberKind.String),
                value = JToken.FromObject(value),
            },
        };

        private static ValuePointer BoolValue(bool value) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = RequiredType(MemberKind.Bool),
                value = JToken.FromObject(value),
            },
        };

        private static VariablePointer VariableValue(string variableId) => new()
        {
            type = PointerKind.Variable,
            variableId = variableId,
        };

        private static NeoClient LoadClient()
        {
            const string packageRoot =
                "Packages/com.ryanbliss.neocompose/Tests";
            return NeoTestSaveStack.LoadClient(
                File.ReadAllText(Path.Combine(
                    packageRoot,
                    "synth-example.json")));
        }

        private sealed class CallbackProfile
        {
            internal CallbackProfile(
                string name,
                NeoClient client,
                FunctionWithReturnType getter)
            {
                Name = name;
                Client = client;
                Getter = getter;
            }

            internal string Name { get; }
            internal NeoClient Client { get; }
            internal FunctionWithReturnType Getter { get; }
        }
    }
}
