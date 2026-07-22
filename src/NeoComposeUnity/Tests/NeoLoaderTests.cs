// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.IO;
using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

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
            // A fresh draft builds default save data; committing persists it to the
            // local store (the durable round-trip the removed handleSave delegate used
            // to perform), so the persisted JSON can be inspected here.
            var stack = NeoTestSaveStack.Create(LoadFixture("synth-example.json"));
            var client = stack.Load();
            Assert.IsNotNull(client);
            client.CommitAsync().GetAwaiter().GetResult();
            string saveBuffer = stack.PersistedContent()!;
            var save = JsonConvert.DeserializeObject<ProjectSaveData>(saveBuffer);
            Assert.IsNotNull(save);
            AssertGeneratedSaveName(save!.name);
            Assert.AreEqual("test-project", save!.projectId);
            Assert.AreEqual("version-1", save.version.id);
            Assert.AreEqual("0.1.0", save.version.label);
            Assert.Greater(save.createdAt.EpochMilliseconds, 0d);
            Assert.GreaterOrEqual(save.updatedAt.EpochMilliseconds, save.createdAt.EpochMilliseconds);

            var serialized = JObject.Parse(saveBuffer);
            Assert.AreNotEqual(JTokenType.String, serialized["createdAt"]!.Type);
            Assert.AreNotEqual(JTokenType.String, serialized["updatedAt"]!.Type);
        }

        [Test]
        public void NeoLoader_UsesCustomSaveNameBuilderForNewSaves()
        {
            var client = NeoTestSaveStack.LoadClient(
                LoadFixture("synth-example.json"),
                localizationOptions: null,
                localizationFileSource: null,
                options: new NeoSaveOptions { BuildSaveName = () => "careful-lantern-777" });

            Assert.IsNotNull(client);
            var save = JsonConvert.DeserializeObject<ProjectSaveData>(client.SerializeSaveData());
            Assert.AreEqual("careful-lantern-777", save!.name);
        }

        [Test]
        public void NeoLoader_WritesSaveDiagnosticsAndDedupesByCapturedValues()
        {
            var stack = NeoTestSaveStack.Create(LoadFixture("synth-example.json"));
            var client = stack.Load();
            client.CommitAsync().GetAwaiter().GetResult();

            var serialized = JObject.Parse(stack.PersistedContent()!);
            Assert.AreEqual(1, serialized["platforms"]!.Count());
            Assert.AreEqual(1, serialized["systems"]!.Count());
            Assert.AreNotEqual(JTokenType.String, serialized["platforms"]![0]!["lastSavedAt"]!.Type);
            Assert.AreNotEqual(JTokenType.String, serialized["systems"]![0]!["lastSavedAt"]!.Type);
        }

        [Test]
        public void NeoLoader_ClearsSaveDiagnosticsWhenDisabledAtRuntime()
        {
            var stack = NeoTestSaveStack.Create(LoadFixture("synth-example.json"));
            var client = stack.Load();

            client.SaveOptions.DiagnosticsEnabled = false;
            client.CommitAsync().GetAwaiter().GetResult();

            var serialized = JObject.Parse(stack.PersistedContent()!);
            Assert.AreEqual(JTokenType.Null, serialized["platforms"]!.Type);
            Assert.AreEqual(JTokenType.Null, serialized["systems"]!.Type);
            Assert.AreEqual(JTokenType.Null, serialized["inputDevices"]!.Type);
        }

        [Test]
        public void NeoLoader_PreservesLoadedSaveName()
        {
            var saveBuffer = @"{
  ""name"": ""ancient-button-321"",
  ""projectId"": ""project-1"",
  ""version"": { ""id"": ""version-1"", ""label"": ""0.1.0"" },
  ""createdAt"": 100,
  ""updatedAt"": 123,
  ""values"": {},
  ""staticBindings"": {}
}";
            var client = LoadLocalizedStringClient(localizable: true, saveBuffer);

            var serialized = JObject.Parse(client.SerializeSaveData());
            Assert.AreEqual("ancient-button-321", serialized["name"]!.Value<string>());
        }

        [Test]
        public void SaveData_SerializeDoesNotChangeUpdatedAt()
        {
            var saveBuffer = @"{
  ""name"": ""quiet-fox-456"",
  ""projectId"": ""project-1"",
  ""version"": { ""id"": ""version-1"", ""label"": ""0.1.0"" },
  ""createdAt"": 100,
  ""updatedAt"": 123,
  ""values"": {},
  ""staticBindings"": {}
}";
            var client = LoadLocalizedStringClient(localizable: true, saveBuffer);

            var serializedBeforeSave = JObject.Parse(client.SerializeSaveData());
            Assert.AreEqual("quiet-fox-456", serializedBeforeSave["name"]!.Value<string>());
            Assert.AreEqual(100d, serializedBeforeSave["createdAt"]!.Value<double>());
            var updatedAtBeforeSave = serializedBeforeSave["updatedAt"]!.Value<double>();

            var serializedAgain = JObject.Parse(client.SerializeSaveData());
            Assert.AreEqual(updatedAtBeforeSave, serializedAgain["updatedAt"]!.Value<double>());

        }

        [Test]
        public void SaveData_UpdatedAtChangesWhenSaveOverrideValueChanges()
        {
            var client = LoadLocalizedStringClient(
                localizable: true,
                @"{
  ""name"": ""quiet-fox-456"",
  ""projectId"": ""project-1"",
  ""version"": { ""id"": ""version-1"", ""label"": ""0.1.0"" },
  ""createdAt"": 100,
  ""updatedAt"": 123,
  ""values"": {},
  ""staticBindings"": {}
}");
            var member = RequireMember<StringMember>(client, "member-title");
            var node = new NeoMemberStringWritable(client, member, null, NeoValueOwnership.Save);

            node.SetLiteralOverride("Manual Title");

            var serialized = JObject.Parse(client.SerializeSaveData());
            Assert.AreEqual(100d, serialized["createdAt"]!.Value<double>());
            Assert.Greater(serialized["updatedAt"]!.Value<double>(), 123d);
        }

        [Test]
        public void SaveData_UpdatedAtDoesNotChangeWhenSessionOverrideValueChanges()
        {
            var client = LoadLocalizedStringClient(
                localizable: true,
                @"{
  ""name"": ""quiet-fox-456"",
  ""projectId"": ""project-1"",
  ""version"": { ""id"": ""version-1"", ""label"": ""0.1.0"" },
  ""createdAt"": 100,
  ""updatedAt"": 123,
  ""values"": {},
  ""staticBindings"": {}
}");
            var member = RequireMember<StringMember>(client, "member-title");
            var node = new NeoMemberStringWritable(client, member, null, NeoValueOwnership.Session);

            node.SetLiteralOverride("Session Title");

            var serialized = JObject.Parse(client.SerializeSaveData());
            Assert.AreEqual(100d, serialized["createdAt"]!.Value<double>());
            Assert.AreEqual(123d, serialized["updatedAt"]!.Value<double>());
        }

        private static void AssertGeneratedSaveName(string name)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(name));
            var parts = name.Split('-');
            Assert.AreEqual(3, parts.Length);
            Assert.IsFalse(string.IsNullOrWhiteSpace(parts[0]));
            Assert.IsFalse(string.IsNullOrWhiteSpace(parts[1]));
            Assert.IsTrue(int.TryParse(parts[2], out var suffix));
            Assert.GreaterOrEqual(suffix, 100);
            Assert.LessOrEqual(suffix, 999);
        }

        [Test]
        public void ProjectData_DeserializesLocalizationMetadata()
        {
            var data = JsonConvert.DeserializeObject<ProjectData>(
                @"{
  ""project"": { ""id"": ""project-1"" },
  ""members"": {},
  ""values"": {},
  ""classes"": {},
  ""enums"": {},
  ""localization"": {
    ""schemaVersion"": 1,
    ""mainLocale"": ""en-US"",
    ""supportedLocales"": [
      { ""locale"": ""en-US"", ""sourceLocale"": null },
      { ""locale"": ""es-ES"", ""sourceLocale"": ""en-US"" }
    ],
    ""textIds"": [""text-title""],
    ""mainLocaleFileName"": ""en-US.json"",
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
            Assert.AreEqual("en-US", data.localization!.mainLocale);
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

        [Test]
        public void NeoLoader_LoadsRootLocalizationLocale()
        {
            var projectJson = AddLocalizationMetadata(
                LoadFixture("synth-example.json"),
                "en-US",
                "es-MX");
            var source = new FakeLocalizationLocaleFileSource();
            source.files["en-US"] = new ProjectLocalizationLocaleFile
            {
                schemaVersion = 1,
                projectId = "test-project",
                versionId = "version-1",
                locale = "en-US",
                formattingSyntax = "smart-format",
                values = new Dictionary<string, string?>
                {
                    ["text-title"] = "Hello",
                },
            };

            var client = NeoTestSaveStack.LoadClient(projectJson, new NeoLocalizationOptions(), source);

            Assert.AreEqual("en-US", client.Localization.MainLocale);
            Assert.AreEqual("en-US", client.Localization.CurrentLocale);
            CollectionAssert.AreEqual(new[] { "en-US", "es-MX" }, client.Localization.SupportedLocales);
            CollectionAssert.AreEqual(new[] { "en-US" }, client.Localization.LoadedLocales.ToArray());
        }

        [Test]
        public void NeoLoader_SelectsExactLocaleOverride()
        {
            var client = LoadClientWithLocalization(new NeoLocalizationOptions
            {
                localeOverride = "es-MX",
                preloadSystemLocale = false,
            });

            Assert.AreEqual("es-MX", client.Localization.CurrentLocale);
        }

        [Test]
        public void NeoLoader_SelectsLanguageLocaleOverride()
        {
            var client = LoadClientWithLocalization(new NeoLocalizationOptions
            {
                localeOverride = "es-ES",
                preloadSystemLocale = false,
            });

            Assert.AreEqual("es-MX", client.Localization.CurrentLocale);
        }

        [Test]
        public void NeoLocalization_SetLocaleFallsBackToRoot()
        {
            var client = LoadClientWithLocalization(new NeoLocalizationOptions
            {
                localeOverride = "fr-FR",
                preloadSystemLocale = false,
            });

            Assert.AreEqual("en-US", client.Localization.CurrentLocale);

            client.Localization.SetLocale("es-AR");

            Assert.AreEqual("es-MX", client.Localization.CurrentLocale);

            client.Localization.SetLocale("ja-JP");

            Assert.AreEqual("en-US", client.Localization.CurrentLocale);
        }

        [Test]
        public void NeoLocalization_ResolvesThroughFallbackChainAndCachesLoadedLocales()
        {
            var projectJson = AddLocalizationMetadata(
                LoadFixture("synth-example.json"),
                "en-US",
                "es-MX");
            var source = new FakeLocalizationLocaleFileSource();
            source.files["en-US"] = LocaleFile("en-US", null, ("title", "Hello {name}"), ("root-only", "Root"));
            source.files["es-MX"] = LocaleFile("es-MX", "en-US", ("title", "Hola {name}"), ("missing", null));

            var client = NeoTestSaveStack.LoadClient(
                projectJson,
                new NeoLocalizationOptions
                {
                    localeOverride = "es-MX",
                    preloadSystemLocale = false,
                },
                source);

            var args = new Dictionary<string, object?> { ["name"] = "Ada" };

            Assert.AreEqual("Hola Ada", client.Localization.ResolveText("title", args));
            Assert.AreEqual("Root", client.Localization.ResolveText("root-only"));
            CollectionAssert.AreEquivalent(new[] { "en-US", "es-MX" }, client.Localization.LoadedLocales);
            Assert.AreEqual(1, source.loadCounts["es-MX"]);
            Assert.AreEqual(1, source.loadCounts["en-US"]);

            Assert.AreEqual("Hola Ada", client.Localization.ResolveText("title", args));
            Assert.AreEqual(1, source.loadCounts["es-MX"]);
        }

        [Test]
        public void NeoLocalization_StreamingModeDoesNotSynchronouslyLoadNonMainLocales()
        {
            var projectJson = AddLocalizationMetadata(
                LoadFixture("synth-example.json"),
                "en-US",
                "es-MX");
            var source = new FakeLocalizationLocaleFileSource();
            source.files["en-US"] = LocaleFile("en-US", null, ("title", "Hello"));
            source.files["es-MX"] = LocaleFile("es-MX", "en-US", ("title", "Hola"));

            var client = NeoTestSaveStack.LoadClient(
                projectJson,
                new NeoLocalizationOptions
                {
                    localeOverride = "es-MX",
                    preloadSystemLocale = false,
                    useStreamingAssetsForNonMainLocales = true,
                },
                source);

            Assert.AreEqual("Hello", client.Localization.ResolveText("title"));
            Assert.IsFalse(source.loadCounts.ContainsKey("es-MX"));
            CollectionAssert.AreEqual(new[] { "en-US" }, client.Localization.LoadedLocales.ToArray());
        }

        [Test]
        public async System.Threading.Tasks.Task NeoLocalization_LoadAsyncLoadsStreamingFallbackChain()
        {
            var projectJson = AddLocalizationMetadata(
                LoadFixture("synth-example.json"),
                "en-US",
                "es-MX");
            var source = new FakeLocalizationLocaleFileSource();
            source.files["en-US"] = LocaleFile("en-US", null, ("title", "Hello"));
            source.streamingFiles["es-MX"] = LocaleFile("es-MX", "en-US", ("title", "Hola"));

            var client = NeoTestSaveStack.LoadClient(
                projectJson,
                new NeoLocalizationOptions
                {
                    localeOverride = "es-MX",
                    preloadSystemLocale = false,
                    useStreamingAssetsForNonMainLocales = true,
                },
                source);

            Assert.AreEqual("Hello", client.Localization.ResolveText("title"));

            await client.Localization.LoadAsync();

            Assert.AreEqual("Hola", client.Localization.ResolveText("title"));
            CollectionAssert.AreEquivalent(new[] { "en-US", "es-MX" }, client.Localization.LoadedLocales);
            Assert.AreEqual(1, source.streamingLoadCounts["es-MX"]);
        }

        [Test]
        public async System.Threading.Tasks.Task NeoLocalization_LoadLocaleAsyncCachesStreamingLocale()
        {
            var client = LoadClientWithStreamingLocalization(out var source);

            Assert.IsTrue(await client.Localization.LoadLocaleAsync("es-MX"));
            Assert.IsTrue(await client.Localization.LoadLocaleAsync("es-MX"));

            Assert.AreEqual("Hola", client.Localization.ResolveText("title"));
            Assert.AreEqual(1, source.streamingLoadCounts["es-MX"]);
        }

        [Test]
        public void NeoLocalization_RecoversFromUnknownTextIdAndFormatterErrors()
        {
            var projectJson = AddLocalizationMetadata(
                LoadFixture("synth-example.json"),
                "en-US",
                "es-MX");
            var source = new FakeLocalizationLocaleFileSource();
            source.files["en-US"] = LocaleFile("en-US", null, ("bad-format", "{missing"));

            var client = NeoTestSaveStack.LoadClient(
                projectJson,
                new NeoLocalizationOptions { preloadSystemLocale = false },
                source);

            Assert.AreEqual("unknown", client.Localization.ResolveText("unknown"));
            Assert.AreEqual("{missing", client.Localization.ResolveText("bad-format"));
        }

        [Test]
        public void NeoLocalization_WarnsOnlyOncePerMissingTextId()
        {
            var localization = NeoLocalization.CreateEmpty(null);
            LogAssert.Expect(
                LogType.Warning,
                "Neo Compose localized text id 'missing-description' was not found.");

            Assert.AreEqual("missing-description", localization.ResolveText("missing-description"));
            Assert.AreEqual("missing-description", localization.ResolveText("missing-description"));
        }

        [Test]
        public void ResourcesLocalizationLocaleFileSource_RecoversFromInvalidJson()
        {
            const string assetPath = "Assets/Resources/Neo/Localization/bad-json.json";
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
            File.WriteAllText(assetPath, "{ invalid json");
            AssetDatabase.Refresh();

            try
            {
                var localization = new ProjectLocalizationExport
                {
                    mainLocale = "en-US",
                    localeFileNames = new Dictionary<string, string>
                    {
                        ["en-US"] = "bad-json.json",
                    },
                };

                var source = new NeoResourcesLocalizationLocaleFileSource();

                Assert.IsFalse(source.TryLoadResourcesLocale(localization, "en-US", out var file));
                Assert.IsNull(file);
            }
            finally
            {
                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void NeoMemberString_TextResolvesLocalizableTextIds()
        {
            var client = LoadLocalizedStringClient(localizable: true);
            var member = RequireMember<StringMember>(client, "member-title");
            var node = new NeoMemberString(client, member, null);

            Assert.AreEqual("text-title", node.TextId);
            Assert.AreEqual("Localized Title", node.Text);
        }

        [Test]
        public void NeoMemberString_TextKeepsNonLocalizableStringsLiteral()
        {
            var client = LoadLocalizedStringClient(localizable: false);
            var member = RequireMember<StringMember>(client, "member-title");
            var node = new NeoMemberString(client, member, null);

            Assert.IsNull(node.TextId);
            Assert.AreEqual("text-title", node.Text);
        }

        [Test]
        public void NeoMemberString_SetLiteralOverrideDoesNotOverwriteLocalizedValue()
        {
            var client = LoadLocalizedStringClient(localizable: true);
            var member = RequireMember<StringMember>(client, "member-title");
            var node = new NeoMemberStringWritable(client, member, null, NeoValueOwnership.Save);

            Assert.AreEqual("Localized Title", node.Text);

            node.SetLiteralOverride("Manual Title");

            Assert.AreEqual("Manual Title", node.Text);
            Assert.IsNull(node.TextId);
            Assert.AreEqual(NeoStringLocalizationMode.Literal, node.value!.neoLocalizationMode);

            node.ClearOverride();

            Assert.AreEqual("Localized Title", node.Text);
            Assert.AreEqual("text-title", node.TextId);
        }

        [Test]
        public void NeoMemberString_SetLiteralOverrideSupportsNullUnlessRequired()
        {
            var client = LoadLocalizedStringClient(localizable: true);
            var member = RequireMember<StringMember>(client, "member-title");
            var node = new NeoMemberStringWritable(client, member, null, NeoValueOwnership.Save);

            node.SetLiteralOverride(null);

            Assert.IsNull(node.Text);
            Assert.IsNull(node.TextId);
            Assert.AreEqual(NeoStringLocalizationMode.Literal, node.value!.neoLocalizationMode);

            member.required = true;

            Assert.Throws<System.ArgumentNullException>(() => node.SetLiteralOverride(null));
        }

        private static NeoClient LoadClientWithLocalization(NeoLocalizationOptions options)
        {
            var projectJson = AddLocalizationMetadata(
                LoadFixture("synth-example.json"),
                "en-US",
                "es-MX");
            var source = new FakeLocalizationLocaleFileSource();
            source.files["en-US"] = new ProjectLocalizationLocaleFile
            {
                schemaVersion = 1,
                projectId = "test-project",
                versionId = "version-1",
                locale = "en-US",
                formattingSyntax = "smart-format",
            };

            return NeoTestSaveStack.LoadClient(projectJson, options, source);
        }

        private static NeoClient LoadLocalizedStringClient(bool localizable, string initialSave = "")
        {
            var projectJson = $@"{{
  ""metadata"": {{
    ""schemaVersion"": 11,
    ""projectId"": ""project-1"",
    ""versionId"": ""version-1""
  }},
  ""project"": {{
    ""id"": ""project-1"",
    ""rootAssetsMemberId"": ""root-assets"",
    ""rootSaveFileMemberId"": ""root-save"",
    ""rootSessionMemberId"": ""root-session""
  }},
  ""internalRecordRelations"": {{}},
  ""members"": {{
    ""root-assets"": {{
      ""id"": ""root-assets"",
      ""projectId"": ""project-1"",
      ""name"": ""Assets"",
      ""kind"": 7,
      ""isStatic"": false, ""accessModifierKind"": ""public"",
      ""classId"": ""class-root"",
      ""valueId"": ""assets-value""
    }},
    ""root-save"": {{
      ""id"": ""root-save"",
      ""projectId"": ""project-1"",
      ""name"": ""Save"",
      ""kind"": 7,
      ""isStatic"": false, ""accessModifierKind"": ""public"",
      ""classId"": ""class-root""
    }},
    ""root-session"": {{
      ""id"": ""root-session"",
      ""projectId"": ""project-1"",
      ""name"": ""Session"",
      ""kind"": 7,
      ""isStatic"": false, ""accessModifierKind"": ""public"",
      ""classId"": ""class-root""
    }},
    ""member-title"": {{
      ""id"": ""member-title"",
      ""projectId"": ""project-1"",
      ""name"": ""Title"",
      ""kind"": 3,
      ""isStatic"": false, ""accessModifierKind"": ""public"",
      ""valueId"": ""title-value"",
      ""localizable"": {localizable.ToString().ToLowerInvariant()}
    }}
  }},
  ""classes"": {{
    ""class-root"": {{
      ""id"": ""class-root"",
      ""projectId"": ""project-1"",
      ""name"": ""Root"",
      ""members"": {{ ""Title"": ""member-title"" }}
    }}
  }},
  ""values"": {{
    ""assets-value"": {{
      ""id"": ""assets-value"",
      ""createdAt"": ""1970-01-01T00:00:00.000Z"",
      ""updatedAt"": ""1970-01-01T00:00:00.000Z"",
      ""value"": {{ ""Title"": ""title-value"" }},
      ""classId"": ""class-root""
    }},
    ""title-value"": {{
      ""id"": ""title-value"",
      ""createdAt"": ""1970-01-01T00:00:00.000Z"",
      ""updatedAt"": ""1970-01-01T00:00:00.000Z"",
      ""value"": ""text-title""
    }}
  }},
  ""enums"": {{}},
  ""dialogues"": {{}},
  ""dialogueGroups"": {{}},
  ""priorityGroups"": {{}},
  ""localization"": {{
    ""schemaVersion"": 1,
    ""mainLocale"": ""en-US"",
    ""supportedLocales"": [
      {{ ""locale"": ""en-US"", ""sourceLocale"": null }}
    ],
    ""textIds"": [""text-title""],
    ""mainLocaleFileName"": ""en-US.json"",
    ""localeFileNames"": {{ ""en-US"": ""en-US.json"" }},
    ""formatting"": {{ ""syntax"": ""smart-format"", ""sourceSyntax"": ""icu"" }}
  }}
}}";
            var source = new FakeLocalizationLocaleFileSource();
            source.files["en-US"] = LocaleFile("en-US", null, ("text-title", "Localized Title"));

            return NeoTestSaveStack.LoadClient(
                projectJson,
                new NeoLocalizationOptions { preloadSystemLocale = false },
                source,
                initialSaveContent: string.IsNullOrEmpty(initialSave) ? null : initialSave);
        }

        private static T RequireMember<T>(NeoClient client, string id)
            where T : Member
        {
            if (!client.TryGetMember(id, out T? member))
            {
                Assert.Fail($"Fixture is missing member '{id}' of type {typeof(T).Name}");
                throw new System.InvalidOperationException("unreachable");
            }
            return member;
        }

        private static NeoClient LoadClientWithStreamingLocalization(
            out FakeLocalizationLocaleFileSource source)
        {
            var projectJson = AddLocalizationMetadata(
                LoadFixture("synth-example.json"),
                "en-US",
                "es-MX");
            source = new FakeLocalizationLocaleFileSource();
            source.files["en-US"] = LocaleFile("en-US", null, ("title", "Hello"));
            source.streamingFiles["es-MX"] = LocaleFile("es-MX", "en-US", ("title", "Hola"));

            return NeoTestSaveStack.LoadClient(
                projectJson,
                new NeoLocalizationOptions
                {
                    localeOverride = "es-MX",
                    preloadSystemLocale = false,
                    useStreamingAssetsForNonMainLocales = true,
                },
                source);
        }

        private static string AddLocalizationMetadata(
            string projectJson,
            string mainLocale,
            string childLocale)
        {
            var json = JObject.Parse(projectJson);
            json["localization"] = JObject.Parse($@"{{
  ""schemaVersion"": 1,
  ""mainLocale"": ""{mainLocale}"",
  ""supportedLocales"": [
    {{ ""locale"": ""{mainLocale}"", ""sourceLocale"": null }},
    {{ ""locale"": ""{childLocale}"", ""sourceLocale"": ""{mainLocale}"" }}
  ],
  ""textIds"": [""text-title""],
  ""mainLocaleFileName"": ""{mainLocale}.json"",
  ""localeFileNames"": {{
    ""{mainLocale}"": ""{mainLocale}.json"",
    ""{childLocale}"": ""{childLocale}.json""
  }},
  ""formatting"": {{
    ""syntax"": ""smart-format"",
    ""sourceSyntax"": ""icu""
  }}
}}");
            return json.ToString(Formatting.None);
        }

        private sealed class FakeLocalizationLocaleFileSource : INeoLocalizationLocaleFileSource
        {
            public readonly Dictionary<string, ProjectLocalizationLocaleFile> files = new();
            public readonly Dictionary<string, ProjectLocalizationLocaleFile> streamingFiles = new();
            public readonly Dictionary<string, int> loadCounts = new();
            public readonly Dictionary<string, int> streamingLoadCounts = new();

            public bool TryLoadResourcesLocale(
                ProjectLocalizationExport localization,
                string locale,
                out ProjectLocalizationLocaleFile? file)
            {
                loadCounts[locale] = loadCounts.TryGetValue(locale, out var count) ? count + 1 : 1;
                return files.TryGetValue(locale, out file);
            }

            public System.Threading.Tasks.Task<ProjectLocalizationLocaleFile?> LoadStreamingAssetsLocaleAsync(
                ProjectLocalizationExport localization,
                string locale,
                string streamingAssetsRelativePath)
            {
                streamingLoadCounts[locale] = streamingLoadCounts.TryGetValue(locale, out var count)
                    ? count + 1
                    : 1;
                streamingFiles.TryGetValue(locale, out var file);
                return System.Threading.Tasks.Task.FromResult<ProjectLocalizationLocaleFile?>(file);
            }
        }

        private static ProjectLocalizationLocaleFile LocaleFile(
            string locale,
            string? sourceLocale,
            params (string id, string? value)[] values)
        {
            return new ProjectLocalizationLocaleFile
            {
                schemaVersion = 1,
                projectId = "test-project",
                versionId = "version-1",
                locale = locale,
                sourceLocale = sourceLocale,
                formattingSyntax = "smart-format",
                values = values.ToDictionary(entry => entry.id, entry => entry.value),
            };
        }
    }
}
