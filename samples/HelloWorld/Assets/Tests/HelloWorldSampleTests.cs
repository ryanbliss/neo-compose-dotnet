// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NUnit.Framework;
using UnityEngine;
using HelloWorld.Assets.Scripts;
using HelloWorld.Assets.Scripts.Neo;

namespace HelloWorld.Assets.Tests
{
    /// <summary>
    /// Sample-level tests for the generated <see cref="HelloWorldNeo"/> client and the
    /// menu's reachability. Gameplay behavior lives in
    /// <see cref="HelloWorldGameplayTests"/>.
    /// </summary>
    public class HelloWorldSampleTests
    {
        // Fixtures for the sample tests live alongside the test sources
        // under `Assets/Tests/`. Unity treats the project's working
        // directory as the project root, so a leading-slashless
        // `Assets/...` path resolves through the standard asset
        // pipeline. (The package's own tests load from
        // `Packages/com.ryanbliss.neocompose/Tests/` because that's
        // where the fixtures live inside the package — different file
        // tree, different prefix.)
        private const string FixturesRoot = "Assets/Tests";
        private const string SampleProjectRoot = "Assets/Resources/Neo";

        private static string LoadFixture(string fileName)
        {
            return File.ReadAllText(Path.Combine(FixturesRoot, fileName));
        }

