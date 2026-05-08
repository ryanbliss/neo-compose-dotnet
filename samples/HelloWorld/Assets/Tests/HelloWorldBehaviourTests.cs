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
                handleSave);

            Assert.AreEqual(Planet.earth, client.Save.World);
            Assert.AreEqual("Hello", client.Assets.Computed.baseText);

            Assert.AreEqual("Hello Earth!", client.Assets.Computed.fullText);
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
                handleSave);

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
                handleSave);

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
                node.Next();
            };
            dialogue.OnFinish += () => finished = true;

            dialogue.Start();

            Assert.IsTrue(finished);
            Assert.AreEqual(3, shown.Count);
            CollectionAssert.IsEmpty(client.FindUnlinkedSaveValueIds());
        }

        [Test]
        public void Behaviour_VisitPlanet_UpdatesGeneratedTextAndVisitedList()
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

                Assert.AreEqual("Hello Earth!", behaviour.HelloWorldText);
                Assert.AreEqual(Planet.earth, behaviour.World);
                CollectionAssert.AreEqual(
                    new[] { Planet.earth },
                    VisitedPlanets(behaviour));

                behaviour.OnVisit(Planet.mars);

                Assert.AreEqual("Hello Mars!", behaviour.HelloWorldText);
                Assert.AreEqual(Planet.mars, behaviour.World);
                CollectionAssert.AreEqual(
                    new[] { Planet.earth, Planet.mars },
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

                behaviour.OnVisit(Planet.mars);
                Assert.AreEqual("Hello Mars!", behaviour.HelloWorldText);

                behaviour.OnResetSave();

                Assert.AreEqual("Hello Earth!", behaviour.HelloWorldText);
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
                behaviour.OnVisit(Planet.mars);
                behaviour.OnSave();

                var reloaded = second.AddComponent<HelloWorldBehaviour>();
                reloaded.LoadClient();

                Assert.AreEqual("Hello Mars!", reloaded.HelloWorldText);
                Assert.AreEqual(Planet.mars, reloaded.World);
                CollectionAssert.AreEqual(
                    new[] { Planet.earth, Planet.mars },
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

        private static Planet[] VisitedPlanets(HelloWorldBehaviour behaviour)
        {
            var planets = new System.Collections.Generic.List<Planet>();
            foreach (var visit in behaviour.VisitedPlanets)
            {
                planets.Add(visit.World);
            }
            return planets.ToArray();
        }
    }
}
