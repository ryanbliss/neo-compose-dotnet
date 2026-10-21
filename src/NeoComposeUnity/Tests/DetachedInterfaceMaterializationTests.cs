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
    public class DetachedInterfaceMaterializationTests
    {
        private const string FixtureFile = "detached-interface-materialization-parity.json";

        public static IEnumerable<string> Cases() => NeoSourceParityFixture.Cases(FixtureFile);

        [TestCaseSource(nameof(Cases))]
        public void InterfaceChildrenMatchCli(string name) => NeoSourceParityFixture.AssertTrueCase(FixtureFile, name);

        [TestCase(false)]
        [TestCase(true)]
        public void InterfaceChildrenSurviveAcrossInvocations(bool save)
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
            Assert.That(items!.value, Has.Length.EqualTo(2));
            Assert.IsTrue(client.TryGetValue(ownership, items.value![0], out ObjectMemberValue? nullEntry));
            Assert.IsNull(nullEntry!.value);
            string childId = items.value[1];
            Assert.IsTrue(client.TryGetValue(ownership, childId, out ObjectMemberValue? child));
            Assert.AreEqual(client.classes.Values.Single(record => record.name == "Item").id, child!.classId);
            foreach (string name in new[] { "ReadItems", "RemoveNull" })
            {
                string method = client.members.Values.Single(member => member.name == name).id;
                using var invoke = new NeoMemberNSFunction(client, method, null, ownership);
                Assert.AreEqual(true, invoke.Invoke(id, Array.Empty<object?>()), name);
            }
            Assert.IsFalse(client.TryGetValue(ownership, nullEntry.id, out MemberValue? _));
            Assert.IsTrue(client.TryGetValue(ownership, childId, out ObjectMemberValue? _));
        }
    }
}
