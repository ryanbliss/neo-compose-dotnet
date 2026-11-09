// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class UnorderedCompositeInsertionTests
    {
        private const string FixtureFile = "unordered-composite-insertion-parity.json";

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void EntriesRetainBindingsAndReleaseChildren(bool save, bool text)
        {
            using NeoClient client = NeoSourceParityFixture.CreateClient(FixtureFile);
            string factory = client.members.Values.Single(member => member.name == (text ? "CreateString" : "CreateInt")).id;
            object created = NeoMemberNSFunction.GetOrCreateStatic(client, factory).InvokeStatic()!;
            string id = ((INeoValueReference)created).valueId!;
            NeoValueOwnership ownership = save ? NeoValueOwnership.Save : NeoValueOwnership.Session;
            if (save)
                client.ImportValueReference(ownership, id, out HashSet<string>? _);
            object expected = text ? "x" : 7d;
            foreach (string name in new[] { "WriteEntries", "ReadAndMutate" })
            {
                string method = client.members.Values.Single(member => member.name == name).id;
                using var invoke = new NeoMemberNSFunction(client, method, null, ownership);
                Assert.AreEqual(true, invoke.Invoke(id, new[] { expected }), name);
            }
            Assert.IsTrue(client.TryGetValue(ownership, id, out ObjectMemberValue? owner));
            var released = new List<string>();
            foreach (string name in new[] { "Items", "Pairs", "MapRows" })
            {
                string container = owner!.value![name];
                string entry = client.GetUnorderedListEntryIds(container).Single();
                Assert.IsTrue(client.TryGetValue(ownership, entry, out MemberValue? row));
                Assert.AreEqual(container, row!.containerId);
                // P127 §12.1: only a binding node stamps; an entry reads its
                // type from its owner through typeSource.
                if (name != "Pairs")
                {
                    Assert.That(row.genericBindings, Is.Null);
                    Assert.That(row.typeSource, Is.Not.Null);
                }
                released.Add(entry);
                released.AddRange(row is ArrayMemberValue list ? list.value! : ((ObjectMemberValue)row).value!.Values);
            }
            string clear = client.members.Values.Single(member => member.name == "ClearEntries").id;
            using var clearEntries = new NeoMemberNSFunction(client, clear, null, ownership);
            Assert.AreEqual(true, clearEntries.Invoke(id, System.Array.Empty<object?>()));
            foreach (string child in released)
                Assert.IsFalse(client.TryGetValue(ownership, child, out MemberValue? _), "Cleared composite children must be released.");
        }

        public static IEnumerable<string> Cases() => NeoSourceParityFixture.Cases(FixtureFile);

        [TestCaseSource(nameof(Cases))]
        public void AuthoredCompositeInsertion(string name) => NeoSourceParityFixture.AssertTrueCase(FixtureFile, name);
    }
}
