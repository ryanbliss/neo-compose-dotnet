// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Tests
{
    /// <summary>
    /// TS-vs-C# parity for the NSGetter evaluator. The companion script
    /// <c>scripts/dump-nsgetter-expected.ts</c> drives the TS-side
    /// <c>evaluateNSGetter</c> against every NSGetter in the real-world
    /// <c>project-example.json</c> fixture (and every plausible
    /// <c>__this__</c> binding for it), then writes the produced
    /// values to <c>nsgetter-expected.json</c>. This test loads the
    /// same fixture + the dump and asserts that the SDK's
    /// <see cref="NeoAttributeNSGetter.Compute(object?)"/> produces
    /// identical results entry-by-entry.
    ///
    /// <para>Re-run the dump script after touching the TS evaluator;
    /// the C# test becomes the parity gate.</para>
    /// </summary>
    public class NSGetterParityTests
    {
        private const string PackageRoot =
            "Packages/com.ryanbliss.neocompose/Tests";

        private static string LoadFixture(string fileName)
        {
            return File.ReadAllText(Path.Combine(PackageRoot, fileName));
        }

        private static NeoClient LoadProjectClient()
        {
            var loader = new NeoLoader();
            string saveBuffer = "";
            string loadSave() => saveBuffer;
            void handleSave(string file) => saveBuffer = file;
            return loader.Load(LoadFixture("project-example.json"), loadSave, handleSave);
        }

        private class ExpectedEntry
        {
            public string attributeId { get; set; } = "";
            public string? thisValueId { get; set; }
            public bool ok { get; set; }
            public JToken? value { get; set; }
            public string? error { get; set; }
        }

        private static List<ExpectedEntry> LoadExpected()
        {
            var json = LoadFixture("nsgetter-expected.json");
            var entries = JsonConvert.DeserializeObject<List<ExpectedEntry>>(json);
            Assert.IsNotNull(entries, "nsgetter-expected.json failed to parse");
            return entries!;
        }

        [Test]
        public void Compute_MatchesTypeScriptExpectations_ForEveryEntry()
        {
            var client = LoadProjectClient();
            var expected = LoadExpected();
            Assert.Greater(expected.Count, 0, "Expected dump shouldn't be empty");

            // Collect mismatches and assert at the end so a single test
            // run reports every divergence — easier to debug a port
            // regression than getting one failure at a time.
            var failures = new List<string>();
            int matched = 0;
            foreach (var entry in expected)
            {
                if (!client.TryGetAttribute(entry.attributeId, out NSGetterAttribute? attr))
                {
                    failures.Add($"[{entry.attributeId}] not found in fixture");
                    continue;
                }
                var node = new NeoAttributeNSGetter(client, attr, null);
                // Use the row-id overload so the receiver is unwrapped
                // through the evaluator's per-context cache + reverse
                // index — required for `is`-checks against Custom types
                // and runtime-override dispatch on `this` directly.
                var actual = string.IsNullOrEmpty(entry.thisValueId)
                    ? node.Compute()
                    : node.Compute(entry.thisValueId!);

                string label = $"[{entry.attributeId} | this={entry.thisValueId ?? "null"}]";
                if (entry.ok)
                {
                    if (!actual.ok)
                    {
                        failures.Add($"{label} expected ok, got error: {actual.error}");
                        continue;
                    }
                    var expectedJson = NormalizeForCompare(entry.value);
                    var actualJson = NormalizeForCompare(actual.value);
                    if (expectedJson != actualJson)
                    {
                        failures.Add(
                            $"{label} value mismatch:\n" +
                            $"    expected: {expectedJson}\n" +
                            $"    actual:   {actualJson}");
                        continue;
                    }
                }
                else
                {
                    if (actual.ok)
                    {
                        failures.Add(
                            $"{label} expected error '{entry.error}', got ok with value {NormalizeForCompare(actual.value)}");
                        continue;
                    }
                    if (actual.error != entry.error)
                    {
                        failures.Add(
                            $"{label} error message mismatch:\n" +
                            $"    expected: {entry.error}\n" +
                            $"    actual:   {actual.error}");
                        continue;
                    }
                }
                matched++;
            }

            if (failures.Count > 0)
            {
                string summary = $"{matched}/{expected.Count} matched. {failures.Count} divergence(s):\n" +
                    string.Join("\n", failures);
                Assert.Fail(summary);
            }
        }

        /// <summary>
        /// Canonicalise a value for cross-runtime equality. The TS
        /// evaluator emits JSON via <c>JSON.stringify</c>; the SDK
        /// evaluator returns CLR objects (<c>object?[]</c>,
        /// <c>Dictionary&lt;string, object?&gt;</c>, primitives). Round-
        /// tripping both sides through Newtonsoft's serializer with
        /// the same settings gives a stable string form for comparison.
        ///
        /// <para>One coercion: TS numbers are always JS doubles, so the
        /// expected JSON stores ints as floats (or bare ints depending
        /// on the value). The SDK returns <c>double</c> for
        /// arithmetic, which serializes as a JSON number — Newtonsoft
        /// emits <c>3.0</c> as <c>3.0</c> by default. Both sides read
        /// back as the same JToken comparison, so we use
        /// <see cref="JToken.DeepEquals"/> on the parsed output rather
        /// than literal string equality.</para>
        /// </summary>
        private static string NormalizeForCompare(object? value)
        {
            // Serialize → parse → re-serialize through JToken to get a
            // canonical form. This collapses int-vs-float-formatted
            // numbers into the same JToken.Float representation.
            string raw = JsonConvert.SerializeObject(value);
            JToken token = JToken.Parse(raw);
            return CanonicalizeToken(token).ToString(Formatting.None);
        }

        /// <summary>
        /// Walks a JToken and rewrites integer-typed numbers as floats
        /// so int vs double doesn't matter for comparison. TS
        /// represents 3 and 3.0 the same way; our evaluator returns
        /// double from arithmetic but might surface integers from
        /// other paths. Normalizing collapses the difference.
        /// </summary>
        private static JToken CanonicalizeToken(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Integer:
                    return new JValue((double)token.Value<long>());
                case JTokenType.Float:
                    return token;
                case JTokenType.Object:
                {
                    var obj = (JObject)token;
                    var result = new JObject();
                    // Sort keys so dict entry order doesn't matter.
                    var sortedKeys = obj.Properties().Select(p => p.Name).OrderBy(k => k, System.StringComparer.Ordinal);
                    foreach (var key in sortedKeys)
                    {
                        result[key] = CanonicalizeToken(obj[key]!);
                    }
                    return result;
                }
                case JTokenType.Array:
                {
                    var arr = (JArray)token;
                    var result = new JArray();
                    foreach (var item in arr) result.Add(CanonicalizeToken(item));
                    return result;
                }
                default:
                    return token;
            }
        }
    }
}
