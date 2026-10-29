// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>The SDK consumer of the B3 source/export parity pair.</summary>
    public class P127InterfacesParityTests
    {
        private const string Root = "Packages/com.ryanbliss.neocompose/Tests/";
        private const string Export = Root + "p127-interfaces-parity-export.json";

        public static IEnumerable<TestCaseData> Cases() => JObject.Parse(File.ReadAllText(Root + "p127-interfaces-parity-fixture.json"))["returns"]!
            .Children<JProperty>().Select(test => new TestCaseData(test.Name, test.Value).SetName("B3_" + test.Name));

        [Test]
        public void GenericStaticCollectionsSaveRoundTrip()
        {
            var schema = NeoTestExport.Read(File.ReadAllText(Export));
            var owner = schema.classes.Values.Single(type => type.name == "StaticBox");
            foreach (string id in owner.schema.Values)
                if (schema.members[id] is not NSFunctionMember)
                    schema.members[id].Storage = NeoMemberStorage.Save;
            var probe = schema.classes.Values.Single(type => type.name == "Probe");
            var function = (NSFunctionMember)schema.members[probe.schema["GenericStaticCollections"]];
            // This Save variant uses the matching compiled write lifetime.
            foreach (var mutation in function.action!.instructions.OfType<CollectionCallInstruction>())
                mutation.target.writability = WritabilityKind.Save;
            object? Run(NeoClient client) => NeoMemberNSFunction.GetOrCreateStatic(client, function.id).InvokeStatic(Array.Empty<object?>());
            using var original = NeoTestSaveStack.ClientFromSchema(schema, assumeCurrentSchema: false);
            Assert.That(Run(original), Is.EqualTo(20));
            string saved = original.SerializeSaveData();
            using var restored = NeoTestSaveStack.ClientFromSchema(schema, loadedSaveContent: saved, assumeCurrentSchema: false);
            Assert.That(Run(restored), Is.EqualTo(40), "The touched list restores with its two entries before the next two are added.");
        }

        [TestCaseSource(nameof(Cases))]
        public void ReturnsWhatTheSharedFixtureRequires(string name, JToken expected)
        {
            Assert.That(File.Exists(Export), Is.True, "Vendor the validated B3 export before running parity.");
            using var client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText(Export)), assumeCurrentSchema: false);
            var probe = client.ProjectDataForRuntime.classes.Values.Single(type => type.name == "Probe");
            var function = (NSFunctionMember)client.ProjectDataForRuntime.members[probe.schema[name]];
            object? value = NeoMemberNSFunction.GetOrCreateStatic(client, function.id).InvokeStatic(Array.Empty<object?>());
            JToken actual = value is null ? JValue.CreateNull() : JToken.FromObject(value);
            if (expected.Type is JTokenType.Integer or JTokenType.Float)
                Assert.That(actual.Value<double>(), Is.EqualTo(expected.Value<double>()), name);
            else
                Assert.That(JToken.DeepEquals(actual, expected), Is.True, $"{name}: {actual}");
        }
    }
}
