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
    public class GenericCollectionEntryTests
    {
        private const string FixtureFile = "generic-collection-entry-parity.json";

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void EntryBindingsSurviveLaterInvocationAndSave(bool save, bool text)
        {
            using NeoClient client = NeoSourceParityFixture.CreateClient(FixtureFile);
            string factory = client.members.Values.Single(member => member.name == (text ? "CreateString" : "CreateInt")).id;
            object created = NeoMemberNSFunction.GetOrCreateStatic(client, factory).InvokeStatic()!;
            string id = ((INeoValueReference)created).valueId!;
            NeoValueOwnership ownership = save ? NeoValueOwnership.Save : NeoValueOwnership.Session;
            if (save)
                client.ImportValueReference(ownership, id, out HashSet<string>? _);
            object expected = text ? "x" : 7d;
            foreach (string name in new[] { "WriteEntries", "ReadEntries", "AppendEntries", "ReplaceEntries", "ReadEntries", "AppendEntries" })
            {
                string method = client.members.Values.Single(member => member.name == name).id;
                using var invoke = new NeoMemberNSFunction(client, method, null, ownership);
                Assert.AreEqual(true, invoke.Invoke(id, new[] { expected }), name);
            }
            Assert.IsTrue(client.TryGetValue(ownership, id, out ObjectMemberValue? owner));
            Assert.IsTrue(client.TryGetValue(ownership, owner!.value!["Map"], out ObjectMemberValue? map));
            Assert.IsTrue(client.TryGetValue(ownership, map!.value!["a"], out ArrayMemberValue? nested));
            // P127 §12.1: only the binding node stamps; the nested entry
            // reads its type from the owner through its typeSource.
            Assert.That(nested!.genericBindings, Is.Null);
            Assert.That(nested.typeSource?.ValueId, Is.EqualTo(id));
        }

        public static IEnumerable<string> Cases() => NeoSourceParityFixture.Cases(FixtureFile);

        [TestCaseSource(nameof(Cases))]
        public void EntryWritesMatchCli(string name) => NeoSourceParityFixture.AssertTrueCase(FixtureFile, name);
    }
}
