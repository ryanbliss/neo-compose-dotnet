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
    public class GenericCollectionAssignmentTests
    {
        private const string FixtureFile = "generic-collection-assignment-parity.json";

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void AssignedBindingsSurviveLaterInvocationAndSave(bool save, bool text)
        {
            using NeoClient client = NeoSourceParityFixture.CreateClient(FixtureFile);
            string factory = client.members.Values.Single(member => member.name == (text ? "CreateStringBox" : "CreateIntBox")).id;
            object created = NeoMemberNSFunction.GetOrCreateStatic(client, factory).InvokeStatic()!;
            string id = ((INeoValueReference)created).valueId!;
            NeoValueOwnership ownership = save ? NeoValueOwnership.Save : NeoValueOwnership.Session;
            if (save)
                client.ImportValueReference(ownership, id, out HashSet<string>? _);
            Assert.IsTrue(client.TryGetValue(ownership, id, out ObjectMemberValue? owner));
            Assert.IsTrue(client.TryGetValue(ownership, owner!.value!["Items"], out ArrayMemberValue? items));
            Assert.That(items!.genericBindings, Is.Not.Null.And.Not.Empty);
            string method = client.members.Values.Single(member => member.name == "HasValue").id;
            using var contains = new NeoMemberNSFunction(client, method, null, ownership);
            Assert.AreEqual(true, contains.Invoke(id, new object?[] { text ? "x" : 7d }));
        }

        public static IEnumerable<string> Cases() => NeoSourceParityFixture.Cases(FixtureFile);

        [TestCaseSource(nameof(Cases))]
        public void ClosedTargetAssignmentsMatchCli(string name) => NeoSourceParityFixture.AssertTrueCase(FixtureFile, name);
    }
}
