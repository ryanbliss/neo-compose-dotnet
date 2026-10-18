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
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P115 §4.5. The .NET half of the string library and number text parity
    /// gate. Mono and .NET disagree with the web by default on whitespace,
    /// midpoint rounding, and number formatting, so <see cref="NeoText"/>
    /// implements every member by hand and this fixture, shared verbatim with
    /// <c>packages/models/src/neoscript/text-parity.test.ts</c>, keeps the two
    /// in step.
    /// </summary>
    public class NeoScriptTextParityTests
    {
        [Test]
        public void EveryEvaluateCaseProducesTheSharedResult()
        {
            var ctx = new NSGetterEvaluator.Context(BuildClient(), null, null);

            // Collect divergences and assert once at the end, the way
            // NeoScriptMathParityTests does.
            var failures = new List<string>();
            foreach (JObject testCase in Cases())
            {
                string name = testCase["name"]!.Value<string>()!;
                object? produced;
                try
                {
                    produced = Evaluate(ctx, testCase);
                }
                catch (NSGetterRuntimeError error)
                {
                    string? expectedMessage = testCase["expectedError"]?.Value<string>();
                    if (error.Message != expectedMessage)
                    {
                        failures.Add(
                            $"[{name}] threw '{error.Message}', expected {Describe(testCase)}");
                    }
                    continue;
                }
                string? divergence = Diverges(testCase, produced);
                if (divergence is not null)
                    failures.Add($"[{name}] {divergence}");
            }

            if (failures.Count > 0)
            {
                Assert.Fail(
                    $"{failures.Count} divergence(s) from the shared text fixture:\n" +
                    string.Join("\n", failures));
            }
        }

        /// <summary>
        /// The vendored copy must still be the whole fixture, so a re-vendoring
        /// that dropped or doubled a case fails instead of shrinking the gate.
        /// </summary>
        [Test]
        public void EveryCaseCarriesExactlyOneExpectation()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            int caseCount = 0;
            foreach (JObject testCase in Cases())
            {
                caseCount++;
                string name = testCase["name"]!.Value<string>()!;
                Assert.IsTrue(names.Add(name), $"Case '{name}' is named twice.");
                int expectations =
                    (testCase.ContainsKey("expected") ? 1 : 0) +
                    (testCase.ContainsKey("expectedList") ? 1 : 0) +
                    (testCase.ContainsKey("expectedError") ? 1 : 0);
                Assert.AreEqual(1, expectations, $"Case '{name}' must carry exactly one expectation.");
            }
            Assert.AreEqual(354, caseCount, "The vendored text fixture lost or gained cases.");
        }

        [Test]
        public void EveryTextOpKindAppearsInTheFixture()
        {
            var covered = new HashSet<string>(StringComparer.Ordinal);
            foreach (JObject testCase in Cases())
            {
                JToken info = testCase["pointer"]!["function"]!["info"]!;
                covered.Add(info["op"]!.Value<string>()! + "/" + info["number"]?.Value<string>());
            }
            var required = new List<string>
            {
                TextOpKind.Length + "/",
                TextOpKind.Substring + "/",
                TextOpKind.IndexOf + "/",
                TextOpKind.LastIndexOf + "/",
                TextOpKind.Split + "/",
                TextOpKind.PadLeft + "/",
                TextOpKind.PadRight + "/",
                TextOpKind.TrimStart + "/",
                TextOpKind.TrimEnd + "/",
                TextOpKind.Join + "/",
                TextOpKind.IsNullOrEmpty + "/",
                TextOpKind.IsNullOrWhiteSpace + "/",
                StringOpKind.Trim + "/",
                StringOpKind.Replace + "/",
                StringOpKind.StartsWith + "/",
                StringOpKind.EndsWith + "/",
            };
            foreach (string op in new[] { TextOpKind.Parse, TextOpKind.TryParse, TextOpKind.Format })
            {
                foreach (string number in new[] { NeoText.Int, NeoText.Float, NeoText.Decimal })
                    required.Add(op + "/" + number);
            }
            foreach (string key in required)
                Assert.IsTrue(covered.Contains(key), $"No fixture case covers '{key}'.");
        }

        /// <summary>
        /// P115 push round trip: a body using <c>Split</c>,
        /// <c>int.TryParse</c>, and <c>ToString("N2")</c>, as the real commit
        /// path stored it and <c>neo export</c> wrote it, evaluates to the text
        /// the web produced.
        /// </summary>
        [Test]
        public void ExportedBodyEvaluatesToTheWebText()
        {
            var export = JObject.Parse(File.ReadAllText(
                "Packages/com.ryanbliss.neocompose/Tests/text-parity-export-getter.json"));
            var getter = JsonConvert.DeserializeObject<FunctionWithReturnType>(export["getter"]!.ToString())!;
            var ctx = new NSGetterEvaluator.Context(BuildClient(), null, null);

            Assert.AreEqual(export["expected"]!.Value<string>(), NSGetterEvaluator.Evaluate(getter, ctx));
        }

        // ------------------------------------------------------------------
        // Harness.
        // ------------------------------------------------------------------

        private static object? Evaluate(NSGetterEvaluator.Context ctx, JObject testCase)
        {
            return NSGetterEvaluator.Evaluate(
                new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
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

        /// <returns>Why <paramref name="produced"/> misses the case's expectation, or null.</returns>
        private static string? Diverges(JObject testCase, object? produced)
        {
            if (testCase.ContainsKey("expectedError"))
                return $"expected {Describe(testCase)}, got {Show(produced)}";
            if (testCase["expectedList"] is JArray list)
            {
                if (produced is not object?[] entries || entries.Length != list.Count)
                    return $"expected {Describe(testCase)}, got {Show(produced)}";
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i] is not string entry || entry != list[i].Value<string>())
                        return $"entry {i}: expected \"{list[i]}\", got {Show(entries[i])}";
                }
                return null;
            }
            JToken expected = testCase["expected"]!;
            bool matches = expected.Type switch
            {
                JTokenType.Null => produced is null,
                JTokenType.String => produced is string text && text == expected.Value<string>(),
                JTokenType.Boolean => produced is bool flag && flag == expected.Value<bool>(),
                _ => NSGetterEvaluator.TryAsDouble(produced, out double number)
                    && number == expected.Value<double>(),
            };
            return matches ? null : $"expected {Describe(testCase)}, got {Show(produced)}";
        }

        private static string Describe(JObject testCase)
        {
            if (testCase["expectedError"] is JToken error)
                return $"error '{error.Value<string>()}'";
            if (testCase["expectedList"] is JToken list)
                return list.ToString(Newtonsoft.Json.Formatting.None);
            return testCase["expected"]!.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string Show(object? value)
        {
            if (value is null)
                return "null";
            if (value is string text)
                return $"\"{text}\"";
            if (value is object?[] entries)
                return $"[{string.Join(", ", Array.ConvertAll(entries, Show))}]";
            return $"{value} ({value.GetType().Name})";
        }

        private static IEnumerable<JObject> Cases()
        {
            var fixture = JObject.Parse(NeoScriptTextParityFixture.Json);
            return ((JArray)fixture["evaluateCases"]!).Values<JObject>();
        }

        /// <summary>
        /// A bare client is enough: every text op reads its operands and no
        /// project state.
        /// </summary>
        private static NeoClient BuildClient()
        {
            var rootClass = new NeoSchemaClass
            {
                id = "class-root",
                projectId = "project-text-parity",
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            return NeoTestSaveStack.ClientFromSchema(new ProjectData
            {
                project = new Project
                {
                    id = "project-text-parity",
                    _id = "project-text-parity",
                    name = "Text parity",
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
            }.WithUserRoot());
        }

        private static ClassMember RootMember(string id, string valueId, string classId)
        {
            return new ClassMember
            {
                id = id,
                projectId = "project-text-parity",
                name = id,
                kind = MemberKind.Class,
                Requirement = NeoMemberRequirementKind.Required,
                valueId = valueId,
                DeclaredClassId = classId,
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
