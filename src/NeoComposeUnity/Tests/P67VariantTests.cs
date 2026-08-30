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

        private static NeoLookupVariant<Hero, Hero> RegisterLookupVariant(
            NeoClient client,
            Hero row)
        {
            const string entryMemberId = "member-lookup-entry";
            const string collectionMemberId = "member-lookup-catalog";
            const string collectionValueId = "value-lookup-catalog";
            client.ProjectDataForRuntime.members[entryMemberId] = new ClassMember
            {
                id = entryMemberId,
                projectId = "synth",
                name = "Entry",
                kind = MemberKind.Class,
                classId = HeroClassId,
                createdAt = "x",
                updatedAt = "x",
            };
            client.ProjectDataForRuntime.members[collectionMemberId] = new ListMember
            {
                id = collectionMemberId,
                projectId = "synth",
                name = "Catalog",
                kind = MemberKind.List,
                entryMemberId = entryMemberId,
                createdAt = "x",
                updatedAt = "x",
            };
            client.ProjectDataForRuntime.values[collectionValueId] = new ArrayMemberValue
            {
                id = collectionValueId,
                value = new[] { row.valueId! },
                createdAt = "x",
                updatedAt = "x",
            };
            client.ProjectDataForRuntime.variantFolders["folder-stages"] =
                new VariantFolderRecord
                {
                    id = "folder-stages",
                    classId = HeroClassId,
                    path = "Stages",
                    binding = new VariantFolderBinding
                    {
                        collectionMemberId = collectionMemberId,
                        collectionValueId = collectionValueId,
                    },
                };
            client.ProjectDataForRuntime.variants["variant-lookup"] =
                Variant("variant-lookup", "Mature", "Stages");
            return NeoGeneratedTypesSupport.ResolveLookupVariant<Hero, Hero>(
                client,
                "variant-lookup");
        }

        // -------------------------------------------------------------------
        // §9 — the collection and the schema handshake.
        // -------------------------------------------------------------------

        [Test]
        public void ExportSchemaVersion_CurrentContractIsThirty()
        {
            Assert.AreEqual(30, NeoProjectExportContract.CurrentSchemaVersion);
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
        // P68 §6/§7 — lookup handles and row erasure.
        // -------------------------------------------------------------------

        [Test]
        public void ResolveLookupVariant_ProjectsAndCachesTheTypedHandle()
        {
            NeoClient client = LoadClient();
            Hero row = RequireHero(client);

            NeoLookupVariant<Hero, Hero> first = RegisterLookupVariant(client, row);
            NeoLookupVariant<Hero, Hero> second =
                NeoGeneratedTypesSupport.ResolveLookupVariant<Hero, Hero>(
                    client,
                    "variant-lookup");

            Assert.AreEqual("variant-lookup", first.VariantId);
            Assert.AreEqual(HeroClassId, first.ClassId);
            Assert.AreEqual("Mature", first.Name);
            Assert.AreEqual("Stages", first.Folder);
            Assert.AreSame(first, second);
        }

        [Test]
        public void LookupVariantBind_ErasesTheRowIntoAPlainVariantValue()
        {
            NeoClient client = LoadClient();
            Hero row = RequireHero(client);
            NeoLookupVariant<Hero, Hero> lookup = RegisterLookupVariant(client, row);

            NeoVariant<Hero> bound = lookup.Bind(row);
            var stored = (VariantRefValue)NeoGeneratedTypesSupport
                .VariantValue(bound)!.value!;

            Assert.AreEqual("variant-lookup", stored.variantId);
            Assert.AreEqual(row.valueId, stored.rowValueId);
        }

        [Test]
        public void VariantMember_ResolvesABoundLookupAsAPlainHandle()
        {
            NeoClient client = LoadClient();
            Hero row = RequireHero(client);
            RegisterLookupVariant(client, row);
            client.RegisterGeneratedClassFactories(
                TestProjectNeo.NeoReadOnlyValueFactories,
                TestProjectNeo.NeoWritableValueFactories);
            NeoMemberVariant node = VariantNode(
                client,
                required: true,
                new VariantRefValue
                {
                    classId = HeroClassId,
                    variantId = "variant-lookup",
                    rowValueId = row.valueId,
                });

            NeoVariant<Hero>? resolved =
                NeoGeneratedTypesSupport.ResolveVariantValue<Hero>(node);

            Assert.IsNotNull(resolved);
            Assert.AreEqual("variant-lookup", resolved!.VariantId);
            Assert.AreEqual(row.valueId, resolved.RowValueId);
        }

        [Test]
        public void LookupVariantMember_StaysUnbound()
        {
            NeoClient client = LoadClient();
            Hero row = RequireHero(client);
            RegisterLookupVariant(client, row);
            NeoMemberVariant node = VariantNode(
                client,
                required: true,
                new VariantRefValue
                {
                    classId = HeroClassId,
                    variantId = "variant-lookup",
                    rowValueId = null,
                });

            NeoLookupVariant<Hero, Hero>? resolved =
                NeoGeneratedTypesSupport.ResolveLookupVariantValue<Hero, Hero>(node);
            Assert.IsNotNull(resolved);

            node.value!.value!.rowValueId = row.valueId;
            Assert.Throws<InvalidOperationException>(() =>
                NeoGeneratedTypesSupport.ResolveLookupVariantValue<Hero, Hero>(node));
        }

        [Test]
        public void LookupVariantBind_RejectsARowOutsideItsCollection()
        {
            NeoClient client = LoadClient();
            Hero row = RequireHero(client);
            NeoLookupVariant<Hero, Hero> lookup = RegisterLookupVariant(client, row);
            ((ArrayMemberValue)client.ProjectDataForRuntime.values[
                "value-lookup-catalog"]).value = Array.Empty<string>();

            var error = Assert.Throws<InvalidOperationException>(() =>
                lookup.Bind(row))!;

            StringAssert.Contains("not an entry", error.Message);
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

        // -------------------------------------------------------------------
        // §7.4 — the Variant member property's read and write halves.
        // -------------------------------------------------------------------

        private static NeoMemberVariant VariantNode(
            NeoClient client,
            bool required,
            VariantRefValue? stored)
        {
            var member = new VariantMember
            {
                id = "member-chosen",
                name = "Chosen",
                kind = MemberKind.Variant,
                Requirement = required ? NeoMemberRequirementKind.Required : NeoMemberRequirementKind.Optional,
                createdAt = "1970-01-01T00:00:00.000Z",
                updatedAt = "1970-01-01T00:00:00.000Z",
            };
            client.ProjectDataForRuntime.values["value-chosen"] =
                new VariantMemberValue
                {
                    id = "value-chosen",
                    value = stored,
                    createdAt = member.createdAt,
                    updatedAt = member.updatedAt,
                };
            return new NeoMemberVariant(client, member, "value-chosen");
        }

        [Test]
        public void ResolveVariantValue_ReadsAStoredPairIntoAHandle()
        {
            NeoClient client = LoadClient();
            client.ProjectDataForRuntime.variants["variant-down"] =
                Variant("variant-down", "Down", null);
            NeoMemberVariant node = VariantNode(
                client,
                required: true,
                new VariantRefValue { classId = HeroClassId, variantId = "variant-down" });

            NeoVariant<Hero>? resolved =
                NeoGeneratedTypesSupport.ResolveVariantValue<Hero>(node);

            Assert.IsNotNull(resolved);
            Assert.AreEqual("variant-down", resolved!.VariantId);
            Assert.AreEqual("Down", resolved.Name);
            // The same cache the static tree uses: a member read and a
            // `Class.Variants.X` read are the same handle.
            Assert.AreSame(
                NeoGeneratedTypesSupport.ResolveVariant<Hero>(client, "variant-down"),
                resolved);
        }

        [Test]
        public void ResolveVariantValue_ReadsTheBaseSelection()
        {
            NeoClient client = LoadClient();
            NeoMemberVariant node = VariantNode(
                client,
                required: true,
                new VariantRefValue { classId = HeroClassId, variantId = null });

            NeoVariant<Hero>? resolved =
                NeoGeneratedTypesSupport.ResolveVariantValue<Hero>(node);

            Assert.IsNotNull(resolved);
            Assert.IsNull(resolved!.VariantId);
            Assert.AreEqual("Base", resolved.Name);
        }

        [Test]
        public void ResolveVariantValue_ResolvesACovariantStoredClass()
        {
            // §6: the pair may name a SUBCLASS of the member's declared target,
            // and the handle has to follow the stored class rather than the
            // declaration — otherwise a base-typed member would construct the
            // wrong object.
            NeoClient client = LoadClient();
            const string subclassId = "class-hero-subclass";
            client.ProjectDataForRuntime.variants["variant-sub"] = new VariantRecord
            {
                id = "variant-sub",
                projectId = "synth",
                classId = subclassId,
                name = "Sub",
                folder = null,
                valueId = "value-variant-graph",
            };
            NeoMemberVariant node = VariantNode(
                client,
                required: true,
                new VariantRefValue { classId = subclassId, variantId = "variant-sub" });

            NeoVariant<Hero>? resolved =
                NeoGeneratedTypesSupport.ResolveVariantValue<Hero>(node);

            Assert.IsNotNull(resolved);
            Assert.AreEqual(subclassId, resolved!.ClassId);
        }

        [Test]
        public void ResolveVariantValue_ReturnsNullOnlyForANullableMember()
        {
            NeoClient client = LoadClient();

            Assert.IsNull(
                NeoGeneratedTypesSupport.ResolveVariantValue<Hero>(
                    VariantNode(client, required: false, stored: null)));

            var error = Assert.Throws<InvalidOperationException>(() =>
                NeoGeneratedTypesSupport.ResolveVariantValue<Hero>(
                    VariantNode(client, required: true, stored: null)))!;
            StringAssert.Contains("Chosen", error.Message);
        }

        [Test]
        public void VariantValue_WritesTheHandleByIdentity()
        {
            NeoClient client = LoadClient();
            client.ProjectDataForRuntime.variants["variant-down"] =
                Variant("variant-down", "Down", null);
            NeoVariant<Hero> handle =
                NeoGeneratedTypesSupport.ResolveVariant<Hero>(client, "variant-down");

            NeoValueWritePayload? payload =
                NeoGeneratedTypesSupport.VariantValue(handle);
            var written = payload!.value as VariantRefValue;

            Assert.IsNotNull(written);
            Assert.AreEqual(HeroClassId, written!.classId);
            Assert.AreEqual("variant-down", written.variantId);

            // A null handle is the "no selection" write, not an empty pair.
            NeoValueWritePayload? cleared =
                NeoGeneratedTypesSupport.VariantValue<Hero>(null);
            Assert.IsNull(cleared!.value);
        }

        // -------------------------------------------------------------------
        // Factory wiring: a class DECLARING a variant member must materialise.
        // The tests above build a NeoMemberVariant directly, which is exactly
        // why the missing factory arms shipped.
        // -------------------------------------------------------------------

        private const string HolderClassId = "class-variant-holder";

        /// <summary>
        /// Registers a class whose schema declares one variant member, plus the
        /// member itself, so node construction walks the real factories.
        /// </summary>
        private static VariantMember RegisterHolderClass(
            NeoClient client,
            VariantMemberValueBase? declarationDefault = null,
            NeoMemberStorage storage = NeoMemberStorage.Save)
        {
            var member = new VariantMember
            {
                id = "member-holder-chosen",
                name = "Chosen",
                kind = MemberKind.Variant,
                Requirement = NeoMemberRequirementKind.Optional,
                Storage = storage,
                defaultValue = declarationDefault,
                createdAt = "1970-01-01T00:00:00.000Z",
                updatedAt = "1970-01-01T00:00:00.000Z",
            };
            client.ProjectDataForRuntime.members[member.id] = member;
            client.ProjectDataForRuntime.classes[HolderClassId] = new NeoSchemaClass
            {
                id = HolderClassId,
                projectId = "synth",
                name = "VariantHolder",
                schema = new Dictionary<string, string> { ["Chosen"] = member.id },
            };
            return member;
        }

        private static ClassMember HolderMember() => new()
        {
            id = "member-holder",
            name = "Holder",
            kind = MemberKind.Class,
            classId = HolderClassId,
            createdAt = "1970-01-01T00:00:00.000Z",
            updatedAt = "1970-01-01T00:00:00.000Z",
        };

        private static string RegisterHolderRow(NeoClient client)
        {
            client.ProjectDataForRuntime.values["value-holder"] = new ObjectMemberValue
            {
                id = "value-holder",
                classId = HolderClassId,
                value = new Dictionary<string, string>(),
                createdAt = "1970-01-01T00:00:00.000Z",
                updatedAt = "1970-01-01T00:00:00.000Z",
            };
            return "value-holder";
        }

        [Test]
        public void WritableNode_MaterialisesAClassDeclaringAVariantMember()
        {
            // Regression: CreateWritable had no VariantMember arm, so merely
            // constructing a Save/Session instance of a class carrying a
            // variant member threw "Unknown member type VariantMember" before
            // any read or write happened.
            NeoClient client = LoadClient();
            RegisterHolderClass(client);
            string valueId = RegisterHolderRow(client);

            var node = new NeoMemberClassWritable(
                client,
                HolderMember(),
                valueId,
                NeoValueOwnership.Save);

            Assert.IsTrue(node.TryGet("Chosen", out NeoMemberVariant? child));
            Assert.IsInstanceOf<NeoMemberVariantWritable>(child);
        }

        [Test]
        public void AsWritable_MaterialisesAClassDeclaringAVariantMember()
        {
            // The write half of every family routes through AsWritable, so the
            // generated setter threw here too, even on an Asset-owned instance.
            NeoClient client = LoadClient();
            RegisterHolderClass(client);
            string valueId = RegisterHolderRow(client);
            var readOnly = new NeoMemberClass(client, HolderMember(), valueId);

            NeoMemberClassWritable writable =
                NeoGeneratedTypesSupport.AsWritable(readOnly);

            Assert.IsTrue(writable.TryGet("Chosen", out NeoMemberVariant? child));
            Assert.IsInstanceOf<NeoMemberVariantWritable>(child);
        }

        [Test]
        public void SetValue_WritesAVariantSelectionThroughTheNode()
        {
            // Regression: MemberValueFactory.Create had no VariantMember arm,
            // so the generated setter's SetValue threw.
            NeoClient client = LoadClient();
            RegisterHolderClass(client);
            client.ProjectDataForRuntime.variants["variant-down"] =
                Variant("variant-down", "Down", null);
            string valueId = RegisterHolderRow(client);
            var node = new NeoMemberClassWritable(
                client,
                HolderMember(),
                valueId,
                NeoValueOwnership.Save);
            NeoVariant<Hero> handle =
                NeoGeneratedTypesSupport.ResolveVariant<Hero>(client, "variant-down");

            NeoGeneratedTypesSupport.SetValue(
                node,
                "Chosen",
                NeoGeneratedTypesSupport.VariantValue(handle));

            Assert.IsTrue(node.TryGet("Chosen", out NeoMemberVariant? child));
            Assert.AreEqual(HeroClassId, child!.value?.value?.classId);
            Assert.AreEqual("variant-down", child.value?.value?.variantId);
            Assert.AreEqual(
                "variant-down",
                NeoGeneratedTypesSupport.ResolveVariantValue<Hero>(child)?.VariantId);
        }

        [Test]
        public void SetValue_ClearsAnOptionalVariantSelection()
        {
            NeoClient client = LoadClient();
            RegisterHolderClass(client);
            client.ProjectDataForRuntime.variants["variant-down"] =
                Variant("variant-down", "Down", null);
            string valueId = RegisterHolderRow(client);
            var node = new NeoMemberClassWritable(
                client,
                HolderMember(),
                valueId,
                NeoValueOwnership.Save);
            NeoGeneratedTypesSupport.SetValue(
                node,
                "Chosen",
                NeoGeneratedTypesSupport.VariantValue(
                    NeoGeneratedTypesSupport.ResolveVariant<Hero>(client, "variant-down")));

            NeoGeneratedTypesSupport.SetValue(
                node,
                "Chosen",
                NeoGeneratedTypesSupport.VariantValue<Hero>(null));

            Assert.IsTrue(node.TryGet("Chosen", out NeoMemberVariant? child));
            Assert.IsNull(NeoGeneratedTypesSupport.ResolveVariantValue<Hero>(child!));
        }

        [Test]
        public void DeclarationDefault_SurvivesAsAVariantSelection()
        {
            // Regression: CreateFromDefault fell to `_ => null`, so an authored
            // `NeoVariant<Tree> Felled = Tree.Variants.ChoppedTree` was
            // silently dropped and read back as "no selection".
            NeoClient client = LoadClient();
            client.ProjectDataForRuntime.variants["variant-down"] =
                Variant("variant-down", "Down", null);
            VariantMember member = RegisterHolderClass(
                client,
                new VariantMemberValueBase
                {
                    value = new VariantRefValue
                    {
                        classId = HeroClassId,
                        variantId = "variant-down",
                    },
                });

            MemberValue? row = MemberValueFactory.CreateFromDefault(
                member,
                "value-default",
                "1970-01-01T00:00:00.000Z",
                "1970-01-01T00:00:00.000Z");

            Assert.IsInstanceOf<VariantMemberValue>(row);
            var variantRow = (VariantMemberValue)row!;
            Assert.AreEqual("variant-down", variantRow.value?.variantId);
            // Copied, not aliased: a write through one instance must not edit
            // the declaration every other instance reads.
            Assert.AreNotSame(member.defaultValue!.value, variantRow.value);
        }

        // -------------------------------------------------------------------
        // Reflection seam behind Initialize().
        // -------------------------------------------------------------------

        [Test]
        public void Materialize_FindsTheGeneratedInternalWritableFactory()
        {
            // Regression: the lookup asked for a PUBLIC method taking
            // NeoMemberClass, but every generated factory is
            // `internal static X CreateWritable(NeoClient, NeoMemberClassWritable)`.
            // Both mismatched, so Initialize() threw for every generated T.
            NeoClient client = LoadClient();
            Hero hero = RequireHero(client);

            Hero materialised = NeoVariantSupport.Materialize<Hero>(
                client,
                hero.WritableBackingNode);

            Assert.IsNotNull(materialised);
            Assert.AreEqual(hero.valueId, materialised.valueId);
        }

        // -------------------------------------------------------------------
        // §6 covariance: one record, two surfaces, one client.
        // -------------------------------------------------------------------

        [Test]
        public void VariantHandles_AreCachedPerRequestingType()
        {
            // Regression: the cache key omitted T, so a base-typed member read
            // and the subclass's own Variants path collided on one key and
            // whichever asked second threw.
            NeoClient client = LoadClient();
            client.ProjectDataForRuntime.variants["variant-shared"] = new VariantRecord
            {
                id = "variant-shared",
                projectId = "synth",
                classId = HeroClassId,
                name = "Shared",
                folder = null,
                valueId = "value-variant-graph",
            };

            NeoVariant<Hero> asHero =
                NeoGeneratedTypesSupport.ResolveVariant<Hero>(client, "variant-shared");
            NeoVariant<Root> asRoot =
                NeoGeneratedTypesSupport.ResolveVariant<Root>(client, "variant-shared");

            Assert.AreEqual("variant-shared", asHero.VariantId);
            Assert.AreEqual("variant-shared", asRoot.VariantId);
            // Handle stability per surface is the cache's actual purpose.
            Assert.AreSame(
                asHero,
                NeoGeneratedTypesSupport.ResolveVariant<Hero>(client, "variant-shared"));
            Assert.AreSame(
                asRoot,
                NeoGeneratedTypesSupport.ResolveVariant<Root>(client, "variant-shared"));
        }

        [Test]
        public void BaseVariantHandles_AreAlsoCachedPerRequestingType()
        {
            NeoClient client = LoadClient();

            NeoVariant<Hero> asHero =
                NeoGeneratedTypesSupport.ResolveBaseVariant<Hero>(client, HeroClassId);
            NeoVariant<Root> asRoot =
                NeoGeneratedTypesSupport.ResolveBaseVariant<Root>(client, HeroClassId);

            Assert.IsNull(asHero.VariantId);
            Assert.IsNull(asRoot.VariantId);
            Assert.AreSame(
                asHero,
                NeoGeneratedTypesSupport.ResolveBaseVariant<Hero>(client, HeroClassId));
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
