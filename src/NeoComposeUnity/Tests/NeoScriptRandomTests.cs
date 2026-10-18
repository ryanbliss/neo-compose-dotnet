// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Reflection;
using NeoCompose.Runtime.NeoScript;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class NeoScriptRandomTests
    {
        private static uint Bits(float value) => unchecked((uint)BitConverter.SingleToInt32Bits(value));

        [Test]
        public void SerializedCompilerIrSharesClientStateAcrossCallsAndClosures()
        {
            var client = NeoScriptMathParityTests.BuildClient();
            client.Random.InitState(17);
            var baseline = new NeoRandom(17);
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var getters = JObject.Parse(NeoScriptRandomIrFixture.Json);
            double Evaluate(string name) => (double)NSGetterEvaluator.Evaluate(getters[name]!.ToObject<FunctionWithReturnType>()!, ctx)!;
            Assert.AreEqual((double)baseline.value, Evaluate("value"));
            Assert.AreEqual(baseline.RangeInt(int.MinValue, int.MaxValue), Evaluate("rangeInt"));
            Assert.AreEqual((double)baseline.Range(-3f, 5f), Evaluate("rangeFloat"));
            float min = baseline.value;
            float max = baseline.value;
            Assert.AreEqual((double)baseline.Range(min, max), Evaluate("nested"));
            baseline.InitState(-1);
            Assert.AreEqual((double)baseline.value, Evaluate("reset"));
            Assert.AreEqual((double)baseline.value, Evaluate("value"));
        }

        [Test]
        public void PortableStreamMatchesIndependentVectors()
        {
            foreach (JObject test in JArray.Parse(NeoScriptRandomFixture.Json))
            {
                double seed = test["seed"]!.Value<double>();
                var random = new NeoRandom(seed);
                string[] fields = { "x", "y", "z", "w" };
                for (int i = 0; i < fields.Length; i++)
                    Assert.AreEqual(test["state"]![i]!.Value<uint>(), typeof(NeoRandom).GetField(fields[i], BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(random));
                foreach (JToken word in test["words"]!)
                    Assert.AreEqual(word.Value<uint>(), (uint)(random.RangeInt(int.MinValue, int.MaxValue) + 2147483648d));
                random.InitState(seed);
                foreach (JToken value in test["values"]!)
                    Assert.AreEqual(value.Value<uint>(), Bits(random.value));
                random.InitState(seed);
                long before = random.DrawCount;
                foreach (JObject call in test["mixed"]!)
                {
                    string op = call["op"]!.Value<string>()!;
                    double min = call["min"]!.Value<double>();
                    double max = call["max"]!.Value<double>();
                    // JSON readers may normalize -0; the vector explicitly checks its sign.
                    if (call["bits"]?.Value<uint>() == 0x80000000)
                        min = BitConverter.Int32BitsToSingle(int.MinValue);
                    if (op == "rangeInt")
                        Assert.AreEqual(call["expected"]!.Value<double>(), random.RangeInt(min, max));
                    else
                        Assert.AreEqual(call["bits"]!.Value<uint>(), Bits(op == "value" ? random.value : random.Range((float)min, (float)max)));
                    Assert.AreEqual(call["draws"]!.Value<int>(), random.DrawCount - before);
                }
            }
        }

        [Test]
        public void ErrorsAndDegenerateRangesDoNotDraw()
        {
            var random = new NeoRandom(17);
            Assert.AreEqual("Random.Range int min must be an integer.", Assert.Throws<NSGetterRuntimeError>(() => random.RangeInt(0.5, 1))!.Message);
            Assert.AreEqual("Random.Range int max must be an integer.", Assert.Throws<NSGetterRuntimeError>(() => random.RangeInt(0, 0.5))!.Message);
            Assert.AreEqual("Random.Range int min must fit int32.", Assert.Throws<NSGetterRuntimeError>(() => random.RangeInt(-2147483649d, 0))!.Message);
            Assert.AreEqual("Random.Range int max must fit int32.", Assert.Throws<NSGetterRuntimeError>(() => random.RangeInt(0, 2147483648d))!.Message);
            Assert.AreEqual("Random.Range float min must be finite.", Assert.Throws<NSGetterRuntimeError>(() => random.Range(float.NaN, 1))!.Message);
            Assert.AreEqual("Random.Range float max must be finite.", Assert.Throws<NSGetterRuntimeError>(() => random.Range(0, float.PositiveInfinity))!.Message);
            Assert.AreEqual("Random.InitState requires a safe integer seed.", Assert.Throws<NSGetterRuntimeError>(() => random.InitState(9007199254740992d))!.Message);
            Assert.AreEqual(0x80000000, Bits(random.Range(BitConverter.Int32BitsToSingle(int.MinValue), 0f)));
            Assert.AreEqual(3, random.Range(3, 3));
            Assert.AreEqual(0, random.DrawCount);
            Assert.AreEqual(new NeoRandom(17).value, random.value);
        }

        [Test]
        public void IdentityFramesRestoreNestedReseedsAndExceptions()
        {
            var random = new NeoRandom(17);
            var baseline = new NeoRandom(17);
            Assert.AreEqual(baseline.value, random.value);
            float first, second;
            using (random.EnterIdentity("root:🌱"))
            {
                first = random.value;
                second = random.value;
            }
            using (random.EnterIdentity("root:🌱"))
            {
                Assert.AreEqual(first, random.value);
                Assert.Throws<InvalidOperationException>(() =>
                {
                    using var nested = random.EnterIdentity("child");
                    random.InitState(42);
                    _ = random.value;
                    throw new InvalidOperationException("failed");
                });
                Assert.AreEqual(second, random.value);
            }
            Assert.AreEqual(baseline.value, random.value);
        }

        [Test]
        public void ClientStateIsIndependentAndDrawsAllocateNothing()
        {
            var a = NeoScriptMathParityTests.BuildClient();
            var b = NeoScriptMathParityTests.BuildClient();
            a.Random.InitState(17);
            b.Random.InitState(17);
            Assert.AreEqual(a.Random.value, b.Random.value);
            a.Random.InitState(99);
            Assert.AreEqual(new NeoRandom(99).value, a.Random.value);
            var random = new NeoRandom(17);
            for (int i = 0; i < 100; i++)
            {
                _ = random.value;
                _ = random.Range(int.MinValue, int.MaxValue);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            double total = 0;
            for (int i = 0; i < 10000; i++)
            {
                total += random.value;
                total += random.Range(int.MinValue, int.MaxValue);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated);
            Assert.IsFalse(double.IsNaN(total));
        }
    }
}
