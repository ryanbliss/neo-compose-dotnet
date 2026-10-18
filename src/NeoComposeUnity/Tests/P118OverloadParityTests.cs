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
using Parity = NeoCompose.Tests.OverloadParity;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P118 function overloading: the Unity half of the parity gate. The cases
    /// and their source are a verbatim copy of neo-compose's
    /// <c>src/models/neoscript/function-overloads-parity-fixture.json</c>; the
    /// export is that source's <c>neo export</c>, which neo-compose's
    /// <c>cli/src/commands/function-overloads-parity.test.ts</c> writes when
    /// <c>NEO_OVERLOAD_PARITY_EXPORT</c> names this package's copy.
    /// </summary>
    public class P118OverloadParityTests
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

        [Test]
        public void CSharpCallsEachInstanceOverload()
        {
            using var app = new Parity.TestProjectNeo(Client());
            Parity.Inventory bag = Parity.Probe.Bag;

            Assert.AreEqual(1, bag.Add(new Parity.Item()));
            Assert.AreEqual(3, bag.Add(new Parity.Item(), 3));
            Assert.AreEqual(2, bag.Add(new[] { new Parity.Item(), new Parity.Item() }));
        }

        [Test]
        public void CSharpCallsEachStaticOverload()
        {
            using var app = new Parity.TestProjectNeo(Client());

            Assert.AreEqual(4, Parity.Inventory.Make(4));
            Assert.AreEqual(3, Parity.Inventory.Make("abc"));
        }

        [Test]
        public void OverrideDispatchesThroughTheBaseReceiverBesideItsOverloads()
        {
            using var app = new Parity.TestProjectNeo(Client());
            Parity.Inventory box = Parity.Probe.Box;

            Assert.AreEqual("chest(inventory,inventory@2)", box.Describe());
            Assert.AreEqual("inventory@1", box.Describe(1));
            Assert.AreEqual("chest:lid", Parity.Probe.Box.Describe("lid"));
        }

        [Test]
        public void InterfaceImplementationBindsTheDispatchedOverload()
        {
            using var app = new Parity.TestProjectNeo(Client());
            Parity.IStore store = Parity.Probe.Bag;

            Assert.AreEqual(10, store.Count(new Parity.Item()));
            Assert.AreEqual(20, Parity.Probe.Bag.Count("seed"));
        }

        private static NeoClient Client()
        {
            ProjectData data = NeoTestExport.Read(File.ReadAllText(Path.Combine(PackageRoot, "function-overloads-parity-export.json")));
            return NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false);
        }

        private static JObject Fixture() =>
            JObject.Parse(File.ReadAllText(Path.Combine(PackageRoot, "function-overloads-parity-fixture.json")));

        private static object? InvokeProbe(NeoClient client, string name)
        {
            Member function = client.members.Values.Single(member =>
                member is NSFunctionMember && member.name == name);
            return NeoMemberNSFunction.GetOrCreateStatic(client, function.id)
                .InvokeStatic(Array.Empty<object?>());
        }
    }
}
