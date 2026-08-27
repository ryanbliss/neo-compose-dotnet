// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P43 §6.1 / §12.3. The .NET half of the declared-constructor parity gate.
    ///
    /// <para>Construction is the one place two evaluators can implement every
    /// individual step correctly and still produce different instances, because
    /// the answer is an <b>order</b>: member initializers, then the base chain,
    /// then the body, then the call-site initializer block — and the last one
    /// wins even for a member the body already wrote. Prose cannot pin that, so
    /// the order is pinned by a hand-written fixture shared verbatim with
    /// <c>src/models/neoscript/neoscript-constructor-parity.test.ts</c> and
    /// vendored here as <see cref="NeoConstructorParityFixture"/>.</para>
    ///
    /// <para>Both halves interpret the fixture identically: build a client over
    /// <c>document</c>, evaluate each case's getter with <c>__this__</c> null
    /// and <c>__root__</c> derived from the project's three root members, then
    /// either read the produced record's <c>schemaKey → row value</c> pairs and
    /// compare them to <c>expectedFields</c>, or assert the thrown message
    /// contains <c>expectedErrorContains</c>.</para>
    /// </summary>
    public class NeoConstructorParityTests
    {
        // -------------------------------------------------------------------
        // Fixture shape. A case list that silently shrinks is the failure mode
        // this whole gate exists to prevent, so the shape is asserted too.
        // -------------------------------------------------------------------

        [Test]
        public void FixtureDeclaresAtLeastOneCaseOfEveryPinnedBehavior()
        {
            JArray cases = EvaluateCases();
            // 13 for P43's order and base-chain cases, plus P49 §1.5's three:
            // the required constructor's base clause and init body, the
            // call-site block beating the base-clause block, and the implicit
            // new being rejected, plus issue #280's nested collection case.
            Assert.GreaterOrEqual(
                cases.Count,
                17,
                "The shared fixture lost evaluate cases; re-vendor it from the web repo.");

            int errorCases = 0;
            foreach (JToken testCase in cases)
            {
                if (testCase["expectedErrorContains"] is not null) errorCases++;
            }
            Assert.GreaterOrEqual(
                errorCases,
                4,
                "The shared fixture lost its throwing cases; re-vendor it from the web repo.");
        }

        [Test]
        public void FixtureDocumentLoadsIntoTheRuntimeSchema()
        {
            NeoClient client = BuildClient();

            // Every constructor the fixture declares must be reachable through
            // the same accessor the evaluator resolves overloads with — a
            // fixture whose constructors did not load would make every
            // construction case fall back to the implicit `new()` and pass for
            // the wrong reason.
            foreach (JToken record in DocumentList("constructors"))
            {
                string id = Text(record, "id");
                Assert.IsTrue(
                    client.TryGetConstructor(id, out ConstructorRecord? resolved),
                    $"Constructor '{id}' did not load from the shared fixture.");
                Assert.AreEqual(
                    Text(record, "classId"),
                    resolved!.classId,
                    $"Constructor '{id}' loaded against the wrong class.");
            }
        }

        // -------------------------------------------------------------------
        // The gate itself: one NUnit case per fixture case.
        // -------------------------------------------------------------------

        [TestCaseSource(nameof(EvaluateCaseNames))]
        public void EvaluateCaseMatchesTheSharedFixture(string caseName)
        {
            JObject testCase = RequireCase(caseName);
            NeoClient client = BuildClient();
            NSGetterEvaluator.Context ctx = BuildContext(client);
            FunctionWithReturnType getter = Getter(testCase, caseName);

            if (testCase["expectedErrorContains"] is JToken expectedError)
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

            if (testCase["expectedFields"] is not JObject expectedFields)
            {
                throw new InvalidOperationException(
                    $"Case '{caseName}' declares neither expectedFields nor expectedErrorContains.");
            }

            object? result = NSGetterEvaluator.Evaluate(getter, ctx);
            ObjectMemberValue root = RequireConstructedRoot(client, ctx, result, caseName);
            foreach (JProperty expected in expectedFields.Properties())
            {
                AssertFieldMatches(client, root, expected, caseName);
            }
        }

        // -------------------------------------------------------------------
        // Assertions.
        // -------------------------------------------------------------------

        private static void AssertFieldMatches(
            NeoClient client,
            ObjectMemberValue root,
            JProperty expected,
            string caseName)
        {
            Assert.IsTrue(
                root.value!.TryGetValue(expected.Name, out string childId),
                $"Case '{caseName}' produced no '{expected.Name}'. Keys: {string.Join(",", root.value.Keys)}");
            Assert.IsTrue(
                client.TryGetValue(
                    NeoValueOwnership.Session,
                    childId,
                    out MemberValue? child),
                $"Case '{caseName}' produced no session row for '{expected.Name}'.");

            string message =
                $"Case '{caseName}' produced a different '{expected.Name}' than the shared fixture.";
            AssertValueMatches(client, child!, expected.Value, message);
        }

        private static void AssertValueMatches(
            NeoClient client,
            MemberValue actual,
            JToken expected,
            string message)
        {
            switch (expected.Type)
            {
                case JTokenType.String:
                    Assert.IsInstanceOf<StringMemberValue>(actual, message);
                    Assert.AreEqual(
                        expected.Value<string>(),
                        ((StringMemberValue)actual).value,
                        message);
                    return;
                case JTokenType.Integer:
                case JTokenType.Float:
                    Assert.IsInstanceOf<NumberMemberValue>(actual, message);
                    Assert.AreEqual(
                        expected.Value<double>(),
                        ((NumberMemberValue)actual).value,
                        message);
                    return;
                case JTokenType.Boolean:
                    Assert.IsInstanceOf<BoolMemberValue>(actual, message);
                    Assert.AreEqual(
                        expected.Value<bool>(),
                        ((BoolMemberValue)actual).value,
                        message);
                    return;
                case JTokenType.Null:
                    // P43 §6.1 step 4 — an explicit null in the call-site
                    // initializer block is an assignment, so the slot survives
                    // and its row is cleared. "No key at all" would be the
                    // wrong shape: that is what an OMITTED field produces.
                    Assert.IsTrue(
                        IsClearedRow(actual),
                        $"{message} Expected a cleared row, got {actual.GetType().Name} with a value.");
                    return;
                case JTokenType.Array:
                    Assert.IsInstanceOf<ArrayMemberValue>(actual, message);
                    string[]? entryIds = ((ArrayMemberValue)actual).value;
                    Assert.IsNotNull(entryIds, message);
                    var expectedEntries = (JArray)expected;
                    Assert.AreEqual(expectedEntries.Count, entryIds!.Length, message);
                    for (int i = 0; i < entryIds.Length; i++)
                    {
                        Assert.IsTrue(
                            client.TryGetValue(
                                NeoValueOwnership.Session,
                                entryIds[i],
                                out MemberValue? entry),
                            $"{message} Collection entry {i} has no session row.");
                        AssertValueMatches(
                            client,
                            entry!,
                            expectedEntries[i],
                            $"{message} Collection entry {i} differs.");
                    }
                    return;
                case JTokenType.Object:
                    Assert.IsInstanceOf<ObjectMemberValue>(actual, message);
                    Dictionary<string, string>? record =
                        ((ObjectMemberValue)actual).value;
                    Assert.IsNotNull(record, message);
                    foreach (JProperty property in ((JObject)expected).Properties())
                    {
                        Assert.IsTrue(
                            record!.TryGetValue(property.Name, out string childId),
                            $"{message} Nested object has no '{property.Name}'.");
                        Assert.IsTrue(
                            client.TryGetValue(
                                NeoValueOwnership.Session,
                                childId,
                                out MemberValue? child),
                            $"{message} Nested field '{property.Name}' has no session row.");
                        AssertValueMatches(
                            client,
                            child!,
                            property.Value,
                            $"{message} Nested field '{property.Name}' differs.");
                    }
                    return;
                default:
                    throw new InvalidOperationException(
                        $"The shared fixture expects a {expected.Type}, which this harness cannot compare. Teach both halves the new shape together.");
            }
        }

        /// <summary>
        /// Whether a stored row carries "no value" — the shape an explicitly
        /// nulled optional member ends up with, whatever its declared kind.
        /// </summary>
        private static bool IsClearedRow(MemberValue row)
        {
            return row switch
            {
                NullMemberValue => true,
                StringMemberValue value => value.value is null,
                NumberMemberValue value => value.value is null,
                BoolMemberValue value => value.value is null,
                ObjectMemberValue value => value.value is null,
                ArrayMemberValue value => value.value is null,
                SpriteMemberValue value => value.value is null,
                FileMemberValue value => value.value is null,
                _ => false,
            };
        }

        private static ObjectMemberValue RequireConstructedRoot(
            NeoClient client,
            NSGetterEvaluator.Context ctx,
            object? result,
            string caseName)
        {
            Assert.IsNotNull(result, $"Case '{caseName}' produced no value.");
            string? valueId = NSGetterEvaluator.FindRowIdByReference(result, ctx);
            Assert.IsNotNull(
                valueId,
                $"Case '{caseName}' produced a value with no backing row.");
            Assert.IsTrue(
                client.TryGetValue(
                    NeoValueOwnership.Session,
                    valueId!,
                    out ObjectMemberValue? row),
                $"Case '{caseName}' produced row '{valueId}', which is not a session class row.");
            return row!;
        }

        // -------------------------------------------------------------------
        // Harness.
        // -------------------------------------------------------------------

        /// <summary>
        /// <c>__root__</c> is derived from the loaded document rather than
        /// stored in the fixture — exactly as the web half derives it from the
        /// project's three root members — so the fixture cannot drift from
        /// either runtime's own notion of the root record.
        /// </summary>
        private static NSGetterEvaluator.Context BuildContext(NeoClient client)
        {
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            return ctx.WithRoot(NeoScriptValueMarshaller.ResolveRoot(client, ctx));
        }

        private static FunctionWithReturnType Getter(JObject testCase, string caseName)
        {
            JToken getter = testCase["getter"]
                ?? throw new InvalidOperationException(
                    $"Case '{caseName}' declares no getter.");
            return JsonConvert.DeserializeObject<FunctionWithReturnType>(getter.ToString())
                ?? throw new InvalidOperationException(
                    $"Case '{caseName}' declares a getter that did not deserialize.");
        }

        /// <summary>
        /// The fixture stores each record collection as a list so the JSON
        /// reads as authored source; the runtime schema is keyed by id. The
        /// conversion lives here rather than in the fixture so both halves
        /// consume the identical text.
        /// </summary>
        private static ProjectData BuildDocument()
        {
            JObject document = Document();
            var schema = new JObject
            {
                ["metadata"] = new JObject
                {
                    ["schemaVersion"] = NeoProjectExportContract.CurrentSchemaVersion,
                    ["projectId"] = "constructor-parity-project",
                    ["versionId"] = "constructor-parity-version",
                },
                ["project"] = document["project"],
                ["members"] = ById(document, "members"),
                ["values"] = ById(document, "values"),
                ["classes"] = ById(document, "classes"),
                ["constructors"] = ById(document, "constructors"),
                ["variantFolders"] = new JObject(),
                ["internalRecordRelations"] = new JObject(),
                ["enums"] = new JObject(),
            };
            return JsonConvert.DeserializeObject<ProjectData>(schema.ToString())
                ?? throw new InvalidOperationException(
                    "The shared constructor parity fixture's document did not deserialize.");
        }

        private static NeoClient BuildClient()
        {
            return NeoTestSaveStack.ClientFromSchema(BuildDocument());
        }

        private static JObject ById(JObject document, string collection)
        {
            var keyed = new JObject();
            foreach (JToken record in (JArray)document[collection]!)
            {
                keyed[Text(record, "id")] = record;
            }
            return keyed;
        }

        private static JObject Fixture() =>
            JObject.Parse(NeoConstructorParityFixture.Json);

        private static JObject Document() => (JObject)Fixture()["document"]!;

        private static JArray DocumentList(string collection) =>
            (JArray)Document()[collection]!;

        private static JArray EvaluateCases() => (JArray)Fixture()["evaluateCases"]!;

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
