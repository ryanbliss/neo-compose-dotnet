#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Assets.Scripts.Neo;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P67 §7.2/§9 — the variant export collection and the
    /// <see cref="NeoVariant{T}"/> handle it resolves through.
    ///
    /// <para>These cover the record plumbing and the handle's identity and
    /// caching. The construction and application paths' closure halves
    /// (Initialize/Apply IR, Overrides, ChildOverrides) run against a real
    /// pushed variant graph in the rig E2E rather than a hand-built one:
    /// authoring compiled NeoScript IR by hand in a fixture would assert the
    /// fixture, not the pipeline.</para>
    /// </summary>
    public class P67VariantTests
    {
        private const string PackageRoot = "Packages/com.ryanbliss.neocompose/Tests";
        private const string HeroClassId = "class-hero";

        private static string LoadFixture(string fileName)
        {
            return File.ReadAllText(Path.Combine(PackageRoot, fileName));
        }

        private static NeoClient LoadClient()
        {
            return NeoTestSaveStack.LoadClient(LoadFixture("synth-example.json"));
        }

        private static VariantRecord Variant(
            string id,
            string name,
            string? folder,
            string valueId = "value-variant-graph")
        {
            return new VariantRecord
            {
                id = id,
                projectId = "synth",
                classId = HeroClassId,
                name = name,
                folder = folder,
                valueId = valueId,
            };
        }

        // -------------------------------------------------------------------
        // §9 — the collection and the schema handshake.
        // -------------------------------------------------------------------

        [Test]
        public void ExportSchemaVersion_CurrentContractIsTwentyOne()
        {
            Assert.AreEqual(21, NeoProjectExportContract.CurrentSchemaVersion);
        }

        [Test]
        public void Variants_DefaultToAnEmptyCollection()
        {
            // A project that declares none, and a hand-built ProjectData that
            // left the field null, are the same answer.
            NeoClient client = LoadClient();
            Assert.IsFalse(client.TryGetVariant("nope", out VariantRecord? missing));
            Assert.IsNull(missing);
        }

        [Test]
        public void Variants_ResolveByRecordId()
        {
            NeoClient client = LoadClient();
            VariantRecord record = Variant("variant-down", "Down", null);
            client.ProjectDataForRuntime.variants[record.id] = record;

            Assert.IsTrue(client.TryGetVariant("variant-down", out VariantRecord? found));
            Assert.AreEqual("Down", found!.name);
            Assert.IsNull(found.folder);
        }

        // -------------------------------------------------------------------
        // §7.2 — handle identity.
        // -------------------------------------------------------------------

        [Test]
        public void ResolveVariant_ProjectsTheRecordOntoTheHandle()
        {
            NeoClient client = LoadClient();
            client.ProjectDataForRuntime.variants["variant-oak"] =
                Variant("variant-oak", "Oak", "Trees/Oak");

            NeoVariant<Hero> variant =
                NeoGeneratedTypesSupport.ResolveVariant<Hero>(client, "variant-oak");

            Assert.AreEqual("variant-oak", variant.VariantId);
            Assert.AreEqual(HeroClassId, variant.ClassId);
            Assert.AreEqual("Oak", variant.Name);
            Assert.AreEqual("Trees/Oak", variant.Folder);
        }

        [Test]
        public void ResolveBaseVariant_IsTheClassWithNoVariantApplied()
        {
            NeoClient client = LoadClient();

            NeoVariant<Hero> baseVariant =
                NeoGeneratedTypesSupport.ResolveBaseVariant<Hero>(client, HeroClassId);

            // §3.4/§6 — the base entry stores `{classId, variantId: null}`.
            Assert.IsNull(baseVariant.VariantId);
            Assert.AreEqual(HeroClassId, baseVariant.ClassId);
            Assert.AreEqual("Base", baseVariant.Name);
            Assert.IsNull(baseVariant.Folder);
        }

        [Test]
        public void ResolveVariant_CachesOneHandlePerVariant()
        {
            NeoClient client = LoadClient();
            client.ProjectDataForRuntime.variants["variant-down"] =
                Variant("variant-down", "Down", null);

            NeoVariant<Hero> first =
                NeoGeneratedTypesSupport.ResolveVariant<Hero>(client, "variant-down");
            NeoVariant<Hero> second =
                NeoGeneratedTypesSupport.ResolveVariant<Hero>(client, "variant-down");

            // A `Variants` entry is a property, so a loop reading it every frame
            // must not mint a handle per read.
            Assert.AreSame(first, second);
        }

        [Test]
        public void ResolveBaseVariant_CachesSeparatelyFromDeclaredVariants()
        {
            NeoClient client = LoadClient();
            client.ProjectDataForRuntime.variants[HeroClassId] =
                Variant(HeroClassId, "Collide", null);

            NeoVariant<Hero> declared =
                NeoGeneratedTypesSupport.ResolveVariant<Hero>(client, HeroClassId);
            NeoVariant<Hero> baseVariant =
                NeoGeneratedTypesSupport.ResolveBaseVariant<Hero>(client, HeroClassId);

            // The key prefix is what keeps a record id and a class id apart even
            // when a project mints them equal.
            Assert.AreNotSame(declared, baseVariant);
            Assert.AreEqual(HeroClassId, declared.VariantId);
            Assert.IsNull(baseVariant.VariantId);
        }

        [Test]
        public void ResolveVariant_RejectsAnIdThatIsNotInTheExport()
        {
            NeoClient client = LoadClient();

            var error = Assert.Throws<InvalidOperationException>(() =>
                NeoGeneratedTypesSupport.ResolveVariant<Hero>(client, "variant-missing"))!;

            StringAssert.Contains("variant-missing", error.Message);
            StringAssert.Contains("not in this project export", error.Message);
        }

        [Test]
        public void ResolveVariant_RequiresAVariantId()
        {
            NeoClient client = LoadClient();
            Assert.Throws<ArgumentException>(() =>
                NeoGeneratedTypesSupport.ResolveVariant<Hero>(client, "  "));
        }

        // -------------------------------------------------------------------
        // §4.2/§4.3 — application is in place and returns the receiver.
        // -------------------------------------------------------------------

        [Test]
        public void ToVariant_ReturnsTheReceiverForTheBaseEntry()
        {
            NeoClient client = LoadClient();
            Hero hero = RequireHero(client);
            NeoVariant<Hero> baseVariant =
                NeoGeneratedTypesSupport.ResolveBaseVariant<Hero>(client, HeroClassId);

            Hero applied = NeoGeneratedTypesSupport.ApplyVariant(hero, baseVariant);

            // §4.2 step 4: always the same instance, never a replacement.
            Assert.AreSame(hero, applied);
        }

        [Test]
        public void ApplyVariant_RejectsNullOperands()
        {
            NeoClient client = LoadClient();
            Hero hero = RequireHero(client);
            NeoVariant<Hero> baseVariant =
                NeoGeneratedTypesSupport.ResolveBaseVariant<Hero>(client, HeroClassId);

            Assert.Throws<ArgumentNullException>(() =>
                NeoGeneratedTypesSupport.ApplyVariant<Hero>(null!, baseVariant));
            Assert.Throws<ArgumentNullException>(() =>
                NeoGeneratedTypesSupport.ApplyVariant(hero, null!));
        }

        private static Hero RequireHero(NeoClient client)
        {
            // The synth fixture places Hero rows in whichever store its roots
            // resolve to, so search all three rather than pinning one.
            var stores = new (IReadOnlyDictionary<string, MemberValue> rows,
                NeoValueOwnership ownership)[]
            {
                (client.saveValues, NeoValueOwnership.Save),
                (client.sessionValues, NeoValueOwnership.Session),
                (client.values, NeoValueOwnership.Asset),
            };
            foreach ((IReadOnlyDictionary<string, MemberValue> rows,
                NeoValueOwnership ownership) in stores)
            foreach (KeyValuePair<string, MemberValue> pair in rows)
            {
                if (pair.Value is not ObjectMemberValue row) continue;
                if (row.classId != HeroClassId) continue;
                var member = new ClassMember
                {
                    id = "__test_hero",
                    name = "Hero",
                    kind = MemberKind.Class,
                    classId = HeroClassId,
                    createdAt = row.createdAt,
                    updatedAt = row.updatedAt,
                };
                return Hero.CreateWritable(
                    client,
                    new NeoMemberClassWritable(
                        client,
                        member,
                        row.id,
                        ownership));
            }
            throw new InvalidOperationException(
                "The synth fixture carries no Hero row to apply a variant to.");
        }
    }
}
