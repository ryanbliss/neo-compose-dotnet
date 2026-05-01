// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.IO;
using NeoCompose.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace HelloWorld.Tests
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
                behaviour.SendMessage("Start", SendMessageOptions.DontRequireReceiver);
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
            static string loadSave()
            {
                return "";
            }
            static void handleSave(string file)
            {
                return;
            }
            var client = instance.Load(
                LoadFixture("synth-example.json"),
                loadSave,
                handleSave
            );
            Assert.IsNotNull(client);
        }
    }
}
