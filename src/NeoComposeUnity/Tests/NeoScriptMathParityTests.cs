// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
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
    /// P69 §3. The .NET half of the <c>Math</c> builtin parity gate.
    ///
    /// <para><c>Math</c> is the one NeoScript intrinsic whose two hosts
    /// disagree by default — JS <c>Math.round</c> is half-up while
    /// <c>System.Math.Round</c> is half-even — so the spec pins one semantic
    /// per operation and the divergent host implements it by hand. Nothing
    /// structural forces the two implementations to stay in step, so the
    /// agreement is pinned by a hand-written fixture shared verbatim with
    /// <c>src/models/neoscript/math-parity.test.ts</c>.</para>
    ///
    /// <para>Each case carries the compiled <c>mathOp</c> IR both runtimes
    /// receive; this side wraps it in a getter whose return type is the case's
    /// <c>typeInfo</c> and asserts the produced number, the NaN result, or the
    /// byte-exact error message. The <c>Math</c> arms read no project state,
    /// which is exactly what makes a bare client enough here.</para>
    /// </summary>
    public class NeoScriptMathParityTests
    {
        [Test]
        public void EveryEvaluateCaseProducesTheSharedResult()
        {
            var ctx = new NSGetterEvaluator.Context(BuildClient(), null, null);

            // Collect divergences and assert once at the end, the way
            // NSGetterParityTests does: a systematic port regression across 80+
            // cases is far easier to read as one report than one case at a time.
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

                if (testCase["expectedNaN"] is not null)
                {
                    if (produced is not double nan || !double.IsNaN(nan))
                    {
                        failures.Add($"[{name}] expected NaN, got {Describe(produced)}");
                    }
                    continue;
                }

                JToken expected = testCase["expected"]!;
                if (expected.Type == JTokenType.String)
                {
                    // Decimal arms produce canonical decimal strings, and the
                    // spelling is the assertion: "1.10" and "1.1" are equal
                    // decimals that Min must not swap for one another.
                    if (produced is not string decimalText
                        || decimalText != expected.Value<string>())
                    {
                        failures.Add(
                            $"[{name}] expected decimal \"{expected.Value<string>()}\", got {Describe(produced)}");
                    }
                    continue;
                }

                if (!TryAsDouble(produced, out double actual))
                {
                    failures.Add($"[{name}] expected a number, got {Describe(produced)}");
                    continue;
                }
                if (actual != expected.Value<double>())
                {
                    failures.Add(
                        $"[{name}] expected {expected.Value<double>()}, got {actual}");
                }
            }

            if (failures.Count > 0)
            {
                Assert.Fail(
                    $"{failures.Count} divergence(s) from the shared math fixture:\n" +
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
                    (testCase["expected"] is null ? 0 : 1) +
                    (testCase["expectedNaN"] is null ? 0 : 1) +
                    (testCase["expectedError"] is null ? 0 : 1);
                Assert.AreEqual(
                    1,
                    expectations,
                    $"Case '{name}' must carry exactly one expectation.");
            }
            Assert.Greater(caseCount, 0, "The vendored math fixture has no cases.");
        }

        /// <summary>
        /// Every <see cref="MathOpKind"/> the compiler can emit is exercised
        /// above — the acceptance criterion that all ten functions evaluate on
        /// both runtimes, checked against the constants rather than a count.
        /// </summary>
        [Test]
        public void EveryMathOpKindAppearsInTheFixture()
        {
            var covered = new HashSet<string>(StringComparer.Ordinal);
            foreach (JObject testCase in EvaluateCases())
            {
                covered.Add(testCase["pointer"]!["function"]!["info"]!["op"]!.Value<string>()!);
            }
            foreach (string op in new[]
            {
                MathOpKind.Min,
                MathOpKind.Max,
                MathOpKind.Clamp,
                MathOpKind.Round,
                MathOpKind.Floor,
                MathOpKind.Ceiling,
                MathOpKind.Truncate,
                MathOpKind.Abs,
                MathOpKind.Sign,
                MathOpKind.Sqrt,
            })
            {
                Assert.IsTrue(covered.Contains(op), $"No fixture case covers '{op}'.");
            }
        }

        // ------------------------------------------------------------------
        // Harness.
        // ------------------------------------------------------------------

        /// <summary>
        /// Evaluates a case's raw IR as `return &lt;pointer&gt;;` typed by the
        /// case's own <c>typeInfo</c> — the getter shape the fixture's
        /// <c>$evaluateComment</c> tells both consumers to build. The return
        /// type matters: a Decimal-typed getter canonicalizes a numeric return
        /// at the return seam.
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
        /// Error messages are compared byte-for-byte, never by substring: they
        /// are the contract the fixture shares with the web evaluator, and P69
        /// §3 keeps every numeric value out of them precisely so the two hosts'
        /// number formatting can never leak into the comparison.
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

        /// <summary>
        /// Numbers normalize to <c>double</c> before comparison, the same
        /// coercion the NSGetter parity harness applies: the TypeScript
        /// evaluator has nothing but doubles, while this one can surface a
        /// <c>long</c> straight off an Int literal pointer.
        /// </summary>
        private static bool TryAsDouble(object? value, out double result)
        {
            switch (value)
            {
                case double d: result = d; return true;
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
            var fixture = JObject.Parse(NeoScriptMathParityFixture.Json);
            return ((JArray)fixture["evaluateCases"]!).Values<JObject>();
        }

        private static string Text(JToken token, string key)
        {
            return token[key]!.Value<string>()!;
        }

        /// <summary>
        /// The evaluator needs a client for its context and nothing more: every
        /// <c>mathOp</c> arm reads its operands and no project state at all,
        /// which is the property this fixture is here to keep true.
        /// </summary>
        private static NeoClient BuildClient()
        {
            var rootClass = new NeoSchemaClass
            {
                id = "class-root",
                projectId = "project-math-parity",
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            return NeoTestSaveStack.ClientFromSchema(new ProjectData
            {
                project = new Project
                {
                    id = "project-math-parity",
                    _id = "project-math-parity",
                    name = "Math parity",
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
                projectId = "project-math-parity",
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
