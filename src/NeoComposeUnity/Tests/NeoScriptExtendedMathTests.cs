// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class NeoScriptExtendedMathTests
    {
        private static float FromBits(uint bits) => BitConverter.Int32BitsToSingle(unchecked((int)bits));
        private static uint Bits(double value) => double.IsNaN(value) ? 0x7fc00000 : unchecked((uint)BitConverter.SingleToInt32Bits((float)value));

        [Test]
        public void SerializedIrMatchesIndependentReferenceBits()
        {
            var fixture = JObject.Parse(NeoScriptExtendedMathFixture.Json);
            var getters = ((JObject)fixture["getters"]!).Properties().ToDictionary(p => p.Name, p => p.Value.ToObject<FunctionWithReturnType>()!);
            var ctx = new NSGetterEvaluator.Context(NeoScriptMathParityTests.BuildClient(), null, null);
            foreach (JObject test in fixture["cases"]!)
            {
                string op = test["op"]!.Value<string>()!;
                object?[] inputs = test["inputs"]!.Select(n => (object?)(double)FromBits(n.Value<uint>())).ToArray();
                if (test["error"] is JToken error)
                {
                    var failure = Assert.Catch<NSGetterRuntimeError>(() => NSGetterEvaluator.Evaluate(getters[op], ctx, inputs));
                    Assert.AreEqual(error.Value<string>(), failure!.Message, test.ToString());
                }
                else
                {
                    object? result = NSGetterEvaluator.Evaluate(getters[op], ctx, inputs);
                    Assert.That(result, Is.TypeOf<double>(), test.ToString());
                    Assert.AreEqual(test["expected"]!.Value<uint>(), Bits((double)result!), test.ToString());
                }
            }
        }

        [Test]
        public void WarmKernelsReuseRangeReductionStorage()
        {
            double total = 0;
            for (int i = 0; i < 100; i++)
                total += NeoExtendedMath.Evaluate(MathOp.Sin, float.MaxValue, 0, 0);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++)
                total += NeoExtendedMath.Evaluate(MathOp.Sin, float.MaxValue, 0, 0);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated);
            Assert.IsFalse(double.IsNaN(total));
        }

        [Test]
        public void ConcurrentKernelsHaveIndependentScratch()
        {
            var cases = JObject.Parse(NeoScriptExtendedMathFixture.Json)["cases"]!
                .Where(t => t["error"] is null).ToArray();
            Parallel.For(0, 16, _ =>
            {
                foreach (JToken test in cases)
                {
                    var op = (MathOp)System.Enum.Parse(typeof(MathOp), test["op"]!.Value<string>()!, true);
                    float[] inputs = test["inputs"]!.Select(n => FromBits(n.Value<uint>())).ToArray();
                    double result = NeoExtendedMath.Evaluate(op, inputs[0], inputs.Length > 1 ? inputs[1] : 0, inputs.Length > 2 ? inputs[2] : 0);
                    Assert.AreEqual(test["expected"]!.Value<uint>(), Bits(result), test.ToString());
                }
            });
        }
    }
}
