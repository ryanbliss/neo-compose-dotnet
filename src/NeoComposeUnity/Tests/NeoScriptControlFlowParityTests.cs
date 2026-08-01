// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// The Unity half of the P50-P52 raw-IR control-flow parity gate. The web
    /// test and this test evaluate the same hand-authored instruction stream,
    /// vendored verbatim as <see cref="NeoScriptControlFlowParityFixture"/>.
    /// Keeping compilation out of this gate ensures neither runtime can hide a
    /// disagreement in wire shape, execution order, loop transfers, collection
    /// snapshotting, or the shared iteration budget.
    /// </summary>
    public class NeoScriptControlFlowParityTests
    {
        private const string PackageRoot =
            "Packages/com.ryanbliss.neocompose/Tests";

        [Test]
        public void FixturePinsEveryP50P51AndP52ControlFlowBehavior()
        {
            string[] expectedNames =
            {
                "for consumes continue and break while updating an outer local",
                "reverse for loop executes its decrement iterator",
                "false initial condition consumes no iteration",
                "foreach snapshots list membership and preserves order under remove",
                "foreach dictionary binds values in collection order",
                "return escapes nested loops",
                "nested loops share the top-level iteration budget",
                "foreach dictionary uses JavaScript Object.values order for numeric-like keys",
                "foreach over an empty collection consumes no iteration",
                "foreach iterates a hand-authored derived Where collection",
                "foreach evaluates its derived collection receiver exactly once",
                "throw escapes a foreach body without visiting later entries",
                "foreach consumes ordered values through the Lookup collection contract",
                "switch matches an int stacked label and only the selected section writes",
                "switch matches a string label and propagates return",
                "switch matches a bool label",
                "switch matches an enum label by normalized option",
                "switch matches null for an optional selector",
                "switch matches null for an optional int selector",
                "switch runs default when no case matches",
                "switch without default falls through when no case matches",
                "switch consumes break and propagates continue to its enclosing for loop",
                "switch propagates throw from the selected section",
                "switch evaluates its derived selector exactly once",
                "switch-in-switch consumes inner break before completing the outer section",
                "loop-in-switch consumes loop break before completing the selected section",
                "try-inside-switch catches an authored error before the section breaks",
                "try selects the first true filter and skips later clauses",
                "try continues past false filters and the fallback catches",
                "try propagates the original error when no catch matches",
                "try treats a catchable filter error as false and preserves the original",
                "an error in a selected catch escapes siblings to an enclosing try",
                "return propagates through try without entering catches",
                "break and continue propagate through try to the enclosing for loop",
                "writes completed before a caught error remain visible",
                "try catches a deliberate arithmetic runtime error with its exact message",
                "try preserves an empty thrown message",
            };
            JArray cases = EvaluateCases();
            Assert.AreEqual(
                expectedNames.Length,
                cases.Count,
                "The shared control-flow fixture must contain the finalized 13 P50, 13 P51, and 11 P52 cases; re-vendor it from the web repo.");

            var names = new HashSet<string>();
            int errorCases = 0;
            foreach (JToken testCase in cases)
            {
                string name = Text(testCase, "name");
                Assert.IsTrue(names.Add(name), $"The shared fixture repeats case '{name}'.");
                if (testCase["expectedError"] is not null) errorCases++;

                FunctionWithReturnType getter = Getter((JObject)testCase, name);
                int caseIndex = Array.IndexOf(expectedNames, name);
                Assert.AreEqual(
                    caseIndex >= 26
                        ? 6
                        : caseIndex >= 13
                            ? 5
                            : 4,
                    getter.compilerRevision,
                    $"Case '{name}' must remain authored against its feature's wire revision.");
            }

            CollectionAssert.AreEquivalent(expectedNames, names);
            Assert.AreEqual(4, errorCases);
        }

        [Test]
        public void FixtureBytesMatchTheReviewedWebSource()
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(
                    NeoScriptControlFlowParityFixture.Json);
                string actual = BitConverter.ToString(
                        sha256.ComputeHash(bytes))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
                Assert.AreEqual(
                    "1f7690413eb59d22af11c68ed603f316b5ea64382d964315a4d0a893aea820b2",
                    actual,
                    "The vendored fixture bytes drifted from the reviewed web source.");
            }
        }

        [TestCaseSource(nameof(EvaluateCaseNames))]
        public void EvaluateCaseMatchesTheSharedFixture(string caseName)
        {
            JObject testCase = RequireCase(caseName);
            FunctionWithReturnType getter = Getter(testCase, caseName);
            NeoClient client = LoadClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);

            if (testCase["expectedError"] is JToken expectedError)
            {
                Exception error = Assert.Catch<Exception>(
                    () => NSGetterEvaluator.Evaluate(getter, ctx),
                    $"Case '{caseName}' was expected to throw.")!;
                StringAssert.Contains(
                    expectedError.Value<string>(),
                    error.Message,
                    $"Case '{caseName}' threw a message the shared fixture does not describe.");
                return;
            }

            JToken expected = testCase["expected"]
                ?? throw new InvalidOperationException(
                    $"Case '{caseName}' declares neither expected nor expectedError.");
            object? result = NSGetterEvaluator.Evaluate(getter, ctx);
            AssertTokenMatches(
                expected,
                result is null ? JValue.CreateNull() : JToken.FromObject(result),
                caseName);
        }

        private static void AssertTokenMatches(
            JToken expected,
            JToken actual,
            string path)
        {
            if (IsNumber(expected) && IsNumber(actual))
            {
                Assert.AreEqual(
                    expected.Value<double>(),
                    actual.Value<double>(),
                    $"'{path}' produced a different numeric result.");
                return;
            }

            if (expected is JArray expectedArray && actual is JArray actualArray)
            {
                Assert.AreEqual(
                    expectedArray.Count,
                    actualArray.Count,
                    $"'{path}' produced a different collection length.");
                for (int i = 0; i < expectedArray.Count; i++)
                {
                    AssertTokenMatches(
                        expectedArray[i],
                        actualArray[i],
                        $"{path}[{i}]");
                }
                return;
            }

            if (expected is JObject expectedObject && actual is JObject actualObject)
            {
                Assert.AreEqual(
                    expectedObject.Count,
                    actualObject.Count,
                    $"'{path}' produced a different object shape.");
                foreach (JProperty property in expectedObject.Properties())
                {
                    JToken? actualProperty = actualObject[property.Name];
                    Assert.IsNotNull(
                        actualProperty,
                        $"'{path}' produced no '{property.Name}' property.");
                    AssertTokenMatches(
                        property.Value,
                        actualProperty!,
                        $"{path}.{property.Name}");
                }
                return;
            }

            Assert.IsTrue(
                JToken.DeepEquals(expected, actual),
                $"'{path}' expected {expected} but produced {actual}.");
        }

        private static bool IsNumber(JToken token) =>
            token.Type == JTokenType.Integer || token.Type == JTokenType.Float;

        private static NeoClient LoadClient()
        {
            string json = File.ReadAllText(
                Path.Combine(PackageRoot, "synth-example.json"));
            return NeoTestSaveStack.LoadClient(json);
        }

        private static FunctionWithReturnType Getter(
            JObject testCase,
            string caseName)
        {
            JToken getter = testCase["getter"]
                ?? throw new InvalidOperationException(
                    $"Case '{caseName}' declares no getter.");
            return JsonConvert.DeserializeObject<FunctionWithReturnType>(getter.ToString())
                ?? throw new InvalidOperationException(
                    $"Case '{caseName}' declares a getter that did not deserialize.");
        }

        private static JObject Fixture() =>
            JObject.Parse(NeoScriptControlFlowParityFixture.Json);

        private static JArray EvaluateCases() =>
            (JArray)Fixture()["evaluateCases"]!;

        public static IEnumerable<string> EvaluateCaseNames()
        {
            foreach (JToken testCase in EvaluateCases())
            {
                yield return Text(testCase, "name");
            }
        }

        private static JObject RequireCase(string caseName)
        {
            foreach (JToken testCase in EvaluateCases())
            {
                if (Text(testCase, "name") == caseName) return (JObject)testCase;
            }
            throw new InvalidOperationException(
                $"The shared fixture declares no evaluate case named '{caseName}'.");
        }

        private static string Text(JToken token, string key) =>
            token[key]!.Value<string>()!;
    }
}
