// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using NUnit.Framework;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Tests
{
    /// <summary>
    /// Integration coverage for <see cref="NeoMemberClass.mergedSchema"/>
    /// across an inheritance chain. The synth fixture wires three Class
    /// classes in an <c>extendsClassId</c> chain — see the dump script
    /// (<c>scripts/dump-synth-export.ts</c>) for the exact shape:
    ///
    /// <code>
    ///   class-base      schema: { Name: member-name }
    ///   class-derived   extends class-base, schema: { Health: member-health }
    ///   class-override  extends class-base, schema: { Name: member-altname }
    /// </code>
    ///
    /// Three Class members (one per class) give us live nodes the
    /// tests instantiate via <see cref="NeoMember.Create"/> and
    /// inspect.
    /// </summary>
    public class NeoMemberClassInheritanceTests
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

        private static NeoMemberClass CreateClass(NeoClient client, string memberId)
        {
            if (!client.TryGetMember(memberId, out ClassMember? member))
            {
                Assert.Fail($"Fixture is missing ClassMember '{memberId}'");
                throw new System.InvalidOperationException("unreachable");
            }
            return new NeoMemberClass(client, member, null);
        }

        // -----------------------------------------------------------------
        // No-inheritance baseline — `member-hero` uses `class-hero`, which
        // has no `extendsClassId`. The merged schema should be a single
        // chain link with the class's own keys, in declared order.
        // -----------------------------------------------------------------

        [Test]
        public void MergedSchema_NoInheritance_MatchesClassSchema()
        {
            var client = LoadClient();
            var hero = CreateClass(client, "member-hero");

            Assert.AreEqual(1, hero.inheritanceChain.Count,
                "Class with no extendsClassId has a single-link chain");
            Assert.AreEqual("class-hero", hero.inheritanceChain[0].id);

            // Name, Health, BaseDamage, Position, GridCell, Path, MoveTo,
            // ElementAffinity.
            Assert.AreEqual(8, hero.mergedSchema.Count);
            // Owner is the declared class for every entry — no inheritance.
            foreach (var entry in hero.mergedSchema)
            {
                Assert.AreEqual("class-hero", entry.ownerClassId);
            }
        }

        [Test]
        public void NullableClassValue_DoesNotMaterializeComputedDefaultChildren()
        {
            ProjectData data = JsonConvert.DeserializeObject<ProjectData>(
                LoadFixture("synth-example.json"))!;
            const string saveClassId = "class-nullable-save";
            const string selectorClassId = "class-color-category-selector";
            const string selectorMemberId = "member-hat-color";
            const string categoryKindMemberId = "member-category-kind";
            const string enumId = "enum-category-kind";
            const string nullValueId = "value-null-hat-color";

            data.enums[enumId] = new NeoCompose.Runtime.Json.Enum
            {
                id = enumId,
                projectId = "test-project",
                name = "ColorCategoryKind",
                options = new Dictionary<string, EnumOption>
                {
                    ["standard"] = new EnumOption { text = "Standard" },
                },
                optionKeyOrder = new List<string> { "standard" },
                createdAt = 0,
                updatedAt = 0,
            };
            data.members[categoryKindMemberId] = new EnumMember
            {
                id = categoryKindMemberId,
                projectId = "test-project",
                name = "CategoryKind",
                kind = MemberKind.Enum,
                requirement = NeoMemberRequirementKind.Required,
                enumId = enumId,
                defaultValue = new ArrayMemberValueBase
                {
                    init = new InitializerBody { code = "categoryKind" },
                },
                createdAt = 0,
                updatedAt = 0,
            };
            data.classes[selectorClassId] = new NeoSchemaClass
            {
                id = selectorClassId,
                projectId = "test-project",
                name = "ColorCategorySelector",
                schema = new Dictionary<string, string>
                {
                    ["CategoryKind"] = categoryKindMemberId,
                },
                createdAt = 0,
                updatedAt = 0,
            };
            data.members[selectorMemberId] = new ClassMember
            {
                id = selectorMemberId,
                projectId = "test-project",
                name = "HatColor",
                kind = MemberKind.Class,
                requirement = NeoMemberRequirementKind.Optional,
                classId = selectorClassId,
                defaultValue = new ObjectMemberValueBase
                {
                    init = new InitializerBody { code = "new(.Standard)" },
                },
                createdAt = 0,
                updatedAt = 0,
            };
            data.classes[saveClassId] = new NeoSchemaClass
            {
                id = saveClassId,
                projectId = "test-project",
                name = "NullableSave",
                schema = new Dictionary<string, string>
                {
                    ["HatColor"] = selectorMemberId,
                },
                createdAt = 0,
                updatedAt = 0,
            };
            data.values[nullValueId] = new NullMemberValue
            {
                id = nullValueId,
                value = null,
                createdAt = 0,
                updatedAt = 0,
            };
            var saveMember = (ClassMember)data.members[data.project.rootSaveFileMemberId];
            saveMember.classId = saveClassId;
            var saveValue = (ObjectMemberValue)data.values[saveMember.valueId!];
            saveValue.classId = saveClassId;
            saveValue.value = new Dictionary<string, string>
            {
                ["HatColor"] = nullValueId,
            };

            using (NeoClient client = NeoTestSaveStack.ClientFromSchema(data))
            {
                Assert.IsTrue(client.save.TryGet(
                    "HatColor",
                    out NeoMemberClassWritable? selector));
                Assert.IsNull(selector!.value);
                Assert.IsFalse(selector.TryGet(
                    "CategoryKind",
                    out NeoMemberEnum? categoryKind));
                Assert.IsNull(categoryKind);
            }

            // Absence is not an explicit null. A required Class with an
            // object default must still walk its schema and surface the
            // computed child that cannot be literalized.
            saveValue.value.Clear();
            var selectorMember = (ClassMember)data.members[selectorMemberId];
            selectorMember.requirement = NeoMemberRequirementKind.Required;
            selectorMember.defaultValue = new ObjectMemberValueBase
            {
                value = new Dictionary<string, string>(),
            };

            System.InvalidOperationException error = Assert.Throws<System.InvalidOperationException>(
                () =>
                {
                    using NeoClient _ = NeoTestSaveStack.ClientFromSchema(data);
                })!;
            StringAssert.Contains("CategoryKind", error.Message);
        }
        // -----------------------------------------------------------------
        // Derived class — keys flow base-first (root ancestor's keys
        // first, then descendant's new keys). Each entry's ownerClassId
        // names the class that contributed the entry.
        // -----------------------------------------------------------------

        [Test]
        public void MergedSchema_Derived_IncludesAncestorKeysBaseFirst()
        {
            var client = LoadClient();
            var derived = CreateClass(client, "member-derived");

            // Chain is child-first.
            Assert.AreEqual(2, derived.inheritanceChain.Count);
            Assert.AreEqual("class-derived", derived.inheritanceChain[0].id);
            Assert.AreEqual("class-base", derived.inheritanceChain[1].id);

            // Merged schema is base-first: Name (from class-base) then
            // Health (from class-derived).
            Assert.AreEqual(2, derived.mergedSchema.Count);

            Assert.AreEqual("Name", derived.mergedSchema[0].schemaKey);
            Assert.AreEqual("member-name", derived.mergedSchema[0].memberId);
            Assert.AreEqual("class-base", derived.mergedSchema[0].ownerClassId);

            Assert.AreEqual("Health", derived.mergedSchema[1].schemaKey);
            Assert.AreEqual("member-health", derived.mergedSchema[1].memberId);
            Assert.AreEqual("class-derived", derived.mergedSchema[1].ownerClassId);
        }

        // -----------------------------------------------------------------
        // Override — child's `Name` rebinds the ancestor's `Name` key
        // to a different member id. Key insertion order is preserved
        // (Name stays first because the base introduced it), but the
        // resolved memberId + ownerClassId both flip to the override.
        // -----------------------------------------------------------------

        [Test]
        public void MergedSchema_Override_ChildRebindsAncestorKey()
        {
            var client = LoadClient();
            var overrideClass = CreateClass(client, "member-override");

            Assert.AreEqual(2, overrideClass.inheritanceChain.Count);
            Assert.AreEqual("class-override", overrideClass.inheritanceChain[0].id);
            Assert.AreEqual("class-base", overrideClass.inheritanceChain[1].id);

            Assert.AreEqual(1, overrideClass.mergedSchema.Count,
                "Override type only redefines the existing Name key — no new keys");

            var entry = overrideClass.mergedSchema[0];
            Assert.AreEqual("Name", entry.schemaKey);
            // Child wins: member id flips from member-name (base) to
            // member-altname (override).
            Assert.AreEqual("member-altname", entry.memberId);
            Assert.AreEqual("class-override", entry.ownerClassId);
        }

        // -----------------------------------------------------------------
        // Direct-helper unit coverage — exercises ResolveChain /
        // MergeSchemas without going through NeoMemberClass.
        // -----------------------------------------------------------------

        [Test]
        public void ResolveChain_WalksExtendsClassId()
        {
            var client = LoadClient();
            NeoSchemaClass? Lookup(string id) => client.TryGetClass(id, out var t) ? t : null;

            var chain = NeoSchemaClassInheritance.ResolveChain("class-derived", Lookup);

            Assert.AreEqual(2, chain.Count);
            Assert.AreEqual("class-derived", chain[0].id);
            Assert.AreEqual("class-base", chain[1].id);
        }

        [Test]
        public void MergeSchemas_ChildWinsAtSharedKey()
        {
            var client = LoadClient();
            NeoSchemaClass? Lookup(string id) => client.TryGetClass(id, out var t) ? t : null;

            var chain = NeoSchemaClassInheritance.ResolveChain("class-override", Lookup);
            var merged = NeoSchemaClassInheritance.MergeSchemas(chain);

            Assert.AreEqual(1, merged.Count);
            Assert.AreEqual("member-altname", merged[0].memberId);
            Assert.AreEqual("class-override", merged[0].ownerClassId);
        }
    }
}
