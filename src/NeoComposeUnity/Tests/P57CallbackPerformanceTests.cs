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
        private const int MeasurementCount = 20;

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
                for (int warmup = 0; warmup < 5; warmup++)
                {
                    MeasureBaseline(profile.Callback);
                    MeasurePrepared(profile.Callback);
                }
                var baselineSamples = new Measurement[MeasurementCount];
                var preparedSamples = new Measurement[MeasurementCount];
                for (int sample = 0; sample < MeasurementCount; sample++)
                {
                    if (sample % 2 == 0)
                    {
                        baselineSamples[sample] = MeasureBaseline(
                            profile.Callback);
                        preparedSamples[sample] = MeasurePrepared(
                            profile.Callback);
                    }
                    else
                    {
                        preparedSamples[sample] = MeasurePrepared(
                            profile.Callback);
                        baselineSamples[sample] = MeasureBaseline(
                            profile.Callback);
                    }
                }

                Measurement baseline = MedianMeasurement(baselineSamples);
                Measurement prepared = MedianMeasurement(preparedSamples);
                double lower95SavingsMs = Lower95MedianSavings(
                    baselineSamples,
                    preparedSamples);
                double speedupPercent =
                    (baseline.DurationMs - prepared.DurationMs)
                    / baseline.DurationMs
                    * 100d;

                string beforeSamples = string.Join(",", baselineSamples.Select(
                    sample => sample.DurationMs.ToString("F3")));
                string afterSamples = string.Join(",", preparedSamples.Select(
                    sample => sample.DurationMs.ToString("F3")));

                TestContext.WriteLine(
                    "P57_DOTNET_PROFILE " +
                    $"profile={profile.Name} entries={EntryCount} " +
                    $"beforeMedianMs={baseline.DurationMs:F3} " +
                    $"afterMedianMs={prepared.DurationMs:F3} " +
                    $"speedupPercent={speedupPercent:F2} " +
                    $"lower95SavingsMs={lower95SavingsMs:F3} " +
                    $"beforeBytes={baseline.AllocatedBytes} " +
                    $"afterBytes={prepared.AllocatedBytes} " +
                    $"beforeScopeAllocations={baseline.ScopeAllocations} " +
                    $"afterScopeAllocations={prepared.ScopeAllocations} " +
                    $"beforeSamplesMs=[{beforeSamples}] " +
                    $"afterSamplesMs=[{afterSamples}]");

                Assert.Greater(
                    lower95SavingsMs,
                    0d,
                    $"{profile.Name} runtime improvement was not statistically significant.");
                Assert.AreEqual(EntryCount, baseline.ScopeAllocations);
                Assert.AreEqual(1, prepared.ScopeAllocations);
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
            GC.Collect();
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
                GC.GetAllocatedBytesForCurrentThread() - beforeBytes,
                EntryCount);
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
            GC.Collect();
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
                GC.GetAllocatedBytesForCurrentThread() - beforeBytes,
                1);
        }

        private static Measurement MedianMeasurement(
            Measurement[] measurements)
        {
            Measurement[] ordered = measurements
                .OrderBy(measurement => measurement.DurationMs)
                .ToArray();
            return ordered[ordered.Length / 2];
        }
        private static double Lower95MedianSavings(
            Measurement[] baseline,
            Measurement[] prepared)
        {
            const int BootstrapSamples = 5_000;
            var savings = new double[BootstrapSamples];
            var beforeResample = new double[MeasurementCount];
            var afterResample = new double[MeasurementCount];
            uint randomState = 0x57C0FFEEu;
            for (int sample = 0; sample < BootstrapSamples; sample++)
            {
                for (int index = 0; index < MeasurementCount; index++)
                {
                    beforeResample[index] = baseline[
                        NextIndex(ref randomState, baseline.Length)].DurationMs;
                    afterResample[index] = prepared[
                        NextIndex(ref randomState, prepared.Length)].DurationMs;
                }
                savings[sample] = Median(beforeResample)
                    - Median(afterResample);
            }
            Array.Sort(savings);
            return savings[(int)(BootstrapSamples * 0.025d)];
        }

        private static int NextIndex(ref uint state, int length)
        {
            state = unchecked(state * 1_664_525u + 1_013_904_223u);
            return (int)(state % (uint)length);
        }

        private static double Median(double[] values)
        {
            var ordered = (double[])values.Clone();
            Array.Sort(ordered);
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
                long allocatedBytes,
                int scopeAllocations)
            {
                DurationMs = durationMs;
                AllocatedBytes = allocatedBytes;
                ScopeAllocations = scopeAllocations;
            }

            internal double DurationMs { get; }
            internal long AllocatedBytes { get; }
            internal int ScopeAllocations { get; }
        }
    }
}
