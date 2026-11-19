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
    /// <summary>Every runtime case from Compose's B6 equality and ordering fixtures.</summary>
    public sealed class P127ComparerParityTests
    {
        private const string Root = "Packages/com.ryanbliss.neocompose/Tests/";
        private static string Fixture(string family) => Root + "p127-" + family + "-parity-fixture.json";
        private static string Export(string family) => Root + "p127-" + family + "-parity-export.json";

        public static IEnumerable<TestCaseData> Cases()
        {
            foreach (string family in new[] { "equality", "ordering" })
            {
                if (!File.Exists(Fixture(family)))
                {
                    yield return new TestCaseData(family, null, null, false).SetName("B6_" + family + "_MissingFixture");
                    continue;
                }
                JObject fixture = JObject.Parse(File.ReadAllText(Fixture(family)));
                int count = 0;
                foreach (string section in new[] { "returns", "refuses" })
                    foreach (var test in fixture[section]!.Children<JProperty>())
                    {
                        count++;
                        yield return new TestCaseData(family, test.Name, test.Value, section == "refuses").SetName("B6_" + family + "_" + test.Name);
                    }
                if (count == 0)
                    yield return new TestCaseData(family, null, null, false).SetName("B6_" + family + "_EmptyFixture");
            }
        }

        [TestCaseSource(nameof(Cases))]
        public void RunsEverySharedCase(string family, string? name, JToken? expected, bool refusal)
        {
            Assert.That(File.Exists(Fixture(family)), Is.True, "Vendor Compose's final B6 source fixture.");
            Assert.That(File.Exists(Export(family)), Is.True, "Vendor Compose's final B6 compiled export.");
            Assert.That(name, Is.Not.Null, "The shared fixture must contain runtime cases.");
            Assert.That(Directory.Exists(Root + "Fixtures~/P127" + char.ToUpperInvariant(family[0]) + family.Substring(1) + "Parity/Generated"), Is.True, "Vendor the B8 generated artifact alongside its export.");
            using var client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText(Export(family))), assumeCurrentSchema: false);
            var probe = client.ProjectDataForRuntime.classes.Values.Single(type => type.name == "Probe");
            object? Invoke() => NeoMemberNSFunction.GetOrCreateStatic(client, probe.schema[name!]).InvokeStatic(Array.Empty<object?>());
            if (refusal)
            {
                var error = Assert.Catch<NSGetterRuntimeError>(() => Invoke())!;
                // Compose's toThrow(string) asserts the message fragment; ownership
                // refusals include runtime ids that differ on each construction.
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
