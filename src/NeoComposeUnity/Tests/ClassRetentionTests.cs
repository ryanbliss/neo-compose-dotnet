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
    public class ClassRetentionTests
    {
        private const string FixtureFile = "class-retention-parity.json";

        public static IEnumerable<string> Cases() => NeoSourceParityFixture.Cases(FixtureFile);

        [TestCaseSource(nameof(Cases))]
        public void RetainedInstancesMatchCli(string name) => NeoSourceParityFixture.AssertTrueCase(FixtureFile, name);

        private static string MemberId(NeoClient client, string name) =>
            client.members.Single(entry => entry.Value.name == name).Key;

        [TestCase(false)]
        [TestCase(true)]
        public void SavedOwnerReleasesOldChildrenAndRetainsReturnedInstance(bool returnsInstance)
        {
            using NeoClient client = NeoSourceParityFixture.CreateClient(FixtureFile);
            object created = NeoMemberNSFunction.GetOrCreateStatic(client, MemberId(client, "CreateHolder")).InvokeStatic()!;
            string holderId = ((INeoValueReference)created).valueId!;
            client.ImportValueReference(NeoValueOwnership.Save, holderId, out HashSet<string>? _);
            Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Save, holderId, out ObjectMemberValue? holder));
            string oldChildId = holder!.value!["It"];
            Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Save, oldChildId, out ObjectMemberValue? oldChild));
            string oldLeafId = oldChild!.value!["V"];
            int temporaryCount = client.sessionValues.Count;
            using var clear = new NeoMemberNSFunction(client, MemberId(client,
                returnsInstance ? "ClearAndReturn" : "ClearAndRead"), null, NeoValueOwnership.Save);
            object? result = clear.Invoke(holderId, Array.Empty<object?>());
            Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Save, oldChildId, out ObjectMemberValue? cleared));
            Assert.IsNull(cleared!.value);
            Assert.IsFalse(client.saveValues.ContainsKey(oldLeafId), "Cleared ownership must release saved descendants.");
            if (returnsInstance)
            {
                string retainedId = ((INeoValueReference)result!).valueId!;
                Assert.AreNotEqual(oldChildId, retainedId);
                using var read = new NeoMemberNSFunction(client, MemberId(client, "Read"), null, NeoValueOwnership.Session);
                Assert.AreEqual(3d, read.Invoke(retainedId, Array.Empty<object?>()));
                Assert.IsTrue(client.sessionValues.ContainsKey(retainedId));
            }
            else
            {
                Assert.AreEqual(true, result);
                Assert.AreEqual(temporaryCount, client.sessionValues.Count, "Unescaped retained instances are reclaimed.");
            }
        }

        [Test]
        public void SavedClassRetainsSessionChildAliases()
        {
            using NeoClient client = NeoSourceParityFixture.CreateClient(FixtureFile);
            object created = NeoMemberNSFunction.GetOrCreateStatic(client, MemberId(client, "CreateMixedHolder")).InvokeStatic()!;
            string holderId = ((INeoValueReference)created).valueId!;
            client.ImportValueReference(NeoValueOwnership.Save, holderId, out HashSet<string>? _);
            using var clear = new NeoMemberNSFunction(client, MemberId(client, "ClearMixed"), null, NeoValueOwnership.Save);
            Assert.AreEqual(true, clear.Invoke(holderId, Array.Empty<object?>()));
        }

        [TestCase("ClearEntry")]
        [TestCase("ClearMapEntry")]
        [TestCase("ClearEntryRead")]
        [TestCase("ReadThenClear")]
        public void SavedOwnerEntryClearDoesNotLeakSessionRows(string method)
        {
            using NeoClient client = NeoSourceParityFixture.CreateClient(FixtureFile);
            object created = NeoMemberNSFunction.GetOrCreateStatic(client, MemberId(client, "CreateBag")).InvokeStatic()!;
            string bagId = ((INeoValueReference)created).valueId!;
            client.ImportValueReference(NeoValueOwnership.Save, bagId, out HashSet<string>? _);
            int before = client.sessionValues.Count;
            using var clear = new NeoMemberNSFunction(client, MemberId(client, method), null, NeoValueOwnership.Save);
            Assert.AreEqual(true, clear.Invoke(bagId, Array.Empty<object?>()));
            Assert.AreEqual(before, client.sessionValues.Count, $"{method} left retained Session rows behind.");
        }

        [Test]
        public void RetainedTemporaryIsReclaimedAfterInvocation()
        {
            int before = 0;
            NeoSourceParityFixture.AssertTrueCase(FixtureFile, "SharedAliases",
                before: client => before = client.sessionValues.Count,
                after: client => Assert.AreEqual(before, client.sessionValues.Count));
        }
    }
}
