// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Reflection;
using System.Collections.Generic;
using NeoCompose.Runtime;
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
        public void SerializedCompilerIrSharesClientStateAcrossCallsAndNestedArguments()
        {
            var client = NeoScriptMathParityTests.BuildClient(NeoScriptRandomIrFixture.Install);
            client.Random.Seed = 17;
            var baseline = new NeoRandom(17);
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var getters = JObject.Parse(NeoScriptRandomIrFixture.Json);
            double Evaluate(string name) => (double)NSGetterEvaluator.Evaluate(getters[name]!.ToObject<FunctionWithReturnType>()!, ctx)!;
            Assert.AreEqual((double)baseline.Value(), Evaluate("value"));
            Assert.AreEqual(baseline.RangeInt(int.MinValue, int.MaxValue), Evaluate("rangeInt"));
            Assert.AreEqual((double)baseline.Range(-3f, 5f), Evaluate("rangeFloat"));
            float min = baseline.Value();
            float max = baseline.Value();
            Assert.AreEqual((double)baseline.Range(min, max), Evaluate("nested"));
            baseline.Seed = -1;
            Assert.AreEqual((double)baseline.Value(), Evaluate("reset"));
            Assert.AreEqual((double)baseline.Value(), Evaluate("value"));
        }

        [Test]
        public void InstanceSeedAndOptionalCallsUseOrdinaryCompilerIr()
        {
            using var client = NeoScriptMathParityTests.BuildClient(NeoScriptRandomIrFixture.Install);
            client.Random.Seed = 17;
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var getters = JObject.Parse(NeoScriptRandomIrFixture.Json);
            double Evaluate(string name) => (double)NSGetterEvaluator.Evaluate(getters[name]!.ToObject<FunctionWithReturnType>()!, ctx)!;
            var expected = new NeoRandom(42);
            _ = expected.Value();
            Assert.AreEqual((double)expected.Value(), Evaluate("instances"));
            Assert.AreEqual((double)new NeoRandom(-4294967294).Value(), Evaluate("seed"));
            Assert.AreEqual((double)new NeoRandom(17).Value(), Evaluate("optionalInstance"));
            Assert.AreEqual(3d, Evaluate("forSeed"));
            Assert.AreEqual(17d, Evaluate("attach"));
            StringAssert.Contains("safe integer", Assert.Throws<NeoScriptException>(() => Evaluate("invalidConstructor"))!.Message);
            Assert.Throws<NeoScriptException>(() => Evaluate("nullInstance"));
        }

        [Test]
        public void SeedInvalidatesReadersButKeepsDrawMemoAndIsolatesContent()
        {
            var getters = JObject.Parse(NeoScriptRandomIrFixture.Json);
            using var client = NeoScriptMathParityTests.BuildClient(data =>
            {
                NeoScriptRandomIrFixture.Install(data);
                data.values["rng"] = new ObjectMemberValue
                {
                    id = "rng",
                    classId = NeoRandomContract.Class,
                    value = new Dictionary<string, string> { ["_constructorSeed"] = "rng-seed" },
                };
                data.values["rng-seed"] = new NumberMemberValue { id = "rng-seed", value = 17d };
                foreach (string name in new[] { "instanceSeed", "instanceDraw", "sharedSeed", "value" })
                    data.members[name] = new NSPropertyMember
                    {
                        id = name,
                        name = name,
                        kind = MemberKind.NSProperty,
                        returnTypeInfo = new PrimitiveTypeInfo { type = MemberKind.Float, required = true },
                        getter = getters[name]!.ToObject<FunctionWithReturnType>()!,
                    };
            });
            NeoMemberNSProperty Property(string id)
            {
                Assert.IsTrue(client.TryGetMember(id, out Member? member));
                return new NeoMemberNSProperty(client, (NSPropertyMember)member!, null);
            }
            client.Random.Seed = 17;
            var sharedSeed = Property("sharedSeed");
            var sharedDraw = Property("value");
            Assert.AreEqual(17d, sharedSeed.Compute("rng").value);
            object? sharedFirst = sharedDraw.Compute("rng").value;
            client.Random.Seed = 42;
            Assert.AreEqual(42d, sharedSeed.Compute("rng").value);
            Assert.AreEqual(sharedFirst, sharedDraw.Compute("rng").value);
            using (client.Random.EnterIdentity("shared-content"))
            {
                client.Random.Seed = 99;
                Assert.AreEqual(99d, sharedSeed.Compute("rng").value);
            }
            Assert.AreEqual(42d, sharedSeed.Compute("rng").value);
            Assert.AreEqual(sharedFirst, sharedDraw.Compute("rng").value);
            var seed = Property(NeoRandomContract.Seed);
            var enclosing = Property("instanceSeed");
            var draw = Property("instanceDraw");
            Assert.AreEqual(17d, seed.Compute("rng").value);
            Assert.AreEqual(17d, enclosing.Compute("rng").value);
            object? first = draw.Compute("rng").value;
            var written = seed.Set("rng", 42d);
            Assert.IsTrue(written.ok, written.error);
            Assert.AreEqual(42d, seed.Compute("rng").value);
            Assert.AreEqual(42d, enclosing.Compute("rng").value);
            Assert.AreEqual(first, draw.Compute("rng").value);
            using (client.Random.EnterIdentity("content"))
            {
                Assert.AreEqual(17d, seed.Compute("rng").value);
                Assert.IsTrue(seed.Set("rng", 99d).ok);
                Assert.AreEqual(99d, enclosing.Compute("rng").value);
            }
            Assert.AreEqual(42d, enclosing.Compute("rng").value);
            Assert.AreEqual(first, draw.Compute("rng").value);
            Assert.IsTrue(seed.Set("rng", 42d).ok);
            Assert.AreEqual(42d, seed.Compute("rng").value);
        }

        [Test]
        public void ObservingTransientSeedPreservesDrawsWhenItMaterializes()
        {
            using var client = NeoScriptMathParityTests.BuildClient(NeoScriptRandomIrFixture.Install);
            var getters = JObject.Parse(NeoScriptRandomIrFixture.Json);
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            object rng = NSGetterEvaluator.Evaluate(getters["generator"]!.ToObject<FunctionWithReturnType>()!, ctx)!;
            var baseline = new NeoRandom(17);
            _ = baseline.Value();
            Assert.IsTrue(client.TryGetMember(NeoRandomContract.Seed, out Member? member));
            var seed = new NeoMemberNSProperty(client, (NSPropertyMember)member!, null);
            Assert.AreEqual(17d, seed.Compute(rng).value);
            Assert.AreEqual((double)baseline.Value(), NSGetterEvaluator.InvokeRandomFunction(client, NeoRandomContract.Value, rng, Array.Empty<object?>()));
            Assert.IsTrue(seed.Set(42d, rng).ok);
            Assert.AreEqual(42d, seed.Compute(rng).value);
            Assert.AreEqual((double)new NeoRandom(42).Value(), NSGetterEvaluator.InvokeRandomFunction(client, NeoRandomContract.Value, rng, Array.Empty<object?>()));
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
                random.Seed = seed;
                foreach (JToken value in test["values"]!)
                    Assert.AreEqual(value.Value<uint>(), Bits(random.Value()));
                random.Seed = seed;
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
                        Assert.AreEqual(call["bits"]!.Value<uint>(), Bits(op == "value" ? random.Value() : random.Range((float)min, (float)max)));
                    Assert.AreEqual(call["draws"]!.Value<int>(), random.DrawCount - before);
                }
            }
        }

        [Test]
        public void ErrorsAndDegenerateRangesDoNotDraw()
        {
            var random = new NeoRandom(17);
            Assert.AreEqual("NeoRandom.Range int min must be an integer.", Assert.Throws<NSGetterRuntimeError>(() => random.RangeInt(0.5, 1))!.Message);
            Assert.AreEqual("NeoRandom.Range int max must be an integer.", Assert.Throws<NSGetterRuntimeError>(() => random.RangeInt(0, 0.5))!.Message);
            Assert.AreEqual("NeoRandom.Range int min must fit int32.", Assert.Throws<NSGetterRuntimeError>(() => random.RangeInt(-2147483649d, 0))!.Message);
            Assert.AreEqual("NeoRandom.Range int max must fit int32.", Assert.Throws<NSGetterRuntimeError>(() => random.RangeInt(0, 2147483648d))!.Message);
            Assert.AreEqual("NeoRandom.Range float min must be finite.", Assert.Throws<NSGetterRuntimeError>(() => random.Range(float.NaN, 1))!.Message);
            Assert.AreEqual("NeoRandom.Range float max must be finite.", Assert.Throws<NSGetterRuntimeError>(() => random.Range(0, float.PositiveInfinity))!.Message);
            Assert.AreEqual("NeoRandom seed must be a safe integer.", Assert.Throws<NSGetterRuntimeError>(() => random.Seed = 9007199254740992d)!.Message);
            Assert.AreEqual(0x80000000, Bits(random.Range(BitConverter.Int32BitsToSingle(int.MinValue), 0f)));
            Assert.AreEqual(3, random.Range(3, 3));
            Assert.AreEqual(0, random.DrawCount);
            Assert.AreEqual(new NeoRandom(17).Value(), random.Value());
        }

        [Test]
        public void IdentityFramesRestoreNestedReseedsAndExceptions()
        {
            var random = new NeoRandom(17);
            var baseline = new NeoRandom(17);
            Assert.AreEqual(baseline.Value(), random.Value());
            float first, second;
            using (random.EnterIdentity("root:🌱"))
            {
                first = random.Value();
                second = random.Value();
            }
            using (random.EnterIdentity("root:🌱"))
            {
                Assert.AreEqual(first, random.Value());
                Assert.Throws<InvalidOperationException>(() =>
                {
                    using var nested = random.EnterIdentity("child");
                    random.Seed = 42;
                    _ = random.Value();
                    throw new InvalidOperationException("failed");
                });
                Assert.AreEqual(second, random.Value());
            }
            Assert.AreEqual(baseline.Value(), random.Value());
        }

        [Test]
        public void ClientStateIsIndependentAndDrawsAllocateNothing()
        {
            var a = NeoScriptMathParityTests.BuildClient(NeoScriptRandomIrFixture.Install);
            var b = NeoScriptMathParityTests.BuildClient(NeoScriptRandomIrFixture.Install);
            a.Random.Seed = 17;
            b.Random.Seed = 17;
            Assert.AreEqual(a.Random.Value(), b.Random.Value());
            a.Random.Seed = 99;
            Assert.AreEqual(new NeoRandom(99).Value(), a.Random.Value());
            var random = new NeoRandom(17);
            for (int i = 0; i < 100; i++)
            {
                _ = random.Value();
                _ = random.Range(int.MinValue, int.MaxValue);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            double total = 0;
            for (int i = 0; i < 10000; i++)
            {
                total += random.Value();
                total += random.Range(int.MinValue, int.MaxValue);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated);
            Assert.IsFalse(double.IsNaN(total));
        }
    }
}
