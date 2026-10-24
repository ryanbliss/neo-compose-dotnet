// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class NeoClassCollectionAssignmentTests
    {
        private const string Root = "Packages/com.ryanbliss.neocompose/Tests/class-collection-assignment-";

        public static IEnumerable<TestCaseData> Cases()
        {
            var fixture = JObject.Parse(File.ReadAllText(Root + "source.json"));
            foreach (var result in ((JObject)fixture["returns"]!).Properties())
                yield return new TestCaseData(result.Name, result.Value.ToObject<object>(), false).SetName(result.Name);
            // Existing SDK field handles follow their row; an assigned local
            // retains its original contents on replacement (P112).
            foreach (var result in ((JObject)fixture["sdkReturns"]!).Properties())
                yield return new TestCaseData(result.Name, result.Value.ToObject<object>(), false).SetName(result.Name);
            foreach (var refusal in ((JObject)fixture["refuses"]!).Properties())
                yield return new TestCaseData(refusal.Name, refusal.Value.ToObject<object>(), true).SetName(refusal.Name);
        }

        [TestCaseSource(nameof(Cases))]
        public void AuthoredAssignmentPreservesIdentityAndOwnership(string name, object expected, bool refusal)
        {
            using var client = NeoTestSaveStack.ClientFromSchema(
                NeoTestExport.Read(File.ReadAllText(Root + "export.json")), assumeCurrentSchema: false);
            var function = client.members.Values.Single(member => member is NSFunctionMember && member.name == name);
            object? Invoke() => NeoMemberNSFunction.GetOrCreateStatic(client, function.id).InvokeStatic(Array.Empty<object?>());
            if (refusal)
                StringAssert.Contains((string)expected, Assert.Catch(() => Invoke())!.ToString());
            else
                Assert.AreEqual(expected, Invoke());
        }
        [Test]
        public void DictionarySelfAssignmentKeepsTheExactRows()
        {
            using var client = Client();
            var map = Stored(client, "Mapped").Get<NeoMemberDictionary>("Rings");
            var row = map.value!;
            var item = map.Single().Value.value!;
            string original = Newtonsoft.Json.JsonConvert.SerializeObject(row);
            var function = client.members.Values.Single(member => member is NSFunctionMember && member.name == "StoredDictionarySelfAssignment");
            Assert.AreEqual(7, NeoMemberNSFunction.GetOrCreateStatic(client, function.id).InvokeStatic(Array.Empty<object?>()));
            Assert.AreSame(row, map.value);
            Assert.AreSame(item, map.Single().Value.value);
            Assert.AreEqual(original, Newtonsoft.Json.JsonConvert.SerializeObject(map.value));
        }

        [Test]
        public void RejectedAssignmentKeepsEveryExistingSessionRowUnchanged()
        {
            using var client = Client();
            object? Invoke(string name) => NeoMemberNSFunction.GetOrCreateStatic(client,
                client.members.Values.Single(member => member is NSFunctionMember && member.name == name).id)
                .InvokeStatic(Array.Empty<object?>());
            Assert.AreEqual(0, Invoke("SetupAtomic"));
            var before = client.sessionValues.ToDictionary(pair => pair.Key, pair => pair.Value);
            var contents = before.ToDictionary(pair => pair.Key, pair => Newtonsoft.Json.JsonConvert.SerializeObject(pair.Value));
            StringAssert.Contains("already owned", Assert.Catch(() => Invoke("RejectStoredReplacement"))!.ToString());
            CollectionAssert.AreEquivalent(before.Keys, client.sessionValues.Keys);
            foreach (var pair in before)
            {
                Assert.AreSame(pair.Value, client.sessionValues[pair.Key], pair.Key);
                Assert.AreEqual(contents[pair.Key], Newtonsoft.Json.JsonConvert.SerializeObject(client.sessionValues[pair.Key]), pair.Key);
            }
            Assert.AreEqual(121, Invoke("RejectedReplacementReportsFailure"));
        }

        [Test]
        public void RemovedDestinationDoesNotRegainAliasProvenance()
        {
            using var client = Client();
            var member = (ListMember)Stored(client, "Filled").Get<NeoMemberList>("Rings").member;
            var row = new ArrayMemberValue { id = Guid.NewGuid().ToString(), value = Array.Empty<string>() };
            var create = new NeoWritePlan(client);
            create.Set(NeoValueOwnership.Session, row);
            create.Commit();
            var context = client.CreateGetterContext(NeoValueOwnership.Session);
            object held = NSGetterEvaluator.UnwrapRow(row, context, NeoValueOwnership.Session, member)!;
            Assert.IsNotNull(NSGetterEvaluator.FindRowReference(held, context));
            var remove = new NeoWritePlan(client);
            NSGetterEvaluator.PrepareAssignedListAlias(remove, context, NeoValueOwnership.Session,
                row.id, member, null);
            remove.Remove(NeoValueOwnership.Session, row.id);
            remove.Commit();
            Assert.IsFalse(client.TryGetValue(NeoValueOwnership.Session, row.id, out _));
            Assert.IsNull(NSGetterEvaluator.FindRowReference(held, context));
        }

        [TestCase(16)]
        [TestCase(128)]
        public void SeparatelyCapturedFieldSnapshotsKeepTheirDestination(int count)
        {
            using var client = Client();
            var list = Stored(client, "Filled").Get<NeoMemberList>("Rings");
            var row = (ArrayMemberValue)list.value!;
            var context = client.CreateGetterContext(NeoValueOwnership.Session);
            var aliases = new List<object?[]>();
            for (int i = 0; i < count; i++)
            {
                var alias = (object?[])NSGetterEvaluator.UnwrapRow(row, context, NeoValueOwnership.Session, list.member)!;
                NSGetterEvaluator.CaptureRuntimeList(alias);
                aliases.Add(alias);
                NSGetterEvaluator.InvalidateCachedCollection(row.id, NeoValueOwnership.Session, context);
            }
            var plan = new NeoWritePlan(client);
            plan.AfterCommit(() => NSGetterEvaluator.EvictCachedRow(context, NeoValueOwnership.Session, row.id));
            NSGetterEvaluator.PrepareAssignedListAlias(plan, context, NeoValueOwnership.Session,
                row.id, (ListMember)list.member, null);
            plan.Commit();
            foreach (object?[] alias in aliases)
            {
                var reference = NSGetterEvaluator.FindRowReference(alias, context);
                Assert.IsNotNull(reference);
                Assert.AreEqual(row.id, reference!.valueId);
                Assert.AreEqual(NeoValueOwnership.Session, reference.ownership);
            }
        }

        private static NeoMemberClass Stored(NeoClient client, string name) => NeoGeneratedTypesSupport.StaticBinding(
            client, client.members.Values.Single(member => member.name == name).id,
            NeoValueOwnership.Session).GetRequiredNode<NeoMemberClass>();

        private static NeoClient Client() => NeoTestSaveStack.ClientFromSchema(
            NeoTestExport.Read(File.ReadAllText(Root + "export.json")), assumeCurrentSchema: false);
    }
}
