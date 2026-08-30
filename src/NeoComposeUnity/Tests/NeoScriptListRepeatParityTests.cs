// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P71 §11. The .NET half of the <c>List.Repeat</c> parity gate.
    ///
    /// <para>Nothing structural forces the two runtimes' <c>listRepeat</c>
    /// arms to stay in step — entry count, entry value, and the negative-count
    /// error text are each an independent decision on each side — so the
    /// agreement is pinned by a hand-written fixture shared verbatim with
    /// <c>src/models/neoscript/list-repeat-parity.test.ts</c>.</para>
    ///
    /// <para>Each case carries the compiled <c>listRepeat</c> IR both runtimes
    /// receive; this side wraps it in a getter whose return type is the case's
    /// <c>typeInfo</c> and asserts the produced list entry-for-entry, or the
    /// byte-exact error message. Once-evaluation, shared-reference entries, and
    /// budget exhaustion are not expressible in raw IR and live in
    /// <see cref="P71ListRepeatTests"/> instead.</para>
    /// </summary>
    public class NeoScriptListRepeatParityTests
    {
        [Test]
        public void EveryEvaluateCaseProducesTheSharedList()
        {
            var ctx = new NSGetterEvaluator.Context(BuildClient(), null, null);

            // Collect divergences and assert once at the end, the way
            // NeoScriptMathParityTests does: a systematic port regression is
            // far easier to read as one report than one case at a time.
            var failures = new List<string>();
            foreach (JObject testCase in EvaluateCases())
            {
                string name = Text(testCase, "name");
                if (testCase["expectedError"] is JToken expectedError)
                {
                    AssertThrewExactly(ctx, testCase, expectedError.Value<string>()!, name, failures);
                    continue;
                }

                object? produced;
                try
                {
                    produced = Evaluate(ctx, testCase);
                }
                catch (NSGetterRuntimeError error)
                {
                    failures.Add($"[{name}] threw unexpectedly: {error.Message}");
                    continue;
                }

                AssertListMatches(
                    (JArray)testCase["expectedList"]!,
                    produced,
                    name,
                    failures);
            }

            if (failures.Count > 0)
            {
                Assert.Fail(
                    $"{failures.Count} divergence(s) from the shared List.Repeat fixture:\n" +
                    string.Join("\n", failures));
            }
        }

        /// <summary>
        /// The vendored copy must still be the whole fixture: every case names
        /// itself uniquely and carries exactly one expectation. A re-vendoring
        /// that dropped or doubled a key would otherwise silently shrink the
        /// gate above rather than fail it.
        /// </summary>
        [Test]
        public void EveryCaseCarriesExactlyOneExpectation()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            int caseCount = 0;
            foreach (JObject testCase in EvaluateCases())
            {
                caseCount++;
                string name = Text(testCase, "name");
                Assert.IsTrue(names.Add(name), $"Case '{name}' is named twice.");
                int expectations =
                    (testCase["expectedList"] is null ? 0 : 1) +
                    (testCase["expectedError"] is null ? 0 : 1);
                Assert.AreEqual(
                    1,
                    expectations,
                    $"Case '{name}' must carry exactly one expectation.");
            }
            Assert.AreEqual(
                12,
                caseCount,
                "The vendored List.Repeat fixture lost or gained cases.");
        }

        /// <summary>
        /// Every case is a <c>listRepeat</c> node whose function info repeats
        /// the getter's own entry type — the post-join type the resolver
        /// inferred (P71 §2), which the IR carries so neither runtime
        /// re-derives the join.
        /// </summary>
        [Test]
        public void EveryCaseDeclaresTheSameEntryTypeOnTheGetterAndTheInfo()
        {
            foreach (JObject testCase in EvaluateCases())
            {
                string name = Text(testCase, "name");
                JToken function = testCase["pointer"]!["function"]!;
                Assert.AreEqual(
                    FunctionKind.ListRepeat,
                    function["type"]!.Value<string>(),
                    $"Case '{name}' is not a listRepeat node.");
                Assert.IsTrue(
                    JToken.DeepEquals(
                        function["info"]!["entryTypeInfo"],
                        testCase["typeInfo"]!["entryTypeInfo"]),
                    $"Case '{name}' disagrees with itself about the entry type.");
            }
        }

        // ------------------------------------------------------------------
        // Harness.
        // ------------------------------------------------------------------

        /// <summary>
        /// Evaluates a case's raw IR as `return &lt;pointer&gt;;` typed by the
        /// case's own <c>typeInfo</c> — the getter shape the fixture's
        /// <c>$evaluateComment</c> tells both consumers to build.
        /// </summary>
        private static object? Evaluate(NSGetterEvaluator.Context ctx, JObject testCase)
        {
            return NSGetterEvaluator.Evaluate(
                new FunctionWithReturnType
                {
                    parameters = Array.Empty<Variable>(),
                    instructions = new Instruction[]
                    {
                        new ReturnInstruction
                        {
                            type = InstructionKind.Return,
                            pointer = testCase["pointer"]!.ToObject<Pointer>()!,
                        },
                    },
                    typeInfo = testCase["typeInfo"]!.ToObject<TypeInfo>()!,
                },
                ctx);
        }

        /// <summary>
        /// Entry-for-entry comparison: numbers normalize to <c>double</c>
        /// (the TypeScript evaluator has nothing else, while this one can
        /// surface a <c>long</c> straight off an Int literal pointer), and
        /// strings and bools compare exactly.
        /// </summary>
        private static void AssertListMatches(
            JArray expected,
            object? produced,
            string caseName,
            List<string> failures)
        {
            if (produced is not IList entries || produced is string)
            {
                failures.Add($"[{caseName}] expected a list, got {Describe(produced)}");
                return;
            }
            if (entries.Count != expected.Count)
            {
                failures.Add(
                    $"[{caseName}] expected {expected.Count} entries, got {entries.Count}");
                return;
            }
            for (int index = 0; index < expected.Count; index++)
            {
                JToken want = expected[index];
                object? got = entries[index];
                switch (want.Type)
                {
                    case JTokenType.String:
                        if (got as string != want.Value<string>())
                        {
                            failures.Add(
                                $"[{caseName}] entry {index}: expected \"{want.Value<string>()}\", " +
                                $"got {Describe(got)}");
                        }
                        break;
                    case JTokenType.Boolean:
                        if (got is not bool flag || flag != want.Value<bool>())
                        {
                            failures.Add(
                                $"[{caseName}] entry {index}: expected {want.Value<bool>()}, " +
                                $"got {Describe(got)}");
                        }
                        break;
                    default:
                        if (!TryAsDouble(got, out double actual)
                            || actual != want.Value<double>())
                        {
                            failures.Add(
                                $"[{caseName}] entry {index}: expected {want.Value<double>()}, " +
                                $"got {Describe(got)}");
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Error messages are compared byte-for-byte, never by substring: they
        /// are the contract the fixture shares with the web evaluator, and a
        /// negative count is an ordinary authored-catchable evaluator error
        /// (P71 §5.3), never a host exception.
        /// </summary>
        private static void AssertThrewExactly(
            NSGetterEvaluator.Context ctx,
            JObject testCase,
            string expectedMessage,
            string caseName,
            List<string> failures)
        {
            try
            {
                object? produced = Evaluate(ctx, testCase);
                failures.Add(
                    $"[{caseName}] expected error '{expectedMessage}', got {Describe(produced)}");
            }
            catch (NSGetterRuntimeError error)
            {
                if (error.Message != expectedMessage)
                {
                    failures.Add(
                        $"[{caseName}] error message mismatch:\n" +
                        $"    expected: {expectedMessage}\n" +
                        $"    actual:   {error.Message}");
                }
            }
        }

        private static bool TryAsDouble(object? value, out double result)
        {
            switch (value)
            {
                case double d: result = d; return true;
                case float f: result = f; return true;
                case long l: result = l; return true;
                case int i: result = i; return true;
                default: result = 0; return false;
            }
        }

        private static string Describe(object? value)
        {
            if (value is null) return "null";
            if (value is string text) return $"\"{text}\"";
            return $"{value} ({value.GetType().Name})";
        }

        private static IEnumerable<JObject> EvaluateCases()
        {
            var fixture = JObject.Parse(NeoScriptListRepeatParityFixture.Json);
            return ((JArray)fixture["evaluateCases"]!).Values<JObject>();
        }

        private static string Text(JToken token, string key)
        {
            return token[key]!.Value<string>()!;
        }

        /// <summary>
        /// The evaluator needs a client for its context and nothing more:
        /// every fixture case is literals and arithmetic, which is the
        /// property the fixture is here to keep true.
        /// </summary>
        private static NeoClient BuildClient()
        {
            var rootClass = new NeoSchemaClass
            {
                id = "class-root",
                projectId = "project-list-repeat-parity",
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            return NeoTestSaveStack.ClientFromSchema(new ProjectData
            {
                project = new Project
                {
                    id = "project-list-repeat-parity",
                    _id = "project-list-repeat-parity",
                    name = "List.Repeat parity",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = new Dictionary<string, JsonMember>
                {
                    ["root-assets"] = RootMember("root-assets", "root-assets-value", rootClass.id),
                    ["root-save"] = RootMember("root-save", "root-save-value", rootClass.id),
                    ["root-session"] = RootMember("root-session", "root-session-value", rootClass.id),
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["root-assets-value"] = ObjectValue("root-assets-value", rootClass.id),
                    ["root-save-value"] = ObjectValue("root-save-value", rootClass.id),
                    ["root-session-value"] = ObjectValue("root-session-value", rootClass.id),
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [rootClass.id] = rootClass,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            });
        }

        private static ClassMember RootMember(string id, string valueId, string classId)
        {
            return new ClassMember
            {
                id = id,
                projectId = "project-list-repeat-parity",
                name = id,
                kind = MemberKind.Class,
                Requirement = NeoMemberRequirementKind.Required,
                valueId = valueId,
                classId = classId,
            };
        }

        private static ObjectMemberValue ObjectValue(string id, string classId)
        {
            return new ObjectMemberValue
            {
                id = id,
                classId = classId,
                value = new Dictionary<string, string>(),
            };
        }
    }
}
