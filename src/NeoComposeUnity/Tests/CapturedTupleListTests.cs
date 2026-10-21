// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class CapturedTupleListTests
    {
        private const string FixtureFile = "captured-tuple-list-parity.json";
        public static IEnumerable<string> Cases() => NeoSourceParityFixture.Cases(FixtureFile);

        [TestCaseSource(nameof(Cases))]
        public void CapturedReadsMatchCli(string name) => NeoSourceParityFixture.AssertTrueCase(FixtureFile, name);

        [TestCase("Get")]
        [TestCase("GetRows")]
        public void HostReceivesCurrentTupleContents(string name)
        {
            using NeoClient client = NeoSourceParityFixture.CreateClient(FixtureFile);
            NeoMemberNSFunction Function(string method) => NeoMemberNSFunction.GetOrCreateStatic(client,
                client.members.Values.Single(member => member.name == method).id);
            object bag = Function("Create").InvokeStatic()!;
            object result = Function(name).InvokeStatic(bag)!;
            object tuple = name == "GetRows" ? ((object?[])result)[0]! : result;
            var projected = NeoGeneratedTypesSupport.ReadTuple<(IReadOnlyList<int> Items, int Number)>(tuple, 2,
                value => (NeoGeneratedTypesSupport.ReadScriptList<int>(NeoGeneratedTypesSupport.TupleElement(value, 0), Convert.ToInt32),
                    Convert.ToInt32(NeoGeneratedTypesSupport.TupleElement(value, 1))));
            CollectionAssert.AreEqual(new[] { 1, 3 }, projected.Items);
            Assert.AreEqual(2, projected.Number);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CapturedMutationPersistsAcrossInvocations(bool save)
        {
            using NeoClient client = NeoSourceParityFixture.CreateClient(FixtureFile);
            string factory = client.members.Values.Single(member => member.name == "Create").id;
            string id = ((INeoValueReference)NeoMemberNSFunction.GetOrCreateStatic(client, factory).InvokeStatic()!).valueId!;
            NeoValueOwnership ownership = save ? NeoValueOwnership.Save : NeoValueOwnership.Session;
            if (save)
                client.ImportValueReference(ownership, id, out HashSet<string>? _);
            foreach (string name in new[] { "Append", "Check" })
            {
                string method = client.members.Values.Single(member => member.name == name).id;
                using var function = new NeoMemberNSFunction(client, method, null, ownership);
                Assert.AreEqual(true, function.Invoke(id, System.Array.Empty<object?>()), name);
            }
        }
    }
}
