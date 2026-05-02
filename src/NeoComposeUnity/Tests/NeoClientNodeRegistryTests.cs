// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.IO;
using NUnit.Framework;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Tests
{
    /// <summary>
    /// Coverage for the flat <see cref="NeoClient.nodes"/> registry +
    /// dedup behavior on <see cref="NeoAttribute.Create"/> /
    /// <see cref="NeoAttribute.CreateSaved"/>.
    ///
    /// The registry's contract:
    ///
    ///   - Every constructed <see cref="NeoAttribute"/> registers itself
    ///     under <c>MakeNodeKey(attribute.id, overrideValueId)</c>.
    ///   - <see cref="NeoAttribute.Create"/> /
    ///     <see cref="NeoAttribute.CreateSaved"/> short-circuit to the
    ///     registered instance when one exists for the requested key.
    ///   - <c>overrideValueId</c> being null produces a key of
    ///     <c>attribute.id</c>; non-null appends <c>"_{valueId}"</c>.
    /// </summary>
    public class NeoClientNodeRegistryTests
    {
        private const string PackageRoot =
            "Packages/com.ryanbliss.neocompose/Tests";

        private static string LoadFixture(string fileName)
        {
            return File.ReadAllText(Path.Combine(PackageRoot, fileName));
        }

        private static NeoClient LoadClient()
        {
            var loader = new NeoLoader();
            string saveBuffer = "";
            string loadSave() => saveBuffer;
            void handleSave(string file) => saveBuffer = file;
            return loader.Load(LoadFixture("synth-example.json"), loadSave, handleSave);
        }

        /// <summary>
        /// Wraps <see cref="NeoClient.TryGetAttribute"/> with an assert
        /// + non-null return so tests can chain through to typed usage
        /// without the nullable flow-analysis fighting them. NUnit's
        /// <c>Assert.IsTrue(TryGet(out var x))</c> doesn't propagate
        /// the not-null narrowing the way an inline <c>if</c> does, so
        /// callers reading <c>x</c> after the assert still see <c>T?</c>.
        /// </summary>
        private static T RequireAttribute<T>(NeoClient client, string id) where T : Attribute
        {
            if (!client.TryGetAttribute(id, out T? attr))
            {
                Assert.Fail($"Fixture is missing attribute '{id}' of type {typeof(T).Name}");
                throw new System.InvalidOperationException("unreachable");
            }
            return attr;
        }

        private static NeoAttribute RequireNode(
            NeoClient client,
            string attributeId,
            string? overrideValueId)
        {
            if (!client.TryGetNode(attributeId, overrideValueId, out NeoAttribute? node))
            {
                Assert.Fail(
                    $"Registry is missing node {NeoClient.MakeNodeKey(attributeId, overrideValueId)}");
                throw new System.InvalidOperationException("unreachable");
            }
            return node;
        }

        [Test]
        public void MakeNodeKey_NoOverride_IsBareAttributeId()
        {
            Assert.AreEqual("attr-x", NeoClient.MakeNodeKey("attr-x", null));
            Assert.AreEqual("attr-x", NeoClient.MakeNodeKey("attr-x", ""));
        }

        [Test]
        public void MakeNodeKey_WithOverride_AppendsValueId()
        {
            Assert.AreEqual("attr-x_v-7", NeoClient.MakeNodeKey("attr-x", "v-7"));
        }

        [Test]
        public void NeoClient_RootsAreRegistered()
        {
            var client = LoadClient();

            // The two roots are constructed in NeoClient's ctor; both
            // self-register.
            Assert.AreSame(client.assets, RequireNode(client, "root-assets", null));
            Assert.AreSame(client.save, RequireNode(client, "root-save", null));
        }

        [Test]
        public void Create_ReturnsCachedInstance_OnSecondCall()
        {
            var client = LoadClient();
            var nameAttr = RequireAttribute<StringAttribute>(client, "attr-name");

            var first = NeoAttribute.Create(client, nameAttr, null);
            var second = NeoAttribute.Create(client, nameAttr, null);

            Assert.AreSame(first, second,
                "Create should short-circuit to the cached node, not construct a duplicate");
        }

        [Test]
        public void CreateSaved_ReturnsCachedInstance_OnSecondCall()
        {
            var client = LoadClient();
            var nameAttr = RequireAttribute<StringAttribute>(client, "attr-name");

            var first = NeoAttribute.CreateSaved(client, nameAttr, null);
            var second = NeoAttribute.CreateSaved(client, nameAttr, null);

            Assert.AreSame(first, second);
            Assert.IsInstanceOf<NeoAttributeStringSaved>(first);
        }

        [Test]
        public void Create_OverrideValueId_RegistersUnderComposedKey()
        {
            var client = LoadClient();
            var nameAttr = RequireAttribute<StringAttribute>(client, "attr-name");

            var noOverride = NeoAttribute.Create(client, nameAttr, null);
            var withOverride = NeoAttribute.Create(client, nameAttr, "v-str");

            Assert.AreNotSame(noOverride, withOverride,
                "Different override-value ids must compose distinct registry keys");

            Assert.AreSame(noOverride, RequireNode(client, "attr-name", null));
            Assert.AreSame(withOverride, RequireNode(client, "attr-name", "v-str"));
        }

        [Test]
        public void NeoClient_Nodes_ContainsWalkedChildren()
        {
            var client = LoadClient();
            var heroAttr = RequireAttribute<CustomAttribute>(client, "attr-hero");
            // Construct a Custom bound to the stored v-dict row
            // (defaultValue alone wouldn't trigger a child walk —
            // attr-hero has no static valueId of its own). v-dict
            // carries `{ Name: "v-name", Level: "v-level" }`; "Level"
            // isn't in the type-hero schema so only the "Name" child
            // is walked + registered.
            var hero = NeoAttribute.Create(client, heroAttr, "v-dict") as NeoAttributeCustom;
            Assert.IsNotNull(hero);

            Assert.IsTrue(
                client.nodes.ContainsKey("attr-hero_v-dict"),
                "Parent registers under its composed key");
            var nameChild = RequireNode(client, "attr-name", "v-name");
            Assert.IsInstanceOf<NeoAttributeString>(nameChild);
        }

        [Test]
        public void Create_FollowedByCreateSaved_ReturnsFirstInstance()
        {
            // Pinned behavior: the cache is first-write-wins for the
            // composed key. If a consumer mixes Create + CreateSaved
            // for the same attribute they get whichever was first —
            // there's no Saved-vs-read-only flag in the key. The right
            // way to build a writeable sub-tree is to bootstrap from
            // the root with CreateSaved.
            var client = LoadClient();
            var altAttr = RequireAttribute<StringAttribute>(client, "attr-altname");

            var first = NeoAttribute.Create(client, altAttr, null);
            var second = NeoAttribute.CreateSaved(client, altAttr, null);

            Assert.AreSame(first, second);
            Assert.IsInstanceOf<NeoAttributeString>(first);
            Assert.IsNotInstanceOf<NeoAttributeStringSaved>(first,
                "First Create call wins — no Saved variant is constructed even though CreateSaved was the second call");
        }
    }
}
