// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P127 §1.2. <c>instance-env-parity-fixture.json</c> is copied verbatim
    /// from compose's
    /// <c>packages/models/src/classes/instance-env-parity-fixture.json</c>;
    /// <see cref="NeoGenericResolution.ResolveInstanceEnv(NeoClient, string, IReadOnlyDictionary{string, TypeBinding}, IReadOnlyDictionary{string, NeoGenericEnvEntry})"/>
    /// must resolve each case as the server's <c>resolveInstanceEnv</c> does.
    /// A bound entry names its type; in memory it names the type's view.
    /// </summary>
    public class P127InstanceEnvParityTests
    {
        private const string PackageRoot = "Packages/com.ryanbliss.neocompose/Tests";

        private static JObject Fixture() =>
            JObject.Parse(File.ReadAllText(Path.Combine(PackageRoot, "instance-env-parity-fixture.json")));

        private static IEnumerable<TestCaseData> Cases() =>
            Fixture()["cases"]!.Select(testCase => new TestCaseData(testCase.Value<string>("name")).SetName(testCase.Value<string>("name")));

        [TestCaseSource(nameof(Cases))]
        public void ResolvesTheSharedCase(string name)
        {
            JObject fixture = Fixture();
            JToken testCase = fixture["cases"]!.Single(candidate => candidate.Value<string>("name") == name);
            ProjectData schema = NeoGenericTestFixture.BuildProjectData();
            foreach (JToken schemaClass in fixture["classes"]!)
                schema.classes[schemaClass.Value<string>("id")!] = schemaClass.ToObject<NeoSchemaClass>()!;
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(schema);

            IReadOnlyDictionary<string, NeoGenericEnvEntry> env = NeoGenericResolution.ResolveInstanceEnv(
                client,
                testCase.Value<string>("classId")!,
                testCase["classArguments"]!.ToObject<Dictionary<string, TypeBinding>>(),
                ((JObject)testCase["ownerEnv"]!).Properties().ToDictionary(entry => entry.Name, entry => Entry(entry.Value)));

            Assert.That(
                env.Select(entry => $"{entry.Key}={Spell(entry.Value)}"),
                Is.EquivalentTo(((JObject)testCase["expected"]!).Properties().Select(entry => $"{entry.Name}={Spell(Entry(entry.Value))}")));
        }

        private static NeoGenericEnvEntry Entry(JToken entry)
        {
            if (entry.Value<string>("kind") != "member")
                return NeoGenericEnvEntry.Unbound(entry.Value<string>("paramId")!);
            string viewId = NeoTypeBindingViews.Id(entry["type"]!.ToObject<TypeBinding>()!);
            if (entry["bindings"] is not JObject bindings)
                return NeoGenericEnvEntry.Bound(viewId);
            return NeoGenericEnvEntry.Bound(
                viewId,
                bindings.Properties().ToDictionary(binding => binding.Name, binding => Entry(binding.Value)));
        }

        /// <summary>An entry with its view spelled as the binding it stands for, so a mismatch reads as types.</summary>
        private static string Spell(NeoGenericEnvEntry entry)
        {
            if (!entry.IsBound)
                return $"unbound:{entry.unboundParamId}";
            TypeBinding? type = NeoTypeBindingViews.BindingOf(entry.memberId!);
            string spelled = type is null ? $"non-view:{entry.memberId}" : JsonConvert.SerializeObject(type);
            if (entry.bindings is null)
                return spelled;
            IEnumerable<string> bindings = entry.bindings
                .OrderBy(binding => binding.Key, System.StringComparer.Ordinal)
                .Select(binding => $"{binding.Key}={Spell(binding.Value)}");
            return $"{spelled}{{{string.Join(",", bindings)}}}";
        }
    }
}
