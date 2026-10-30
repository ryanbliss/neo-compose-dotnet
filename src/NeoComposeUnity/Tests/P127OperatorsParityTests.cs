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
    /// <summary>The SDK consumer of Compose's B5 source and compiled export.</summary>
    public sealed class P127OperatorsParityTests
    {
        private const string Root = "Packages/com.ryanbliss.neocompose/Tests/";
        private const string Fixture = Root + "p127-operators-parity-fixture.json";
        private const string Export = Root + "p127-operators-parity-export.json";

        [Test]
        public void SharedSourceFixtureIsVendored()
        {
            Assert.That(File.Exists(Fixture), Is.True, "Vendor Compose's B5 source fixture.");
            Assert.That(JObject.Parse(File.ReadAllText(Fixture))["returns"], Is.Not.Empty);
        }

        [Test]
        public void SharedExportIsVendored()
        {
            Assert.That(File.Exists(Export), Is.True, "Vendor Compose's validated B5 export.");
            Assert.That(NeoTestExport.Read(File.ReadAllText(Export)).classes.Values.Any(type => type.name == "Probe"), Is.True);
        }

        public static IEnumerable<TestCaseData> Cases() => JObject.Parse(File.ReadAllText(Fixture))["returns"]!
            .Children<JProperty>().Select(test => new TestCaseData(test.Name, test.Value).SetName("B5_" + test.Name));

        public static IEnumerable<TestCaseData> Refusals() => JObject.Parse(File.ReadAllText(Fixture))["refuses"]!
            .Children<JProperty>().Select(test => new TestCaseData(test.Name, test.Value.Value<string>()).SetName("B5_" + test.Name));

        [TestCaseSource(nameof(Cases))]
        public void ReturnsWhatTheSharedFixtureRequires(string name, JToken expected)
        {
            using var client = LoadClient();
            object? value = Invoke(client, name);
            JToken actual = value is null ? JValue.CreateNull() : JToken.FromObject(value);
            if (expected.Type is JTokenType.Integer or JTokenType.Float)
            {
                var probe = client.ProjectDataForRuntime.classes.Values.Single(type => type.name == "Probe");
                bool single = NeoNSFunctionRuntime.ResolveSignature(client, probe.schema[name]).ReturnTypeInfo.type == MemberKind.Float;
                // Fixture JSON prints Single's shortest decimal; compare its exact binary32 value.
                double number = single ? (double)(float)expected.Value<double>() : expected.Value<double>();
                Assert.That(actual.Value<double>(), Is.EqualTo(number), name);
            }
            else
                Assert.That(JToken.DeepEquals(actual, expected), Is.True, $"{name}: {actual}");
        }

        [TestCaseSource(nameof(Refusals))]
        public void RefusesWhatTheSharedFixtureRequires(string name, string expected)
        {
            using var client = LoadClient();
            var error = Assert.Catch<NSGetterRuntimeError>(() => Invoke(client, name))!;
            Assert.That(error.Message, Is.EqualTo(expected));
            Assert.That(error.Payload.Message, Is.EqualTo(expected));
        }

        private static NeoClient LoadClient()
        {
            Assert.That(File.Exists(Export), Is.True, "Vendor Compose's validated B5 export.");
            return NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText(Export)), assumeCurrentSchema: false);
        }

        private static object? Invoke(NeoClient client, string name)
        {
            var probe = client.ProjectDataForRuntime.classes.Values.Single(type => type.name == "Probe");
            return NeoMemberNSFunction.GetOrCreateStatic(client, probe.schema[name]).InvokeStatic(Array.Empty<object?>());
        }
    }
}
