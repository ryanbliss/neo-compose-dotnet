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
    public class TupleClassRetentionTests
    {
        private const string FixtureFile = "tuple-class-retention-parity.json";
        public static IEnumerable<string> Cases() => NeoSourceParityFixture.Cases(FixtureFile);

        [TestCaseSource(nameof(Cases))]
        public void RetainedChildrenMatchCli(string name) => NeoSourceParityFixture.AssertTrueCase(FixtureFile, name);

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ReplacementReleasesOldRowsAndRetainsEscapedChildren(bool save, bool escape)
        {
            using NeoClient client = NeoSourceParityFixture.CreateClient(FixtureFile);
            string MemberId(string name) => client.members.Values.Single(member => member.name == name).id;
            object created = NeoMemberNSFunction.GetOrCreateStatic(client, MemberId("Create")).InvokeStatic()!;
            string ownerId = ((INeoValueReference)created).valueId!;
            NeoValueOwnership ownership = save ? NeoValueOwnership.Save : NeoValueOwnership.Session;
            if (save)
                client.ImportValueReference(ownership, ownerId, out HashSet<string>? _);
            Assert.IsTrue(client.TryGetValue(ownership, ownerId, out ObjectMemberValue? owner));
            string tupleId = owner!.value!["Pair"];
            Assert.IsTrue(client.TryGetValue(ownership, tupleId, out ArrayMemberValue? tuple));
            string oldChildId = tuple!.value![0];
            Assert.IsTrue(client.TryGetValue(ownership, oldChildId, out ObjectMemberValue? child));
            string oldLeafId = child!.value!["V"];
            int sessionBefore = client.sessionValues.Count;
            using var replace = new NeoMemberNSFunction(client, MemberId(escape ? "ReplaceReturn" : "ReplaceRead"), null, ownership);
            object? result = replace.Invoke(ownerId, Array.Empty<object?>());
            Assert.IsFalse(client.TryGetValue(ownership, oldChildId, out MemberValue? _), "The old owned child is removed.");
            Assert.IsFalse(client.TryGetValue(ownership, oldLeafId, out MemberValue? _), "The old owned field is removed.");
            if (escape)
            {
                string retainedId = ((INeoValueReference)result!).valueId!;
                Assert.AreNotEqual(oldChildId, retainedId);
                using var read = new NeoMemberNSFunction(client, MemberId("Read"), null, NeoValueOwnership.Session);
                Assert.AreEqual(7d, read.Invoke(retainedId, Array.Empty<object?>()));
            }
            else
            {
                Assert.AreEqual(true, result);
                Assert.AreEqual(sessionBefore, client.sessionValues.Count, "Unescaped retained rows are reclaimed.");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReattachRetainedChild(bool save)
        {
            using NeoClient client = NeoSourceParityFixture.CreateClient(FixtureFile);
            string MemberId(string name) => client.members.Values.Single(member => member.name == name).id;
            object created = NeoMemberNSFunction.GetOrCreateStatic(client, MemberId("Create")).InvokeStatic()!;
            string ownerId = ((INeoValueReference)created).valueId!;
            NeoValueOwnership ownership = save ? NeoValueOwnership.Save : NeoValueOwnership.Session;
            if (save)
                client.ImportValueReference(ownership, ownerId, out HashSet<string>? _);
            Assert.IsTrue(client.TryGetValue(ownership, ownerId, out ObjectMemberValue? owner));
            string tupleId = owner!.value!["Pair"];
            Assert.IsTrue(client.TryGetValue(ownership, tupleId, out ArrayMemberValue? tuple));
            string oldChildId = tuple!.value![0];
            int sessionBefore = client.sessionValues.Count;
            using var reattach = new NeoMemberNSFunction(client, MemberId("ReplaceReattach"), null, ownership);
            Assert.AreEqual(true, reattach.Invoke(ownerId, Array.Empty<object?>()));
            Assert.IsTrue(client.TryGetValue(ownership, tupleId, out ArrayMemberValue? after));
            string newChildId = after!.value![0];
            Assert.AreNotEqual(oldChildId, newChildId);
            Assert.IsFalse(client.TryGetValue(ownership, oldChildId, out MemberValue? _), "Old child is released.");
            Assert.IsTrue(client.TryGetValue(ownership, newChildId, out ObjectMemberValue? child), "Reattached child is stored in the owner's scope.");
            Assert.IsTrue(client.TryGetValue(ownership, child!.value!["V"], out MemberValue? leaf), "Reattached child's field is stored in the owner's scope.");
            if (save)
                Assert.AreEqual(sessionBefore, client.sessionValues.Count, "No retained Session rows leak after reattaching into Save.");
        }
    }
}
