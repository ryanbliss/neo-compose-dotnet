// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    /// <summary>
    /// What a computed <c>OnChanged</c> costs on a leaf write: the memo drop,
    /// the per-instance notification, and the handler's read. The baseline
    /// reads the same getters again by hand with nothing watching, so the
    /// difference is the delivery alone.
    /// </summary>
    public partial class NSPropertySetterTests
    {
        private static readonly int[] FanOutReceiverCounts = { 100, 1_000 };
        private const int FanOutSamples = 5;
        private const int FanOutWrites = 100;

        [Test]
        [Explicit("Getter fan-out measurement; run serially by name.")]
        public void GetterFanOutPerformance_LeafWriteNotifiesAndReadsAgain()
        {
            foreach (int count in FanOutReceiverCounts)
            {
                MeasureGetterFanOut(count, watched: false);
                MeasureGetterFanOut(count, watched: true);
            }
        }

        private static void MeasureGetterFanOut(int count, bool watched)
        {
            using var client = BuildClient(out NSPropertyMember property);
            // return this.Count + root.Save.Target;
            property.getter = Function(new ReturnInstruction
            {
                type = InstructionKind.Return,
                pointer = ArithmeticPointer(
                    ArithmeticOpKind.Addition,
                    KeyOf(ThisVariable(), "Count"),
                    RootTargetPointer()),
            });
            property.getter.typeInfo = IntType();
            Assert.IsTrue(client.TryGetMember("member-count", out JsonMember? countMember));
            Assert.IsTrue(client.TryGetMember("member-target", out JsonMember? targetMember));

            var rows = new List<MemberValue>(2 * count);
            for (int i = 0; i < count; i++)
            {
                rows.Add(Number($"value-fan-{i}-count", i));
                rows.Add(ObjectValue($"value-fan-{i}", "class-receiver", ("Count", $"value-fan-{i}-count")));
            }
            client.SetWritableValues(NeoValueOwnership.Save, rows);

            var views = new TestReceiverView[count];
            var heard = new int[count];
            var watches = new List<IDisposable>(watched ? count : 0);
            for (int i = 0; i < count; i++)
            {
                views[i] = TestReceiverView.Create(client, $"value-fan-{i}");
                int index = i;
                if (watched)
                    watches.Add(views[i].OnComputedChanged((_, _) => heard[index]++));
                else
                    Assert.IsTrue(views[i].ComputeComputed().ok);
            }

            var counts = new[] { Number("value-fan-0-count", 1_000), Number("value-fan-0-count", 1_001) };
            var targets = new[] { Number("value-target", 1), Number("value-target", 2) };
            // One receiver's Count reaches its getter alone.
            void WriteOneCount()
            {
                for (int write = 0; write < FanOutWrites; write++)
                {
                    Assert.IsTrue(client.TryWriteLeaf(NeoValueOwnership.Save, counts[write & 1], countMember!, "value"));
                    if (!watched)
                        views[0].ComputeComputed();
                }
            }
            // Save.Target reaches every receiver's getter.
            void WriteSharedTarget()
            {
                for (int write = 0; write < FanOutWrites; write++)
                {
                    Assert.IsTrue(client.TryWriteLeaf(NeoValueOwnership.Save, targets[write & 1], targetMember!, "value"));
                    if (!watched)
                    {
                        for (int i = 0; i < count; i++)
                            views[i].ComputeComputed();
                    }
                }
            }

            try
            {
                int writes = (1 + FanOutSamples) * FanOutWrites;
                WriteOneCount();
                var one = PerformanceSampling.Repeat(FanOutSamples, WriteOneCount);
                if (watched)
                {
                    Assert.AreEqual(writes, heard[0], "Every Count write reaches its own getter.");
                    for (int i = 1; i < count; i++)
                        Assert.AreEqual(0, heard[i], $"Receiver {i} read nothing that changed.");
                }

                WriteSharedTarget();
                var shared = PerformanceSampling.Repeat(FanOutSamples, WriteSharedTarget);
                if (watched)
                {
                    Assert.AreEqual(2 * writes, heard[0]);
                    for (int i = 1; i < count; i++)
                        Assert.AreEqual(writes, heard[i], $"Receiver {i} hears every Target write.");
                }
                Assert.AreEqual(1_001 + 2, Convert.ToInt32(views[0].ComputeComputed().value));

                string scale = $"receivers={count} {(watched ? "watched" : "unwatched-read")}";
                PerformanceSampling.Report("GetterFanOut", "one-count", scale, one, FanOutWrites);
                PerformanceSampling.Report("GetterFanOut", "shared-target", scale, shared, FanOutWrites);
            }
            finally
            {
                foreach (IDisposable watch in watches)
                    watch.Dispose();
            }
        }
    }
}
