// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using Unity.Profiling;

namespace NeoCompose.Tests
{
    /// <summary>Wall time and Unity GC bytes for EditMode measurements.</summary>
    internal static class PerformanceSampling
    {
        internal readonly struct Sample
        {
            public Sample(double ms, long gcBytes)
            {
                Ms = ms;
                GcBytes = gcBytes;
            }

            public double Ms
            {
                get;
            }

            /// <summary>Unity's "GC Allocated In Frame" counter.</summary>
            public long GcBytes
            {
                get;
            }
        }

        internal static List<Sample> Repeat(int samples, Action action)
        {
            var taken = new List<Sample>(samples);
            for (int sample = 0; sample < samples; sample++)
                taken.Add(Measure(action));
            return taken;
        }

        internal static Sample Measure(Action action)
        {
            using var recorder = new ProfilerRecorder(
                ProfilerCategory.Memory,
                "GC Allocated In Frame",
                1,
                ProfilerRecorderOptions.WrapAroundWhenCapacityReached
                    | ProfilerRecorderOptions.SumAllSamplesInFrame);
            Assert.IsTrue(recorder.Valid, "Unity GC allocation counter is unavailable.");
            recorder.Start();
            long gcBefore = recorder.CurrentValue;
            var stopwatch = Stopwatch.StartNew();
            action();
            stopwatch.Stop();
            long gcAfter = recorder.CurrentValue;
            recorder.Stop();
            return new Sample(
                stopwatch.Elapsed.TotalMilliseconds,
                gcAfter - gcBefore);
        }

        internal static void Report(string label, string name, string scale, List<Sample> samples, int per)
        {
            var ms = samples.Select(sample => sample.Ms).OrderBy(value => value).ToArray();
            var bytes = samples.Select(sample => sample.GcBytes).OrderBy(value => value).ToArray();
            TestContext.WriteLine(
                $"{label} {name} {scale} per={per} " +
                $"medianMs={ms[ms.Length / 2]:F3} minMs={ms[0]:F3} maxMs={ms[^1]:F3} " +
                $"medianBytes={bytes[bytes.Length / 2]} minBytes={bytes[0]} maxBytes={bytes[^1]} " +
                $"samplesMs=[{string.Join(",", samples.Select(sample => sample.Ms.ToString("F3")))}] " +
                $"samplesBytes=[{string.Join(",", samples.Select(sample => sample.GcBytes))}]");
        }
    }
}
