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
    public class TupleLocalKeyTests
    {
        private const string FixtureFile = "tuple-local-key-parity.json";
        public static IEnumerable<string> Cases() => NeoSourceParityFixture.Cases(FixtureFile);
        [TestCase(false)]
        [TestCase(true)]
        public void KeysSurviveLaterInvocations(bool save)
        {
            using var client = NeoSourceParityFixture.CreateClient(FixtureFile);
            string MemberId(string name) => client.members.Values.Single(member => member.name == name).id;
            var owner = (INeoValueReference)NeoMemberNSFunction.GetOrCreateStatic(client, MemberId("CreateSaved")).InvokeStatic()!;
            string id = owner.valueId!;
            NeoValueOwnership ownership = save ? NeoValueOwnership.Save : NeoValueOwnership.Session;
            if (save)
                client.ImportValueReference(ownership, id, out HashSet<string>? _);
            foreach (string name in new[] { "SeedKeys", "ReadKeys", "MutateKey", "ReadKeys", "RemoveKey" })
            {
                using var invoke = new NeoMemberNSFunction(client, MemberId(name), null, ownership);
                Assert.That(invoke.Invoke(id, Array.Empty<object?>()), Is.True, name);
            }
        }

        // A Set stores a copy of a stored row's list, so a held Add, Contains,
        // and a committed Contains all find no source list in it.
        [TestCase("SdkStoredSourceCopy")]
        [TestCase("SdkStoredSourceAddsACopy")]
        public void StoredSourcesKeepCopySemantics(string name) =>
            NeoSourceParityFixture.AssertTrueCase(FixtureFile, name);

        [TestCaseSource(nameof(Cases))]
        public void LocalKeysMatchCli(string name) => NeoSourceParityFixture.AssertTrueCase(FixtureFile, name);
    }
}
