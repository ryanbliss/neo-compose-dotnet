// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Assets.Scripts.Neo;

namespace NeoCompose.Tests
{
    /// <summary>
    /// specs/dictionary-key-types.md §13.3 — fixture-based validation of
    /// enum-keyed dictionaries end to end. All typed access here goes through
    /// the real §13.2 codegen output: <c>NeoGeneratedTypes.cs</c> is emitted
    /// verbatim from <c>synth-example.json</c> by the web repo's
    /// <c>scripts/dump-nsgetter-expected.ts</c> (no hand-idealized wrappers),
    /// so these tests consume exactly what codegen emits.
    ///
    /// <para>The §13.1 fixture shapes exercised (see the synth export
    /// script's `attr-elem-*` records):
    /// <c>Root.ElementStats</c> (Save storage, authored `fire` entry + the
    /// deliberately dangling `storm` key), <c>Root.ElementMultipliers</c>
    /// (Static storage), <c>Hero.ElementAffinity</c> (nested inside a
    /// Custom type), and <c>Root.ElementChampions</c> (Custom-typed
    /// entries). The real-world <c>project-example.json</c> carries the
    /// mirror shapes plus the two enum-keyed NSGetters asserted at the
    /// bottom (and gated wholesale by <see cref="NSGetterParityTests"/>).</para>
    /// </summary>
    public class EnumKeyedDictionaryFixtureTests
    {
        private const string PackageRoot =
            "Packages/com.ryanbliss.neocompose/Tests";

        private static string LoadFixture(string fileName)
        {
            return File.ReadAllText(Path.Combine(PackageRoot, fileName));
        }

        private static TestProjectNeo LoadGeneratedClient(out NeoTestSaveStack stack)
        {
            stack = NeoTestSaveStack.Create(LoadFixture("synth-example.json"));
            return TestProjectNeo.Load(stack.Synchronizer)
                .GetAwaiter()
                .GetResult();
        }

        // ------------------------------------------------------------------
        // Save storage: authored + overlay-created entries, round-trip.
        // ------------------------------------------------------------------

        [Test]
        public void SaveDictionary_ReadsAuthoredEntryThroughEnumKey()
        {
            var app = LoadGeneratedClient(out _);

            Assert.IsTrue(app.Save.ElementStats.ContainsKey(Element.fire));
            Assert.AreEqual(12, app.Save.ElementStats[Element.fire]);
            Assert.IsTrue(app.Save.ElementStats.TryGetValue(Element.fire, out int? fireStat));
            Assert.AreEqual(12, fireStat);
            // `ice` has no authored entry.
            Assert.IsFalse(app.Save.ElementStats.ContainsKey(Element.ice));
        }

        [Test]
        public void SaveDictionary_OverlayCreatedEntry_RoundTripsUnderOptionIdKeys()
        {
            var app = LoadGeneratedClient(out var stack);

            app.Save.ElementStats[Element.ice] = 5;
            Assert.AreEqual(5, app.Save.ElementStats[Element.ice]);
            app.CommitAsync().GetAwaiter().GetResult();

            // The persisted save keys the entry by the raw option id.
            string persisted = stack.PersistedContent()!;
            StringAssert.Contains("\"ice\"", persisted);

            var reloaded = TestProjectNeo.Load(stack.Reopen())
                .GetAwaiter()
                .GetResult();
            Assert.AreEqual(5, reloaded.Save.ElementStats[Element.ice]);
            // Authored entry and the dangling `storm` key both survive.
            Assert.AreEqual(12, reloaded.Save.ElementStats[Element.fire]);
            Assert.AreEqual(99, reloaded.Save.ElementStats[Element.FromOptionId("storm")]);
        }

        // ------------------------------------------------------------------
        // Static storage.
        // ------------------------------------------------------------------

        [Test]
        public void StaticDictionary_ReadsAuthoredEntryThroughEnumKey()
        {
            var app = LoadGeneratedClient(out _);

            // Static-storage dictionaries emit read-only two-arity wrappers.
            Assert.AreEqual(3, app.Save.ElementMultipliers[Element.ice]);
            Assert.IsFalse(app.Save.ElementMultipliers.ContainsKey(Element.fire));
        }

        // ------------------------------------------------------------------
        // Nested-in-Custom + Custom-typed entries.
        // ------------------------------------------------------------------

        [Test]
        public void CustomTypedEntries_ReadAuthoredChampionAndNestedAffinity()
        {
            var app = LoadGeneratedClient(out _);

            var champion = app.Save.ElementChampions[Element.fire];
            Assert.AreEqual("Ignis", champion.Name);
            // The dictionary nested inside the Hero custom type.
            Assert.AreEqual("scorch", champion.ElementAffinity[Element.fire]);
            Assert.IsFalse(champion.ElementAffinity.ContainsKey(Element.ice));
        }