        [Test]
        public void Menu_CanBeInstantiated()
        {
            // Verifies the sample can reference + use the package's surface: adding the
            // menu brings up the store-backed save list.
            var go = new GameObject("HelloWorld Menu");
            try
            {
                var menu = go.AddComponent<HelloWorldMenu>();
                Assert.IsNotNull(menu);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void NeoLoader_IsReachableFromSample()
        {
            // Smoke check that the sample's asmdef references resolve.
            // Placeholder smoke test — verifies the asmdef + test wiring
            // builds and the class is reachable. Replace as the real
            // surface lands.
            var instance = new NeoLoader();
            Assert.IsNotNull(instance);
            // Builds a client through the save stack (project store → synchronizer)
            // over the fixture schema, exercising the loader end to end.
            var client = LoadRawClient(LoadFixture("synth-example.json"));
            Assert.IsNotNull(client);
        }

        [Test]
        public void GeneratedSampleTypes_ComputeNSGetterFromSampleProject()
        {
            var client = LoadSampleClient(EnglishLocalizationOptions());

            Assert.AreEqual(Planet.earth, client.Save.World);
            Assert.AreEqual("Hello", client.Assets.Computed.baseText);

            Assert.AreEqual("Hello Earth!", client.Assets.Computed.fullText);
        }

        [Test]
        public void GeneratedSampleTypes_ExplicitSpanishLocalizationResolvesGeneratedText()
        {
            var client = LoadSampleClient(SpanishLocalizationOptions());

            Assert.AreEqual("es-ES", client.Localization.CurrentLocale);
            Assert.AreEqual("Hola", client.Assets.Computed.baseText);
            Assert.AreEqual("Tierra", Planet.earth.Text);
            Assert.AreEqual("Hola Tierra!", client.Assets.Computed.fullText);
        }

        [Test]
        public void GeneratedSampleTypes_LoadUsesResourcesConfigWhenLocalizationOptionsAreNull()
        {
            var projectJson = File.ReadAllText(Path.Combine(SampleProjectRoot, "project.json"));
            var configOptions = NeoComposeConfig.LoadDefault()!.ToLocalizationOptions();

            var defaultClient = LoadSampleClient(projectJson, localizationOptions: null);
            var explicitConfigClient = LoadSampleClient(projectJson, localizationOptions: configOptions);

            Assert.AreEqual(explicitConfigClient.Localization.CurrentLocale, defaultClient.Localization.CurrentLocale);
            Assert.AreEqual(explicitConfigClient.Assets.Computed.baseText, defaultClient.Assets.Computed.baseText);
            Assert.AreEqual(explicitConfigClient.Assets.Computed.fullText, defaultClient.Assets.Computed.fullText);
        }

        [Test]
        public void GeneratedEnumValues_CompareStaticObjectsToGeneratedProperties()
        {
            var client = LoadSampleClient(EnglishLocalizationOptions());

            var savedWorld = client.Save.World;
            Assert.AreSame(Planet.earth, savedWorld);
            Assert.IsTrue(savedWorld == Planet.earth);
            Assert.IsTrue(savedWorld.Equals(Planet.earth));

            var earthOutpost = client.Assets.Outposts.FirstOrDefault(outpost =>
                outpost.Planet == Planet.earth);
            Assert.IsNotNull(earthOutpost);
            if (earthOutpost == null) return;

            Assert.AreSame(Planet.earth, earthOutpost.Planet);
            Assert.IsTrue(earthOutpost.Planet == Planet.earth);
            Assert.IsTrue(earthOutpost.Planet.Equals(Planet.earth));

            var customPlanet = Planet.FromOptionId("modded-planet");
            Assert.AreSame(customPlanet, Planet.FromOptionId("modded-planet"));
            Assert.IsTrue(customPlanet == Planet.FromOptionId("modded-planet"));
            Assert.IsFalse(Planet.IsKnown("modded-planet"));
        }

        [Test]
        public void GeneratedDialogue_LinkedTextNodePrimaryResolvesTypedAsset()
        {
            var client = LoadSampleClient(EnglishLocalizationOptions());

            var capitol = client.Assets.Outposts.FirstOrDefault(outpost =>
                outpost.Name == "Capitol OG");
            Assert.IsNotNull(capitol);
            if (capitol == null) return;

            Assert.IsTrue(client.Dialogues.Outposts.Introductions.TryTrigger(
                capitol,
                out NeoDialogue dialogue));

            var shown = new System.Collections.Generic.List<NeoDialogueTextNode>();
            bool finished = false;
            dialogue.OnShow += node =>
            {
                shown.Add(node);
                Assert.IsInstanceOf<ReadOnlyOutpost>(node.Primary);
                var primary = (ReadOnlyOutpost)node.Primary!;
                Assert.AreEqual(capitol.valueId, primary.valueId);
                Assert.AreEqual("Capitol OG", primary.Name);
                if (node.Options.Count > 0)
                {
                    node.Options[0].Select();
                    return;
                }
                node.Next();
            };
            dialogue.OnFinish += () => finished = true;

            dialogue.Start();

            Assert.IsTrue(finished);
            Assert.GreaterOrEqual(shown.Count, 1);
            CollectionAssert.IsEmpty(client.FindUnlinkedSaveValueIds());
        }

        [Test]
        public void GeneratedNSGetters_InRepeatedCustomValuesResolveAgainstEachOutpost()
        {
            var client = LoadSampleClient(EnglishLocalizationOptions());

            _ = client.Save.Location.FullDisplayText;
            var displayTexts = client.Assets.Outposts
                .Select(outpost => outpost.FullDisplayText)
                .ToArray();

            Assert.Greater(displayTexts.Length, 3);
            Assert.Contains("Mercurial, Mercury", displayTexts);
            Assert.Contains("Venusian, Venus", displayTexts);
            Assert.Contains("Capitol OG, Earth", displayTexts);
            Assert.Greater(displayTexts.Distinct().Count(), 3);
        }

        [Test]
        public void GeneratedNSGetters_SaveUnsafeResolvesPerOutpost()
        {
            var client = LoadSampleClient(EnglishLocalizationOptions());

            var outposts = client.Assets.Outposts.ToArray();
            Assert.Greater(outposts.Length, 3);

            foreach (var outpost in outposts)
            {
                Assert.IsNotNull(outpost.SaveUnsafe, outpost.FullDisplayText);
                Assert.AreEqual(
                    outpost.valueId,
                    client.Save.OutpostSaveMap.First(pair => pair.Value.valueId == outpost.SaveUnsafe!.valueId).Key,
                    outpost.FullDisplayText);
            }

            Assert.AreEqual(outposts.Length, client.Save.OutpostSaveMap.Count);
            Assert.AreEqual(
                outposts.Length,
                outposts.Select(outpost => outpost.SaveUnsafe!.valueId).Distinct().Count());
        }

        [Test]
        public void GeneratedCustomValues_ReturnCachedInstances()
        {
            var client = LoadSampleClient(EnglishLocalizationOptions());

            var firstRead = client.Assets.Outposts.ToArray();
            var secondRead = client.Assets.Outposts.ToArray();
            Assert.Greater(firstRead.Length, 3);
            Assert.AreEqual(firstRead.Length, secondRead.Length);

            for (int i = 0; i < firstRead.Length; i++)
            {
                Assert.AreSame(firstRead[i], secondRead[i], firstRead[i].FullDisplayText);
            }

            var outpost = firstRead[0];
            Assert.AreSame(outpost.Save, outpost.Save);
            Assert.AreSame(outpost.SaveUnsafe, outpost.SaveUnsafe);
        }

        [Test]
        public void GeneratedSampleTypes_ExplicitSpanishVisitedPlanetTextResolvesEnumText()
        {
            var client = LoadSampleClient(SpanishLocalizationOptions());

            Assert.AreEqual("es-ES", client.Localization.CurrentLocale);
            Assert.AreEqual("Tierra", client.Save.Visited[0].World.Text);
        }
        // Builds the generated sample client over the Phase 9 save stack (project
        // store → save synchronizer) in place of the removed loadSave/handleSave
        // delegates. The async load completes synchronously over the in-hand JSON +
        // in-memory store, so blocking here is safe.
        private static HelloWorldNeo LoadSampleClient(NeoLocalizationOptions? localizationOptions = null)
        {
            return LoadSampleClient(
                File.ReadAllText(Path.Combine(SampleProjectRoot, "project.json")),
                localizationOptions);
        }

        private static HelloWorldNeo LoadSampleClient(
            string projectJson,
            NeoLocalizationOptions? localizationOptions)
        {
            var store = new NeoProjectStore(dataSource: new NeoJsonProjectDataSource(projectJson), localStore: new NeoInMemoryLocalSaveStore());
            store.LoadAsync().GetAwaiter().GetResult();
            return HelloWorldNeo.Load(store.Open("save"), localizationOptions: localizationOptions)
                .GetAwaiter()
                .GetResult();
        }

        // Builds a raw NeoClient (not the generated HelloWorld facade) over the save
        // stack — for smoke-testing the loader against an arbitrary project schema.
        private static NeoClient LoadRawClient(string projectJson)
        {
            var store = new NeoProjectStore(dataSource: new NeoJsonProjectDataSource(projectJson), localStore: new NeoInMemoryLocalSaveStore());
            store.LoadAsync().GetAwaiter().GetResult();
            return new NeoLoader().Load(store.Open("save")).GetAwaiter().GetResult();
        }

        private static NeoLocalizationOptions EnglishLocalizationOptions()
        {
            return new NeoLocalizationOptions
            {
                localeOverride = "en-US",
                preloadSystemLocale = false,
            };
        }

        private static NeoLocalizationOptions SpanishLocalizationOptions()
        {
            return new NeoLocalizationOptions
            {
                localeOverride = "es-ES",
                preloadSystemLocale = false,
            };
        }
    }
}
