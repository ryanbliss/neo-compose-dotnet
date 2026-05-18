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
    /// <see cref="NeoAttribute.CreateWritable"/>.
    ///
    /// The registry's contract:
    ///
        ///   - Every constructed <see cref="NeoAttribute"/> registers itself
        ///     under <c>MakeNodeKey(attribute.id, overrideValueId, ownership)</c>.
    ///   - <see cref="NeoAttribute.Create"/> /
    ///     <see cref="NeoAttribute.CreateWritable"/> short-circuit to the
    ///     registered instance when one exists for the requested key.
        ///   - <c>overrideValueId</c> being null produces a key scoped by
        ///     ownership; non-null appends <c>"_{valueId}"</c>.
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
            string? overrideValueId,
            NeoValueOwnership ownership = NeoValueOwnership.Asset)
        {
            if (!client.TryGetNode(attributeId, overrideValueId, ownership, out NeoAttribute? node))
            {
                Assert.Fail(
                    $"Registry is missing node {NeoClient.MakeNodeKey(attributeId, overrideValueId, ownership)}");
                throw new System.InvalidOperationException("unreachable");
            }
            return node;
        }

        [Test]
        public void MakeNodeKey_NoOverride_IsBareAttributeId()
        {
            Assert.AreEqual("asset:attr-x", NeoClient.MakeNodeKey("attr-x", null));
            Assert.AreEqual("asset:attr-x", NeoClient.MakeNodeKey("attr-x", ""));
            Assert.AreEqual("save:attr-x", NeoClient.MakeNodeKey(
                "attr-x",
                null,
                NeoValueOwnership.Save));
        }

        [Test]
        public void MakeNodeKey_WithOverride_AppendsValueId()
        {
            Assert.AreEqual("asset:attr-x_v-7", NeoClient.MakeNodeKey("attr-x", "v-7"));
        }

        [Test]
        public void NeoClient_RootsAreRegistered()
        {
            var client = LoadClient();

            // The roots are constructed in NeoClient's ctor; all
            // self-register.
            Assert.AreSame(client.assets, RequireNode(client, "root-assets", null));
            Assert.AreSame(client.save, RequireNode(
                client,
                "root-save",
                null,
                NeoValueOwnership.Save));
            Assert.AreSame(client.session, RequireNode(
                client,
                "root-session",
                null,
                NeoValueOwnership.Session));
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
        public void CreateWritable_ReturnsCachedInstance_OnSecondCall()
        {
            var client = LoadClient();
            var nameAttr = RequireAttribute<StringAttribute>(client, "attr-name");

            var first = NeoAttribute.CreateWritable(client, nameAttr, null);
            var second = NeoAttribute.CreateWritable(client, nameAttr, null);

            Assert.AreSame(first, second);
            Assert.IsInstanceOf<NeoAttributeStringWritable>(first);
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
                client.nodes.ContainsKey("asset:attr-hero_v-dict"),
                "Parent registers under its composed key");
            var nameChild = RequireNode(client, "attr-name", "v-name");
            Assert.IsInstanceOf<NeoAttributeString>(nameChild);
        }

        [Test]
        public void Create_FollowedByCreateWritable_ReplacesReadOnlyWithSavedInstance()
        {
            // Assets are constructed before Save and can register
            // read-only children for shared schema attributes. A later
            // saved construction for the same key must upgrade the
            // registry entry so save-side generated wrappers can get
            // writeable child nodes.
            var client = LoadClient();
            var altAttr = RequireAttribute<StringAttribute>(client, "attr-altname");

            var first = NeoAttribute.Create(client, altAttr, null);
            var second = NeoAttribute.CreateWritable(client, altAttr, null);

            Assert.AreNotSame(first, second);
            Assert.IsInstanceOf<NeoAttributeString>(first);
            Assert.IsInstanceOf<NeoAttributeStringWritable>(second);
            Assert.AreSame(second, RequireNode(
                client,
                "attr-altname",
                null,
                NeoValueOwnership.Session));
        }
    }
}