        [Test]
        public void CustomTypedEntries_OverlayCreatedChampion_ReadsBackThroughEnumKey()
        {
            var app = LoadGeneratedClient(out _);

            app.Save.ElementChampions[Element.ice] = new Hero(Name: "Frost");

            Assert.AreEqual("Frost", app.Save.ElementChampions[Element.ice].Name);
            Assert.AreEqual(2, app.Save.ElementChampions.Count);
            StringAssert.Contains("Frost", app.SerializeSaveData());
        }

        // ------------------------------------------------------------------
        // Stale / dangling keys.
        // ------------------------------------------------------------------

        [Test]
        public void StaleKey_EnumerationDegradesToAdHocWrapperInstances()
        {
            var app = LoadGeneratedClient(out _);

            var keys = app.Save.ElementStats.Keys.ToList();
            Assert.AreEqual(2, keys.Count);
            // Live option materializes as the interned generated instance…
            Assert.IsTrue(keys.Any(key => ReferenceEquals(key, Element.fire)));
            // …and the dangling `storm` key degrades to a first-class ad-hoc
            // instance minted by FromOptionId, readable like any live option.
            var stale = keys.Single(key => key.optionId == "storm");
            Assert.AreSame(Element.FromOptionId("storm"), stale);
            Assert.AreEqual(99, app.Save.ElementStats[stale]);
        }

        // ------------------------------------------------------------------
        // JSON read-compat: keyKind absent ⇒ string-keyed.
        // ------------------------------------------------------------------

        /// <summary>
        /// Stale local exports predate the web-side backfill migration and
        /// omit <c>keyKind</c>/<c>keyEnumId</c>. A fixture variant with the
        /// fields stripped must still load, expose a null <c>keyKind</c>,
        /// and read its rows through the string-keyed single-arity wrapper.
        /// </summary>
        [Test]
        public void KeyKindAbsentInJson_ReadsAsStringKeyedDictionary()
        {
            var json = JObject.Parse(LoadFixture("synth-example.json"));
            var statsAttr = (JObject)json["attributes"]!["attr-elem-stats"]!;
            Assert.IsTrue(statsAttr.Remove("keyKind"), "fixture should carry keyKind");
            Assert.IsTrue(statsAttr.Remove("keyEnumId"), "fixture should carry keyEnumId");

            var client = NeoTestSaveStack.LoadClient(json.ToString());
            if (!client.TryGetAttribute("attr-elem-stats", out DictionaryAttribute? attr))
            {
                Assert.Fail("attr-elem-stats missing from the stripped fixture");
            }
            Assert.IsNull(attr!.keyKind);
            Assert.IsNull(attr.keyEnumId);

            var node = client.save.Get<NeoAttributeDictionaryWritable>("ElementStats");
            // Same entry factory shape the codegen output constructs with.
            var stringView = new NeoReadOnlyDictionary<int?>(
                client,
                node,
                (_, child) => NeoGeneratedTypesSupport.ReadInt((NeoAttributeInt)child));
            Assert.AreEqual(12, stringView["fire"]);
            Assert.AreEqual(99, stringView["storm"]);
        }

        // ------------------------------------------------------------------
        // Real-world fixture: the §13.1 enum-keyed NSGetters. The full dump
        // is gated by NSGetterParityTests; these pin the two new pointer
        // shapes by name so a regression fails with a readable message.
        // ------------------------------------------------------------------

        private const string ProjectExampleAssetsRootValueId =
            "e5b02003-d505-46a7-95fb-c11f1a412b61";

        [Test]
        public void ProjectExample_EnumLiteralKeyGetter_FoldsToStringPointer()
        {
            var client = NeoTestSaveStack.LoadClient(LoadFixture("project-example.json"));
            if (!client.TryGetAttribute(
                    "attr-elem-fire-damage-get", out NSGetterAttribute? attr))
            {
                Assert.Fail("attr-elem-fire-damage-get missing from project-example.json");
            }

            var node = new NeoAttributeNSGetter(client, attr!, null);
            var result = node.Compute(ProjectExampleAssetsRootValueId);

            Assert.IsTrue(result.ok, result.error);
            // Static ElementDamage[fire] (30) + Save ElementStats[fire] (12).
            Assert.AreEqual(42d, System.Convert.ToDouble(result.value));
        }

        [Test]
        public void ProjectExample_RowBackedEnumKeyGetter_ReadsThroughIndexZeroKeyOf()
        {
            var client = NeoTestSaveStack.LoadClient(LoadFixture("project-example.json"));
            if (!client.TryGetAttribute(
                    "attr-elem-selected-damage-get", out NSGetterAttribute? attr))
            {
                Assert.Fail("attr-elem-selected-damage-get missing from project-example.json");
            }

            var node = new NeoAttributeNSGetter(client, attr!, null);
            var result = node.Compute(ProjectExampleAssetsRootValueId);

            Assert.IsTrue(result.ok, result.error);
            // SelectedElement is authored to `water`; ElementDamage[water] = 20.
            Assert.AreEqual(20d, System.Convert.ToDouble(result.value));
        }
    }
}
