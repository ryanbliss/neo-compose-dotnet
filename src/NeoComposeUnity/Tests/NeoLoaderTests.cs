// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.IO;
using NUnit.Framework;
using NeoCompose.Runtime;

namespace NeoCompose.Tests
{
    public class NeoLoaderTests
    {
        private const string PackageRoot =
            "Packages/com.ryanbliss.neocompose/Tests";

        private static string LoadFixture(string fileName)
        {
            return File.ReadAllText(Path.Combine(PackageRoot, fileName));
        }

        [Test]
        public void NeoLoader_CanBeInstantiated()
        {
            // Placeholder smoke test — verifies the asmdef + test wiring
            // builds and the class is reachable. Replace as the real
            // surface lands.
            var instance = new NeoLoader();
            Assert.IsNotNull(instance);
            // In-memory save round-trip: `handleSave` writes to a
            // closed-over string; `loadSave` reads it back. Mimics what
            // a real host (PlayerPrefs, file I/O, etc.) does so
            // NeoClient's bootstrap (BuildDefaultSaveData →
            // EmitHandleSave → LoadUnsafe) round-trips correctly.
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
    }
}
