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
    /// <summary>Runs the same generic base-constructor cases as the CLI source fixture.</summary>
    public class GenericBaseConstructorParityTests
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
        public void ClassForwarding_KeepsDownstreamSignatureRetentionBounded()
        {
            using NeoClient client = Client();
            Member forward = client.members.Values.Single(member => member.name == "ForwardConstruction");
            FunctionWithReturnType action = NeoNSFunctionRuntime.ResolveSignature(client, forward.id).Action;
            var instruction = (ReturnInstruction)action.instructions.Single();
            var site = (CallFunctionPointer)instruction.pointer!;
            for (int i = 0; i < 128; i++)
            {
                Assert.AreEqual(7, InvokeProbe(client, "ChainedInt"));
                Assert.AreEqual("chain", InvokeProbe(client, "ChainedString"));
                int retained = 0;
                for (NeoBoundNSFunctionSignature? bound = site.BoundSignatures; bound is not null; bound = bound.Next)
                    retained++;
                Assert.That(retained, Is.InRange(1, 16));
            }
        }

        private static NeoClient Client()
        {
            ProjectData data = NeoTestExport.Read(File.ReadAllText(Path.Combine(PackageRoot, "generic-base-constructor-parity-export.json")));
            return NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false);
        }

        private static JObject Fixture() =>
            JObject.Parse(File.ReadAllText(Path.Combine(PackageRoot, "generic-base-constructor-parity-fixture.json")));

        private static object? InvokeProbe(NeoClient client, string name)
        {
            Member function = client.members.Values.Single(member =>
                member is NSFunctionMember && member.name == name);
            return NeoMemberNSFunction.GetOrCreateStatic(client, function.id)
                .InvokeStatic(Array.Empty<object?>());
        }
    }
}
