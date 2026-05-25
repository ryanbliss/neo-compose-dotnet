// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.IO;
using NUnit.Framework;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;

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

        [Test]
        public void ProjectData_DeserializesLocalizationMetadata()
        {
            var data = JsonConvert.DeserializeObject<ProjectData>(
                @"{
  ""project"": { ""id"": ""project-1"" },
  ""attributes"": {},
  ""values"": {},
  ""types"": {},
  ""enums"": {},
  ""localization"": {
    ""schemaVersion"": 1,
    ""rootLocale"": ""en-US"",
    ""supportedLocales"": [
      { ""locale"": ""en-US"", ""sourceLocale"": null },
      { ""locale"": ""es-ES"", ""sourceLocale"": ""en-US"" }
    ],
    ""textIds"": [""text-title""],
    ""rootLocaleFileName"": ""en-US.json"",
    ""localeFileNames"": {
      ""en-US"": ""en-US.json"",
      ""es-ES"": ""es-ES.json""
    },
    ""formatting"": {
      ""syntax"": ""smart-format"",
      ""sourceSyntax"": ""icu""
    }
  }
}");

            Assert.IsNotNull(data);
            Assert.IsNotNull(data!.localization);
            Assert.AreEqual("en-US", data.localization!.rootLocale);
            Assert.AreEqual(2, data.localization.supportedLocales.Length);
            Assert.AreEqual("es-ES", data.localization.supportedLocales[1].locale);
            Assert.AreEqual("en-US", data.localization.supportedLocales[1].sourceLocale);
            Assert.AreEqual("text-title", data.localization.textIds[0]);
            Assert.AreEqual("es-ES.json", data.localization.localeFileNames["es-ES"]);
            Assert.AreEqual("smart-format", data.localization.formatting.syntax);
            Assert.AreEqual("icu", data.localization.formatting.sourceSyntax);
        }

        [Test]
        public void ProjectLocalizationLocaleFile_DeserializesNullValues()
        {
            var file = JsonConvert.DeserializeObject<ProjectLocalizationLocaleFile>(
                @"{
  ""schemaVersion"": 1,
  ""projectId"": ""project-1"",
  ""versionId"": ""version-1"",
  ""locale"": ""es-ES"",
  ""sourceLocale"": ""en-US"",
  ""formattingSyntax"": ""smart-format"",
  ""values"": {
    ""text-title"": ""Hola"",
    ""text-missing"": null
  }
}");

            Assert.IsNotNull(file);
            Assert.AreEqual("es-ES", file!.locale);
            Assert.AreEqual("en-US", file.sourceLocale);
            Assert.AreEqual("Hola", file.values["text-title"]);
            Assert.IsTrue(file.values.ContainsKey("text-missing"));
            Assert.IsNull(file.values["text-missing"]);
        }
    }
}
