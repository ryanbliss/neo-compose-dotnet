// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class LocalListAliasTests
    {
        private const string PackageRoot = "Packages/com.ryanbliss.neocompose/Tests";

        private static JObject Fixture() => JObject.Parse(File.ReadAllText(Path.Combine(PackageRoot, "local-list-alias-parity.json")));

        public static IEnumerable<string> Cases() => Fixture()["cases"]!.Values<string>()!;

        [TestCaseSource(nameof(Cases))]
        public void CompiledListAliasesPreserveIdentity(string name)
        {
            // Reuse the system sequence declarations for the generator probes.
            JObject project = JObject.Parse(File.ReadAllText(Path.Combine(PackageRoot, "sequence-generated-export.json")));
            JObject fixture = Fixture();
            ((JObject)project["members"]!).Merge(fixture["members"]);
            ((JObject)project["classes"]!).Merge(fixture["classes"]);
            var data = NeoTestExport.Read(project.ToString());
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false);
            string id = ((JObject)fixture["members"]!).Properties().Single(property => (string?)property.Value["name"] == name).Name;
            object? result = NeoMemberNSFunction.GetOrCreateStatic(client, id).InvokeStatic(Array.Empty<object?>());
            Assert.That(result, Is.EqualTo(true), name);
        }
    }
}
