// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using System.Collections.Generic;
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
        private static readonly string SampleProjectJson =
            File.ReadAllText(Path.Combine(SampleProjectRoot, "project.json"));
        private static NeoJsonProjectDataSource CreateSampleProjectSource() =>
            new NeoJsonProjectDataSource(SampleProjectJson);

        private readonly List<System.IDisposable> ownedResources = new();

        [TearDown]
        public void TearDown()
        {
            for (var index = ownedResources.Count - 1; index >= 0; index -= 1)
            {
                ownedResources[index].Dispose();
            }
            ownedResources.Clear();
        }
        private const string PlayerSpawnObjectValueId = "8f96912d-5bbb-428c-84eb-8932ef588151";
        private const string VaultPlaqueObjectValueId = "8f96912d-5bbb-428c-84eb-8932ef588152";
        private const string BlockedPathValueId = "432f5226-99d8-4d59-8cf0-4d86ca64462f";
        private static readonly string[] OldConsoleLandingDialogueIds =
        {
            "2a49e84a-ab1f-4468-a9a3-f29796cbf086",
            "d755935f-4c3a-4d43-8c40-4ba3f7d28063",
            "12729fbc-56a7-4d8f-b04a-ac039604dfe9",
            "d5a8097d-f02b-41c7-8356-9442a4a29412",
            "7a6bcb67-d42a-4eb8-9934-0263d506e85c",
            "da73bce9-0d39-4c27-bb09-32b538f97f61",
            "bbda459e-c77e-4084-9047-22b1dfbb0bff",
        };
        private static readonly IReadOnlyDictionary<string, string> OldConsoleLandingExpectedTextByDialogueId =
            new Dictionary<string, string>
            {
                ["2a49e84a-ab1f-4468-a9a3-f29796cbf086"] =
                    "A wall of teal light hums across the corridor",
                ["d755935f-4c3a-4d43-8c40-4ba3f7d28063"] =
                    "The field collapses into a line of falling sparks.",
                ["12729fbc-56a7-4d8f-b04a-ac039604dfe9"] =
                    "The dark tile wakes under your boots",
                ["d5a8097d-f02b-41c7-8356-9442a4a29412"] =
                    "Relaying now.",
                ["7a6bcb67-d42a-4eb8-9934-0263d506e85c"] =
                    "Your ship's launch console idles",
                ["da73bce9-0d39-4c27-bb09-32b538f97f61"] =
                    "RECOVERY CACHE AHEAD",
                ["bbda459e-c77e-4084-9047-22b1dfbb0bff"] =
                    "SEAL RELEASED",
            };

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
            Assert.AreEqual("Tierra", client.Save.Visited[0].World.Text);
            Assert.AreEqual("Hola Tierra!", client.Assets.Computed.fullText);
        }

        [Test]
        public void GeneratedSampleTypes_LoadUsesResourcesConfigWhenLocalizationOptionsAreNull()
        {
            var configOptions = NeoComposeConfig.LoadDefault()!.ToLocalizationOptions();

            var defaultClient = LoadSampleClient(CreateSampleProjectSource(), localizationOptions: null);
            var explicitConfigClient = LoadSampleClient(CreateSampleProjectSource(), localizationOptions: configOptions);

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
                Assert.IsInstanceOf<Outpost>(node.Primary);
                var primary = (Outpost)node.Primary!;
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
            CollectionAssert.IsEmpty(client.FindUnlinkedSaveValueIds(), client.SerializeSaveData());
        }

        [Test]
        public void GeneratedDialogue_OldConsoleLandingFlowsAreAuthoredNeoDialogues()
        {
            var client = LoadSampleClient(EnglishLocalizationOptions());

            foreach (var dialogueId in OldConsoleLandingDialogueIds)
            {
                Assert.IsTrue(
                    client.Dialogues.TryTrigger(dialogueId, out NeoDialogue dialogue),
                    $"Expected old-console landing dialogue '{dialogueId}' to trigger directly.");

                var shown = new System.Collections.Generic.List<NeoDialogueTextNode>();
                bool finished = false;
                dialogue.OnShow += node =>
                {
                    shown.Add(node);
                    if (node.Primary is not null)
                    {
                        Assert.IsInstanceOf<BlockedPath>(node.Primary);
                    }
                    if (node.Options.Count > 0)
                    {
                        Assert.IsFalse(string.IsNullOrWhiteSpace(node.Options[0].Text));
                        node.Options[0].Select();
                        return;
                    }
                    node.Next();
                };
                dialogue.OnFinish += () => finished = true;

                dialogue.Start();

                Assert.IsTrue(finished, dialogueId);
                Assert.GreaterOrEqual(shown.Count, 1, dialogueId);
                Assert.IsTrue(
                    shown.Any(node => node.Text.Contains(OldConsoleLandingExpectedTextByDialogueId[dialogueId])),
                    dialogueId);
                dialogue.Dispose();
            }
        }

        [Test]
        public void DialogueUI_RendersAboveSceneCameras()
        {
            var dialogueUI = new DialogueUI();
            try
            {
                dialogueUI.Show("Console", null, "The dialogue should sit above the active scene camera.");

                var root = GameObject.Find("Dialogue UI");
                Assert.IsNotNull(root);
                var canvas = root!.GetComponent<Canvas>();
                Assert.IsNotNull(canvas);
                Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
                Assert.GreaterOrEqual(canvas.sortingOrder, 1000);
            }
            finally
            {
                dialogueUI.Dispose();
            }
        }

        [Test]
        public void GeneratedNSGetters_InRepeatedClassValuesResolveAgainstEachOutpost()
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
        public void GeneratedReadOnlyOutpost_AllowsSaveBackedChildMutation()
        {
            var client = LoadSampleClient(EnglishLocalizationOptions());

            IReadOnlyOutpost outpost = client.Assets.Outposts.First();
            Assert.IsTrue(outpost.IsReadOnly);
            Assert.IsInstanceOf<Outpost>(outpost);
            Assert.IsFalse(outpost.TryWritable(out Outpost writableOutpost));
            Assert.IsNull(writableOutpost);

            var before = outpost.Save.VisitCount;
            outpost.Save.VisitCount = before + 1;

            Assert.AreEqual(before + 1, outpost.Save.VisitCount);
            var serialized = client.SerializeSaveData();
            StringAssert.Contains($"\"value\":{before + 1}", serialized);
            CollectionAssert.IsEmpty(client.FindUnlinkedSaveValueIds());
        }

        [Test]
        public void GeneratedSaveBackedDescendant_AllowsWritableCastFromBaseInterface()
        {
            var client = LoadSampleClient(EnglishLocalizationOptions());

            IReadOnlyNeoLayerGroupBase group = client.Assets.Worlds.OldConsoleLanding.Children
                .First(check => check.Name == "Blocked Path");

            Assert.IsFalse(group.IsReadOnly);
            Assert.IsTrue(group.TryWritable(out BlockedPath blocked));
            Assert.IsFalse(blocked.IsReadOnly);
            Assert.DoesNotThrow(() => Assert.GreaterOrEqual(blocked.Tiles.Count, 0));
            Assert.Greater(blocked.Tiles.Count, 0);

            Assert.DoesNotThrow(() => Assert.IsTrue(blocked.ClearPath()));
            Assert.AreEqual(0, blocked.Tiles.Count);
            // Clearing an unordered containment list persists as removal
            // tombstones at the authored member ids (membership by join; the
            // container's discriminator row is never rewritten).
            StringAssert.Contains("\"mark\":\"removed\"", client.SerializeSaveData());
        }

        [Test]
        public void GeneratedChildAccessors_ResolveTypedChildrenFromTheLiveList()
        {
            var client = LoadSampleClient(EnglishLocalizationOptions());
            var grid = client.Assets.Worlds.OldConsoleLanding;

            // GetComponent-style: first assignable child, writable-resolved.
            var blocked = grid.GetRequiredChild<BlockedPath>();
            Assert.IsFalse(blocked.IsReadOnly);
            Assert.Greater(blocked.Tiles.Count, 0);

            Assert.AreSame(blocked, grid.GetChild<BlockedPath>());
            Assert.AreSame(blocked, grid.GetChild<BlockedPath>("Blocked Path"));
            Assert.IsNull(grid.GetChild<BlockedPath>("Not A Real Group"));

            Assert.IsTrue(grid.TryGetChild(out BlockedPath viaTry));
            Assert.AreSame(blocked, viaTry);

            // Assignability: querying by the base link type also matches.
            Assert.IsNotNull(grid.GetChild<NeoTileLayerLink>());
            CollectionAssert.Contains(grid.GetChildren<BlockedPath>().ToArray(), blocked);

            var missing = Assert.Throws<System.InvalidOperationException>(
                () => grid.GetRequiredChild<JupiterOutpost>());
            StringAssert.Contains("has no child of type 'JupiterOutpost'", missing!.Message);
        }

        [Test]
        public void CellPatternQueries_FindNearbyTilesAndObjectsNearestFirst()
        {
            var client = LoadSampleClient(EnglishLocalizationOptions());
            var content = client.Assets.Worlds.OldConsoleLanding.Content;
            var blocked = client.Assets.Worlds.OldConsoleLanding.GetRequiredChild<BlockedPath>();
            var reach = NeoCellPattern.Cross(1);

            // The seal barrier projects a collision tile at (0, 1): standing on
            // it or beside it is within reach, the far side of the map is not.
            Assert.IsNotNull(content.GetTile(blocked, new Vector2Int(0, 1), reach));
            Assert.IsNotNull(content.GetTile(blocked, new Vector2Int(1, 1), reach));
            Assert.IsNull(content.GetTile(blocked, new Vector2Int(9, 0), reach));

            // Typed tile query: the boot glyph at (-7, -6) is reachable from a neighbor.
            Assert.IsNotNull(
                content.Background.GetTile<BootGlyphTile>(new Vector2Int(-7, -5), reach));
            Assert.IsNull(
                content.Background.GetTile<BootGlyphTile>(new Vector2Int(0, 0), reach));

            // Typed object query: the player spawn at (-7, 2) from one cell away.
            Assert.IsNotNull(
                content.Objects.GetObject<PlayerSpawnObject>(new Vector2Int(-6, 2), reach));
            Assert.IsNull(
                content.Objects.GetObject<PlayerSpawnObject>(new Vector2Int(9, 0), reach));

            // Nearest-first: standing on the barrier, the center cell wins.
            var nearest = content.GetTile(blocked, new Vector2Int(0, 1), reach);
            Assert.AreEqual(new Vector2Int(0, 1), nearest!.Cell);
        }

        [Test]
        public void TileLayerLinkQueries_MatchTheTilesTheLinkProjectsOntoItsLayer()
        {
            var client = LoadSampleClient(EnglishLocalizationOptions());
            var content = client.Assets.Worlds.OldConsoleLanding.Content;
            var blocked = client.Assets.Worlds.OldConsoleLanding.GetRequiredChild<BlockedPath>();

            var tiles = blocked.GetTiles();
            Assert.AreEqual(blocked.Tiles.Count, tiles.Count);
            foreach (var tile in tiles)
            {
                Assert.AreEqual(NeoTileOutputSourceKind.TileLayerLink, tile.SourceKind);
                Assert.AreEqual(blocked.valueId, tile.SourceTileLayerLinkId);
                Assert.AreEqual(content.Collisions.LayerId, tile.LayerId);
                // The link's grid-space cells line up with the Collisions layer.
                Assert.AreEqual(
                    blocked.valueId,
                    content.Collisions.GetTile(tile.Cell)?.SourceTileLayerLinkId);
            }

            var barrier = tiles.First();
            Assert.IsInstanceOf<SealBarrierTile>(barrier.Info);
            Assert.IsNotNull(blocked.GetTile(barrier.Cell));
            Assert.IsNotNull(blocked.GetTile<SealBarrierTile>(barrier.Cell));

            // Pattern overloads: adjacent is within reach, far away is not.
            var reach = NeoCellPattern.Cross(1);
            Assert.IsNotNull(blocked.GetTile(barrier.Cell + Vector2Int.right, reach));
            Assert.IsNull(blocked.GetTile(barrier.Cell + new Vector2Int(50, 50), reach));
        }

        [Test]
        public void TileLayerLinkQueries_SeeTheCurrentTilesDuringChangeCallbacks()
        {
            // Regression guard for the notification-ordering trap: value rows
            // update before change events fire, while wrapper child nodes can
            // lag one dispatch behind. The link projection must read current
            // state even inside a collision-layer change callback.
            var client = LoadSampleClient(EnglishLocalizationOptions());
            var content = client.Assets.Worlds.OldConsoleLanding.Content;
            var blocked = client.Assets.Worlds.OldConsoleLanding.GetRequiredChild<BlockedPath>();
            var go = new GameObject("Link projection callback test");
            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(content);
                int notifications = 0;
                int projectionDuringCallback = -1;
                using var subscription = content.Collisions.OnChanged(_ =>
                {
                    notifications++;
                    projectionDuringCallback = blocked.GetTiles().Count;
                });

                blocked.Tiles.Clear();

                Assert.Greater(
                    notifications,
                    0,
                    "The collision layer subscription should hear the link clear.");
                Assert.AreEqual(0, projectionDuringCallback);
                Assert.AreEqual(0, blocked.GetTiles().Count);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ComputeCellBounds_SpansTheAuthoredWorld()
        {
            var client = LoadSampleClient(EnglishLocalizationOptions());
            var content = client.Assets.Worlds.OldConsoleLanding.Content;

            var bounds = content.ComputeCellBounds();

            Assert.AreNotEqual(Vector3Int.zero, bounds.size);
            // Known authored extremes from the landing grid.
            Assert.IsTrue(bounds.Contains(new Vector3Int(-9, 2, 0)), "exit prompt cell");
            Assert.IsTrue(bounds.Contains(new Vector3Int(9, 0, 0)), "glass floor cell");
            Assert.IsTrue(bounds.Contains(new Vector3Int(-7, -6, 0)), "boot glyph cell");
            Assert.IsTrue(bounds.Contains(new Vector3Int(-6, 5, 0)), "void tile cell");
            Assert.AreEqual(1, bounds.size.z);
        }

        [Test]
        public void GeneratedClassValues_ReturnCachedInstances()
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
        public void TileGridRenderer_RendersOldConsoleLandingGeneratedContent()
        {
            var client = LoadSampleClient(EnglishLocalizationOptions());
            var content = client.Assets.Worlds.OldConsoleLanding.Content;

            Assert.IsInstanceOf<VoidTile>(
                content.Background.GetTile(new Vector2Int(-6, 5))?.Info);
            Assert.IsInstanceOf<BootGlyphTile>(
                content.Background.GetTile(new Vector2Int(-7, -6))?.Info);
            NeoResolvedTileInstance<BootGlyphTile> typedBootGlyph =
                content.Background.GetTile<BootGlyphTile>(new Vector2Int(-7, -6));
            Assert.IsNotNull(typedBootGlyph);
            Assert.IsInstanceOf<RedNovaWarningTile>(
                content.Background.GetTile(new Vector2Int(1, 1))?.Info);
            Assert.IsInstanceOf<GlassFloorTile>(
                content.Background.GetTile(new Vector2Int(9, 0))?.Info);
            Assert.IsInstanceOf<SealBarrierTile>(
                content.Collisions.GetTile(new Vector2Int(0, 1))?.Info);
            Assert.IsNull(content.Collisions.GetTile(new Vector2Int(1, 1)));
            Assert.IsNull(content.Collisions.GetTile(new Vector2Int(2, 1)));
            Assert.IsInstanceOf<PlayerSpawnObject>(
                content.Objects.GetObject(new Vector2Int(-7, 2))?.Info);
            NeoResolvedObjectInstance<PlayerSpawnObject> typedPlayerSpawn =
                content.Objects.GetObject<PlayerSpawnObject>(new Vector2Int(-7, 2));
            Assert.IsNotNull(typedPlayerSpawn);

            var go = new GameObject("Neo TileGrid Renderer Smoke");
            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();

                renderer.Render(content);

                var tilemaps = go.GetComponentsInChildren<Tilemap>();
                Assert.AreEqual(2, tilemaps.Length);
                var backgroundTilemap = tilemaps.Single(tilemap =>
                    tilemap.gameObject.name == "Tile Layer - Background");
                Assert.IsNotNull(backgroundTilemap.GetTile(new Vector3Int(-6, 5, 0)));
                Assert.IsNotNull(backgroundTilemap.GetTile(new Vector3Int(-7, -6, 0)));
                var collisionTilemap = tilemaps.Single(tilemap =>
                    tilemap.gameObject.name == "Tile Layer - Collisions");
                Assert.IsNotNull(collisionTilemap.GetTile(new Vector3Int(0, 1, 0)));

                var objectLayer = go.transform.Find("Object Layer - Objects");
                Assert.IsNotNull(
                    objectLayer,
                    "Expected the generated object layer to render as a child root.");
                Assert.AreEqual(2, objectLayer!.childCount);
                AssertRenderedObject(
                    objectLayer,
                    RenderedInstanceId(content, new Vector2Int(-7, 2)),
                    new Vector3(-7f, 2f, 0f),
                    InstanceHasCollider(content, new Vector2Int(-7, 2)),
                    "4bdf7916-db7e-42f9-8b75-02ab429ac1f2-player-spawn-object");
                AssertRenderedObject(
                    objectLayer,
                    RenderedInstanceId(content, new Vector2Int(6, 2)),
                    new Vector3(6f, 2f, 0f),
                    InstanceHasCollider(content, new Vector2Int(6, 2)),
                    "ea87154e-d1dd-49f4-8050-96f1493a81fc-recovery-cache-object");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// Resolves a placed instance's id from the content data so renderer
        /// assertions survive instances being deleted and re-placed in the
        /// editor (re-placement mints a new instance id; the cell is the
        /// stable authoring intent).
        /// </summary>
        private static string RenderedInstanceId(
            ReadOnlyOldConsoleLandingGridContent content,
            Vector2Int cell)
        {
            var instance = content.Objects.GetObject(cell);
            if (instance is null)
            {
                throw new System.InvalidOperationException(
                    $"No object instance is placed at cell {cell} in the OldConsoleLanding grid.");
            }

            return instance.InstanceId.Value;
        }

        /// <summary>
        /// True when the placed instance's own value model resolves a collider —
        /// an explicit record on the instance; a missing or null-valued
        /// Collider reads as "no collider" and must render without a
        /// BoxCollider2D.
        /// </summary>
        private static bool InstanceHasCollider(
            ReadOnlyOldConsoleLandingGridContent content,
            Vector2Int cell)
        {
            return content.Objects.GetObject(cell)?.Info is NeoObject obj
                && obj.Collider is not null;
        }

        private static void AssertRenderedObject(
            Transform objectLayer,
            string instanceId,
            Vector3 expectedLocalPosition,
            bool expectCollider,
            params string[] expectedSpriteNameFragments)
        {
            var rendered = objectLayer.Find($"Object - {instanceId}");
            Assert.IsNotNull(rendered, $"Expected object instance '{instanceId}' to render.");
            Assert.AreEqual(expectedLocalPosition, rendered!.localPosition);

            var behaviour = rendered.GetComponent<NeoObjectBehaviour>();
            Assert.IsNotNull(
                behaviour,
                $"Expected object instance '{instanceId}' to carry a NeoObjectBehaviour bridge.");
            Assert.AreEqual(instanceId, behaviour!.InstanceId.Value);
            Assert.IsInstanceOf<NeoObject>(
                behaviour.Object,
                $"Expected NeoObjectBehaviour on '{instanceId}' to expose the generated object value.");

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
            Assert.AreEqual(
                expectCollider,
                rendered.GetComponent<BoxCollider2D>() != null,
                $"Object instance '{instanceId}' collider presence should match its resolved Collider value.");
        }

        [Test]
        public void TileGridSaveMutation_RemovesOldConsoleLandingCollisionBarrier()
        {
            var (store, client) = LoadSampleStack(EnglishLocalizationOptions());
            var blockerCell = new Vector2Int(0, 1);
            var saveContent = OldConsoleLandingGridContent.ResolveForSave(
                client.Client,
                client.Assets.Worlds.OldConsoleLanding.valueId!);

            var blocker = client.Assets.Worlds.OldConsoleLanding.Content.Collisions.GetTiles()
                .Single(tile => tile.Cell == blockerCell);
            Assert.AreEqual(NeoTileOutputSourceKind.TileLayerLink, blocker.SourceKind);
            Assert.AreEqual(BlockedPathValueId, blocker.SourceTileLayerLinkId);
            Assert.IsInstanceOf<SealBarrierTile>(blocker.Info);

            AssertPlacementOk(saveContent.Collisions.TryRemoveTile(blocker.InstanceId));
            Assert.IsNull(client.Assets.Worlds.OldConsoleLanding.Content.Collisions.GetTile(blockerCell));
            client.CommitAsync().GetAwaiter().GetResult();

            var reopened = ReopenSampleClient(store, EnglishLocalizationOptions());
            Assert.IsNull(reopened.Assets.Worlds.OldConsoleLanding.Content.Collisions.GetTile(blockerCell));
            Assert.IsInstanceOf<SealBarrierTile>(
                reopened.Assets.Worlds.OldConsoleLanding.Content.Collisions.GetTile(new Vector2Int(0, 2))?.Info);
            Assert.IsNull(reopened.Assets.Worlds.OldConsoleLanding.Content.Collisions.GetTile(new Vector2Int(1, 1)));
        }

        [Test]
        public void GeneratedBlockedPathClearPath_RemovesLinkedCollisionTiles()
        {
            var (store, client) = LoadSampleStack(EnglishLocalizationOptions());
            var blocked = client.Assets.Worlds.OldConsoleLanding.Children
                .First(check => check.Name == "Blocked Path") as BlockedPath;
            Assert.IsNotNull(blocked);

            var blockerCells = client.Assets.Worlds.OldConsoleLanding.Content.Collisions.GetTiles()
                .Where(tile => tile.SourceTileLayerLinkId == BlockedPathValueId)
                .Select(tile => tile.Cell)
                .ToArray();
            Assert.Greater(blockerCells.Length, 0);

            Assert.IsTrue(blocked!.ClearPath());

            CollectionAssert.IsEmpty(
                client.Assets.Worlds.OldConsoleLanding.Content.Collisions.GetTiles()
                    .Where(tile => tile.SourceTileLayerLinkId == BlockedPathValueId)
                    .Select(tile => tile.Cell)
                    .ToArray());
            foreach (var cell in blockerCells)
            {
                Assert.IsNull(client.Assets.Worlds.OldConsoleLanding.Content.Collisions.GetTile(cell));
            }
            Assert.AreEqual(0, blocked.Tiles.Count);
            CollectionAssert.IsEmpty(client.FindUnlinkedSaveValueIds());

            client.CommitAsync().GetAwaiter().GetResult();
            var reopened = ReopenSampleClient(store, EnglishLocalizationOptions());
            foreach (var cell in blockerCells)
            {
                Assert.IsNull(reopened.Assets.Worlds.OldConsoleLanding.Content.Collisions.GetTile(cell));
            }
        }

        [Test]
        public void TileGridRenderer_LiveSyncClearsOldConsoleLandingBarrierWhenSourceTilesClear()
        {
            var client = LoadSampleClient(EnglishLocalizationOptions());
            var content = client.Assets.Worlds.OldConsoleLanding.Content;
            var blocked = client.Assets.Worlds.OldConsoleLanding.Children
                .First(check => check.Name == "Blocked Path") as BlockedPath;
            Assert.IsNotNull(blocked);
            var blockerCells = content.Collisions.GetTiles()
                .Where(tile => tile.SourceTileLayerLinkId == BlockedPathValueId)
                .Select(tile => tile.Cell)
                .ToArray();
            Assert.Greater(blockerCells.Length, 0);

            var go = new GameObject("Neo TileGrid Renderer Live Barrier Clear");
            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(content);

                Assert.IsTrue(renderer.IsLiveSynced);
                Assert.AreSame(content, renderer.CurrentContent);
                var collisionTilemap = go.GetComponentsInChildren<Tilemap>()
                    .Single(tilemap => tilemap.gameObject.name == "Tile Layer - Collisions");
                foreach (var cell in blockerCells)
                {
                    Assert.IsNotNull(collisionTilemap.GetTile(new Vector3Int(cell.x, cell.y, 0)));
                }

                NeoTileLayerChangedArgs observedLayerChange = null;
                using var layerSubscription = content.Collisions.OnChanged(args =>
                    observedLayerChange = args);
                blocked!.Tiles.Clear();

                Assert.IsNotNull(observedLayerChange);
                Assert.AreEqual(NeoTileGridChangeSourceKind.TileLayerLink, observedLayerChange!.SourceKind);
                Assert.AreEqual(BlockedPathValueId, observedLayerChange.SourceId);
                CollectionAssert.AreEquivalent(blockerCells, observedLayerChange.CellsToClear);
                Assert.AreEqual(0, observedLayerChange.CellsToSetOrRefresh.Count);
                foreach (var cell in blockerCells)
                {
                    Assert.IsNull(collisionTilemap.GetTile(new Vector3Int(cell.x, cell.y, 0)));
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TileGridContent_CellLookupsResolveLayersObjectsAndLiveDeltas()
        {
            var client = LoadSampleClient(EnglishLocalizationOptions());
            var content = client.Assets.Worlds.OldConsoleLanding.Content;
            var blocked = client.Assets.Worlds.OldConsoleLanding.Children
                .First(check => check.Name == "Blocked Path") as BlockedPath;
            Assert.IsNotNull(blocked);

            var blocker = content.Collisions.GetTiles()
                .First(tile => tile.SourceTileLayerLinkId == BlockedPathValueId);
            var blockerCell = blocker.Cell;
            var playerSpawn = content.Objects.GetObjects()
                .Single(instance => instance.Info is PlayerSpawnObject);

            Assert.IsInstanceOf<SealBarrierTile>(content.Collisions.GetTile(blockerCell)?.Info);
            Assert.AreEqual(BlockedPathValueId, content.GetTile(blockerCell)?.SourceTileLayerLinkId);
            Assert.AreEqual(BlockedPathValueId, blocked!.GetTile(content, blockerCell)?.SourceTileLayerLinkId);
            Assert.IsInstanceOf<PlayerSpawnObject>(content.GetObject(playerSpawn.Cell)?.Info);

            blocked.Tiles.Clear();

            Assert.IsNull(content.Collisions.GetTile(blockerCell));
            Assert.IsNull(blocked.GetTile(content, blockerCell));
        }

        [Test]
        public void TileGridSaveAndSessionMutation_ConvertsOldConsoleLandingTiles()
        {
            var (store, client) = LoadSampleStack(EnglishLocalizationOptions());
            var cell = new Vector2Int(20, 20);
            var glassFloor = ResolveSampleValue<GlassFloorTile>(client, GlassFloorTileValueId);
            var redNova = ResolveSampleValue<RedNovaWarningTile>(client, RedNovaWarningTileValueId);
            var bootGlyph = ResolveSampleValue<BootGlyphTile>(client, BootGlyphTileValueId);
            var saveContent = OldConsoleLandingGridContent.ResolveForSave(
                client.Client,
                client.Assets.Worlds.OldConsoleLanding.valueId!);

            Assert.IsNull(client.Assets.Worlds.OldConsoleLanding.Content.Background.GetTile(cell));
            AssertPlacementOk(saveContent.Background.TrySetTile(cell, glassFloor));
            var placed = client.Assets.Worlds.OldConsoleLanding.Content.Background.GetTiles()
                .Single(tile => tile.Cell == cell);
            Assert.AreSame(glassFloor, placed.Info);

            AssertPlacementOk(saveContent.Background.TryConvertTile(placed.InstanceId, redNova));
            Assert.IsInstanceOf<RedNovaWarningTile>(
                client.Assets.Worlds.OldConsoleLanding.Content.Background.GetTile(cell)?.Info);
            client.CommitAsync().GetAwaiter().GetResult();

            var reopened = ReopenSampleClient(store, EnglishLocalizationOptions());
            var reopenedTile = reopened.Assets.Worlds.OldConsoleLanding.Content.Background.GetTiles()
                .Single(tile => tile.Cell == cell);
            Assert.AreEqual(placed.InstanceId, reopenedTile.InstanceId);
            Assert.IsInstanceOf<RedNovaWarningTile>(reopenedTile.Info);

            var sessionContent = OldConsoleLandingGridContent.ResolveForSession(
                reopened.Client,
                reopened.Assets.Worlds.OldConsoleLanding.valueId!);
            AssertPlacementOk(sessionContent.Background.TryConvertTile(placed.InstanceId, bootGlyph));

            Assert.IsInstanceOf<BootGlyphTile>(
                reopened.Assets.Worlds.OldConsoleLanding.Content.Background.GetTile(cell)?.Info);

            var persistedAfterSession = ReopenSampleClient(store, EnglishLocalizationOptions());
            var persistedTile = persistedAfterSession.Assets.Worlds.OldConsoleLanding.Content.Background.GetTiles()
                .Single(tile => tile.Cell == cell);
            Assert.AreEqual(placed.InstanceId, persistedTile.InstanceId);
            Assert.IsInstanceOf<RedNovaWarningTile>(persistedTile.Info);
        }

        [Test]
        public void TileGridSaveMutation_SpawnsSwapsAndDespawnsOldConsoleLandingObjects()
        {
            var (store, client) = LoadSampleStack(EnglishLocalizationOptions());
            var cell = new Vector2Int(21, 20);
            var playerSpawn = ResolveSampleValue<PlayerSpawnObject>(client, PlayerSpawnObjectValueId);
            var vaultPlaque = ResolveSampleValue<VaultPlaqueObject>(client, VaultPlaqueObjectValueId);
            var saveContent = OldConsoleLandingGridContent.ResolveForSave(
                client.Client,
                client.Assets.Worlds.OldConsoleLanding.valueId!);

            Assert.IsNull(client.Assets.Worlds.OldConsoleLanding.Content.Objects.GetObject(cell));
            AssertPlacementOk(saveContent.Objects.TrySpawn(cell, playerSpawn));

            var placed = client.Assets.Worlds.OldConsoleLanding.Content.Objects.GetObjects()
                .Single(obj => obj.Cell == cell);
            Assert.IsInstanceOf<PlayerSpawnObject>(placed.Info);

            var duplicate = saveContent.Objects.TrySpawn(cell, vaultPlaque);
            Assert.IsFalse(duplicate.Ok);
            Assert.AreEqual("tile-grid-object-cell-occupied", duplicate.ErrorCode);

            AssertPlacementOk(saveContent.Objects.TrySwapVariant(placed.InstanceId, vaultPlaque));
            var swapped = client.Assets.Worlds.OldConsoleLanding.Content.Objects.GetObjects()
                .Single(obj => obj.Cell == cell);
            Assert.AreEqual(placed.InstanceId, swapped.InstanceId);
            Assert.IsInstanceOf<VaultPlaqueObject>(swapped.Info);
            client.CommitAsync().GetAwaiter().GetResult();

            var reopened = ReopenSampleClient(store, EnglishLocalizationOptions());
            var reopenedInstance = reopened.Assets.Worlds.OldConsoleLanding.Content.Objects.GetObjects()
                .Single(obj => obj.Cell == cell);
            Assert.AreEqual(placed.InstanceId, reopenedInstance.InstanceId);
            Assert.IsInstanceOf<VaultPlaqueObject>(reopenedInstance.Info);

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
        private HelloWorldNeo LoadSampleClient(NeoLocalizationOptions localizationOptions = null)
        {
            return LoadSampleClient(CreateSampleProjectSource(), localizationOptions);
        }

        private HelloWorldNeo LoadSampleClient(
            IProjectDataSource projectSource,
            NeoLocalizationOptions localizationOptions)
        {
            var store = Own(new NeoProjectStore(
                dataSource: projectSource,
                localStore: new NeoInMemoryLocalSaveStore()));
            store.LoadAsync().GetAwaiter().GetResult();
            return Own(HelloWorldNeo.Load(
                    store.Open("save"),
                    localizationOptions: localizationOptions)
                .GetAwaiter()
                .GetResult());
        }

        private (NeoProjectStore Store, HelloWorldNeo Client) LoadSampleStack(
            NeoLocalizationOptions localizationOptions)
        {
            var store = Own(new NeoProjectStore(
                dataSource: CreateSampleProjectSource(),
                localStore: new NeoInMemoryLocalSaveStore()));
            store.LoadAsync().GetAwaiter().GetResult();
            return (store, ReopenSampleClient(store, localizationOptions));
        }

        private HelloWorldNeo ReopenSampleClient(
            NeoProjectStore store,
            NeoLocalizationOptions localizationOptions)
        {
            return Own(HelloWorldNeo.Load(
                    store.Open("save"),
                    localizationOptions: localizationOptions)
                .GetAwaiter()
                .GetResult());
        }

        private static T ResolveSampleValue<T>(HelloWorldNeo client, string valueId)
            where T : class
        {
            var resolved = NeoGeneratedTypesSupport.ResolveClassValue(
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
        private NeoClient LoadRawClient(string projectJson)
        {
            var store = Own(new NeoProjectStore(
                dataSource: new NeoJsonProjectDataSource(projectJson),
                localStore: new NeoInMemoryLocalSaveStore()));
            store.LoadAsync().GetAwaiter().GetResult();
            return Own(new NeoLoader().Load(store.Open("save")).GetAwaiter().GetResult());
        }

        private T Own<T>(T resource) where T : System.IDisposable
        {
            ownedResources.Add(resource);
            return resource;
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
