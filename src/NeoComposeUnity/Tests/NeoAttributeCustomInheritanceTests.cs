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
    /// Integration coverage for <see cref="NeoAttributeCustom.mergedSchema"/>
    /// across an inheritance chain. The synth fixture wires three Custom
    /// types in an <c>extendsTypeId</c> chain — see the dump script
    /// (<c>scripts/dump-synth-export.ts</c>) for the exact shape:
    ///
    /// <code>
    ///   type-base      schema: { Name: attr-name }
    ///   type-derived   extends type-base, schema: { Health: attr-health }
    ///   type-override  extends type-base, schema: { Name: attr-altname }
    /// </code>
    ///
    /// Three Custom attributes (one per type) give us live nodes the
    /// tests instantiate via <see cref="NeoAttribute.Create"/> and
    /// inspect.
    /// </summary>
    public class NeoAttributeCustomInheritanceTests
    {
        private const string PackageRoot =
            "Packages/com.ryanbliss.neocompose/Tests";

        private static string LoadFixture(string fileName)
        {
            return File.ReadAllText(Path.Combine(PackageRoot, fileName));
        }

        private static NeoClient LoadClient()
        {
            return NeoTestSaveStack.LoadClient(LoadFixture("synth-example.json"));
        }

        private static NeoAttributeCustom CreateCustom(NeoClient client, string attributeId)
        {
            if (!client.TryGetAttribute(attributeId, out CustomAttribute? attr))
            {
                Assert.Fail($"Fixture is missing CustomAttribute '{attributeId}'");
                throw new System.InvalidOperationException("unreachable");
            }
            return new NeoAttributeCustom(client, attr, null);
        }

        // -----------------------------------------------------------------
        // No-inheritance baseline — `attr-hero` uses `type-hero`, which
        // has no `extendsTypeId`. The merged schema should be a single
        // chain link with the type's own keys, in declared order.
        // -----------------------------------------------------------------

        [Test]
        public void MergedSchema_NoInheritance_MatchesTypeSchema()
        {
            var client = LoadClient();
            var hero = CreateCustom(client, "attr-hero");

            Assert.AreEqual(1, hero.inheritanceChain.Count,
                "Type with no extendsTypeId has a single-link chain");
            Assert.AreEqual("type-hero", hero.inheritanceChain[0].id);

            Assert.AreEqual(2, hero.mergedSchema.Count);
            // Owner is the declared type for every entry — no inheritance.
            foreach (var entry in hero.mergedSchema)
            {
                Assert.AreEqual("type-hero", entry.ownerTypeId);
            }
        }

        // -----------------------------------------------------------------
        // Derived type — keys flow base-first (root ancestor's keys
        // first, then descendant's new keys). Each entry's ownerTypeId
        // names the type that contributed the entry.
        // -----------------------------------------------------------------

        [Test]
        public void MergedSchema_Derived_IncludesAncestorKeysBaseFirst()
        {
            var client = LoadClient();
            var derived = CreateCustom(client, "attr-derived");

            // Chain is child-first.
            Assert.AreEqual(2, derived.inheritanceChain.Count);
            Assert.AreEqual("type-derived", derived.inheritanceChain[0].id);
            Assert.AreEqual("type-base", derived.inheritanceChain[1].id);

            // Merged schema is base-first: Name (from type-base) then
            // Health (from type-derived).
            Assert.AreEqual(2, derived.mergedSchema.Count);

            Assert.AreEqual("Name", derived.mergedSchema[0].schemaKey);
            Assert.AreEqual("attr-name", derived.mergedSchema[0].attributeId);
            Assert.AreEqual("type-base", derived.mergedSchema[0].ownerTypeId);

            Assert.AreEqual("Health", derived.mergedSchema[1].schemaKey);
            Assert.AreEqual("attr-health", derived.mergedSchema[1].attributeId);
            Assert.AreEqual("type-derived", derived.mergedSchema[1].ownerTypeId);
        }

        // -----------------------------------------------------------------
        // Override — child's `Name` rebinds the ancestor's `Name` key
        // to a different attribute id. Key insertion order is preserved
        // (Name stays first because the base introduced it), but the
        // resolved attributeId + ownerTypeId both flip to the override.
        // -----------------------------------------------------------------

        [Test]
        public void MergedSchema_Override_ChildRebindsAncestorKey()
        {
            var client = LoadClient();
            var overrideCustom = CreateCustom(client, "attr-override");

            Assert.AreEqual(2, overrideCustom.inheritanceChain.Count);
            Assert.AreEqual("type-override", overrideCustom.inheritanceChain[0].id);
            Assert.AreEqual("type-base", overrideCustom.inheritanceChain[1].id);

            Assert.AreEqual(1, overrideCustom.mergedSchema.Count,
                "Override type only redefines the existing Name key — no new keys");

            var entry = overrideCustom.mergedSchema[0];
            Assert.AreEqual("Name", entry.schemaKey);
            // Child wins: attribute id flips from attr-name (base) to
            // attr-altname (override).
            Assert.AreEqual("attr-altname", entry.attributeId);
            Assert.AreEqual("type-override", entry.ownerTypeId);
        }

        // -----------------------------------------------------------------
        // Direct-helper unit coverage — exercises ResolveChain /
        // MergeSchemas without going through NeoAttributeCustom.
        // -----------------------------------------------------------------

        [Test]
        public void ResolveChain_WalksExtendsTypeId()
        {
            var client = LoadClient();
            CustomType? Lookup(string id) => client.TryGetType(id, out var t) ? t : null;

            var chain = CustomTypeInheritance.ResolveChain("type-derived", Lookup);

            Assert.AreEqual(2, chain.Count);
            Assert.AreEqual("type-derived", chain[0].id);
            Assert.AreEqual("type-base", chain[1].id);
        }

        [Test]
        public void MergeSchemas_ChildWinsAtSharedKey()
        {
            var client = LoadClient();
            CustomType? Lookup(string id) => client.TryGetType(id, out var t) ? t : null;

            var chain = CustomTypeInheritance.ResolveChain("type-override", Lookup);
            var merged = CustomTypeInheritance.MergeSchemas(chain);

            Assert.AreEqual(1, merged.Count);
            Assert.AreEqual("attr-altname", merged[0].attributeId);
            Assert.AreEqual("type-override", merged[0].ownerTypeId);
        }
    }
}
