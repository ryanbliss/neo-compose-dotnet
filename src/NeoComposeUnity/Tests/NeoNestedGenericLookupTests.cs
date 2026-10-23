// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class NeoNestedGenericLookupTests
    {
        private const string Export = "Packages/com.ryanbliss.neocompose/Tests/nested-generic-lookup-export.json";

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void AddRetainsSelectedTargetsOwnership(bool dictionary, bool immutable)
        {
            using var client = Client();
            var lookup = Lookup(client, dictionary);
            var coin = Static(client, immutable ? "FrozenCoin" : "LiveCoin");
            Assert.IsTrue(lookup.Add(coin.value!.id));
            Assert.IsFalse(lookup.Add(coin.value.id));
            CollectionAssert.AreEqual(new[] { coin.value.id }, lookup.Selected());
            var selected = (NeoMemberClass)lookup.GetFirstSelected()!;
            Assert.AreEqual(immutable ? NeoValueOwnership.Asset : NeoValueOwnership.Session, selected.ownership);
            Assert.AreEqual(!immutable, selected is NeoMemberClassWritable);
            Assert.IsTrue(lookup.Remove(coin.value.id));
            Assert.IsEmpty(lookup.Selected());
            Assert.AreEqual(immutable ? 11 : 3, coin.Get<NeoMemberInt>("Cents").value!.value);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InvalidTargetRefusesWithoutChangingSelection(bool dictionary)
        {
            using var client = Client();
            var lookup = Lookup(client, dictionary);
            var coin = Static(client, "LiveCoin");
            lookup.Add(coin.value!.id);
            var before = lookup.Selected();
            Assert.Throws<InvalidOperationException>(() => lookup.Add(Static(client, "Bound").value!.id));
            Assert.Throws<InvalidOperationException>(() => lookup.Add(coin.Get<NeoMemberInt>("Cents").value!.id));
            Assert.Throws<InvalidOperationException>(() => lookup.Add("missing-row"));
            CollectionAssert.AreEqual(before, lookup.Selected());
        }

        [Test]
        public void NestedSlotAndHandleBindingsRemainDistinctAcrossCalls()
        {
            using var client = Client();
            var coin = Static(client, "LiveCoin");
            string slotId = coin.Get<NeoMemberInt>("Cents").value!.id;
            var slots = NestedLookup(client, "Integers");
            var handles = NestedLookup(client, "Coins");
            for (int repeat = 0; repeat < 3; repeat++)
            {
                Assert.IsTrue(slots.Add(slotId));
                Assert.IsTrue(handles.Add(coin.value!.id));
                Assert.Catch(() => slots.Add(coin.value.id));
                Assert.Catch(() => handles.Add(slotId));
                CollectionAssert.AreEqual(new[] { slotId }, slots.Selected());
                CollectionAssert.AreEqual(new[] { coin.value.id }, handles.Selected());
                Assert.IsTrue(slots.Remove(slotId));
                Assert.IsTrue(handles.Remove(coin.value.id));
            }
            Assert.AreEqual(3, coin.Get<NeoMemberInt>("Cents").value!.value);
        }

        private static NeoMemberLookupWritable NestedLookup(NeoClient client, string name) =>
            (NeoMemberLookupWritable)((NeoMemberDictionaryWritable)Static(client, name)
                .Get<NeoMemberListWritable>("Values").Single()).Single().Value;

        [TestCase(false)]
        [TestCase(true)]
        public void IndexedOwnershipTracksReplacementRemovalAndReattachment(bool dictionary)
        {
            using var client = Client();
            var list = Static(client, "Coins").Get<NeoMemberListWritable>("Values");
            // Use committed additions, rather than inherited virtual-default
            // edges, so replacement exercises the writable placement index.
            list.AddSerialized(NeoValueWritePayload.FromValue(new System.Collections.Generic.Dictionary<string, string>()));
            var map = (NeoMemberDictionaryWritable)list.Last();
            map.SetSerialized("fresh", NeoValueWritePayload.FromValue(Array.Empty<string>()));
            MemberValue parent = dictionary ? map.value! : list.value!;
            string childId = dictionary ? map.Single().Value.value!.id : map.value!.id;
            Assert.IsTrue(client.TryFindOwnedParent(NeoValueOwnership.Session, childId, out var initial));
            Assert.AreEqual(parent.id, initial);
            Assert.IsFalse(client.TryFindOwnedParent(NeoValueOwnership.Asset, childId, out _));

            for (int repeat = 0; repeat < 3; repeat++)
            {
                var replacement = client.CloneRowForWrite(parent);
                if (replacement is ArrayMemberValue array)
                    array.value = Array.Empty<string>();
                else
                    ((ObjectMemberValue)replacement).value = new System.Collections.Generic.Dictionary<string, string>();
                var plan = new NeoWritePlan(client);
                plan.Set(NeoValueOwnership.Session, replacement);
                using (client.ReadCandidate(plan))
                    Assert.IsFalse(client.TryFindOwnedParent(NeoValueOwnership.Session, childId, out _), "Pending replacement must override the committed index.");
                Assert.IsTrue(client.TryFindOwnedParent(NeoValueOwnership.Session, childId, out _), "Exiting the candidate restores committed edges.");
                plan.Commit();
                Assert.IsFalse(client.TryFindOwnedParent(NeoValueOwnership.Session, childId, out _), "Removed edges must leave the index.");

                var restore = new NeoWritePlan(client);
                restore.Set(NeoValueOwnership.Session, client.CloneRowForWrite(parent));
                using (client.ReadCandidate(restore))
                {
                    Assert.IsTrue(client.TryFindOwnedParent(NeoValueOwnership.Session, childId, out var pending));
                    Assert.AreEqual(parent.id, pending);
                }
                restore.Commit();
                Assert.IsTrue(client.TryFindOwnedParent(NeoValueOwnership.Session, childId, out var attached));
                Assert.AreEqual(parent.id, attached);
            }
        }

        [Test]
        public void CandidateInferenceRetainsCommittedParentOrderAndReplacement()
        {
            using var client = Client();
            var list = Static(client, "Coins").Get<NeoMemberListWritable>("Values");
            list.AddSerialized(NeoValueWritePayload.FromValue(new System.Collections.Generic.Dictionary<string, string>()));
            var first = (NeoMemberDictionaryWritable)list.Last();
            first.SetSerialized("fresh", NeoValueWritePayload.FromValue(Array.Empty<string>()));
            list.AddSerialized(NeoValueWritePayload.FromValue(new System.Collections.Generic.Dictionary<string, string>()));
            var second = (NeoMemberDictionaryWritable)list.Last();
            string child = first.Single().Value.value!.id;
            var committedOrder = client.sessionValues.Keys.Where(id => id == first.value!.id || id == second.value!.id).ToArray();
            var proposedSecond = (ObjectMemberValue)client.CloneRowForWrite(second.value!);
            proposedSecond.value!["shared"] = child;
            var proposedFirst = client.CloneRowForWrite(first.value!);
            var plan = new NeoWritePlan(client);
            // Reverse insertion order in the candidate must not reorder existing rows.
            plan.Set(NeoValueOwnership.Session, proposedSecond);
            plan.Set(NeoValueOwnership.Session, proposedFirst);
            using (client.ReadCandidate(plan))
                CollectionAssert.AreEqual(committedOrder,
                    client.InferMemberParents(child).Select(pair => pair.Key));
            var remove = (ObjectMemberValue)client.CloneRowForWrite(first.value!);
            remove.value!.Clear();
            plan.Set(NeoValueOwnership.Session, remove);
            using (client.ReadCandidate(plan))
                CollectionAssert.AreEqual(new[] { second.value!.id },
                    client.InferMemberParents(child).Select(pair => pair.Key));
            CollectionAssert.AreEqual(new[] { first.value!.id },
                client.InferMemberParents(child).Select(pair => pair.Key));
        }

        private static NeoMemberLookupWritable Lookup(NeoClient client, bool dictionary)
        {
            var owner = Static(client, "Bound");
            return dictionary
                ? (NeoMemberLookupWritable)owner.Get<NeoMemberDictionaryWritable>("Map").Single().Value
                : (NeoMemberLookupWritable)owner.Get<NeoMemberListWritable>("Picks").Single();
        }

        private static NeoMemberClass Static(NeoClient client, string name) =>
            NeoGeneratedTypesSupport.StaticBinding(client, client.members.Values.Single(m => m.name == name).id,
                name == "FrozenCoin" ? NeoValueOwnership.Asset : NeoValueOwnership.Session).GetRequiredNode<NeoMemberClass>();

        private static NeoClient Client() =>
            NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText(Export)), assumeCurrentSchema: false);
    }
}
