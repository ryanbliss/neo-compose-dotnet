// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.IO;
using NeoCompose.Runtime;
using NUnit.Framework;
using UnityEngine;
using HelloWorld.Assets.Scripts;

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
        private const string SampleScriptsRoot = "Assets/Scripts";

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
                File.ReadAllText(Path.Combine(SampleScriptsRoot, "project.json")),
                loadSave,
                handleSave);

            Assert.AreEqual(Planet.earth, client.Save.world?.optionId);
            Assert.AreEqual("Hello", client.Assets.computed.baseText);

            Assert.AreEqual("Hello Earth!", client.Assets.computed.fullText);
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
                Assert.AreEqual(Planet.earth, behaviour.World?.optionId);
                CollectionAssert.AreEqual(
                    new[] { Planet.earth },
                    VisitedPlanetIds(behaviour));

                behaviour.Visit(Planet.mars);

                Assert.AreEqual("Hello Mars!", behaviour.HelloWorldText);
                Assert.AreEqual(Planet.mars, behaviour.World?.optionId);
                CollectionAssert.AreEqual(
                    new[] { Planet.earth, Planet.mars },
                    VisitedPlanetIds(behaviour));
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

                behaviour.Visit(Planet.mars);
                Assert.AreEqual("Hello Mars!", behaviour.HelloWorldText);

                behaviour.ResetSave();

                Assert.AreEqual("Hello Earth!", behaviour.HelloWorldText);
                Assert.AreEqual(Planet.earth, behaviour.World?.optionId);
                CollectionAssert.AreEqual(
                    new[] { Planet.earth },
                    VisitedPlanetIds(behaviour));
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
                behaviour.Visit(Planet.mars);
                behaviour.Save();

                var reloaded = second.AddComponent<HelloWorldBehaviour>();
                reloaded.LoadClient();

                Assert.AreEqual("Hello Mars!", reloaded.HelloWorldText);
                Assert.AreEqual(Planet.mars, reloaded.World?.optionId);
                CollectionAssert.AreEqual(
                    new[] { Planet.earth, Planet.mars },
                    VisitedPlanetIds(reloaded));
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

        private static string[] VisitedPlanetIds(HelloWorldBehaviour behaviour)
        {
            var ids = new System.Collections.Generic.List<string>();
            foreach (var visit in behaviour.VisitedPlanets)
            {
                ids.Add(visit.world.optionId);
            }
            return ids.ToArray();
        }
    }
}
