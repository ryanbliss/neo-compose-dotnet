// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class TupleCollectionMutationTests
    {
        private const string FixtureFile = "tuple-collection-mutation-parity.json";

        [TestCase(false)]
        [TestCase(true)]
        public void StoredReplacementReleasesOldChildren(bool save)
        {
            using NeoClient client = NeoSourceParityFixture.CreateClient(FixtureFile);
            string factory = client.members.Values.Single(member => member.name == "CreateBag").id;
            object created = NeoMemberNSFunction.GetOrCreateStatic(client, factory).InvokeStatic()!;
            string id = ((INeoValueReference)created).valueId!;
            NeoValueOwnership ownership = save ? NeoValueOwnership.Save : NeoValueOwnership.Session;
            if (save)
                client.ImportValueReference(ownership, id, out HashSet<string>? _);
            Assert.IsTrue(client.TryGetValue(ownership, id, out ObjectMemberValue? owner));
            Assert.IsTrue(client.TryGetValue(ownership, owner!.value!["Items"], out ArrayMemberValue? items));
            Assert.IsTrue(client.TryGetValue(ownership, items!.value![0], out ArrayMemberValue? tuple));
            string[] previousChildren = tuple!.value!.ToArray();
            string method = client.members.Values.Single(member => member.name == "Mutate").id;
            using var mutate = new NeoMemberNSFunction(client, method, null, ownership);
            Assert.AreEqual(true, mutate.Invoke(id, Array.Empty<object?>()));
            foreach (string child in previousChildren)
                Assert.IsFalse(client.TryGetValue(ownership, child, out MemberValue? _), "Replaced tuple children must be released.");
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void RemoveTupleAcrossInvocations(bool save, bool unordered)
        {
            using NeoClient client = NeoSourceParityFixture.CreateClient(FixtureFile);
            if (unordered)
                foreach (ListMember member in client.members.Values.OfType<ListMember>().Where(member => member.name == "Items"))
                    member.ListKind = NeoListKind.Unordered;
            string factory = client.members.Values.Single(member => member.name == "CreateRemovalBag").id;
            object created = NeoMemberNSFunction.GetOrCreateStatic(client, factory).InvokeStatic()!;
            string id = ((INeoValueReference)created).valueId!;
            NeoValueOwnership ownership = save ? NeoValueOwnership.Save : NeoValueOwnership.Session;
            if (save)
                client.ImportValueReference(ownership, id, out HashSet<string>? _);
            string method = client.members.Values.Single(member => member.name == "RemoveFirst").id;
            using var remove = new NeoMemberNSFunction(client, method, null, ownership);
            Assert.AreEqual(true, remove.Invoke(id, Array.Empty<object?>()));
        }

        public static IEnumerable<string> Cases() => NeoSourceParityFixture.Cases(FixtureFile);

        [TestCaseSource(nameof(Cases))]
        public void StoredTupleMutationsMatchCli(string name) => NeoSourceParityFixture.AssertTrueCase(FixtureFile, name);
    }
}
