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
