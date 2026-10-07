// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Unity.Editor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace NeoCompose.Tests
{
    public partial class NeoComposeEditorTests
    {
        private const string TempRoot = "Assets/NeoComposeEditorTestsTemp";
        // Tile syncs write here, never over the sample's own generated tiles.
        private const string GeneratedTempRoot = TempRoot + "/Generated";

        [TearDown]
        public void TearDown()
        {
            CleanupTempRoot();
        }

        [Test]
        public void PathUtility_AcceptsExpectedAssetDirectories()
        {
            Assert.IsTrue(NeoComposePathUtility.TryNormalizeAssetDirectory(
                "Assets/Scripts/Neo/",
                out var scripts,
                out var scriptsError));
            Assert.AreEqual("", scriptsError);
            Assert.AreEqual("Assets/Scripts/Neo", scripts);

            Assert.IsTrue(NeoComposePathUtility.TryNormalizeAssetDirectory(
                "Assets\\Resources\\Neo",
                out var resources,
                out var resourcesError));
            Assert.AreEqual("", resourcesError);
            Assert.AreEqual("Assets/Resources/Neo", resources);
        }

        [Test]
        public void PathUtility_RejectsPathsOutsideAssets()
        {
            Assert.IsFalse(NeoComposePathUtility.TryNormalizeAssetDirectory(
                "/tmp/Neo",
                out _,
                out var absoluteError));
            Assert.IsTrue(absoluteError.Contains("project-relative"));

            Assert.IsFalse(NeoComposePathUtility.TryNormalizeAssetDirectory(
                "Packages/com.example",
                out _,
                out var packageError));
            Assert.IsTrue(packageError.Contains("Assets/"));

            Assert.IsFalse(NeoComposePathUtility.TryNormalizeAssetDirectory(
                "Assets/../ProjectSettings",
                out _,
                out var parentError));
            Assert.IsTrue(parentError.Contains(".."));
        }

        [Test]
        public void PathUtility_ValidatesLocalizationDirectories()
        {
            Assert.IsTrue(NeoComposePathUtility.TryNormalizeResourcesDirectory(
                "Assets\\Resources\\Neo\\Localization\\",
                out var resources,
                out var resourcesError));
            Assert.AreEqual("", resourcesError);
            Assert.AreEqual("Assets/Resources/Neo/Localization", resources);

            Assert.IsFalse(NeoComposePathUtility.TryNormalizeResourcesDirectory(
                "Assets/StreamingAssets/Neo/Localization",
                out _,
                out var invalidResourcesError));
            Assert.IsTrue(invalidResourcesError.Contains("Assets/Resources/"));

            Assert.IsTrue(NeoComposePathUtility.TryNormalizeStreamingAssetsDirectory(
                "Assets\\StreamingAssets\\Neo\\Localization\\",
                out var streaming,
                out var streamingError));
            Assert.AreEqual("", streamingError);
            Assert.AreEqual("Assets/StreamingAssets/Neo/Localization", streaming);

            Assert.IsFalse(NeoComposePathUtility.TryNormalizeStreamingAssetsDirectory(
                "Assets/Resources/Neo/Localization",
                out _,
                out var invalidStreamingError));
            Assert.IsTrue(invalidStreamingError.Contains("Assets/StreamingAssets/"));
        }

        [Test]
        public void ConfigProvider_CreatesDefaultConfigInResourcesFolder()
        {
            EnsureTempRoot();
            var path = $"{TempRoot}/Resources/Neo/NeoComposeConfig.asset";

            var config = NeoComposeConfigProvider.LoadOrCreate(path, new[] { TempRoot });

            Assert.IsNotNull(config);
            Assert.AreEqual(path, AssetDatabase.GetAssetPath(config));
            Assert.AreEqual(NeoComposeDefaults.ApiBaseUrl, config.apiBaseUrl);
            Assert.AreEqual(NeoComposeDefaults.GeneratedTypesDirectory, config.generatedTypesDirectory);
            Assert.AreEqual(NeoComposeDefaults.ProjectJsonDirectory, config.projectJsonDirectory);
            Assert.AreEqual(NeoComposeDefaults.LocalizationResourcesDirectory, config.localizationResourcesDirectory);
            Assert.AreEqual(NeoComposeDefaults.LocalizationStreamingAssetsDirectory, config.localizationStreamingAssetsDirectory);
            Assert.IsFalse(config.useStreamingAssetsForNonMainLocales);
            Assert.IsTrue(config.preloadSystemLocale);
            Assert.AreEqual("", config.localeOverride);
            Assert.AreEqual(NeoComposeDefaults.SpriteDirectory, config.spriteDirectory);
            Assert.AreEqual(NeoComposeDefaults.AudioClipDirectory, config.audioClipDirectory);
            Assert.AreEqual(NeoComposeDefaults.NamespaceForGeneratedTypes, config.namespaceForGeneratedTypes);
            Assert.AreEqual(NeoComposeDefaults.Singleton, config.singleton);
        }

        [Test]
        public void ConfigProvider_FindsMovedConfigByType()
        {
            EnsureTempRoot();
            AssetDatabase.CreateFolder(TempRoot, "Moved");
            var path = $"{TempRoot}/Moved/NeoComposeConfig.asset";
            var moved = ScriptableObject.CreateInstance<NeoComposeConfig>();
            moved.apiBaseUrl = "http://localhost:4000";
            AssetDatabase.CreateAsset(moved, path);
            AssetDatabase.SaveAssets();

            var config = NeoComposeConfigProvider.LoadOrCreate(
                $"{TempRoot}/Resources/Neo/NeoComposeConfig.asset",
                new[] { TempRoot });

            Assert.AreSame(moved, config);
            Assert.AreEqual("http://localhost:4000", config.apiBaseUrl);
        }

        [Test]
        public void Config_ClearProject_OnlyUnlinksProjectFields()
        {
            var config = ScriptableObject.CreateInstance<NeoComposeConfig>();
            config.SelectProject("project-1", "Project One");
            config.targetReleaseChannelId = "development";
            config.versionId = "version-1";
            config.generatedTypesDirectory = "Assets/NeoSchemaClasses";
            config.projectJsonDirectory = "Assets/CustomJson";
            config.localizationResourcesDirectory = "Assets/Resources/CustomLocalization";
            config.localizationStreamingAssetsDirectory = "Assets/StreamingAssets/CustomLocalization";
            config.useStreamingAssetsForNonMainLocales = true;
            config.preloadSystemLocale = false;
            config.localeOverride = "es-ES";
            config.spriteDirectory = "Assets/CustomSprites";
            config.audioClipDirectory = "Assets/CustomAudio";
            config.namespaceForGeneratedTypes = "Game.Generated";
            config.singleton = false;

            config.ClearProject();

            Assert.AreEqual("", config.projectId);
            Assert.AreEqual("", config.projectName);
            Assert.AreEqual("", config.targetReleaseChannelId);
            Assert.AreEqual("", config.versionId);
            Assert.AreEqual("Assets/NeoSchemaClasses", config.generatedTypesDirectory);
            Assert.AreEqual("Assets/CustomJson", config.projectJsonDirectory);
            Assert.AreEqual("Assets/Resources/CustomLocalization", config.localizationResourcesDirectory);
            Assert.AreEqual("Assets/StreamingAssets/CustomLocalization", config.localizationStreamingAssetsDirectory);
            Assert.IsTrue(config.useStreamingAssetsForNonMainLocales);
            Assert.IsFalse(config.preloadSystemLocale);
            Assert.AreEqual("es-ES", config.localeOverride);
            Assert.AreEqual("Assets/CustomSprites", config.spriteDirectory);
            Assert.AreEqual("Assets/CustomAudio", config.audioClipDirectory);
            Assert.AreEqual("Game.Generated", config.namespaceForGeneratedTypes);
            Assert.IsFalse(config.singleton);
        }

        [Test]
        public void GeneratedTypesSupport_LookupSelectionId_ReturnsBoundValueId()
        {
            Assert.AreEqual("value-1", NeoGeneratedTypesSupport.LookupSelectionId("value-1"));
        }

        [Test]
        public void VersionSelection_DefaultsToDevelopmentChannel()
        {
            var channels = new[]
            {
                new NeoComposeProjectReleaseChannel { id = "production", name = "Production", slug = "production", sortOrder = 1 },
                new NeoComposeProjectReleaseChannel { id = "development", name = "Development", slug = "development", sortOrder = 0 },
            };

            Assert.AreEqual("development", NeoComposeVersionSelectionUtility.SelectDefaultReleaseChannelId(channels));
        }

        [Test]
        public void ResolveTextureSettings_InlineCustomSettings_HonorsSingleSpriteMode()
        {
            // One-off (custom) settings arrive inline with templateId null —
            // the field repro: the web stored single-sprite settings but the
            // applier dropped them and kept slicing with the default template.
            var json = @"{
                ""id"": ""file-1"", ""name"": ""Vault plaque.png"",
                ""unityTextureSettings"": {
                    ""templateId"": null,
                    ""type"": ""texture-2d"",
                    ""textureType"": ""sprite"", ""textureShape"": ""2d"",
                    ""sRGBTexture"": true, ""alphaSource"": ""input-texture-alpha"",
                    ""alphaIsTransparency"": true, ""nonPowerOfTwoScale"": ""none"",
                    ""ignorePngGamma"": false, ""readWriteEnabled"": true,
                    ""virtualTextureOnly"": false, ""generateMipMaps"": false,
                    ""borderMipMaps"": false, ""mipMapFiltering"": ""box"",
                    ""mipMapsPreserveCoverage"": false, ""alphaCutoffValue"": 0.5,
                    ""fadeOutMipMaps"": false, ""mipMapFadeDistanceStart"": 1,
                    ""mipMapFadeDistanceEnd"": 3, ""anisoLevel"": 1,
                    ""wrapMode"": ""clamp"", ""filterMode"": ""point"",
                    ""maxTextureSize"": 2048, ""resizeAlgorithm"": ""mitchell"",
                    ""textureCompression"": ""none"", ""compressionQuality"": 50,
                    ""crunchedCompression"": false,
                    ""platformSettings"": { ""default"": {
                        ""compressionQuality"": 50, ""crunchedCompression"": false,
                        ""format"": ""automatic"", ""maxTextureSize"": 2048,
                        ""resizeAlgorithm"": ""mitchell"", ""textureCompression"": ""none"" } },
                    ""spriteSettings"": {
                        ""spriteMode"": ""single"", ""pixelsPerUnit"": 16,
                        ""meshType"": ""tight"", ""extrudeEdges"": 1,
                        ""pivotAlignment"": ""center"", ""pivot"": { ""x"": 0.5, ""y"": 0.5 },
                        ""generatePhysicsShape"": true,
                        ""spriteEditor"": { ""slices"": {} } }
                }
            }";
            var file = Newtonsoft.Json.JsonConvert.DeserializeObject<NeoCompose.Runtime.Json.ProjectFile>(json);
            var resolved = NeoComposeUnityImportSettingsApplier.ResolveTextureSettings(
                file!, new NeoCompose.Runtime.Json.ProjectData());

            Assert.IsNotNull(resolved, "inline custom settings must resolve");
            Assert.AreEqual("single", resolved!.spriteSettings!.spriteMode);
            Assert.AreEqual("point", resolved.filterMode);
        }

        [Test]
        public void ResolveAudioSettings_InlineCustomSettings_HonorsCustomFields()
        {
            // Same silent-drop bug as textures: one-off audio settings ride
            // inline with templateId null and must survive deserialization.
            var json = @"{
                ""id"": ""file-2"", ""name"": ""Rocket thrust.wav"",
                ""unityAudioClipSettings"": {
                    ""templateId"": null,
                    ""forceToMono"": true, ""normalize"": true,
                    ""loadInBackground"": false, ""ambisonic"": false,
                    ""loadType"": ""decompress-on-load"",
                    ""compressionFormat"": ""adpcm"",
                    ""quality"": 0.7,
                    ""sampleRateSetting"": ""preserve"",
                    ""preloadAudioData"": true
                }
            }";
            var file = Newtonsoft.Json.JsonConvert.DeserializeObject<NeoCompose.Runtime.Json.ProjectFile>(json);
            var resolved = NeoComposeUnityImportSettingsApplier.ResolveAudioSettings(
                file!, new NeoCompose.Runtime.Json.ProjectData());

            Assert.IsNotNull(resolved, "inline custom audio settings must resolve");
            Assert.AreEqual("adpcm", resolved!.compressionFormat);
            Assert.AreEqual("decompress-on-load", resolved.loadType);
            Assert.IsTrue(resolved.forceToMono);
            Assert.IsTrue(resolved.preloadAudioData);
        }

        [Test]
        public void VersionSelection_DisplayLabel_BranchesShowNameNotPlaceholderSemver()
        {
            var release = Version("v-1-0-0", "draft", 1, 0, 0);
            release.kind = "release";
            var branch = Version("v-branch", "draft", 1, 0, 0);
            branch.kind = "branch";
            branch.name = "cli-proof";
            var legacy = Version("v-0-1-1", "draft", 0, 1, 1);

            Assert.AreEqual("1.0.0", NeoComposeVersionSelectionUtility.DisplayLabel(release));
            Assert.AreEqual("cli-proof", NeoComposeVersionSelectionUtility.DisplayLabel(branch));
            Assert.AreEqual("0.1.1", NeoComposeVersionSelectionUtility.DisplayLabel(legacy));
        }

        [Test]
        public void GeneratedTypesSupport_LookupSelectionId_RejectsMissingValueId()
        {
            Assert.Throws<System.InvalidOperationException>(
                () => NeoGeneratedTypesSupport.LookupSelectionId(null));
        }

        [Test]
        public void GeneratedTypesSupport_ValuePayload_ReadsProvider()
        {
            var payload = new NeoValuePayload("value", "class-id");

            Assert.AreSame(
                payload,
                NeoGeneratedTypesSupport.ValuePayload(
                    new TestPayloadProvider(payload)));
            Assert.IsNull(NeoGeneratedTypesSupport.ValuePayload(null));
        }

