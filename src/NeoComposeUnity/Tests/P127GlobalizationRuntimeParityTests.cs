// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>Every B7 runtime fixture case, with the three specified Unity results.</summary>
    public sealed class P127GlobalizationRuntimeParityTests
    {
        private const string Root = "Packages/com.ryanbliss.neocompose/Tests/";
        private const string Fixture = Root + "p127-globalization-parity-fixture.json";
        private const string Export = Root + "p127-globalization-parity-export.json";

        public static IEnumerable<TestCaseData> Cases()
        {
            if (!File.Exists(Fixture))
            {
                yield return new TestCaseData(null, null, false, false).SetName("B7_MissingFixture");
                yield break;
            }
            JObject fixture = JObject.Parse(File.ReadAllText(Fixture));
            var unityDiff = new HashSet<string>(fixture["unityDiff"]!.Values<string>()!, StringComparer.Ordinal);
            int count = 0;
            foreach (string section in new[] { "returns", "refuses" })
                foreach (var test in fixture[section]!.Children<JProperty>())
                {
                    count++;
                    yield return new TestCaseData(test.Name, test.Value, section == "refuses", unityDiff.Contains(test.Name)).SetName("B7_" + test.Name);
                }
            if (count == 0)
                yield return new TestCaseData(null, null, false, false).SetName("B7_EmptyFixture");
        }

        [Test]
        public void UnityDifferenceTagsNameExactlyThreeReturnCases()
        {
            JObject fixture = JObject.Parse(File.ReadAllText(Fixture));
            string[] names = fixture["unityDiff"]!.Values<string>().ToArray()!;
            Assert.That(names.Length, Is.EqualTo(3));
            Assert.That(names.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(3));
            foreach (string name in names)
                Assert.That(((JObject)fixture["returns"]!).Property(name), Is.Not.Null, name);
        }

        [TestCaseSource(nameof(Cases))]
        public void RunsEverySharedCase(string? name, JToken? expected, bool refusal, bool unityDiff)
        {
            Assert.That(File.Exists(Fixture), Is.True, "Vendor Compose's final B7 source fixture.");
            Assert.That(File.Exists(Export), Is.True, "Vendor Compose's final B7 compiled export.");
            Assert.That(name, Is.Not.Null, "The shared fixture must contain runtime cases.");
            Assert.That(Directory.Exists(Root + "Fixtures~/P127GlobalizationParity/Generated"), Is.True,
                "Vendor the B8 generated artifact alongside its export.");
            Assert.That(File.Exists(Root + "Fixtures~/P127GlobalizationParity/NeoGeneratedFiles.json"), Is.True);
            using var client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText(Export)), assumeCurrentSchema: false);
            var probe = client.ProjectDataForRuntime.classes.Values.Single(type => type.name == "Probe");
            object? Invoke() => NeoMemberNSFunction.GetOrCreateStatic(client, probe.schema[name!]).InvokeStatic(Array.Empty<object?>());
            // Spec §14 and B7 contract change 1: run all three cases with
            // Unity's own CLR results. Every other row keeps the shared expectation.
            if (unityDiff)
            {
                Assert.That(refusal, Is.False);
                // The fixture selects the rows; their distinct .NET results
                // identify the three semantic differences authorized by §14.
                if (expected!.Type == JTokenType.String && expected.Value<string>() == "ivl")
                    expected = new JValue("IVL");
                else if (expected.Type == JTokenType.Integer && expected.Value<int>() == 0)
                    expected = new JValue(127);
                else if (expected.Type == JTokenType.Integer && expected.Value<int>() == 16)
                {
                    var mono = Assert.Catch<NSGetterRuntimeError>(() => Invoke())!;
                    Assert.That(mono.Payload.ClassId, Is.EqualTo(NeoErrorContract.NeoArgumentOutOfRangeError));
                    Assert.That(mono.DiagnosticMessage, Is.EqualTo("Valid values are between 0 and 15, inclusive.\nParameter name: CurrencyNegativePattern"));
                    return;
                }
                else
                    Assert.Fail($"Unknown Unity difference for {name}: {expected}");
            }
            if (refusal)
            {
                var error = Assert.Catch<NSGetterRuntimeError>(() => Invoke())!;
                Assert.That(error.DiagnosticMessage, Does.Contain(expected!.Value<string>()));
                Assert.That(error.Payload.Message, Is.EqualTo(error.DiagnosticMessage));
                return;
            }
            object? result = Invoke();
            JToken actual = result is null ? JValue.CreateNull() : JToken.FromObject(result);
            if (expected!.Type is JTokenType.Integer or JTokenType.Float)
            {
                bool single = NeoNSFunctionRuntime.ResolveSignature(client, probe.schema[name!]).ReturnTypeInfo.type == MemberKind.Float;
                double number = single ? (double)(float)expected.Value<double>() : expected.Value<double>();
                Assert.That(actual.Value<double>(), Is.EqualTo(number), name);
            }
            else
                Assert.That(JToken.DeepEquals(actual, expected), Is.True, $"{name}: {actual}");
        }
    }
}
