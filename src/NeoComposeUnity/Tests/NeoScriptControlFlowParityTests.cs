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

        /// <summary>
        /// Every case in the shared fixture, in fixture order, paired with the
        /// wire revision its feature was authored against: 13 P50 cases at
        /// revision 4, 13 P51 cases at 5, and 11 P52 cases at 6.
        /// </summary>
        private static readonly (string Name, int Revision)[] ExpectedCases =
        {
            ("for consumes continue and break while updating an outer local", 4),
            ("reverse for loop executes its decrement iterator", 4),
            ("false initial condition consumes no iteration", 4),
            ("foreach snapshots list membership and preserves order under remove", 4),
            ("foreach dictionary binds values in collection order", 4),
            ("return escapes nested loops", 4),
            ("nested loops share the top-level iteration budget", 4),
            ("foreach dictionary uses JavaScript Object.values order for numeric-like keys", 4),
            ("foreach over an empty collection consumes no iteration", 4),
            ("foreach iterates a hand-authored derived Where collection", 4),
            ("foreach evaluates its derived collection receiver exactly once", 4),
            ("throw escapes a foreach body without visiting later entries", 4),
            ("foreach consumes ordered values through the Lookup collection contract", 4),
            ("switch matches an int stacked label and only the selected section writes", 5),
            ("switch matches a string label and propagates return", 5),
            ("switch matches a bool label", 5),
            ("switch matches an enum label by normalized option", 5),
            ("switch matches null for an optional selector", 5),
            ("switch matches null for an optional int selector", 5),
            ("switch runs default when no case matches", 5),
            ("switch without default falls through when no case matches", 5),
            ("switch consumes break and propagates continue to its enclosing for loop", 5),
            ("switch propagates throw from the selected section", 5),
            ("switch evaluates its derived selector exactly once", 5),
            ("switch-in-switch consumes inner break before completing the outer section", 5),
            ("loop-in-switch consumes loop break before completing the selected section", 5),
            ("try-inside-switch catches an authored error before the section breaks", 6),
            ("try selects the first true filter and skips later clauses", 6),
            ("try continues past false filters and the fallback catches", 6),
            ("try propagates the original error when no catch matches", 6),
            ("try treats a catchable filter error as false and preserves the original", 6),
            ("an error in a selected catch escapes siblings to an enclosing try", 6),
            ("return propagates through try without entering catches", 6),
            ("break and continue propagate through try to the enclosing for loop", 6),
            ("writes completed before a caught error remain visible", 6),
            ("try catches a deliberate arithmetic runtime error with its exact message", 6),
            ("try preserves an empty thrown message", 6),
        };

        [Test]
        public void FixturePinsEveryP50P51AndP52ControlFlowBehavior()
        {
            JArray cases = EvaluateCases();
            Assert.AreEqual(
                ExpectedCases.Length,
                cases.Count,
                "The shared control-flow fixture must contain the finalized 13 P50, 13 P51, and 11 P52 cases; re-vendor it from the web repo.");

            var expectedRevisions = new Dictionary<string, int>();
            foreach ((string caseName, int caseRevision) in ExpectedCases)
            {
                expectedRevisions.Add(caseName, caseRevision);
            }

            var names = new HashSet<string>();
            int errorCases = 0;
            foreach (JToken testCase in cases)
            {
                string name = Text(testCase, "name");
                Assert.IsTrue(names.Add(name), $"The shared fixture repeats case '{name}'.");
                if (testCase["expectedError"] is not null) errorCases++;

                Assert.IsTrue(
                    expectedRevisions.TryGetValue(name, out int expectedRevision),
                    $"The shared fixture declares an unlisted case '{name}'.");
                FunctionWithReturnType getter = Getter((JObject)testCase, name);
                Assert.AreEqual(
                    expectedRevision,
                    getter.compilerRevision,
                    $"Case '{name}' must remain authored against its feature's wire revision.");
            }

            CollectionAssert.AreEquivalent(expectedRevisions.Keys, names);
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

        [Test]
        public void P54BoundsStraightLineWorkAndResetsEachInvocation()
        {
            FunctionWithReturnType getter = SimpleIntGetter();
            NeoClient client = LoadClient();
            var ctx = new NSGetterEvaluator.Context(
                client,
                null,
                null,
                executionBudgetLimits: new NeoScriptExecutionBudgetLimits(
                    workUnits: 2));

            Assert.AreEqual(1d, NSGetterEvaluator.Evaluate(getter, ctx));
            Assert.AreEqual(1d, NSGetterEvaluator.Evaluate(getter, ctx));

            var exhausted = new NSGetterEvaluator.Context(
                client,
                null,
                null,
                executionBudgetLimits: new NeoScriptExecutionBudgetLimits(
                    workUnits: 1));
            NeoScriptResourceLimitError error =
                Assert.Throws<NeoScriptResourceLimitError>(
                    () => NSGetterEvaluator.Evaluate(getter, exhausted))!;
            Assert.AreEqual(
                "NeoScript work unit limit of 1 exceeded.",
                error.Message);
        }

        [Test]
        public void P54ResourceFaultEscapesAuthoredTryCatch()
        {
            JObject testCase = RequireCase(
                "try catches a deliberate arithmetic runtime error with its exact message");
            FunctionWithReturnType getter = Getter(testCase, "resource limit");
            var ctx = new NSGetterEvaluator.Context(
                LoadClient(),
                null,
                null,
                executionBudgetLimits: new NeoScriptExecutionBudgetLimits(
                    workUnits: 1));

            Assert.Throws<NeoScriptResourceLimitError>(
                () => NSGetterEvaluator.Evaluate(getter, ctx));
        }

        [Test]
        public void P54BoundsEvaluatorCreatedCollectionEntries()
        {
            const string json = @"{
              ""compilerRevision"": 1,
              ""parameters"": [],
              ""instructions"": [{
                ""type"": ""return"",
                ""pointer"": {
                  ""type"": ""listLiteral"",
                  ""typeInfo"": {
                    ""type"": 6,
                    ""required"": true,
                    ""entryTypeInfo"": { ""type"": 2, ""required"": true }
                  },
                  ""entries"": [
                    { ""type"": ""value"", ""value"": {
                      ""typeInfo"": { ""type"": 2, ""required"": true },
                      ""value"": 1
                    }},
                    { ""type"": ""value"", ""value"": {
                      ""typeInfo"": { ""type"": 2, ""required"": true },
                      ""value"": 2
                    }}
                  ]
                }
              }],
              ""typeInfo"": {
                ""type"": 6,
                ""required"": true,
                ""entryTypeInfo"": { ""type"": 2, ""required"": true }
              }
            }";
            FunctionWithReturnType getter =
                JsonConvert.DeserializeObject<FunctionWithReturnType>(json)!;
            var ctx = new NSGetterEvaluator.Context(
                LoadClient(),
                null,
                null,
                executionBudgetLimits: new NeoScriptExecutionBudgetLimits(
                    producedCollectionEntries: 1));

            NeoScriptResourceLimitError error =
                Assert.Throws<NeoScriptResourceLimitError>(
                    () => NSGetterEvaluator.Evaluate(getter, ctx))!;
            Assert.AreEqual(
                "NeoScript produced collection entry limit of 1 exceeded.",
                error.Message);
        }

        [Test]
        public void P54BoundsCollectionVisits()
        {
            const string caseName =
                "foreach snapshots list membership and preserves order under remove";
            FunctionWithReturnType getter = Getter(
                RequireCase(caseName),
                caseName);
            var ctx = new NSGetterEvaluator.Context(
                LoadClient(),
                null,
                null,
                executionBudgetLimits: new NeoScriptExecutionBudgetLimits(
                    collectionVisits: 1));

            NeoScriptResourceLimitError error =
                Assert.Throws<NeoScriptResourceLimitError>(
                    () => NSGetterEvaluator.Evaluate(getter, ctx))!;
            Assert.AreEqual(
                "NeoScript collection visit limit of 1 exceeded.",
                error.Message);
        }

        [Test]
        public void P54RejectsHostLimitsAboveTheSafetyCeiling()
        {
            ArgumentOutOfRangeException error =
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => new NeoScriptExecutionBudgetLimits(
                        workUnits:
                            NeoScriptExecutionBudgetLimits.DefaultWorkUnits + 1))!;
            StringAssert.Contains(
                "cannot exceed the safety ceiling of 100000",
                error.Message);
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

        private static FunctionWithReturnType SimpleIntGetter()
        {
            const string json = @"{
              ""compilerRevision"": 1,
              ""parameters"": [],
              ""instructions"": [{
                ""type"": ""return"",
                ""pointer"": {
                  ""type"": ""value"",
                  ""value"": {
                    ""typeInfo"": { ""type"": 2, ""required"": true },
                    ""value"": 1
                  }
                }
              }],
              ""typeInfo"": { ""type"": 2, ""required"": true }
            }";
            return JsonConvert.DeserializeObject<FunctionWithReturnType>(json)!;
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
