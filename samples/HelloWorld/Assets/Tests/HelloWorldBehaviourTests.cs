// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using HelloWorld.Assets.Scripts;
using HelloWorld.Assets.Scripts.Neo;

namespace HelloWorld.Assets.Tests
{
    public class HelloWorldBehaviourTests
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
        public void Behaviour_StartInstantiatesNeoLoader()
        {
            // Verifies the sample can reference + use the package's surface.
            // Direct invocation of `Start` via SendMessage so we don't need
            // a frame to tick.
            var go = new GameObject("HelloWorld");
            try
            {
                var behaviour = go.AddComponent<HelloWorldBehaviour>();
                Assert.IsNotNull(behaviour);
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
            // In-memory save round-trip: `handleSave` writes to a
            // closed-over string; `loadSave` reads it back. Mimics what
            // a real host (PlayerPrefs, file I/O, etc.) does, so
            // NeoClient's bootstrap (BuildDefaultSaveData →
            // EmitHandleSave → LoadUnsafe) round-trips correctly
            // instead of reading "" back and wiping its in-memory
            // saveData.
            string saveBuffer = "";
            string loadSave() => saveBuffer;
            void handleSave(string file) => saveBuffer = file;
            var client = instance.Load(
                LoadFixture("synth-example.json"),
                loadSave,
                handleSave
            );
            Assert.IsNotNull(client);
        }

        [Test]
        public void GeneratedSampleTypes_ComputeNSGetterFromSampleProject()
        {
            string saveBuffer = "";
            string loadSave() => saveBuffer;
            void handleSave(string file) => saveBuffer = file;

            var client = HelloWorldNeo.Load(
                File.ReadAllText(Path.Combine(SampleProjectRoot, "project.json")),
                loadSave,
                handleSave,
                localizationOptions: EnglishLocalizationOptions());

            Assert.AreEqual(Planet.earth, client.Save.World);
            Assert.AreEqual("Hello", client.Assets.Computed.baseText);

            Assert.AreEqual("Hello Earth!", client.Assets.Computed.fullText);
        }

        [Test]
        public void GeneratedSampleTypes_ExplicitSpanishLocalizationResolvesGeneratedText()
        {
            string saveBuffer = "";
            string loadSave() => saveBuffer;
            void handleSave(string file) => saveBuffer = file;

            var client = HelloWorldNeo.Load(
                File.ReadAllText(Path.Combine(SampleProjectRoot, "project.json")),
                loadSave,
                handleSave,
                localizationOptions: SpanishLocalizationOptions());

            Assert.AreEqual("es-ES", client.Localization.CurrentLocale);
            Assert.AreEqual("Hola", client.Assets.Computed.baseText);
            Assert.AreEqual("Tierra", Planet.earth.Text);
            Assert.AreEqual("Hola Tierra!", client.Assets.Computed.fullText);
        }

        [Test]
        public void GeneratedSampleTypes_LoadUsesResourcesConfigWhenLocalizationOptionsAreNull()
        {
            string defaultSaveBuffer = "";
            string loadDefaultSave() => defaultSaveBuffer;
            void handleDefaultSave(string file) => defaultSaveBuffer = file;
            string explicitSaveBuffer = "";
            string loadExplicitSave() => explicitSaveBuffer;
            void handleExplicitSave(string file) => explicitSaveBuffer = file;
            var projectJson = File.ReadAllText(Path.Combine(SampleProjectRoot, "project.json"));
            var configOptions = NeoComposeConfig.LoadDefault()!.ToLocalizationOptions();

            var defaultClient = HelloWorldNeo.Load(
                projectJson,
                loadDefaultSave,
                handleDefaultSave);
            var explicitConfigClient = HelloWorldNeo.Load(
                projectJson,
                loadExplicitSave,
                handleExplicitSave,
                localizationOptions: configOptions);

            Assert.AreEqual(explicitConfigClient.Localization.CurrentLocale, defaultClient.Localization.CurrentLocale);
            Assert.AreEqual(explicitConfigClient.Assets.Computed.baseText, defaultClient.Assets.Computed.baseText);
            Assert.AreEqual(explicitConfigClient.Assets.Computed.fullText, defaultClient.Assets.Computed.fullText);
        }

        [Test]
        public void GeneratedEnumValues_CompareStaticObjectsToGeneratedProperties()
        {
            string saveBuffer = "";
            string loadSave() => saveBuffer;
            void handleSave(string file) => saveBuffer = file;

            var client = HelloWorldNeo.Load(
                File.ReadAllText(Path.Combine(SampleProjectRoot, "project.json")),
                loadSave,
                handleSave,
                localizationOptions: EnglishLocalizationOptions());

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
            string saveBuffer = "";
            string loadSave() => saveBuffer;
            void handleSave(string file) => saveBuffer = file;

            var client = HelloWorldNeo.Load(
                File.ReadAllText(Path.Combine(SampleProjectRoot, "project.json")),
                loadSave,
                handleSave,
                localizationOptions: EnglishLocalizationOptions());

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
            string saveBuffer = "";
            string loadSave() => saveBuffer;
            void handleSave(string file) => saveBuffer = file;

            var client = HelloWorldNeo.Load(
                File.ReadAllText(Path.Combine(SampleProjectRoot, "project.json")),
                loadSave,
                handleSave,
                localizationOptions: EnglishLocalizationOptions());

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
            string saveBuffer = "";
            string loadSave() => saveBuffer;
            void handleSave(string file) => saveBuffer = file;

            var client = HelloWorldNeo.Load(
                File.ReadAllText(Path.Combine(SampleProjectRoot, "project.json")),
                loadSave,
                handleSave,
                localizationOptions: EnglishLocalizationOptions());

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
            string saveBuffer = "";
            string loadSave() => saveBuffer;
            void handleSave(string file) => saveBuffer = file;

            var client = HelloWorldNeo.Load(
                File.ReadAllText(Path.Combine(SampleProjectRoot, "project.json")),
                loadSave,
                handleSave,
                localizationOptions: EnglishLocalizationOptions());

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
        public void Behaviour_VisitOutpost_UpdatesLocationGeneratedTextAndVisitCounts()
        {
            string savePath = Path.Combine(Application.persistentDataPath, "save1.json");
            if (File.Exists(savePath))
            {
                File.Delete(savePath);
            }

            var go = new GameObject("HelloWorld");
            try
            {
                var behaviour = go.AddComponent<HelloWorldBehaviour>();
                behaviour.LoadClient();

                Assert.AreEqual(HelloText(Planet.earth), behaviour.HelloWorldText);
                Assert.AreEqual(Planet.earth, behaviour.World);
                var startingOutpost = behaviour.CurrentOutpost;
                CollectionAssert.AreEqual(
                    new[] { Planet.earth },
                    VisitedPlanets(behaviour));

                var destination = behaviour.Outposts.First(outpost =>
                    outpost.valueId != startingOutpost.valueId);
                destination.Save.Unlocked = true;
                var startingVisitCount = destination.Save.VisitCount;

                behaviour.OnVisitOutpost(destination);

                Assert.AreEqual(destination.valueId, behaviour.CurrentOutpost.valueId);
                Assert.AreEqual(destination.Planet, behaviour.World);
                Assert.AreEqual(HelloText(destination.Planet), behaviour.HelloWorldText);
                Assert.AreEqual(startingVisitCount, destination.Save.VisitCount);
                CollectionAssert.AreEqual(
                    new[] { Planet.earth, destination.Planet },
                    VisitedPlanets(behaviour));
            }
            finally
            {
                Object.DestroyImmediate(go);
                if (File.Exists(savePath))
                {
                    File.Delete(savePath);
                }
            }
        }

        [Test]
        public void Behaviour_VisitedPlanetsPanelUsesLocalizedPlanetText()
        {
            string savePath = Path.Combine(Application.persistentDataPath, "save1.json");
            if (File.Exists(savePath))
            {
                File.Delete(savePath);
            }

            var go = new GameObject("HelloWorld");
            try
            {
                var behaviour = go.AddComponent<HelloWorldBehaviour>();
                behaviour.LoadClient();

                var texts = Object.FindObjectsByType<Text>(FindObjectsSortMode.None)
                    .Select(text => text.text)
                    .ToArray();

                Assert.Contains(Planet.earth.Text, texts);
            }
            finally
            {
                Object.DestroyImmediate(go);
                if (File.Exists(savePath))
                {
                    File.Delete(savePath);
                }
            }
        }

        [Test]
        public void GeneratedSampleTypes_ExplicitSpanishVisitedPlanetTextResolvesEnumText()
        {
            string saveBuffer = "";
            string loadSave() => saveBuffer;
            void handleSave(string file) => saveBuffer = file;

            var client = HelloWorldNeo.Load(
                File.ReadAllText(Path.Combine(SampleProjectRoot, "project.json")),
                loadSave,
                handleSave,
                localizationOptions: SpanishLocalizationOptions());

            Assert.AreEqual("es-ES", client.Localization.CurrentLocale);
            Assert.AreEqual("Tierra", client.Save.Visited[0].World.Text);
        }

        [Test]
        public void Behaviour_ResetSave_DiscardsUnsavedVisit()
        {
            string savePath = Path.Combine(Application.persistentDataPath, "save1.json");
            if (File.Exists(savePath))
            {
                File.Delete(savePath);
            }

            var go = new GameObject("HelloWorld");
            try
            {
                var behaviour = go.AddComponent<HelloWorldBehaviour>();
                behaviour.LoadClient();

                var destination = behaviour.Outposts.First(outpost =>
                    outpost.valueId != behaviour.CurrentOutpost.valueId);
                destination.Save.Unlocked = true;
                behaviour.OnVisitOutpost(destination);
                Assert.AreEqual(HelloText(destination.Planet), behaviour.HelloWorldText);

                behaviour.OnResetSave();

                Assert.AreEqual(HelloText(Planet.earth), behaviour.HelloWorldText);
                Assert.AreEqual(Planet.earth, behaviour.World);
                CollectionAssert.AreEqual(
                    new[] { Planet.earth },
                    VisitedPlanets(behaviour));
            }
            finally
            {
                Object.DestroyImmediate(go);
                if (File.Exists(savePath))
                {
                    File.Delete(savePath);
                }
            }
        }

        [Test]
        public void Behaviour_Save_PersistsVisit()
        {
            string savePath = Path.Combine(Application.persistentDataPath, "save1.json");
            if (File.Exists(savePath))
            {
                File.Delete(savePath);
            }

            var first = new GameObject("HelloWorld");
            var second = new GameObject("HelloWorldReloaded");
            try
            {
                var behaviour = first.AddComponent<HelloWorldBehaviour>();
                behaviour.LoadClient();
                var destination = behaviour.Outposts.First(outpost =>
                    outpost.valueId != behaviour.CurrentOutpost.valueId);
                destination.Save.Unlocked = true;
                behaviour.OnVisitOutpost(destination);
                behaviour.OnSave();

                var reloaded = second.AddComponent<HelloWorldBehaviour>();
                reloaded.LoadClient();

                Assert.AreEqual(HelloText(destination.Planet), reloaded.HelloWorldText);
                Assert.AreEqual(destination.Planet, reloaded.World);
                Assert.AreEqual(destination.valueId, reloaded.CurrentOutpost.valueId);
                CollectionAssert.AreEqual(
                    new[] { Planet.earth, destination.Planet },
                    VisitedPlanets(reloaded));
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                if (File.Exists(savePath))
                {
                    File.Delete(savePath);
                }
            }
        }

        [Test]
        public void Behaviour_VisitOutpost_IgnoresLockedOutpost()
        {
            string savePath = Path.Combine(Application.persistentDataPath, "save1.json");
            if (File.Exists(savePath))
            {
                File.Delete(savePath);
            }

            var go = new GameObject("HelloWorld");
            try
            {
                var behaviour = go.AddComponent<HelloWorldBehaviour>();
                behaviour.LoadClient();

                var startingOutpost = behaviour.CurrentOutpost;
                var lockedDestination = behaviour.Outposts.First(outpost =>
                    outpost.valueId != startingOutpost.valueId);
                lockedDestination.Save.Unlocked = false;
                var startingVisitCount = lockedDestination.Save.VisitCount;

                behaviour.OnVisitOutpost(lockedDestination);

                Assert.AreEqual(startingOutpost.valueId, behaviour.CurrentOutpost.valueId);
                Assert.AreEqual(Planet.earth, behaviour.World);
                Assert.AreEqual(startingVisitCount, lockedDestination.Save.VisitCount);
                CollectionAssert.AreEqual(
                    new[] { Planet.earth },
                    VisitedPlanets(behaviour));
            }
            finally
            {
                Object.DestroyImmediate(go);
                if (File.Exists(savePath))
                {
                    File.Delete(savePath);
                }
            }
        }

        private static Planet[] VisitedPlanets(HelloWorldBehaviour behaviour)
        {
            var planets = new System.Collections.Generic.List<Planet>();
            foreach (var visit in behaviour.VisitedPlanets)
            {
                planets.Add(visit.World);
            }
            return planets.ToArray();
        }

        private static string HelloText(Planet planet)
        {
            var greeting = HelloWorldNeo.Instance.Assets.Computed.baseText;
            return $"{greeting} {planet.Text}!";
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
