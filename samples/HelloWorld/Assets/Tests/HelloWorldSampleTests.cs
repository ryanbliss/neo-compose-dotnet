// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
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
        private const string GlassFloorTileValueId = "8f96912d-5bbb-428c-84eb-8932ef588142";
        private const string BootGlyphTileValueId = "8f96912d-5bbb-428c-84eb-8932ef588143";
        private const string RedNovaWarningTileValueId = "8f96912d-5bbb-428c-84eb-8932ef588144";
        private const string PlayerSpawnObjectValueId = "8f96912d-5bbb-428c-84eb-8932ef588151";
        private const string VaultPlaqueObjectValueId = "8f96912d-5bbb-428c-84eb-8932ef588152";

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

        [Test]
        public void TileGridRenderer_RendersOldConsoleLandingGeneratedContent()
        {
            var client = LoadSampleClient(EnglishLocalizationOptions());
            var content = client.Assets.Worlds.OldConsoleLanding.Content;

            Assert.IsInstanceOf<ReadOnlyGlassFloorTile>(
                content.Background.GetTile(new Vector2Int(-6, -5)));
            Assert.IsInstanceOf<ReadOnlyBootGlyphTile>(
                content.Background.GetTile(new Vector2Int(-5, -2)));
            Assert.IsInstanceOf<ReadOnlyRedNovaWarningTile>(
                content.Background.GetTile(new Vector2Int(-8, -5)));
            Assert.IsInstanceOf<ReadOnlyVoidTile>(
                content.Background.GetTile(new Vector2Int(9, 0)));
            Assert.IsInstanceOf<ReadOnlyPlayerSpawnObject>(
                content.Objects.GetObject(new Vector2Int(0, -4)));
            Assert.IsInstanceOf<ReadOnlyVaultPlaqueObject>(
                content.Objects.GetObject(new Vector2Int(0, 0)));
            Assert.IsInstanceOf<ReadOnlyExitPromptObject>(
                content.Objects.GetObject(new Vector2Int(6, -1)));

            var go = new GameObject("Neo TileGrid Renderer Smoke");
            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();

                renderer.Render(content);

                var tilemaps = go.GetComponentsInChildren<Tilemap>();
                Assert.AreEqual(2, tilemaps.Length);
                var backgroundTilemap = tilemaps.Single(tilemap =>
                    tilemap.gameObject.name == "Tile Layer - Background");
                Assert.IsNotNull(backgroundTilemap.GetTile(new Vector3Int(-6, -5, 0)));
                Assert.IsNotNull(backgroundTilemap.GetTile(new Vector3Int(-5, -2, 0)));

                var objectLayer = go.transform.Find("Object Layer - Objects");
                Assert.IsNotNull(
                    objectLayer,
                    "Expected the generated object layer to render as a child root.");
                Assert.AreEqual(3, objectLayer!.childCount);
                AssertRenderedObject(
                    objectLayer,
                    "old-console-object:player-spawn",
                    new Vector3(0f, -4f, 0f),
                    "4bdf7916-db7e-42f9-8b75-02ab429ac1f2-player-spawn-object");
                AssertRenderedObject(
                    objectLayer,
                    "old-console-object:vault-plaque",
                    Vector3.zero,
                    "ea5a70da-6213-4b4b-bd44-ab84adc449e0-vault-plaque-object");
                AssertRenderedObject(
                    objectLayer,
                    "old-console-object:exit-prompt",
                    new Vector3(6f, -1f, 0f),
                    "58b3b0b3-257a-46ee-ba87-bab09972ff63-exit-prompt-object",
                    "2c68221a-2a3c-45d4-8565-c5c23c0654d3-boot-glyph-tile");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static void AssertRenderedObject(
            Transform objectLayer,
            string instanceId,
            Vector3 expectedLocalPosition,
            params string[] expectedSpriteNameFragments)
        {
            var rendered = objectLayer.Find($"Object - {instanceId}");
            Assert.IsNotNull(rendered, $"Expected object instance '{instanceId}' to render.");
            Assert.AreEqual(expectedLocalPosition, rendered!.localPosition);

            var spriteNames = rendered.GetComponentsInChildren<SpriteRenderer>()
                .Select(spriteRenderer => spriteRenderer.sprite == null
                    ? string.Empty
                    : spriteRenderer.sprite.name)
                .ToArray();
            Assert.Greater(
                spriteNames.Length,
                0,
                "Expected object composition children to render sprite output.");
            foreach (var expectedSpriteNameFragment in expectedSpriteNameFragments)
            {
                Assert.IsTrue(
                    spriteNames.Any(spriteName => spriteName.Contains(expectedSpriteNameFragment)),
                    $"Expected object instance '{instanceId}' to render sprite '{expectedSpriteNameFragment}'. Rendered: {string.Join(", ", spriteNames)}");
            }
            Assert.IsFalse(
                spriteNames.Any(spriteName =>
                    spriteName.Contains("First world icon")
                    || spriteName.Contains("Ship (thruster)")
                    || spriteName.Contains("Vault plaque")),
                $"Object instance '{instanceId}' rendered an old placeholder sprite. Rendered: {string.Join(", ", spriteNames)}");
            foreach (var spriteRenderer in rendered.GetComponentsInChildren<SpriteRenderer>())
            {
                Assert.LessOrEqual(
                    spriteRenderer.bounds.size.x,
                    1.0001f,
                    $"Object instance '{instanceId}' rendered '{spriteRenderer.sprite.name}' wider than one cell.");
                Assert.LessOrEqual(
                    spriteRenderer.bounds.size.y,
                    1.0001f,
                    $"Object instance '{instanceId}' rendered '{spriteRenderer.sprite.name}' taller than one cell.");
            }
            Assert.IsNotNull(rendered.GetComponent<BoxCollider2D>());
        }

        [Test]
        public void TileGridSaveAndSessionMutation_ConvertsOldConsoleLandingTiles()
        {
            var (store, client) = LoadSampleStack(EnglishLocalizationOptions());
            var cell = new Vector2Int(2, -3);
            var glassFloor = ResolveSampleValue<ReadOnlyGlassFloorTile>(client, GlassFloorTileValueId);
            var redNova = ResolveSampleValue<ReadOnlyRedNovaWarningTile>(client, RedNovaWarningTileValueId);
            var bootGlyph = ResolveSampleValue<ReadOnlyBootGlyphTile>(client, BootGlyphTileValueId);
            var saveContent = OldConsoleLandingGridContent.ResolveForSave(
                client.Client,
                client.Assets.Worlds.OldConsoleLanding.valueId!);

            Assert.IsNull(client.Assets.Worlds.OldConsoleLanding.Content.Background.GetTile(cell));
            AssertPlacementOk(saveContent.Background.TrySetTile(cell, glassFloor));
            var placed = client.Assets.Worlds.OldConsoleLanding.Content.Background.GetTiles()
                .Single(tile => tile.Cell == cell);
            Assert.AreSame(glassFloor, placed.Tile);

            AssertPlacementOk(saveContent.Background.TryConvertTile(placed.InstanceId, redNova));
            Assert.IsInstanceOf<ReadOnlyRedNovaWarningTile>(
                client.Assets.Worlds.OldConsoleLanding.Content.Background.GetTile(cell));
            client.CommitAsync().GetAwaiter().GetResult();

            var reopened = ReopenSampleClient(store, EnglishLocalizationOptions());
            var reopenedTile = reopened.Assets.Worlds.OldConsoleLanding.Content.Background.GetTiles()
                .Single(tile => tile.Cell == cell);
            Assert.AreEqual(placed.InstanceId, reopenedTile.InstanceId);
            Assert.IsInstanceOf<ReadOnlyRedNovaWarningTile>(reopenedTile.Tile);

            var sessionContent = OldConsoleLandingGridContent.ResolveForSession(
                reopened.Client,
                reopened.Assets.Worlds.OldConsoleLanding.valueId!);
            AssertPlacementOk(sessionContent.Background.TryConvertTile(placed.InstanceId, bootGlyph));

            Assert.IsInstanceOf<ReadOnlyBootGlyphTile>(
                reopened.Assets.Worlds.OldConsoleLanding.Content.Background.GetTile(cell));

            var persistedAfterSession = ReopenSampleClient(store, EnglishLocalizationOptions());
            var persistedTile = persistedAfterSession.Assets.Worlds.OldConsoleLanding.Content.Background.GetTiles()
                .Single(tile => tile.Cell == cell);
            Assert.AreEqual(placed.InstanceId, persistedTile.InstanceId);
            Assert.IsInstanceOf<ReadOnlyRedNovaWarningTile>(persistedTile.Tile);
        }

        [Test]
        public void TileGridSaveMutation_SpawnsSwapsAndDespawnsOldConsoleLandingObjects()
        {
            var (store, client) = LoadSampleStack(EnglishLocalizationOptions());
            var cell = new Vector2Int(4, -2);
            var playerSpawn = ResolveSampleValue<ReadOnlyPlayerSpawnObject>(client, PlayerSpawnObjectValueId);
            var vaultPlaque = ResolveSampleValue<ReadOnlyVaultPlaqueObject>(client, VaultPlaqueObjectValueId);
            var saveContent = OldConsoleLandingGridContent.ResolveForSave(
                client.Client,
                client.Assets.Worlds.OldConsoleLanding.valueId!);

            Assert.IsNull(client.Assets.Worlds.OldConsoleLanding.Content.Objects.GetObject(cell));
            AssertPlacementOk(saveContent.Objects.TrySpawn(cell, playerSpawn));

            var placed = client.Assets.Worlds.OldConsoleLanding.Content.Objects.GetObjects()
                .Single(obj => obj.Cell == cell);
            Assert.IsInstanceOf<ReadOnlyPlayerSpawnObject>(placed.Object);

            var duplicate = saveContent.Objects.TrySpawn(cell, vaultPlaque);
            Assert.IsFalse(duplicate.Ok);
            Assert.AreEqual("tile-grid-object-cell-occupied", duplicate.ErrorCode);

            AssertPlacementOk(saveContent.Objects.TrySwapVariant(placed.InstanceId, vaultPlaque));
            var swapped = client.Assets.Worlds.OldConsoleLanding.Content.Objects.GetObjects()
                .Single(obj => obj.Cell == cell);
            Assert.AreEqual(placed.InstanceId, swapped.InstanceId);
            Assert.IsInstanceOf<ReadOnlyVaultPlaqueObject>(swapped.Object);
            client.CommitAsync().GetAwaiter().GetResult();

            var reopened = ReopenSampleClient(store, EnglishLocalizationOptions());
            var reopenedInstance = reopened.Assets.Worlds.OldConsoleLanding.Content.Objects.GetObjects()
                .Single(obj => obj.Cell == cell);
            Assert.AreEqual(placed.InstanceId, reopenedInstance.InstanceId);
            Assert.IsInstanceOf<ReadOnlyVaultPlaqueObject>(reopenedInstance.Object);

            var reopenedSaveContent = OldConsoleLandingGridContent.ResolveForSave(
                reopened.Client,
                reopened.Assets.Worlds.OldConsoleLanding.valueId!);
            AssertPlacementOk(reopenedSaveContent.Objects.TryDespawn(reopenedInstance.InstanceId));
            Assert.IsNull(reopened.Assets.Worlds.OldConsoleLanding.Content.Objects.GetObject(cell));
            reopened.CommitAsync().GetAwaiter().GetResult();

            var persistedAfterDespawn = ReopenSampleClient(store, EnglishLocalizationOptions());
            Assert.IsNull(
                persistedAfterDespawn.Assets.Worlds.OldConsoleLanding.Content.Objects.GetObject(cell));
        }

        // Builds the generated sample client over the Phase 9 save stack (project
        // store → save synchronizer) in place of the removed loadSave/handleSave
        // delegates. The async load completes synchronously over the in-hand JSON +
        // in-memory store, so blocking here is safe.
        private static HelloWorldNeo LoadSampleClient(NeoLocalizationOptions localizationOptions = null)
        {
            return LoadSampleClient(
                File.ReadAllText(Path.Combine(SampleProjectRoot, "project.json")),
                localizationOptions);
        }

        private static HelloWorldNeo LoadSampleClient(
            string projectJson,
            NeoLocalizationOptions localizationOptions)
        {
            var store = new NeoProjectStore(dataSource: new NeoJsonProjectDataSource(projectJson), localStore: new NeoInMemoryLocalSaveStore());
            store.LoadAsync().GetAwaiter().GetResult();
            return HelloWorldNeo.Load(store.Open("save"), localizationOptions: localizationOptions)
                .GetAwaiter()
                .GetResult();
        }

        private static (NeoProjectStore Store, HelloWorldNeo Client) LoadSampleStack(
            NeoLocalizationOptions localizationOptions)
        {
            var store = new NeoProjectStore(
                dataSource: new NeoJsonProjectDataSource(File.ReadAllText(Path.Combine(SampleProjectRoot, "project.json"))),
                localStore: new NeoInMemoryLocalSaveStore());
            store.LoadAsync().GetAwaiter().GetResult();
            return (store, ReopenSampleClient(store, localizationOptions));
        }

        private static HelloWorldNeo ReopenSampleClient(
            NeoProjectStore store,
            NeoLocalizationOptions localizationOptions)
        {
            return HelloWorldNeo.Load(store.Open("save"), localizationOptions: localizationOptions)
                .GetAwaiter()
                .GetResult();
        }

        private static T ResolveSampleValue<T>(HelloWorldNeo client, string valueId)
            where T : class
        {
            var resolved = NeoGeneratedTypesSupport.ResolveCustomValue(
                client.Client,
                valueId,
                HelloWorldNeo.NeoReadOnlyValueFactories,
                HelloWorldNeo.NeoWritableValueFactories);
            return resolved as T
                ?? throw new System.InvalidOperationException(
                    $"Expected sample value '{valueId}' to resolve as {typeof(T).Name}.");
        }

        private static void AssertPlacementOk(NeoPlacementResult result)
        {
            Assert.IsTrue(result.Ok, result.Message ?? result.ErrorCode);
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
