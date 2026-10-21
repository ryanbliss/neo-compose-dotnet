// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable
using System;
using System.IO;
using NeoCompose.Runtime;
using Newtonsoft.Json.Linq;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;
namespace NeoCompose.Tests
{
    public class CollectionRuntimeParityTests
    {
        private static FunctionWithReturnType Body(string name)
        {
            var fixtures = JArray.Parse(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/collection-runtime-parity.json"));
            foreach (JToken fixture in fixtures)
            {
                if ((string?)fixture["name"] == name)
                    return fixture["body"]!.ToObject<FunctionWithReturnType>()!;
            }
            throw new System.InvalidOperationException($"Missing collection parity fixture '{name}'.");
        }

        [Test]
        public void FloatGetterArgumentUsesDeclaredPrecision()
        {
            using var client = NeoTestSaveStack.LoadClient(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/synth-example.json"));
            var context = new NSGetterEvaluator.Context(client, null, null);
            Assert.That(NSGetterEvaluator.Evaluate(Body("floatArgument"), context, new object?[] { 16777217d }), Is.EqualTo(true));
        }

        [Test]
        public void FloatGetterReturnUsesDeclaredPrecision()
        {
            using var client = NeoTestSaveStack.LoadClient(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/synth-example.json"));
            Assert.That(NSGetterEvaluator.Evaluate(Body("floatReturn"), new NSGetterEvaluator.Context(client, null, null)), Is.EqualTo(16777216d));
        }

        [TestCase(1e40)]
        [TestCase(-1e40)]
        public void FloatArgumentRejectsOverflowAfterRounding(double value)
        {
            using var client = NeoTestSaveStack.LoadClient(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/synth-example.json"));
            var context = new NSGetterEvaluator.Context(client, null, null);
            Assert.Throws<InvalidOperationException>(() => NeoScriptValueMarshaller.Normalize(client, NeoValueOwnership.Session,
                value, new PrimitiveTypeInfo { type = MemberKind.Float, required = true }, context, "argument"));
        }

        [Test]
        public void GenericSetEqualsMatchesCli()
        {
            using var client = NeoTestSaveStack.LoadClient(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/synth-example.json"));
            var body = Body("genericEquals");
            var context = new NSGetterEvaluator.Context(client, null, null);
            var a = NSGetterEvaluator.MakeRuntimeSet(new object?[] { 1d }, false, false, context);
            var b = NSGetterEvaluator.MakeRuntimeSet(new object?[] { 1d }, false, false, context);
            Assert.That(NSGetterEvaluator.Evaluate(body, context, new object?[] { a, b }), Is.EqualTo(true));
            a.Add(2d, 2d);
            Assert.That(NSGetterEvaluator.Evaluate(body, context, new object?[] { a, b }), Is.EqualTo(false));
            b.Add(2d, 2d);
            Assert.That(NSGetterEvaluator.Evaluate(body, context, new object?[] { a, b }), Is.EqualTo(true));
        }

        [Test]
        public void NumericSetKeysMatchAcrossNaNPayloadsAndBoxedTypes()
        {
            using var client = NeoTestSaveStack.LoadClient(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/synth-example.json"));
            var context = new NSGetterEvaluator.Context(client, null, null);
            double otherNaN = BitConverter.Int64BitsToDouble(0x7ff8000000000001L);
            var set = NSGetterEvaluator.MakeRuntimeSet(new object?[] { double.NaN, otherNaN, float.NaN, 1, 1d, 0d, -0d }, false, false, context);
            Assert.That(set.Count, Is.EqualTo(3));
            Assert.That(set.Contains(otherNaN), Is.True);
            Assert.That(set.Contains(float.NaN), Is.True);
            set.Remove(otherNaN);
            Assert.That(set.Count, Is.EqualTo(2));
            Assert.That(set.Contains(double.NaN), Is.False);
        }

        [TestCase("Vector2")]
        [TestCase("Vector3")]
        [TestCase("Color")]
        public void ComponentSetEqualityIncludesSignedZero(string kind)
        {
            using var client = NeoTestSaveStack.LoadClient(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/synth-example.json"));
            var context = new NSGetterEvaluator.Context(client, null, null);
            object Value(double zero, double last) => kind switch
            {
                "Vector2" => new NeoVector2Value { x = zero, y = last },
                "Vector3" => new NeoVector3Value { x = zero, y = 1, z = last },
                "Color" => new NeoColorValue { r = (float)zero, g = 0.25f, b = 0.5f, a = (float)last },
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
            double negativeZero = BitConverter.Int64BitsToDouble(long.MinValue);
            var set = NSGetterEvaluator.MakeRuntimeSet(new[] { Value(0, 0.5), Value(negativeZero, 0.5), Value(0, 0.75) }, false, false, context);
            Assert.That(set.Count, Is.EqualTo(2));
            Assert.That(set.Contains(Value(negativeZero, 0.5)), Is.True);
            Assert.That(set.Contains(Value(0, 0.75)), Is.True);
            Assert.That(set.Contains(Value(0, 1)), Is.False);
        }

        [TestCase("InfinitySet")]
        [TestCase("SignedZeroSet")]
        [TestCase("NaNContains")]
        [TestCase("OrdinarySet")]
        [TestCase("NaNDistinct")]
        [TestCase("NaNExpression")]
        [TestCase("NaNRemove")]
        [TestCase("NaNListContains")]
        [TestCase("NaNSet")]
        [TestCase("NaNAdd")]
        [TestCase("VectorSet")]
        [TestCase("VectorContains")]
        [TestCase("VectorDistinct")]
        [TestCase("Vector3Set")]
        [TestCase("TupleVectorSet")]
        [TestCase("decimalSetMutation")]
        [TestCase("decimalListRemove")]
        [TestCase("decimalIntMutation")]
        [TestCase("decimalNullable")]
        [TestCase("floatMutation")]
        [TestCase("floatDictionary")]
        [TestCase("floatRepeat")]
        [TestCase("stringControl")]
        [TestCase("dictionaryRead")]
        [TestCase("dictionaryAssignmentControl")]
        [TestCase("dictionaryId")]
        [TestCase("enumFirst")]
        [TestCase("enumFirstDefault")]
        [TestCase("decimalContains")]
        [TestCase("decimalIndexOf")]
        [TestCase("decimalToSet")]
        [TestCase("floatContains")]
        [TestCase("floatIndexOf")]
        [TestCase("floatBinding")]
        public void NewlyCompiledSourceMatchesCli(string probe)
        {
            using var client = NeoTestSaveStack.LoadClient(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/synth-example.json"));
            var body = Body(probe);
            Assert.That(NSGetterEvaluator.Evaluate(body, new NSGetterEvaluator.Context(client, null, null)), Is.EqualTo(true));
        }
    }
}
