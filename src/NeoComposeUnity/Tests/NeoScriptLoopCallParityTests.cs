// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class NeoScriptLoopCallParityTests
    {
        private const string PackageRoot = "Packages/com.ryanbliss.neocompose/Tests";

        public static IEnumerable<string> CaseNames()
        {
            foreach (JToken testCase in Cases())
                yield return testCase["name"]!.Value<string>()!;
        }

        [TestCaseSource(nameof(CaseNames))]
        public void EvaluateMatchesSharedFixture(string name)
        {
            if (name.Contains("budget"))
                Assert.Ignore("Unity intentionally has no script execution budget; this case pins the web sandbox limit.");
            JObject testCase = null!;
            foreach (JObject candidate in Cases())
                if (candidate["name"]!.Value<string>() == name)
                    testCase = candidate;
            Assert.IsNotNull(testCase);
            var getter = JsonConvert.DeserializeObject<FunctionWithReturnType>(
                testCase["getter"]!.ToString())!;
            Assert.AreEqual(FunctionWithReturnType.CurrentCompilerRevision, getter.compilerRevision);
            NeoClient client = NeoTestSaveStack.LoadClient(
                File.ReadAllText(Path.Combine(PackageRoot, "synth-example.json")));
            var context = new NSGetterEvaluator.Context(client, null, null);
            if (testCase["expectedError"] is JToken error)
            {
                Exception actual = Assert.Catch<Exception>(
                    () => NSGetterEvaluator.Evaluate(getter, context))!;
                StringAssert.Contains(error.Value<string>(), actual.Message);
                return;
            }
            object? result = NSGetterEvaluator.Evaluate(getter, context);
            JToken actualToken = result is null ? JValue.CreateNull() : JToken.FromObject(result);
            Assert.That(TokensEqual(testCase["expected"]!, actualToken), Is.True,
                $"Case '{name}' expected {testCase["expected"]} but produced {actualToken}.");
        }

        [TestCase("while")]
        [TestCase("doWhile")]
        public void ConditionalLoopRejectsMalformedWireCondition(string kind)
        {
            Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<Instruction>(new JObject
                {
                    ["type"] = kind,
                    ["condition"] = new JObject(),
                    ["instructions"] = new JArray(),
                }.ToString()));
        }

        private static ValuePointer Text(string text) => new ValuePointer
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true },
                value = JToken.FromObject(text),
            },
        };

        [TestCase("search", true, "string.Replace requires a search argument.")]
        [TestCase("replacement", true, "string.Replace requires a replacement argument.")]
        [TestCase("search", false, "string.Replace search must be a string.")]
        [TestCase("replacement", false, "string.Replace replacement must be a string.")]
        public void ReplaceIdentifiesInvalidArgument(string argument, bool missing, string expected)
        {
            var info = new FunctionStringOpInfo
            {
                op = StringOpKind.Replace,
                receiverPointer = Text("abc"),
                argPointer = Text("a"),
                replacementPointer = Text("b"),
            };
            Pointer? invalid = missing ? null : new ValuePointer
            {
                type = PointerKind.Value,
                value = new Value
                {
                    typeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                    value = JToken.FromObject(1),
                },
            };
            if (argument == "search")
                info.argPointer = invalid;
            else
                info.replacementPointer = invalid;
            var pointer = new FunctionPointer
            {
                type = PointerKind.Function,
                function = new StringOpFunction { type = FunctionKind.StringOp, info = info },
            };
            NeoClient client = NeoTestSaveStack.LoadClient(
                File.ReadAllText(Path.Combine(PackageRoot, "synth-example.json")));
            var context = new NSGetterEvaluator.Context(client, null, null);
            var error = Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.EvaluatePointer(pointer, new NeoScriptScope(), context))!;
            Assert.AreEqual(expected, error.Message);
        }

        private static bool TokensEqual(JToken expected, JToken actual)
        {
            if ((expected.Type == JTokenType.Integer || expected.Type == JTokenType.Float)
                && (actual.Type == JTokenType.Integer || actual.Type == JTokenType.Float))
                return expected.Value<double>() == actual.Value<double>();
            if (expected is JArray expectedArray && actual is JArray actualArray)
            {
                if (expectedArray.Count != actualArray.Count)
                    return false;
                for (int i = 0; i < expectedArray.Count; i++)
                    if (!TokensEqual(expectedArray[i], actualArray[i]))
                        return false;
                return true;
            }
            return JToken.DeepEquals(expected, actual);
        }

        private static JArray Cases() =>
            (JArray)JObject.Parse(File.ReadAllText(Path.Combine(
                PackageRoot, "neoscript-loop-call-parity-fixture.json")))["evaluateCases"]!;
    }
}
