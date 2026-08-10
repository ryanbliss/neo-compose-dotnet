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
    public class P57CallbackPerformanceTests
    {
        private const int EntryCount = 10_000;
        private const int MeasurementCount = 5;

        [Test]
        public void PreparedCallback_KeepsOneBalancedAllocationSession()
        {
            NeoClient client = LoadClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            FunctionWithReturnType callback = OneInstructionCallback();
            var scope = new NeoScriptScope();

            Assert.AreEqual(0, ctx.allocationTracker.ActiveExecutionCount);
            using (NeoScriptExecutor.PreparedCallback prepared =
                NeoScriptExecutor.PrepareCallback(
                    client,
                    callback,
                    ctx,
                    NeoScriptExecutionOptions.ForImmediate(client)))
            {
                Assert.AreEqual(1, ctx.allocationTracker.ActiveExecutionCount);
                Assert.IsTrue(prepared.Execute(scope).Returned);
                scope.ResetLocals();
                Assert.IsTrue(prepared.Execute(scope).Returned);
                Assert.AreEqual(1, ctx.allocationTracker.ActiveExecutionCount);
            }
            Assert.AreEqual(0, ctx.allocationTracker.ActiveExecutionCount);
        }

        [Test]
        public void PreparedCallbacks_OutperformPerEntryExecutorSetup()
        {
            var profiles = new[]
            {
                new CallbackProfile("empty", EmptyCallback()),
                new CallbackProfile(
                    "one-instruction",
                    OneInstructionCallback()),
                new CallbackProfile("call-heavy", CallHeavyCallback()),
            };

            foreach (CallbackProfile profile in profiles)
            {
                MeasureBaseline(profile.Callback);
                MeasurePrepared(profile.Callback);
                Measurement baseline = MedianMeasurement(
                    Enumerable.Range(0, MeasurementCount)
                        .Select(_ => MeasureBaseline(profile.Callback))
                        .ToArray());
                Measurement prepared = MedianMeasurement(
                    Enumerable.Range(0, MeasurementCount)
                        .Select(_ => MeasurePrepared(profile.Callback))
                        .ToArray());

                TestContext.WriteLine(
                    $"{profile.Name}: baselineMs={baseline.DurationMs:F3} " +
                    $"preparedMs={prepared.DurationMs:F3} " +
                    $"baselineBytes={baseline.AllocatedBytes} " +
                    $"preparedBytes={prepared.AllocatedBytes}");

                Assert.Less(
                    prepared.DurationMs,
                    baseline.DurationMs,
                    $"{profile.Name} callback preparation did not reduce runtime.");
                if (baseline.AllocatedBytes > 0)
                {
                    Assert.Less(
                        prepared.AllocatedBytes,
                        baseline.AllocatedBytes,
                        $"{profile.Name} callback preparation did not reduce allocations.");
                }
            }
        }

        private static Measurement MeasureBaseline(
            FunctionWithReturnType callback)
        {
            NeoClient client = LoadClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var parent = new NeoScriptScope();
            NeoScriptExecutionOptions options =
                NeoScriptExecutionOptions.ForImmediate(client);
            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            for (int index = 0; index < EntryCount; index++)
            {
                NeoScriptScope scope = parent.CreateChild();
                NeoScriptExecutionResult result = NeoScriptExecutor.Execute(
                    client,
                    callback,
                    scope,
                    ctx,
                    options);
                GC.KeepAlive(result.ReturnValue);
            }
            stopwatch.Stop();
            return new Measurement(
                stopwatch.Elapsed.TotalMilliseconds,
                GC.GetAllocatedBytesForCurrentThread() - beforeBytes);
        }

        private static Measurement MeasurePrepared(
            FunctionWithReturnType callback)
        {
            NeoClient client = LoadClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var parent = new NeoScriptScope();
            NeoScriptScope scope = parent.CreateChild();
            NeoScriptExecutionOptions options =
                NeoScriptExecutionOptions.ForImmediate(client);
            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            using (NeoScriptExecutor.PreparedCallback prepared =
                NeoScriptExecutor.PrepareCallback(
                    client,
                    callback,
                    ctx,
                    options))
            {
                for (int index = 0; index < EntryCount; index++)
                {
                    scope.ResetLocals();
                    NeoScriptExecutionResult result = prepared.Execute(scope);
                    GC.KeepAlive(result.ReturnValue);
                }
            }
            stopwatch.Stop();
            return new Measurement(
                stopwatch.Elapsed.TotalMilliseconds,
                GC.GetAllocatedBytesForCurrentThread() - beforeBytes);
        }

        private static Measurement MedianMeasurement(
            Measurement[] measurements)
        {
            Measurement[] ordered = measurements
                .OrderBy(measurement => measurement.DurationMs)
                .ToArray();
            return ordered[ordered.Length / 2];
        }

        private static FunctionWithReturnType EmptyCallback() => new()
        {
            parameters = Array.Empty<Variable>(),
            typeInfo = new VoidTypeInfo
            {
                type = MemberKind.Void,
                required = true,
            },
            instructions = Array.Empty<Instruction>(),
        };

        private static FunctionWithReturnType OneInstructionCallback() => new()
        {
            parameters = Array.Empty<Variable>(),
            typeInfo = RequiredType(MemberKind.Bool),
            instructions = new Instruction[]
            {
                new ReturnInstruction
                {
                    type = InstructionKind.Return,
                    pointer = Literal(MemberKind.Bool, true),
                },
            },
        };

        private static FunctionWithReturnType CallHeavyCallback()
        {
            Pointer value = Literal(MemberKind.String, " PaYlOaD ");
            for (int index = 0; index < 4; index++)
            {
                value = StringOp(StringOpKind.Trim, value);
                value = StringOp(StringOpKind.ToLower, value);
                value = StringOp(StringOpKind.ToUpper, value);
            }
            return new FunctionWithReturnType
            {
                parameters = Array.Empty<Variable>(),
                typeInfo = RequiredType(MemberKind.String),
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = value,
                    },
                },
            };
        }

        private static FunctionPointer StringOp(
            string op,
            Pointer receiver) => new()
        {
            type = PointerKind.Function,
            function = new StringOpFunction
            {
                type = FunctionKind.StringOp,
                info = new FunctionStringOpInfo
                {
                    op = op,
                    receiverPointer = receiver,
                },
            },
        };

        private static ValuePointer Literal(
            MemberKind kind,
            object value) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = RequiredType(kind),
                value = JToken.FromObject(value),
            },
        };

        private static PrimitiveTypeInfo RequiredType(MemberKind kind) => new()
        {
            type = kind,
            required = true,
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
                FunctionWithReturnType callback)
            {
                Name = name;
                Callback = callback;
            }

            internal string Name { get; }
            internal FunctionWithReturnType Callback { get; }
        }

        private readonly struct Measurement
        {
            internal Measurement(
                double durationMs,
                long allocatedBytes)
            {
                DurationMs = durationMs;
                AllocatedBytes = allocatedBytes;
            }

            internal double DurationMs { get; }
            internal long AllocatedBytes { get; }
        }
    }
}
