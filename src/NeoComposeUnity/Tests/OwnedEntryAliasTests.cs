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
    public class OwnedEntryAliasTests
    {
        private const string FixtureFile = "owned-entry-alias-parity.json";
        public static IEnumerable<string> Cases() => NeoSourceParityFixture.Cases(FixtureFile);

        [TestCaseSource(nameof(Cases))]
        public void EntryAliasesMatchCli(string name) => NeoSourceParityFixture.AssertTrueCase(FixtureFile, name);

        [TestCase("ReplaceLists")]
        [TestCase("ReplaceMap")]
        public void SavedOwnerPreservesAssignedLocalAlias(string method)
        {
            using var client = NeoSourceParityFixture.CreateClient(FixtureFile);
            string MemberId(string name) => client.members.Values.Single(member => member.name == name).id;
            var holder = (INeoValueReference)NeoMemberNSFunction.GetOrCreateStatic(client, MemberId("CreateHolder")).InvokeStatic()!;
            string holderId = holder.valueId!;
            client.ImportValueReference(NeoValueOwnership.Save, holderId, out HashSet<string>? _);
            using var replace = new NeoMemberNSFunction(client, MemberId(method), null, NeoValueOwnership.Save);
            Assert.That(replace.Invoke(holderId, Array.Empty<object?>()), Is.True);
        }
    }
}
