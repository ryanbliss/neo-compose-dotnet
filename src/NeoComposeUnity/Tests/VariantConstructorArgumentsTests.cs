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
    /// <summary>Runs the same variant constructor cases as the CLI source fixture.</summary>
    public class VariantConstructorArgumentsTests
    {
        private const string PackageRoot = "Packages/com.ryanbliss.neocompose/Tests";

        public static IEnumerable<string> Returns()
        {
            foreach (JProperty testCase in Fixture()["returns"]!.Children<JProperty>())
                yield return testCase.Name;
        }

        [TestCaseSource(nameof(Returns))]
        public void ProbeReturns(string probe)
        {
            using NeoClient client = Client();

            Assert.AreEqual(Fixture()["returns"]![probe]!.ToObject<object>(), InvokeProbe(client, probe));
        }

        private static NeoClient Client()
        {
            ProjectData data = NeoTestExport.Read(File.ReadAllText(Path.Combine(PackageRoot, "variant-constructor-arguments-export.json")));
            return NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false);
        }

        private static JObject Fixture() =>
            JObject.Parse(File.ReadAllText(Path.Combine(PackageRoot, "variant-constructor-arguments-fixture.json")));

        private static object? InvokeProbe(NeoClient client, string name)
        {
            Member function = client.members.Values.Single(member =>
                member is NSFunctionMember && member.name == name);
            return NeoMemberNSFunction.GetOrCreateStatic(client, function.id)
                .InvokeStatic(Array.Empty<object?>());
        }
    }
}
