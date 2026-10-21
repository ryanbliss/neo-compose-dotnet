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
    internal static class NeoSourceParityFixture
    {
        private const string PackageRoot = "Packages/com.ryanbliss.neocompose/Tests";

        private static JObject Read(string fixtureFile) => JObject.Parse(File.ReadAllText(Path.Combine(PackageRoot, fixtureFile)));

        internal static IEnumerable<string> Cases(string fixtureFile) => Read(fixtureFile)["cases"]!.Values<string>()!;

        internal static NeoClient CreateClient(string fixtureFile) => CreateClient(Read(fixtureFile));

        private static NeoClient CreateClient(JObject fixture)
        {
            // The base export supplies system sequence declarations. Each fixture
            // supplies its authored types and any runtime error dependencies.
            JObject project = Read("sequence-generated-export.json");
            foreach (string field in new[] { "members", "classes", "constructors", "values", "interfaces", "enums" })
            {
                if (fixture[field] is JObject records)
                    ((JObject)project[field]!).Merge(records);
            }
            var data = NeoTestExport.Read(project.ToString());
            return NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false);
        }

        internal static void AssertTrueCase(string fixtureFile, string name, Action<NeoClient>? before = null, Action<NeoClient>? after = null)
        {
            JObject fixture = Read(fixtureFile);
            using NeoClient client = CreateClient(fixture);
            string id = ((JObject)fixture["members"]!).Properties().Single(property => (string?)property.Value["name"] == name).Name;
            before?.Invoke(client);
            object? result = NeoMemberNSFunction.GetOrCreateStatic(client, id).InvokeStatic(Array.Empty<object?>());
            Assert.That(result, Is.EqualTo(true), name);
            after?.Invoke(client);
        }
    }
}
