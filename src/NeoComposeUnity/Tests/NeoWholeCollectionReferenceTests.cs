// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class NeoWholeCollectionReferenceTests
    {
        private const string Export = "Packages/com.ryanbliss.neocompose/Tests/whole-collection-reference-export.json";

        [TestCase("Mixed", 7)]
        [TestCase("Empty", 0)]
        [TestCase("Multiple", 2)]
        [TestCase("Dictionary", 7)]
        [TestCase("Set", 2)]
        [TestCase("Selection", 9)]
        [TestCase("Concrete", 4)]
        [TestCase("Body", 4)]
        [TestCase("LiveAlias", 3)]
        [TestCase("ReferenceSlot", 9)]
        [TestCase("ReferenceClass", 9)]
        public void AuthoredConstructorKeepsTheReferenceTarget(string name, int expected)
        {
            using var client = Client();
            var function = client.members.Values.Single(member => member is NSFunctionMember && member.name == name);
            Assert.AreEqual(expected, NeoMemberNSFunction.GetOrCreateStatic(client, function.id).InvokeStatic(Array.Empty<object?>()));
        }

        [Test]
        public void TrackedCollectionSelectionKeepsTheContainerIdAndRejectsChildOrWrongType()
        {
            using var client = Client();
            var input = Input(client);
            var list = input.Get<NeoMemberList>("Numbers");
            var otherList = input.Get<NeoMemberList>("Values");
            var context = new NSGetterEvaluator.Context(client, null, null);
            object runtime = NSGetterEvaluator.UnwrapRow(list.value!, context, NeoValueOwnership.Session, list.member)!;
            var lookup = Lookup(client, "Concrete");
            CollectionAssert.AreEqual(new[] { list.value!.id }, NeoGeneratedTypesSupport.ConstructorLookupIds(runtime, lookup, context));
            Assert.AreNotEqual(list.First().value!.id, NeoGeneratedTypesSupport.ConstructorLookupIds(runtime, lookup, context).Single());
            object wrong = NSGetterEvaluator.UnwrapRow(otherList.value!, context, NeoValueOwnership.Session, otherList.member)!;
            var before = client.sessionValues.ToDictionary(pair => pair.Key, pair => pair.Value);
            Assert.Catch(() => NeoGeneratedTypesSupport.ConstructorLookupIds(wrong, lookup, context));
            var child = new NSSlotReference(list.First().value!.id, null, lookup.declaredTypeInfo!, lookup.id);
            Assert.Catch(() => NSGetterEvaluator.AssertReferencedRow(child, context));
            CollectionAssert.AreEquivalent(before.Keys, client.sessionValues.Keys);
            foreach (var pair in before)
                Assert.AreSame(pair.Value, client.sessionValues[pair.Key]);
            CollectionAssert.AreEqual(new[] { list.value.id }, NeoGeneratedTypesSupport.ConstructorLookupIds(runtime, lookup, context));
        }

        [Test]
        public void MultiSelectionStillEnumeratesReferenceTargets()
        {
            using var client = Client();
            var input = Input(client);
            var coin = input.Get<NeoMemberClass>("C");
            var picks = input.Get<NeoMemberLookupWritable>("Picks");
            picks.Add(coin.value!.id);
            var context = new NSGetterEvaluator.Context(client, null, null);
            object runtime = NSGetterEvaluator.UnwrapRow(picks.value!, context, NeoValueOwnership.Session, picks.member)!;
            CollectionAssert.AreEqual(new[] { coin.value.id }, NeoGeneratedTypesSupport.ConstructorLookupIds(runtime, (LookupMember)picks.member, context));
            CollectionAssert.AreEqual(new[] { picks.value!.id }, NeoGeneratedTypesSupport.ConstructorLookupIds(runtime, Lookup(client, "SelectionReference"), context));
        }

        private static LookupMember Lookup(NeoClient client, string owner) => (LookupMember)client.members[
            client.classes.Values.Single(type => type.name == owner).schema["Value"]];

        private static NeoMemberClass Input(NeoClient client) => NeoGeneratedTypesSupport.StaticBinding(
            client, client.members.Values.Single(member => member.name == "Saved").id,
            NeoValueOwnership.Session).GetRequiredNode<NeoMemberClass>();

        private static NeoClient Client() => NeoTestSaveStack.ClientFromSchema(
            NeoTestExport.Read(File.ReadAllText(Export)), assumeCurrentSchema: false);
    }
}
