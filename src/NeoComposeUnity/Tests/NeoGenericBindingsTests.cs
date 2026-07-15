// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NUnit.Framework;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Tests
{
    /// <summary>
    /// specs/class-generics.md §9: the lazy
    /// <see cref="NeoGenericBinding{T}"/> codec layer generated open classes
    /// resolve per instance (<c>NeoGenericBindings.Resolve&lt;T&gt;(client,
    /// node)</c>, cached in a field). Uses the shared
    /// <see cref="NeoGenericTestFixture"/> hierarchy — DamageCard closes
    /// <c>T = Float(required, default 3.5)</c>; StringCard closes
    /// <c>T = String(optional, literal)</c>.
    /// </summary>
    public class NeoGenericBindingsTests
    {
        private static NeoClient LoadClient()
        {
            return NeoTestSaveStack.ClientFromSchema(NeoGenericTestFixture.BuildProjectData());
        }

        [Test]
        public void Resolve_FloatSlot_ReadsBindingDefault()
        {
            var client = LoadClient();
            var card = client.save.Get<NeoMemberClassWritable>("Card");
            var node = card.Get<NeoMember>("Speed");

            var codec = NeoGenericBindings.Resolve<double>(client, node);

            Assert.AreEqual(MemberKind.Float, codec.Kind);
            Assert.AreEqual(3.5, codec.Read(node));
        }

        [Test]
        public void Resolve_FloatSlot_WriteRoundTrips()
        {
            var client = LoadClient();
            var card = client.save.Get<NeoMemberClassWritable>("Card");
            var node = card.Get<NeoMember>("Speed");

            var codec = NeoGenericBindings.Resolve<double>(client, node);
            codec.Write(node, 4.25);

            // The write binds a fresh row through the parent; re-fetch the
            // child the way a generated property getter would.
            var reboundNode = card.Get<NeoMember>("Speed");
            Assert.AreEqual(4.25, codec.Read(reboundNode));
        }

        [Test]
        public void Resolve_NullableFloat_ReadsAndSerializes()
        {
            var client = LoadClient();
            var card = client.save.Get<NeoMemberClassWritable>("Card");
            var node = card.Get<NeoMember>("Speed");

            var codec = NeoGenericBindings.Resolve<double?>(client, node);
            Assert.AreEqual(3.5, codec.Read(node));

            var payload = codec.Serialize(1.5);
            Assert.IsNotNull(payload);
            Assert.AreEqual(1.5, payload!.value);
        }

        [Test]
        public void Resolve_StringSlot_ReadsNullForOptionalUnsetValue()
        {
            var client = LoadClient();
            var stringCard = client.save.Get<NeoMemberClassWritable>("StringCard");
            var node = stringCard.Get<NeoMember>("Speed");

            var codec = NeoGenericBindings.Resolve<string>(client, node);

            Assert.AreEqual(MemberKind.String, codec.Kind);
            Assert.IsNull(codec.Read(node));
        }

        [Test]
        public void Resolve_StringSlot_WriteRoundTrips()
        {
            var client = LoadClient();
            var stringCard = client.save.Get<NeoMemberClassWritable>("StringCard");
            var node = stringCard.Get<NeoMember>("Speed");

            var codec = NeoGenericBindings.Resolve<string>(client, node);
            codec.Write(node, "enchanted");

            var reboundNode = stringCard.Get<NeoMember>("Speed");
            Assert.AreEqual("enchanted", codec.Read(reboundNode));
        }

        [Test]
        public void Resolve_KindMismatch_ThrowsDescriptively()
        {
            var client = LoadClient();
            var card = client.save.Get<NeoMemberClassWritable>("Card");
            var node = card.Get<NeoMember>("Speed");

            var error = Assert.Throws<System.InvalidOperationException>(
                () => NeoGenericBindings.Resolve<string>(client, node))!;

            StringAssert.Contains("Speed", error.Message);
            StringAssert.Contains("double", error.Message);
            StringAssert.Contains("System.String", error.Message);
        }

        [Test]
        public void Resolve_ListSlot_ReadsWrapperAndMutatesEntries()
        {
            var client = LoadClient();
            var card = client.save.Get<NeoMemberClassWritable>("Card");
            var listNode = card.GetOrCreateCollection<NeoMemberListWritable>("Values");

            var codec = NeoGenericBindings.Resolve<NeoList<double>>(client, listNode);
            Assert.AreEqual(MemberKind.List, codec.Kind);

            var list = codec.Read(listNode);
            list.Add(2.5);

            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(2.5, list[0]);

            // Whole-collection assignment is not part of the codec surface.
            var error = Assert.Throws<System.InvalidOperationException>(
                () => codec.Write(listNode, list))!;
            StringAssert.Contains("wrapper", error.Message);
        }

        [Test]
        public void Resolve_EntryTypeOnCollectionNode_ResolvesEntryCodec()
        {
            // The saved-wrapper serializer hook shape: codegen resolves the
            // ENTRY codec from the stamped collection node (a new entry has
            // no child node yet) — `Resolve<TEntry>(client, collectionNode)`.
            var client = LoadClient();
            var card = client.save.Get<NeoMemberClassWritable>("Card");
            var listNode = card.GetOrCreateCollection<NeoMemberListWritable>("Values");

            var entryCodec = NeoGenericBindings.Resolve<double>(client, listNode);
            Assert.AreEqual(MemberKind.Float, entryCodec.Kind);
            var payload = entryCodec.Serialize(2.75);
            Assert.IsNotNull(payload);
            Assert.AreEqual(2.75, payload!.value);
        }

        [Test]
        public void Resolve_EntryTypeMismatchOnCollectionNode_ThrowsDescriptively()
        {
            var client = LoadClient();
            var card = client.save.Get<NeoMemberClassWritable>("Card");
            var listNode = card.GetOrCreateCollection<NeoMemberListWritable>("Values");

            var error = Assert.Throws<System.InvalidOperationException>(
                () => NeoGenericBindings.Resolve<string>(client, listNode))!;
            StringAssert.Contains("double", error.Message);
        }
    }
}