#if NEO_COMPOSE_NEOWYN_TESTS

#endif

        [Test]
        public void ImportSettingsApplier_AppliesGridSpriteSlices()
        {
            EnsureTempRoot();
            var assetPath = $"{TempRoot}/sheet.png";
            var texture = new Texture2D(32, 16, TextureFormat.RGBA32, false);
            for (var y = 0; y < texture.height; y++)
            {
                for (var x = 0; x < texture.width; x++)
                {
                    texture.SetPixel(x, y, x < 16 ? Color.red : Color.blue);
                }
            }
            texture.Apply();
            File.WriteAllBytes(assetPath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(assetPath);

            var projectData = new ProjectData
            {
                textureTemplates = new Dictionary<string, UnityTexture2DImportSettingsTemplate>
                {
                    ["texture-template-1"] = MakeSpriteTemplate(),
                },
            };
            var file = new ProjectFile
            {
                id = "file-1",
                name = "sheet.png",
                fileType = "image",
                unityTextureSettings = new FileUnityTextureImportSettings
                {
                    templateId = "texture-template-1",
                    type = "texture-2d",
                    overridePaths = System.Array.Empty<string>(),
                },
            };

            new NeoComposeEditorAssetService().ApplyUnityImportSettings(assetPath, file, projectData);

            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            Assert.AreEqual(SpriteImportMode.Multiple, importer.spriteImportMode);
            Assert.AreEqual(16, importer.spritePixelsPerUnit);
            var sprites = AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath)
                .OfType<Sprite>()
                .ToArray();
            Assert.AreEqual(2, sprites.Length);
            CollectionAssert.AreEqual(new[] { "sheet_0_0", "sheet_0_1" }, sprites.Select(sprite => sprite.name).ToArray());
            Assert.AreEqual(new Rect(0, 0, 16, 16), sprites[0].rect);
            Assert.AreEqual(new Rect(16, 0, 16, 16), sprites[1].rect);
        }

        [Test]
        public void AssetDatabase_ResolvesDirectReferencesAndReverseLookup()
        {
            var texture = new Texture2D(1, 1);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero);
            var audio = AudioClip.Create("voice", 1, 1, 44100, false);
            var database = ScriptableObject.CreateInstance<NeoAssetDatabase>();
            try
            {
                database.SetFile(
                    "sprite-file",
                    "hero.png",
                    "Assets/Resources/Neo/Files/Sprites/sprite-file-hero.png",
                    "1970-01-02T00:00:00.000Z",
                    "1970-01-04T00:00:00.000Z",
                    "sprite-record-hash",
                    "texture-template-1",
                    "texture-template-hash",
                    "2026-05-13.2",
                    new[] { sprite },
                    null);
                database.SetFile(
                    "audio-file",
                    "voice.wav",
                    "Assets/Resources/Neo/Files/Audio/audio-file-voice.wav",
                    "1970-01-02T00:00:00.000Z",
                    "1970-01-04T00:00:00.000Z",
                    "audio-record-hash",
                    "audio-template-1",
                    "audio-template-hash",
                    "2026-05-13.2",
                    null,
                    audio);

                Assert.AreSame(sprite, database.TryGetSprite("sprite-file", 0));
                Assert.AreSame(audio, database.TryGetAudioClip("audio-file"));

                var spriteValue = database.TryGetValueForSprite(sprite);
                Assert.IsNotNull(spriteValue);
                Assert.AreEqual("sprite-file", spriteValue!.fileId);
                Assert.AreEqual(0, spriteValue.sliceIndex);

                var audioValue = database.TryGetValueForAudioClip(audio);
                Assert.IsNotNull(audioValue);
                Assert.AreEqual("audio-file", audioValue!.fileId);

                Assert.DoesNotThrow(() =>
                    NeoAssetResolver.ValueForSprite(database, sprite, "texture-template-1", "Portrait"));
                Assert.DoesNotThrow(() =>
                    NeoAssetResolver.ValueForAudioClip(database, audio, "audio-template-1", "Voice"));
                Assert.Throws<System.InvalidOperationException>(() =>
                    NeoAssetResolver.ValueForSprite(database, sprite, "other-template", "Portrait"));
                Assert.Throws<System.InvalidOperationException>(() =>
                    NeoAssetResolver.ValueForAudioClip(database, audio, "other-template", "Voice"));
            }
            finally
            {
                Object.DestroyImmediate(audio);
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(database);
            }
        }

        private abstract class NoSyncCallback : NeoGeneratedClassValue
        {
            protected NoSyncCallback(NeoClient client, NeoMemberClass node)
                : base(client, node, "fixture") { }
        }

        private abstract class SyncCallback : NoSyncCallback
        {
            protected SyncCallback(NeoClient client, NeoMemberClass node) : base(client, node) { }
            public override void OnDidSynchronize()
            {
            }
        }

        private abstract class InheritedSyncCallback : SyncCallback
        {
            protected InheritedSyncCallback(NeoClient client, NeoMemberClass node) : base(client, node) { }
        }

        private class CallbackProject
        {
            internal static readonly IReadOnlyDictionary<System.Type, string> NeoClassIdsByType =
                new Dictionary<System.Type, string>
                {
                    [typeof(NoSyncCallback)] = "no-op",
                    [typeof(SyncCallback)] = "callback",
                    [typeof(InheritedSyncCallback)] = "inherited",
                };
        }

        [Test]
        public void PostSynchronizeProcessor_OnlyResolvesImplementedCallbacks()
        {
            CollectionAssert.AreEquivalent(new[] { "callback", "inherited" },
                NeoComposePostSynchronizeProcessor.GetSynchronizeCallbackClassIds(CallbackProject.NeoClassIdsByType));
        }

        [Test]
        public async Task PostSynchronizeProcessor_AwaitsGeneratedClientInitialization()
        {
            using var store = new NeoProjectStore(
                dataSource: NeoTestExport.Source(File.ReadAllText(
                    "Packages/com.ryanbliss.neocompose/Tests/synth-example.json")),
                localStore: new NeoInMemoryLocalSaveStore());
            using var project = await NeoComposePostSynchronizeProcessor.LoadGeneratedProjectAsync(
                typeof(Assets.Scripts.Neo.TestProjectNeo), store, "");
            var generated = (Assets.Scripts.Neo.TestProjectNeo)project;
            Assert.IsNotNull(generated.Client);
            Assert.IsNotNull(generated.Save);
            Assert.DoesNotThrow(() => generated.Client.SerializeSaveData());
        }

        [Test]
        public void PostSynchronizeProcessor_IndexesConcreteTileClassesIncludingUnplacedAndSparseDefinitions()
        {
            var projectData = new ProjectData
            {
                values = new Dictionary<string, MemberValue>
                {
                    ["first"] = RawTilePlacement("first", "tile-class"),
                    ["second"] = RawTilePlacement("second", "tile-class"),
                    ["sparse"] = RawTilePlacement("sparse", "sparse-tile"),
                    ["footprint"] = RawTilePlacement("footprint", "placement-tile"),
                    ["object"] = RawTilePlacement("object", "object-class"),
                    ["unknown"] = RawTilePlacement("unknown", "missing-class"),
                    ["uncontained"] = new ObjectMemberValue { id = "uncontained", classId = "unused-tile" },
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    ["tile-base"] = new NeoSchemaClass { id = "tile-base", Modifier = NeoClassModifierKind.Abstract, system = JObject.FromObject(new { worldKind = "tile" }) },
                    ["tile-class"] = new NeoSchemaClass { id = "tile-class", extendsClassId = "tile-base" },
                    ["sparse-tile"] = new NeoSchemaClass { id = "sparse-tile", extendsClassId = "tile-class" },
                    ["placement-tile"] = new NeoSchemaClass { id = "placement-tile", extendsClassId = "tile-base", system = JObject.FromObject(new { worldKind = "placementTile" }) },
                    ["unused-tile"] = new NeoSchemaClass { id = "unused-tile", extendsClassId = "tile-base" },
                    ["generic-tile"] = new NeoSchemaClass { id = "generic-tile", extendsClassId = "tile-base", genericParams = new List<GenericParamDeclaration> { new() } },
                    ["object-class"] = new NeoSchemaClass { id = "object-class", system = JObject.FromObject(new { worldKind = "object" }) },
                },
            };
            ((ObjectMemberValue)projectData.values["sparse"]).value = null;

            CollectionAssert.AreEquivalent(
                new[] { "tile-class", "sparse-tile", "placement-tile", "unused-tile" },
                NeoComposePostSynchronizeProcessor.EnumerateTileClassIds(projectData).ToArray());
        }

        private static ObjectMemberValue RawTilePlacement(string id, string classId) => new()
        {
            id = id,
            classId = classId,
            containerId = "layer-items",
            value = new Dictionary<string, string> { ["Cell"] = "position-value" },
        };

        [Test]
        public void PostSynchronizeProcessor_GeneratesOneAssetPerRawTileClassAndPreservesItsUnityReference()
        {
            const string classId = "post-sync-raw-tile-test";
            const string tilePath = GeneratedTempRoot + "/Tiles/" + classId + ".asset";
            const string legacyPath = GeneratedTempRoot + "/Tiles/post-sync-legacy-value-test.asset";
            string databasePath = TempRoot + "/TileAssets.asset";
            var data = NeoTestExport.Read(File.ReadAllText(
                "Packages/com.ryanbliss.neocompose/Tests/synth-example.json"))!;
            data.classes[classId] = new NeoSchemaClass
            {
                id = classId,
                projectId = data.project.id,
                name = "PostSyncTile",
                schema = new Dictionary<string, string>(),
                system = JObject.FromObject(new { worldKind = "tile" }),
            };
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var texture = new Texture2D(1, 1);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero);
            var database = ScriptableObject.CreateInstance<NeoAssetDatabase>();
            bool hasSprite = true;
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                [classId] = (owner, node) => new PostSyncTile(owner, node, classId, hasSprite ? sprite : null),
            };
            try
            {
                if (!AssetDatabase.IsValidFolder(TempRoot))
                    AssetDatabase.CreateFolder("Assets", "NeoComposeEditorTestsTemp");
                AssetDatabase.CreateAsset(database, databasePath);
                NeoComposePostSynchronizeProcessor.SynchronizeGeneratedTileAssets(data, databasePath, client, factories, GeneratedTempRoot);
                var original = database.TryGetTileBaseForClass(classId);
                Assert.IsNotNull(original);
                Assert.AreEqual(1, database.TileAssets.Count);
                string guid = AssetDatabase.AssetPathToGUID(tilePath);

                Assert.AreEqual(classId, original!.name);
                var untouchedTime = new System.DateTime(2001, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(tilePath, untouchedTime);
                File.SetLastWriteTimeUtc(databasePath, untouchedTime);
                var serializedTile = File.ReadAllText(tilePath);
                NeoComposePostSynchronizeProcessor.SynchronizeGeneratedTileAssets(data, databasePath, client, factories, GeneratedTempRoot);
                Assert.AreEqual(untouchedTime, File.GetLastWriteTimeUtc(tilePath));
                Assert.AreEqual(untouchedTime, File.GetLastWriteTimeUtc(databasePath));
                Assert.AreEqual(serializedTile, File.ReadAllText(tilePath));

                // Effective dependencies change without touching the class timestamp.
                // The replacement is persisted so the tile's serialized reference changes.
                string replacementPath = TempRoot + "/ReplacementSprite.asset";
                var replacementTexture = new Texture2D(1, 1);
                AssetDatabase.CreateAsset(replacementTexture, replacementPath);
                var replacementSprite = Sprite.Create(replacementTexture, new Rect(0, 0, 1, 1), Vector2.one);
                AssetDatabase.AddObjectToAsset(replacementSprite, replacementPath);
                var oldSprite = sprite;
                sprite = replacementSprite;
                NeoComposePostSynchronizeProcessor.SynchronizeGeneratedTileAssets(data, databasePath, client, factories, GeneratedTempRoot);
                Assert.AreSame(replacementSprite, ((UnityEngine.Tilemaps.Tile)original).sprite);
                Assert.AreNotEqual(serializedTile, File.ReadAllText(tilePath));
                UnityEngine.Object.DestroyImmediate(oldSprite);

                // A prior per-value entry can share the same class ID. Sync must
                // discard that mapping/file while retaining the canonical asset.
                var legacy = ScriptableObject.CreateInstance<UnityEngine.Tilemaps.Tile>();
                AssetDatabase.CreateAsset(legacy, legacyPath);
                database.SetTileClassAsset(classId, legacyPath, "legacy", legacy);
                NeoComposePostSynchronizeProcessor.SynchronizeGeneratedTileAssets(data, databasePath, client, factories, GeneratedTempRoot);
                Assert.AreEqual(1, database.TileAssets.Count);
                Assert.AreSame(original, database.TryGetTileBaseForClass(classId));
                Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(tilePath));
                Assert.IsNull(AssetDatabase.LoadAssetAtPath<UnityEngine.Tilemaps.TileBase>(legacyPath));

                hasSprite = false;
                NeoComposePostSynchronizeProcessor.SynchronizeGeneratedTileAssets(data, databasePath, client, factories, GeneratedTempRoot);
                Assert.AreEqual(0, database.TileAssets.Count);
                Assert.IsNull(AssetDatabase.LoadAssetAtPath<UnityEngine.Tilemaps.TileBase>(tilePath));
            }
            finally
            {
                AssetDatabase.DeleteAsset(tilePath);
                AssetDatabase.DeleteAsset(legacyPath);
                if (!AssetDatabase.Contains(sprite))
                    UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        /// <summary>
        /// A smart tile class persists as a NeoRuleTile asset that names its
        /// script, loads back as one, and is reused by the next sync. An
        /// asset an older SDK wrote without a script is replaced, and one no
        /// tile class maps to is deleted.
        /// </summary>
        [Test]
        public void PostSynchronizeProcessor_PersistsRuleTilesWithTheirScriptAndReusesThem()
        {
            const string classId = "post-sync-rule-tile-test";
            const string ruleTilePath = GeneratedTempRoot + "/RuleTiles/" + classId + ".asset";
            const string tilePath = GeneratedTempRoot + "/Tiles/" + classId + ".asset";
            // Left by deleted tile classes; nothing maps to them.
            const string orphanRuleTilePath = GeneratedTempRoot + "/RuleTiles/deleted-rule-tile-class.asset";
            const string orphanTilePath = GeneratedTempRoot + "/Tiles/deleted-tile-class.asset";
            string databasePath = TempRoot + "/RuleTileAssets.asset";
            var data = NeoTestExport.Read(File.ReadAllText(
                "Packages/com.ryanbliss.neocompose/Tests/synth-example.json"))!;
            data.classes[classId] = new NeoSchemaClass
            {
                id = classId,
                projectId = data.project.id,
                name = "PostSyncRuleTile",
                schema = new Dictionary<string, string>(),
                system = JObject.FromObject(new { worldKind = "tile" }),
            };
            using var client = NeoTestSaveStack.ClientFromSchema(data);
            var texture = new Texture2D(1, 1);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero);
            var rule = new NeoSmartTileConverterTests.FakeSmartTileRule();
            rule.Sprites.Add(sprite);
            rule.Neighbors.Add(new NeoSmartTileConverterTests.FakeSmartTileNeighbor
            {
                Cell = new Vector2Int(1, 0),
                Condition = NeoSmartTileOptionIds.ConditionThis,
            });
            var smartTile = new NeoSmartTileConverterTests.FakeSmartTile();
            smartTile.Rules.Add(rule);
            var database = ScriptableObject.CreateInstance<NeoAssetDatabase>();
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                [classId] = (owner, node) => new PostSyncRuleTile(owner, node, classId, smartTile),
            };
            try
            {
                if (!AssetDatabase.IsValidFolder(TempRoot))
                    AssetDatabase.CreateFolder("Assets", "NeoComposeEditorTestsTemp");
                AssetDatabase.CreateAsset(database, databasePath);
                NeoComposePostSynchronizeProcessor.SynchronizeGeneratedTileAssets(data, databasePath, client, factories, GeneratedTempRoot);
                AssertPersistedRuleTile();
                string guid = AssetDatabase.AssetPathToGUID(ruleTilePath);

                // The next sync finds the asset and leaves it alone.
                var untouchedTime = new System.DateTime(2001, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(ruleTilePath, untouchedTime);
                var persisted = database.TryGetTileBaseForClass(classId);
                NeoComposePostSynchronizeProcessor.SynchronizeGeneratedTileAssets(data, databasePath, client, factories, GeneratedTempRoot);
                Assert.AreSame(persisted, database.TryGetTileBaseForClass(classId));
                Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(ruleTilePath));
                Assert.AreEqual(untouchedTime, File.GetLastWriteTimeUtc(ruleTilePath));

                // An older SDK wrote the asset with no script, which loads as
                // no tile at all. A stray copy under Tiles/ is just as broken.
                string missingScript = Regex.Replace(
                    File.ReadAllText(ruleTilePath),
                    @"m_Script: \{[^}]*\}",
                    "m_Script: {fileID: 0}");
                File.WriteAllText(ruleTilePath, missingScript);
                AssetDatabase.ImportAsset(ruleTilePath, ImportAssetOptions.ForceUpdate);
                Assert.IsNull(AssetDatabase.LoadAssetAtPath<UnityEngine.Tilemaps.TileBase>(ruleTilePath));
                if (!AssetDatabase.IsValidFolder(GeneratedTempRoot + "/Tiles"))
                    AssetDatabase.CreateFolder(GeneratedTempRoot, "Tiles");
                File.WriteAllText(tilePath, missingScript);
                AssetDatabase.ImportAsset(tilePath, ImportAssetOptions.ForceUpdate);
                Assert.IsTrue(AssetDatabase.AssetPathExists(tilePath));
                // Assets of classes deleted since, scriptless or not, go too.
                File.WriteAllText(orphanRuleTilePath, missingScript);
                AssetDatabase.ImportAsset(orphanRuleTilePath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<UnityEngine.Tilemaps.Tile>(), orphanTilePath);
                Assert.IsTrue(AssetDatabase.AssetPathExists(orphanRuleTilePath));
                NeoComposePostSynchronizeProcessor.SynchronizeGeneratedTileAssets(data, databasePath, client, factories, GeneratedTempRoot);
                AssertPersistedRuleTile();
                Assert.IsFalse(AssetDatabase.AssetPathExists(tilePath));
                Assert.IsFalse(AssetDatabase.AssetPathExists(orphanRuleTilePath));
                Assert.IsFalse(AssetDatabase.AssetPathExists(orphanTilePath));
                Assert.IsFalse(File.Exists(orphanRuleTilePath + ".meta"));
            }
            finally
            {
                AssetDatabase.DeleteAsset(ruleTilePath);
                AssetDatabase.DeleteAsset(tilePath);
                UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(texture);
            }

            void AssertPersistedRuleTile()
            {
                StringAssert.DoesNotContain("m_Script: {fileID: 0}", File.ReadAllText(ruleTilePath));
                AssetDatabase.ImportAsset(ruleTilePath, ImportAssetOptions.ForceUpdate);
                var loaded = AssetDatabase.LoadAssetAtPath<UnityEngine.Tilemaps.TileBase>(ruleTilePath);
                Assert.IsInstanceOf<NeoRuleTile>(loaded);
                Assert.AreSame(loaded, database.TryGetTileBaseForClass(classId));
                MonoScript script = MonoScript.FromScriptableObject(loaded);
                Assert.IsNotNull(script);
                Assert.AreEqual(typeof(NeoRuleTile), script.GetClass());

                // The custom neighbor survives the round trip.
                var ruleTile = (NeoRuleTile)loaded;
                var matcher = new NeoSmartTileConverterTests.RecordingNeighborMatcher();
                ruleTile.Configure(matcher);
                int neighborId = ruleTile.m_TilingRules[0].m_Neighbors[0];
                ruleTile.RuleMatch(neighborId, null!);
                Assert.AreEqual(1, matcher.Observed.Count);
                Assert.AreEqual(NeoSmartTileNeighborKind.ExactTile, matcher.Observed[0].Kind);
                Assert.AreEqual(classId, matcher.Observed[0].TileClassId);
            }
        }

        private sealed class PostSyncRuleTile : NeoGeneratedClassValue, INeoSmartTileSource
        {
            public PostSyncRuleTile(NeoClient client, NeoMemberClass node, string classId, INeoSmartTile smartTile)
                : base(client, node, classId)
            {
                SmartTile = smartTile;
            }

            public INeoSmartTile? SmartTile
            {
                get;
            }
        }

        private sealed class PostSyncTile : NeoGeneratedClassValue
        {
            public PostSyncTile(NeoClient client, NeoMemberClass node, string classId, Sprite? sprite) : base(client, node, classId)
            {
                Sprite = sprite;
            }
            public Sprite? Sprite
            {
                get;
            }
        }

        private sealed class TestPayloadProvider : INeoValuePayloadProvider
        {
            private readonly NeoValuePayload payload;

            public TestPayloadProvider(NeoValuePayload payload)
            {
                this.payload = payload;
            }

            public NeoValuePayload ToNeoValuePayload()
            {
                return payload;
            }
        }

        [Test]
        public async Task ProjectSettingsUpdater_SavesUnityExportSettings()
        {
            var config = MakeConfig();
            config.namespaceForGeneratedTypes = "Game.Generated";
            config.singleton = false;
            var api = new FakeApiClient();
            api.editResponse.project.id = config.projectId;
            api.editResponse.project.name = config.projectName;
            api.editResponse.project.exportSettings = new NeoComposeProjectExportSettings
            {
                unity = new NeoComposeUnityExportSettings
                {
                    namespaceForGeneratedTypes = "Game.Generated",
                    singleton = false,
                },
            };
            var assets = new FakeAssetService();
            var updater = new NeoComposeProjectSettingsUpdater(api, assets);

            var result = await updater.UpdateUnityExportSettingsAsync(config);

            Assert.IsTrue(result.success, result.message);
            Assert.AreEqual("http://localhost:3000", api.lastEditApiBaseUrl);
            Assert.AreEqual("project-1", api.lastEditProjectId);
            Assert.AreEqual("version-1", api.lastEditVersionId);
            Assert.AreEqual("Game.Generated", api.lastEditNamespace);
            Assert.AreEqual(false, api.lastEditSingleton);
            Assert.AreEqual("Game.Generated", config.namespaceForGeneratedTypes);
            Assert.IsFalse(config.singleton);
            Assert.IsTrue(assets.savedConfig);
        }

        private static NeoComposeConfig MakeConfig()
        {
            var config = ScriptableObject.CreateInstance<NeoComposeConfig>();
            config.apiBaseUrl = "http://localhost:3000";
            config.SelectProject("project-1", "Project One");
            config.targetReleaseChannelId = "development";
            config.versionId = "version-1";
            return config;
        }

        private static NeoComposeProjectVersion Version(
            string id,
            string statusId,
            int major,
            int minor,
            int patch)
        {
            return new NeoComposeProjectVersion
            {
                id = id,
                statusId = statusId,
                semver = new NeoComposeProjectVersionSemver
                {
                    major = major,
                    minor = minor,
                    patch = patch,
                    label = $"{major}.{minor}.{patch}",
                },
            };
        }

        private static string ProjectJsonWithFiles(string filesJson)
        {
            return @"
{
  ""metadata"": { ""schemaVersion"": 36, ""projectId"": ""project-1"", ""versionId"": ""version-1"" },
  ""variantFolders"": {},
  ""project"": {
    ""_id"": ""project-1"",
    ""id"": ""project-1"",
    ""name"": ""Project One"",
    ""rootAssetsMemberId"": ""assets-root"",
    ""rootSaveFileMemberId"": ""save-root"",
    ""createdAt"": ""1970-01-01T00:00:00.000Z"",
    ""updatedAt"": ""1970-01-01T00:00:00.000Z""
  },
  ""members"": {},
  ""values"": {},
  ""classes"": {},
  ""enums"": {},
  ""files"": {
" + filesJson + @"
  },
  ""textureTemplates"": {},
  ""audioClipTemplates"": {}
}";
        }

        private static UnityTexture2DImportSettingsTemplate MakeSpriteTemplate()
        {
            return new UnityTexture2DImportSettingsTemplate
            {
                id = "texture-template-1",
                projectId = "project-1",
                name = "Sprites",
                type = "texture-2d",
                textureType = "sprite",
                textureShape = "2d",
                sRGBTexture = true,
                alphaSource = "input-texture-alpha",
                alphaIsTransparency = true,
                nonPowerOfTwoScale = "none",
                ignorePngGamma = false,
                readWriteEnabled = true,
                virtualTextureOnly = false,
                generateMipMaps = false,
                borderMipMaps = false,
                mipMapFiltering = "box",
                mipMapsPreserveCoverage = false,
                alphaCutoffValue = 0.5,
                fadeOutMipMaps = false,
                mipMapFadeDistanceStart = 1,
                mipMapFadeDistanceEnd = 3,
                anisoLevel = 1,
                wrapMode = "clamp",
                filterMode = "point",
                textureCompression = "none",
                compressionQuality = 50,
                crunchedCompression = false,
                createdAt = "1970-01-01T00:00:00.000Z",
                updatedAt = "1970-01-02T00:00:00.000Z",
                spriteSettings = new UnitySpriteTextureSettingsTemplate
                {
                    spriteMode = "multiple",
                    pixelsPerUnit = 16,
                    meshType = "tight",
                    extrudeEdges = 1,
                    pivotAlignment = "center",
                    pivot = new UnityVector2 { x = 0.5, y = 0.5 },
                    generatePhysicsShape = true,
                    spriteEditor = new UnitySpriteEditorSettingsTemplate
                    {
                        slice = new UnitySpriteGridByCellSizeSliceTemplate
                        {
                            type = "grid-by-cell-size",
                            pixelSize = new UnityVector2 { x = 16, y = 16 },
                            offset = new UnityVector2 { x = 0, y = 0 },
                            padding = new UnityVector2 { x = 0, y = 0 },
                            keepEmptyRects = false,
                            pivotAlignment = "center",
                            pivot = new UnityVector2 { x = 0.5, y = 0.5 },
                            border = new UnityVector4 { x = 0, y = 0, z = 0, w = 0 },
                            naming = new UnitySpriteGridNamingConvention
                            {
                                pattern = "{fileName}_{row}_{column}",
                                startIndex = 0,
                                order = "row-major",
                            },
                        },
                    },
                },
            };
        }

        private static void CleanupTempRoot()
        {
            if (AssetDatabase.IsValidFolder(TempRoot))
            {
                AssetDatabase.DeleteAsset(TempRoot);
            }
        }

        private static void EnsureTempRoot()
        {
            CleanupTempRoot();
            AssetDatabase.CreateFolder("Assets", "NeoComposeEditorTestsTemp");
        }

        private sealed class FakeApiClient : INeoComposeEditorApiClient
        {
            public readonly NeoComposeProjectEditResponse editResponse = new();
            public string? lastEditApiBaseUrl;
            public string? lastEditProjectId;
            public string? lastEditVersionId;
            public string? lastEditNamespace;
            public bool? lastEditSingleton;

            public Task<NeoComposeProjectListResponse> ListProjectsAsync(string apiBaseUrl, string? query)
            {
                return Task.FromResult(new NeoComposeProjectListResponse());
            }

            public Task<NeoComposeProjectReleaseChannelListResponse> ListReleaseChannelsAsync(string apiBaseUrl, string projectId)
            {
                return Task.FromResult(new NeoComposeProjectReleaseChannelListResponse());
            }

            public Task<NeoComposeProjectVersionListResponse> ListVersionsAsync(string apiBaseUrl, string projectId)
            {
                return Task.FromResult(new NeoComposeProjectVersionListResponse());
            }

            public Task<NeoComposeProjectVersionStatusListResponse> ListVersionStatusesAsync(string apiBaseUrl, string projectId)
            {
                return Task.FromResult(new NeoComposeProjectVersionStatusListResponse());
            }

            public Task<NeoComposeProjectVersionMetadataResponse> GetVersionMetadataAsync(
                string apiBaseUrl,
                string projectId,
                string versionId)
            {
                return Task.FromResult(new NeoComposeProjectVersionMetadataResponse());
            }

            public Task<NeoComposeProjectEditResponse> UpdateProjectExportSettingsAsync(
                string apiBaseUrl,
                string projectId,
                string versionId,
                string namespaceForGeneratedTypes,
                bool singleton)
            {
                lastEditApiBaseUrl = apiBaseUrl;
                lastEditProjectId = projectId;
                lastEditVersionId = versionId;
                lastEditNamespace = namespaceForGeneratedTypes;
                lastEditSingleton = singleton;
                return Task.FromResult(editResponse);
            }
        }

        private sealed class FakeConfirmationService : INeoComposeConfirmationService
        {
            private readonly Queue<bool> responses = new();
            public readonly List<string> calls = new();

            public FakeConfirmationService(params bool[] responses)
            {
                foreach (var response in responses)
                {
                    this.responses.Enqueue(response);
                }
            }

            public bool Confirm(string title, string message, string ok, string cancel)
            {
                calls.Add(title);
                if (responses.Count == 0)
                    return true;
                return responses.Dequeue();
            }

        }

        private sealed class FakeAssetService : INeoComposeEditorAssetService
        {
            public readonly Dictionary<string, byte[]> binaryFiles = new();
            public readonly List<string> writtenPaths = new();
            public readonly Dictionary<string, Sprite[]> loadedSprites = new();
            public readonly Dictionary<string, AudioClip> loadedAudioClips = new();
            public readonly List<string> createdDirectories = new();
            public readonly List<string> deletedAssets = new();
            public readonly List<string> appliedImportSettings = new();
            public NeoAssetDatabase assetDatabase = ScriptableObject.CreateInstance<NeoAssetDatabase>();
            public bool refreshed;
            public bool saveConfigThroughProvider;
            public bool savedConfig;
            public bool savedAsset;
            public string? postSynchronizeProjectJsonPath;
            public System.Exception? throwOnSchedulePostSynchronize;

            public bool FileExists(string assetPath)
            {
                return binaryFiles.ContainsKey(assetPath);
            }

            public void EnsureDirectory(string assetDirectory)
            {
                createdDirectories.Add(assetDirectory);
            }

            public void WriteAllBytes(string assetPath, byte[] content)
            {
                writtenPaths.Add(assetPath);
                binaryFiles[assetPath] = content;
            }

            public void Refresh()
            {
                refreshed = true;
            }

            public void SaveConfig(NeoComposeConfig config)
            {
                if (saveConfigThroughProvider)
                    NeoComposeConfigProvider.Save(config);
                savedConfig = true;
            }

            public void SchedulePostSynchronize(NeoComposeConfig config, string projectJsonPath)
            {
                if (throwOnSchedulePostSynchronize != null)
                    throw throwOnSchedulePostSynchronize;
                postSynchronizeProjectJsonPath = projectJsonPath;
            }

            public NeoAssetDatabase LoadOrCreateAssetDatabase(string assetPath)
            {
                return assetDatabase;
            }

            public void ApplyUnityImportSettings(string assetPath, ProjectFile file, ProjectData projectData)
            {
                appliedImportSettings.Add(assetPath);
            }

            public Sprite[] LoadSprites(string assetPath)
            {
                return loadedSprites.TryGetValue(assetPath, out var sprites)
                    ? sprites
                    : System.Array.Empty<Sprite>();
            }

            public AudioClip? LoadAudioClip(string assetPath)
            {
                return loadedAudioClips.TryGetValue(assetPath, out var audioClip)
                    ? audioClip
                    : null;
            }

            public void SaveAsset(Object asset)
            {
                savedAsset = true;
            }

            public void DeleteAsset(string assetPath)
            {
                deletedAssets.Add(assetPath);
                binaryFiles.Remove(assetPath);
            }
        }
    }
}
