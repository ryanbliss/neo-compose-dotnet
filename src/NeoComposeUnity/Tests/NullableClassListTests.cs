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
    public class NullableClassListTests
    {
        private const string FixtureFile = "nullable-class-list-parity.json";

        public static IEnumerable<string> Cases() => NeoSourceParityFixture.Cases(FixtureFile);

        [TestCaseSource(nameof(Cases))]
        public void NullMembershipMatchesCli(string name) => NeoSourceParityFixture.AssertTrueCase(FixtureFile, name);

        [Test]
        public void UnorderedNullMembership()
        {
            using NeoClient client = NeoSourceParityFixture.CreateClient(FixtureFile);
            foreach (ListMember member in client.members.Values.OfType<ListMember>().Where(member => member.name == "Items"))
                member.ListKind = NeoListKind.Unordered;
            string factory = client.members.Values.Single(member => member.name == "CreateBag").id;
            object created = NeoMemberNSFunction.GetOrCreateStatic(client, factory).InvokeStatic()!;
            string id = ((INeoValueReference)created).valueId!;
            object? Invoke(string name)
            {
                string method = client.members.Values.Single(member => member.name == name).id;
                using var function = new NeoMemberNSFunction(client, method, null, NeoValueOwnership.Session);
                return function.Invoke(id, Array.Empty<object?>());
            }
            Assert.AreEqual(true, Invoke("HasNull"));
            Invoke("RemoveNull");
            Assert.AreEqual(false, Invoke("HasNull"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StoredNullsAcrossInvocations(bool save)
        {
            using NeoClient client = NeoSourceParityFixture.CreateClient(FixtureFile);
            string factory = client.members.Values.Single(member => member.name == "CreateBag").id;
            object created = NeoMemberNSFunction.GetOrCreateStatic(client, factory).InvokeStatic()!;
            string id = ((INeoValueReference)created).valueId!;
            NeoValueOwnership ownership = save ? NeoValueOwnership.Save : NeoValueOwnership.Session;
            if (save)
                client.ImportValueReference(ownership, id, out HashSet<string>? _);
            object? Invoke(string name)
            {
                string method = client.members.Values.Single(member => member.name == name).id;
                using var function = new NeoMemberNSFunction(client, method, null, ownership);
                return function.Invoke(id, Array.Empty<object?>());
            }
            Assert.IsTrue(client.TryGetValue(ownership, id, out ObjectMemberValue? initialOwner));
            Assert.IsTrue(client.TryGetValue(ownership, initialOwner!.value!["Items"], out ArrayMemberValue? initialItems));
            string nullEntryId = initialItems!.value![1];
            var reads = new HashSet<string>();
            using (client.CaptureValueReads(reads))
                Assert.AreEqual(true, Invoke("HasNull"));
            Assert.That(reads, Does.Contain(nullEntryId), "Null membership must read the null entry, not just its id.");
            Assert.AreEqual(1d, Invoke("NullIndex"));
            Invoke("RemoveNull");
            Assert.AreEqual(false, Invoke("HasNull"));
            Assert.AreEqual(-1d, Invoke("NullIndex"));
            Assert.IsTrue(client.TryGetValue(ownership, id, out ObjectMemberValue? owner));
            Assert.IsTrue(client.TryGetValue(ownership, owner!.value!["Items"], out ArrayMemberValue? items));
            Assert.That(items!.value, Has.Length.EqualTo(1));
        }
    }
}
