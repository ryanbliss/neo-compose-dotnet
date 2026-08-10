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
    public class P56CallbackPerformanceTests
    {
        private const int EntryCount = 10_000;
        private const int MeasurementCount = 5;
        private static readonly int[] CapturedBindingCounts =
            { 1, 10, 100, 1_000 };

        // Unity 6 Mono reports zero from GC.GetAllocatedBytesForCurrentThread.
        // Measure the real callback path here; NeoScriptScopeTests separately
        // asserts that every frame retains only its parameter/local overlay.
        [Test]
        public void CollectionCallback_RuntimeDoesNotScaleWithCapturedScope()
        {
            CallbackWorkload workload = BuildCallbackWorkload();

            foreach (int capturedBindingCount in CapturedBindingCounts)
            {
                MeasureDurationMs(workload, capturedBindingCount);
                MeasureDurationMs(workload, capturedBindingCount);
            }

            CallbackMeasurement[] measurements = CapturedBindingCounts
                .Select(capturedBindingCount => new CallbackMeasurement(
                    capturedBindingCount,
                    Median(Enumerable.Range(0, MeasurementCount)
                        .Select(_ => MeasureDurationMs(
                            workload,
                            capturedBindingCount))
                        .ToArray())))
                .ToArray();

            foreach (CallbackMeasurement measurement in measurements)
            {
                TestContext.WriteLine(
                    $"parent={measurement.CapturedBindingCount} " +
                    $"medianDurationMs={measurement.MedianDurationMs:F3}");
            }

            double runtimeScale =
                measurements.Max(measurement => measurement.MedianDurationMs)
                / measurements.Min(measurement => measurement.MedianDurationMs);
            Assert.LessOrEqual(
                runtimeScale,
                4d,
                "End-to-end callback runtime scaled with captured scope size.");
        }

        private static double MeasureDurationMs(
            CallbackWorkload workload,
            int capturedBindingCount)
        {
            var scope = new Dictionary<string, object?>(
                capturedBindingCount,
                StringComparer.Ordinal);
            for (int index = 0; index < capturedBindingCount; index++)
            {
                scope[$"captured:{index}"] = index;
            }
            var ctx = new NSGetterEvaluator.Context(
                workload.Client,
                null,
                null);

            var stopwatch = Stopwatch.StartNew();
            object? result = NSGetterEvaluator.EvaluatePointer(
                workload.Pointer,
                scope,
                ctx);
            stopwatch.Stop();

            var entries = (object?[])result!;
            Assert.AreEqual(EntryCount, entries.Length);
            GC.KeepAlive(entries);
            return stopwatch.Elapsed.TotalMilliseconds;
        }

        private static double Median(double[] values)
        {
            Array.Sort(values);
            return values[values.Length / 2];
        }

        private static CallbackWorkload BuildCallbackWorkload()
        {
            const string packageRoot =
                "Packages/com.ryanbliss.neocompose/Tests";
            NeoClient client = NeoTestSaveStack.LoadClient(
                File.ReadAllText(Path.Combine(packageRoot, "synth-example.json")));
            PrimitiveTypeInfo stringType = RequiredType(MemberKind.String);
            PrimitiveTypeInfo boolType = RequiredType(MemberKind.Bool);
            Pointer entry = StringValue("entry");
            var callback = new FunctionWithReturnType
            {
                parameters = new[]
                {
                    new Variable
                    {
                        id = "item",
                        typeInfo = stringType,
                        pointer = StringValue(""),
                    },
                },
                typeInfo = boolType,
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = BoolValue(true),
                    },
                },
            };
            var collection = new ListLiteralPointer
            {
                type = PointerKind.ListLiteral,
                typeInfo = new CollectionTypeInfo
                {
                    type = MemberKind.List,
                    required = true,
                    entryTypeInfo = stringType,
                },
                entries = Enumerable.Repeat(entry, EntryCount).ToArray(),
            };
            var pointer = new FunctionPointer
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
            return new CallbackWorkload(client, pointer);
        }

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

        private sealed class CallbackWorkload
        {
            internal CallbackWorkload(
                NeoClient client,
                FunctionPointer pointer)
            {
                Client = client;
                Pointer = pointer;
            }

            internal NeoClient Client { get; }
            internal FunctionPointer Pointer { get; }
        }

        private sealed class CallbackMeasurement
        {
            internal CallbackMeasurement(
                int capturedBindingCount,
                double medianDurationMs)
            {
                CapturedBindingCount = capturedBindingCount;
                MedianDurationMs = medianDurationMs;
            }

            internal int CapturedBindingCount { get; }
            internal double MedianDurationMs { get; }
        }
    }
}
