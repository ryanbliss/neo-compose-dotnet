// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.TestTools;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Tests
{
    public class NeoTileGridRendererTests
    {
        private const string TileTypeId = "tile-type";
        private const string ObjectTypeId = "object-type";
        private const string TileLayerLinkTypeId = "tile-layer-link-type";
        private const string TileInstanceTypeId = "tile-instance-type";
        private const string BaseTileTypeId = "base-tile-type";
        private const string SubTileTypeId = "sub-tile-type";
        private const string OtherTileTypeId = "other-tile-type";

        [Test]
        public void TileLayerLinkPayloadsResolveThroughCurrentObjectPosition()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyCustomFactory>
            {
                [TileTypeId] = (resolvedClient, node) => new TestTile(resolvedClient, node),
            };
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>());

            var tiles = primitive.GetTiles("background-layer", TileTypeId);
            var candidates = primitive.GetTileCandidates<TestTile>(
                "background-layer",
                new Vector2Int(9, 22),
                TileTypeId);

            Assert.AreEqual(1, tiles.Count);
            Assert.AreEqual(new Vector2Int(9, 22), tiles[0].Cell);
            Assert.AreEqual(NeoTileOutputSourceKind.TileLayerLink, tiles[0].SourceKind);
            Assert.AreEqual("shop-1:shop-floor-link:floor-local", tiles[0].InstanceId.Value);
            Assert.AreEqual("shop-1", tiles[0].SourceObjectInstanceId);
            Assert.AreEqual("shop-floor-link", tiles[0].SourceTileLayerLinkId);
            Assert.IsInstanceOf<TestTile>(tiles[0].Info);
            Assert.AreEqual(1, candidates.Count);
            Assert.AreEqual(NeoTileOutputSourceKind.TileLayerLink, candidates[0].SourceKind);
        }

        [Test]
        public void TileLayerLinkPayloadsStopResolvingWhenSourceTilesAreCleared()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            SeedWritableTileLayerLink(client);
            var readOnlyFactories = BuildReadOnlyFactories();
            var writableFactories = BuildWritableFactories();
            var source = (TestTileLayerLink)NeoGeneratedTypesSupport.ResolveCustomValue(
                client,
                "shop-floor-link",
                readOnlyFactories,
                writableFactories)!;
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                readOnlyFactories,
                writableFactories);

            Assert.AreEqual(1, primitive.GetTiles("background-layer", TileTypeId).Count);

            source.ClearTiles();

            Assert.AreEqual(0, primitive.GetTiles("background-layer", TileTypeId).Count);
        }

        [Test]
        public void Render_LiveSyncClearsProjectedTilesWhenSourceTilesAreCleared()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            SeedWritableTileLayerLink(client);
            var readOnlyFactories = BuildReadOnlyFactories();
            var writableFactories = BuildWritableFactories();
            var source = (TestTileLayerLink)NeoGeneratedTypesSupport.ResolveCustomValue(
                client,
                "shop-floor-link",
                readOnlyFactories,
                writableFactories)!;
            var tile = (TestTile)NeoGeneratedTypesSupport.ResolveCustomValue(
                client,
                "floor-tile",
                readOnlyFactories,
                writableFactories)!;
            var sprite = CreateTestSprite("live-linked");
            tile.Sprite = sprite;
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                readOnlyFactories,
                writableFactories);
            var layer = new ThrowingAfterInitialTileLayerRuntime(primitive);
            var content = new TestTileGridContent(primitive, new[] { layer });
            var go = new GameObject("NeoTileGridRenderer live source clear test");
            var changed = 0;

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(content);
                using var subscription = layer.OnChanged(_ => changed++);

                var tilemap = go.GetComponentInChildren<Tilemap>();
                Assert.IsNotNull(tilemap);
                Assert.IsTrue(renderer.IsLiveSynced);
                Assert.AreSame(content, renderer.CurrentContent);
                Assert.AreEqual(0, layer.GetTilesCalls);
                Assert.AreEqual(1, layer.GetRenderSnapshotCalls);
                Assert.IsNotNull(tilemap!.GetTile(new Vector3Int(9, 22, 0)));

                layer.ThrowOnGetTiles = true;
                layer.ThrowOnGetRenderSnapshot = true;
                layer.ThrowOnGetTile = true;
                source.ClearTiles();

                Assert.AreEqual(1, changed);
                Assert.IsNull(tilemap.GetTile(new Vector3Int(9, 22, 0)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(sprite.texture);
                UnityEngine.Object.DestroyImmediate(sprite);
            }
        }

        [UnityTest]
        public IEnumerator RenderAsync_LiveSyncFalseKeepsOneShotRenderedTiles()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            SeedWritableTileLayerLink(client);
            var readOnlyFactories = BuildReadOnlyFactories();
            var writableFactories = BuildWritableFactories();
            var source = (TestTileLayerLink)NeoGeneratedTypesSupport.ResolveCustomValue(
                client,
                "shop-floor-link",
                readOnlyFactories,
                writableFactories)!;
            var tile = (TestTile)NeoGeneratedTypesSupport.ResolveCustomValue(
                client,
                "floor-tile",
                readOnlyFactories,
                writableFactories)!;
            var sprite = CreateTestSprite("one-shot-linked");
            tile.Sprite = sprite;
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                readOnlyFactories,
                writableFactories);
            var layer = new TestGeneratedTileLayerRuntime(primitive);
            var content = new TestTileGridContent(primitive, new[] { layer });
            var go = new GameObject("NeoTileGridRenderer one-shot source clear test");
            Exception? error = null;
            var complete = false;

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                Render();

                for (int frame = 0; frame < 120 && !complete; frame += 1)
                {
                    yield return null;
                }

                if (error != null) throw error;
                Assert.IsTrue(complete, "Async tile grid render did not complete.");

                var tilemap = go.GetComponentInChildren<Tilemap>();
                Assert.IsNotNull(tilemap);
                Assert.IsFalse(renderer.IsLiveSynced);
                Assert.IsNotNull(tilemap!.GetTile(new Vector3Int(9, 22, 0)));

                source.ClearTiles();

                Assert.AreEqual(0, primitive.GetTiles("background-layer", TileTypeId).Count);
                Assert.IsNotNull(tilemap.GetTile(new Vector3Int(9, 22, 0)));

                async void Render()
                {
                    try
                    {
                        await renderer.RenderAsync(
                            content,
                            new NeoTileGridRenderOptions
                            {
                                LiveSync = false,
                                YieldBeforeRender = false,
                            });
                    }
                    catch (Exception exception)
                    {
                        error = exception;
                    }
                    finally
                    {
                        complete = true;
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(sprite.texture);
                UnityEngine.Object.DestroyImmediate(sprite);
            }
        }

        [Test]
        public void Render_AppliesAuthoredTileLayerSorting()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid");
            var go = new GameObject("NeoTileGridRenderer sorting test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    primitive,
                    new[]
                    {
                        new TestTileLayerRuntime(
                            "background-layer",
                            "Background",
                            TileTypeId,
                            "Default",
                            42),
                    });

                var tilemapRenderer = go.GetComponentInChildren<TilemapRenderer>();
                Assert.IsNotNull(tilemapRenderer);
                Assert.AreEqual("Default", tilemapRenderer!.sortingLayerName);
                Assert.AreEqual(42, tilemapRenderer.sortingOrder);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TryClearTile_UpdatesExistingTilemapWithoutRebuilding()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyCustomFactory>
            {
                [TileTypeId] = (resolvedClient, node) => new TestTile(resolvedClient, node),
            };
            var tile = (TestTile)NeoGeneratedTypesSupport.ResolveCustomValue(
                client,
                "floor-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>())!;
            var sprite = CreateTestSprite("clearable");
            tile.Sprite = sprite;
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>());
            var go = new GameObject("NeoTileGridRenderer incremental clear test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    primitive,
                    new[]
                    {
                        new TestTileLayerRuntime(
                            "background-layer",
                            "Background",
                            TileTypeId,
                            null,
                            null,
                            new[]
                            {
                                new NeoResolvedTileInstance(
                                    "tile-1",
                                    "background-layer",
                                    Vector2Int.zero,
                                    tile,
                                    0),
                            }),
                    });

                var tilemap = go.GetComponentInChildren<Tilemap>();
                Assert.IsNotNull(tilemap);
                Assert.IsNotNull(tilemap!.GetTile(Vector3Int.zero));
                var childCount = go.transform.childCount;

                Assert.IsTrue(renderer.TryClearTile("background-layer", Vector2Int.zero));

                Assert.AreEqual(childCount, go.transform.childCount);
                Assert.AreSame(tilemap, go.GetComponentInChildren<Tilemap>());
                Assert.IsNull(tilemap.GetTile(Vector3Int.zero));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(sprite.texture);
                UnityEngine.Object.DestroyImmediate(sprite);
            }
        }

        [UnityTest]
        public IEnumerator RenderAsync_RendersTileLayerOverFrames()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyCustomFactory>
            {
                [TileTypeId] = (resolvedClient, node) => new TestTile(resolvedClient, node),
            };
            var tile = (TestTile)NeoGeneratedTypesSupport.ResolveCustomValue(
                client,
                "floor-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>())!;
            var sprite = CreateTestSprite("async-render");
            tile.Sprite = sprite;
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>());
            var go = new GameObject("NeoTileGridRenderer async render test");
            Exception? error = null;
            var complete = false;

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                Render();

                for (int frame = 0; frame < 120 && !complete; frame += 1)
                {
                    yield return null;
                }

                if (error != null) throw error;
                Assert.IsTrue(complete, "Async tile grid render did not complete.");
                var tilemap = go.GetComponentInChildren<Tilemap>();
                Assert.IsNotNull(tilemap);
                Assert.IsNotNull(tilemap!.GetTile(Vector3Int.zero));

                async void Render()
                {
                    try
                    {
                        await renderer.RenderAsync(
                            primitive,
                            new[]
                            {
                                new TestTileLayerRuntime(
                                    "background-layer",
                                    "Background",
                                    TileTypeId,
                                    null,
                                    null,
                                    new[]
                                    {
                                        new NeoResolvedTileInstance(
                                            "tile-1",
                                            "background-layer",
                                            Vector2Int.zero,
                                            tile,
                                            0),
                                    }),
                            },
                            options: new NeoTileGridRenderOptions
                            {
                                MaxTilesPerFrame = 1,
                                YieldBeforeRender = true,
                            });
                    }
                    catch (Exception exception)
                    {
                        error = exception;
                    }
                    finally
                    {
                        complete = true;
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(sprite.texture);
                UnityEngine.Object.DestroyImmediate(sprite);
            }
        }

        [Test]
        public void RenderAsync_SupersedesInFlightRender_NewestWins()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid");
            var go = new GameObject("NeoTileGridRenderer restart test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                Exception? firstError = null;
                var firstCompleted = false;
                var secondCompleted = false;
                var restarted = false;
                // Kick off a second render from inside the first one — the
                // edit-mode equivalent of a caller restarting mid-flight.
                renderer.Lifecycle = new TileLayerCreatedCallbackLifecycle(() =>
                {
                    if (restarted) return;
                    restarted = true;
                    SecondRender();
                });

                FirstRender();

                Assert.IsTrue(restarted, "The first render never reached its tile layer.");
                Assert.IsTrue(secondCompleted, "The newest render must win.");
                Assert.IsFalse(firstCompleted, "The superseded render must not complete.");
                Assert.IsInstanceOf<OperationCanceledException>(firstError);

                async void FirstRender()
                {
                    try
                    {
                        await renderer.RenderAsync(primitive, new[] { EmptyBackgroundLayer() });
                        firstCompleted = true;
                    }
                    catch (Exception exception)
                    {
                        firstError = exception;
                    }
                }

                async void SecondRender()
                {
                    await renderer.RenderAsync(primitive, new[] { EmptyBackgroundLayer() });
                    secondCompleted = true;
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }

            static TestTileLayerRuntime EmptyBackgroundLayer() => new(
                "background-layer",
                "Background",
                TileTypeId,
                null,
                null);
        }

        [Test]
        public void Render_ShouldRenderObjectVetoSkipsMarkers_AndTryGetObjectRootFindsRendered()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyCustomFactory>
            {
                [ObjectTypeId] = (resolvedClient, node) =>
                    new TestComposedObject(resolvedClient, node),
            };
            var obj = (TestComposedObject)NeoGeneratedTypesSupport.ResolveCustomValue(
                client,
                "shop-object",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>())!;
            var sprite = CreateTestSprite("veto-test");
            obj.Sprite = sprite;
            var go = new GameObject("NeoTileGridRenderer veto test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Lifecycle = new VetoObjectLifecycle("object-2");
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[]
                    {
                        new TestObjectLayerRuntime(
                            "object-layer",
                            "Objects",
                            ObjectTypeId,
                            null,
                            12,
                            new[]
                            {
                                new NeoResolvedObjectInstance(
                                    "object-1",
                                    "object-layer",
                                    new Vector2Int(0, 0),
                                    new[] { new Vector2Int(0, 0) },
                                    obj,
                                    0),
                                new NeoResolvedObjectInstance(
                                    "object-2",
                                    "object-layer",
                                    new Vector2Int(1, 0),
                                    new[] { new Vector2Int(1, 0) },
                                    obj,
                                    1),
                            }),
                    });

                var layerRoot = go.transform.Find("Object Layer - Objects");
                Assert.IsNotNull(layerRoot);
                var renderedRoot = layerRoot!.Find("Object - object-1");
                Assert.IsNotNull(renderedRoot, "The non-vetoed object should render.");
                Assert.IsNull(
                    layerRoot.Find("Object - object-2"),
                    "ShouldRenderObject returning false must skip the instance.");

                Assert.IsTrue(renderer.TryGetObjectRoot("object-1", out var foundRoot));
                Assert.AreSame(renderedRoot!.gameObject, foundRoot);
                Assert.IsFalse(renderer.TryGetObjectRoot("object-2", out _));
                Assert.IsFalse(renderer.TryGetObjectRoot("missing-object", out _));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(sprite.texture);
                UnityEngine.Object.DestroyImmediate(sprite);
            }
        }

        private sealed class TileLayerCreatedCallbackLifecycle : NeoTileGridLifecycle
        {
            private readonly Action onTileLayerCreated;

            public TileLayerCreatedCallbackLifecycle(Action onTileLayerCreated)
            {
                this.onTileLayerCreated = onTileLayerCreated;
            }

            public override void OnTileLayerCreated(NeoTileLayerContext context)
            {
                onTileLayerCreated();
            }
        }

        private sealed class VetoObjectLifecycle : NeoTileGridLifecycle
        {
            private readonly string vetoedInstanceId;

            public VetoObjectLifecycle(string vetoedInstanceId)
            {
                this.vetoedInstanceId = vetoedInstanceId;
            }

            public override bool ShouldRenderObject(NeoObjectRenderContext context)
            {
                return context.Instance.InstanceId.Value != vetoedInstanceId;
            }
        }

        [Test]
        public void TileLayerLinkQueries_ProjectAuthoredTilesFromTheLinkOrigin()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyCustomFactory>
            {
                [TileTypeId] = (resolvedClient, node) => new TestTile(resolvedClient, node),
            };
            var tile = (TestTile)NeoGeneratedTypesSupport.ResolveCustomValue(
                client,
                "floor-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>())!;
            var link = new FakeTileLayerLinkValue
            {
                Position = new Vector3(10f, 20f, 0f),
                Tiles = new object[]
                {
                    new FakeTileInstanceValue { Cell = new Vector2Int(1, 2), Tile = tile },
                    new FakeTileInstanceValue { Cell = new Vector2Int(2, 2), Tile = tile },
                },
            };

            var tiles = link.GetTiles();
            Assert.AreEqual(2, tiles.Count);
            Assert.AreEqual(new Vector2Int(11, 22), tiles[0].Cell);
            Assert.AreEqual(new Vector2Int(12, 22), tiles[1].Cell);
            Assert.AreEqual(NeoTileOutputSourceKind.TileLayerLink, tiles[0].SourceKind);
            Assert.AreEqual("fake-tile-link", tiles[0].SourceTileLayerLinkId);
            Assert.AreEqual("fake-target-layer", tiles[0].LayerId);

            Assert.IsNotNull(link.GetTile(new Vector2Int(11, 22)));
            Assert.IsNull(link.GetTile(new Vector2Int(1, 2)), "local cells must be projected");
            Assert.IsNotNull(link.GetTile<TestTile>(new Vector2Int(11, 22)));

            // Pattern queries search projected cells, nearest first.
            Assert.IsNotNull(link.GetTile(new Vector2Int(13, 22), NeoCellPattern.Cross(1)));
            Assert.IsNull(link.GetTile(new Vector2Int(15, 22), NeoCellPattern.Cross(1)));
            Assert.AreEqual(2, link.GetTiles(new Vector2Int(11, 22), NeoCellPattern.Box(1)).Count);
        }

        [Test]
        public void ObjectLayerLinkQueries_ProjectAuthoredObjectsFromTheLinkOrigin()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyCustomFactory>
            {
                [ObjectTypeId] = (resolvedClient, node) =>
                    new TestComposedObject(resolvedClient, node),
            };
            var obj = (TestComposedObject)NeoGeneratedTypesSupport.ResolveCustomValue(
                client,
                "shop-object",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>())!;
            var link = new FakeObjectLayerLinkValue
            {
                Position = new Vector3(5f, 5f, 0f),
                Objects = new object[] { obj },
            };

            var objects = link.GetObjects();
            Assert.AreEqual(1, objects.Count);
            // TestComposedObject has no Position, so it projects at the link origin.
            Assert.AreEqual(new Vector2Int(5, 5), objects[0].Cell);
            Assert.AreEqual("fake-object-layer", objects[0].LayerId);
            Assert.AreSame(obj, objects[0].Info);

            Assert.IsNotNull(link.GetObject(new Vector2Int(5, 5)));
            Assert.IsNotNull(link.GetObject<TestComposedObject>(new Vector2Int(5, 5)));
            Assert.IsNull(link.GetObject(new Vector2Int(6, 5)));
            Assert.IsNotNull(link.GetObject(new Vector2Int(6, 5), NeoCellPattern.Cross(1)));
            Assert.IsNull(link.GetObject(new Vector2Int(8, 5), NeoCellPattern.Cross(1)));
        }

        private sealed class FakeLayerReference : INeoValueReference
        {
            public FakeLayerReference(string valueId)
            {
                this.valueId = valueId;
            }

            public string? valueId { get; }
        }

        private sealed class FakeTileLayerLinkValue : INeoTileLayerLinkValue
        {
            public string? valueId => "fake-tile-link";
            public Vector3 Position { get; set; }
            public INeoValueReference TileLayer { get; } = new FakeLayerReference("fake-target-layer");
            public IReadOnlyList<object> Tiles { get; set; } = new List<object>();
        }

        private sealed class FakeTileInstanceValue
        {
            public Vector2Int Cell { get; set; }
            public NeoGeneratedCustomValue? Tile { get; set; }
        }

        private sealed class FakeObjectLayerLinkValue : INeoObjectLayerLinkValue
        {
            public string? valueId => "fake-object-link";
            public Vector3 Position { get; set; }
            public INeoValueReference ObjectLayer { get; } = new FakeLayerReference("fake-object-layer");
            public IReadOnlyList<object> Objects { get; set; } = new List<object>();
        }

        [Test]
        public void AssetDatabase_ResolvesGeneratedTileAssets()
        {
            var database = ScriptableObject.CreateInstance<NeoAssetDatabase>();
            var tile = ScriptableObject.CreateInstance<Tile>();
            try
            {
                database.SetTileAsset(
                    "floor-tile",
                    TileTypeId,
                    "Assets/Neo/Generated/Tiles/floor-tile.asset",
                    "hash-1",
                    tile);

                Assert.AreSame(tile, database.TryGetTileBase("floor-tile"));
                var entry = database.TryGetTileEntry("floor-tile");
                Assert.IsNotNull(entry);
                Assert.AreEqual(TileTypeId, entry!.TileTypeId);
                Assert.AreEqual("hash-1", entry.ContentHash);

                var missing = database.FindMissingTileAssets(new HashSet<string>());
                Assert.AreEqual(1, missing.Length);

                database.RemoveTileAsset("floor-tile");
                Assert.IsNull(database.TryGetTileBase("floor-tile"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tile);
                UnityEngine.Object.DestroyImmediate(database);
            }
        }

        [Test]
        public void Render_PrefersEditorGeneratedTileAssetFromAssetDatabase()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyCustomFactory>
            {
                [TileTypeId] = (resolvedClient, node) => new TestTile(resolvedClient, node),
            };
            var tile = (TestTile)NeoGeneratedTypesSupport.ResolveCustomValue(
                client,
                "floor-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>())!;
            tile.Sprite = CreateTestSprite("fallback");
            var generatedTile = ScriptableObject.CreateInstance<Tile>();
            generatedTile.sprite = CreateTestSprite("editor-generated");
            var database = ScriptableObject.CreateInstance<NeoAssetDatabase>();
            database.SetTileAsset(
                "floor-tile",
                TileTypeId,
                "Assets/Neo/Generated/Tiles/floor-tile.asset",
                "hash-1",
                generatedTile);
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>());
            var go = new GameObject("NeoTileGridRenderer generated asset test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.AssetDatabase = database;
                renderer.Render(
                    primitive,
                    new[]
                    {
                        new TestTileLayerRuntime(
                            "background-layer",
                            "Background",
                            TileTypeId,
                            null,
                            null,
                            new[]
                            {
                                new NeoResolvedTileInstance(
                                    "tile-1",
                                    "background-layer",
                                    Vector2Int.zero,
                                    tile,
                                    0),
                            }),
                    });

                var tilemap = go.GetComponentInChildren<Tilemap>();
                Assert.IsNotNull(tilemap);
                Assert.AreSame(generatedTile, tilemap!.GetTile(Vector3Int.zero));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(generatedTile.sprite.texture);
                UnityEngine.Object.DestroyImmediate(generatedTile.sprite);
                UnityEngine.Object.DestroyImmediate(generatedTile);
                UnityEngine.Object.DestroyImmediate(database);
            }
        }

        [Test]
        public void Render_UsesSmartTileRuleTileWhenGeneratedTileExposesSmartTile()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyCustomFactory>
            {
                [TileTypeId] = (resolvedClient, node) => new TestTile(resolvedClient, node),
            };
            var tile = (TestTile)NeoGeneratedTypesSupport.ResolveCustomValue(
                client,
                "floor-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>())!;
            var fallbackSprite = CreateTestSprite("fallback");
            tile.Sprite = fallbackSprite;
            var smartTile = new TestSmartTile
            {
                DefaultCollider = NeoSmartTileOptionIds.ColliderNone,
            };
            smartTile.Rules.Add(new TestSmartTileRule
            {
                Collider = NeoSmartTileOptionIds.ColliderSprite,
                Output = NeoSmartTileOptionIds.OutputSingle,
            });
            tile.SmartTile = smartTile;

            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>());
            var go = new GameObject("NeoTileGridRenderer smart tile test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(
                    primitive,
                    new[]
                    {
                        new TestTileLayerRuntime(
                            "background-layer",
                            "Background",
                            TileTypeId,
                            null,
                            null,
                            new[]
                            {
                                new NeoResolvedTileInstance(
                                    "smart-tile-1",
                                    "background-layer",
                                    Vector2Int.zero,
                                    tile,
                                    0),
                            }),
                    });

                var tilemap = go.GetComponentInChildren<Tilemap>();
                Assert.IsNotNull(tilemap);
                var renderedTile = tilemap!.GetTile(Vector3Int.zero);
                Assert.IsInstanceOf<NeoRuleTile>(renderedTile);
                var ruleTile = (NeoRuleTile)renderedTile;
                Assert.AreSame(fallbackSprite, ruleTile.m_DefaultSprite);
                Assert.AreEqual(Tile.ColliderType.None, ruleTile.m_DefaultColliderType);
                Assert.AreEqual(1, ruleTile.m_TilingRules.Count);
                Assert.AreEqual(
                    Tile.ColliderType.Sprite,
                    ruleTile.m_TilingRules[0].m_ColliderType);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(fallbackSprite.texture);
                UnityEngine.Object.DestroyImmediate(fallbackSprite);
            }
        }

        [Test]
        public void Render_PaintingNeighborRefreshesSmartTileAndMatchesSubtype()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var factories = BuildInheritanceTileFactories();
            var smartTileValue = (TestTile)NeoGeneratedTypesSupport.ResolveCustomValue(
                client,
                "floor-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>())!;
            var neighborValue = (TestTile)NeoGeneratedTypesSupport.ResolveCustomValue(
                client,
                "sub-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>())!;
            var defaultSprite = CreateTestSprite("smart-default");
            var connectedSprite = CreateTestSprite("smart-connected");
            var neighborSprite = CreateTestSprite("subtype-neighbor");
            smartTileValue.Sprite = defaultSprite;
            neighborValue.Sprite = neighborSprite;
            smartTileValue.SmartTile = SmartTileWithInheritsNeighbor(
                connectedSprite,
                "base-tile");

            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>());
            var layer = new MutableTestTileLayerRuntime(
                "background-layer",
                "Background",
                TileTypeId);
            layer.SetTile(new NeoResolvedTileInstance(
                "smart-origin",
                "background-layer",
                Vector2Int.zero,
                smartTileValue,
                0));
            var content = new TestTileGridContent(primitive, new[] { layer });
            var go = new GameObject("NeoTileGridRenderer smart neighbor refresh test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(content);

                var tilemap = go.GetComponentInChildren<Tilemap>();
                Assert.IsNotNull(tilemap);
                Assert.IsInstanceOf<NeoRuleTile>(tilemap!.GetTile(Vector3Int.zero));
                Assert.AreSame(defaultSprite, tilemap.GetSprite(Vector3Int.zero));

                layer.SetTile(new NeoResolvedTileInstance(
                    "subtype-neighbor",
                    "background-layer",
                    new Vector2Int(1, 0),
                    neighborValue,
                    1));
                primitive.NotifyTileLayerChanged(
                    "background-layer",
                    Array.Empty<Vector2Int>(),
                    new[] { new Vector2Int(1, 0) },
                    NeoTileGridChangeSourceKind.Direct,
                    null);

                Assert.AreSame(neighborSprite, tilemap.GetSprite(new Vector3Int(1, 0, 0)));
                Assert.AreSame(connectedSprite, tilemap.GetSprite(Vector3Int.zero));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                DestroyTestSprite(defaultSprite);
                DestroyTestSprite(connectedSprite);
                DestroyTestSprite(neighborSprite);
            }
        }

        [Test]
        public void Render_InheritsFromTypeRuleRejectsUnrelatedNeighbor()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var factories = BuildInheritanceTileFactories();
            var smartTileValue = (TestTile)NeoGeneratedTypesSupport.ResolveCustomValue(
                client,
                "floor-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>())!;
            var neighborValue = (TestTile)NeoGeneratedTypesSupport.ResolveCustomValue(
                client,
                "other-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>())!;
            var defaultSprite = CreateTestSprite("smart-default");
            var connectedSprite = CreateTestSprite("smart-connected");
            var neighborSprite = CreateTestSprite("unrelated-neighbor");
            smartTileValue.Sprite = defaultSprite;
            neighborValue.Sprite = neighborSprite;
            smartTileValue.SmartTile = SmartTileWithInheritsNeighbor(
                connectedSprite,
                "base-tile");

            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>());
            var layer = new MutableTestTileLayerRuntime(
                "background-layer",
                "Background",
                TileTypeId);
            layer.SetTile(new NeoResolvedTileInstance(
                "smart-origin",
                "background-layer",
                Vector2Int.zero,
                smartTileValue,
                0));
            var content = new TestTileGridContent(primitive, new[] { layer });
            var go = new GameObject("NeoTileGridRenderer smart unrelated neighbor test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.Render(content);

                var tilemap = go.GetComponentInChildren<Tilemap>();
                Assert.IsNotNull(tilemap);

                layer.SetTile(new NeoResolvedTileInstance(
                    "unrelated-neighbor",
                    "background-layer",
                    new Vector2Int(1, 0),
                    neighborValue,
                    1));
                primitive.NotifyTileLayerChanged(
                    "background-layer",
                    Array.Empty<Vector2Int>(),
                    new[] { new Vector2Int(1, 0) },
                    NeoTileGridChangeSourceKind.Direct,
                    null);

                Assert.AreSame(neighborSprite, tilemap!.GetSprite(new Vector3Int(1, 0, 0)));
                Assert.AreSame(defaultSprite, tilemap.GetSprite(Vector3Int.zero));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                DestroyTestSprite(defaultSprite);
                DestroyTestSprite(connectedSprite);
                DestroyTestSprite(neighborSprite);
            }
        }

        [Test]
        public void Render_RendersObjectCompositionChildrenInsteadOfParentSprite()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyCustomFactory>
            {
                [ObjectTypeId] = (resolvedClient, node) =>
                    new TestComposedObject(resolvedClient, node),
                [TileTypeId] = (resolvedClient, node) => new TestTile(resolvedClient, node),
            };
            var parentSprite = CreateTestSprite("legacy-parent");
            var childSprite = CreateTestSprite("child-object");
            var tileSprite = CreateTestSprite("child-tile");
            var obj = (TestComposedObject)NeoGeneratedTypesSupport.ResolveCustomValue(
                client,
                "shop-object",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>())!;
            var tile = (TestTile)NeoGeneratedTypesSupport.ResolveCustomValue(
                client,
                "floor-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>())!;
            tile.Sprite = tileSprite;
            obj.Sprite = parentSprite;
            obj.Children = new object[]
            {
                new TestSpriteChild
                {
                    Name = "Sprite Child",
                    Sprite = childSprite,
                    Position = new Vector3(0f, 0f, 0f),
                    Size = new Vector3(2f, 1f, 0f),
                },
                new TestTileLayerLinkChild
                {
                    Name = "Tile Link",
                    Tiles = new object[]
                    {
                        new TestTileInstanceChild
                        {
                            Cell = new Vector2Int(1, 0),
                            Tile = tile,
                        },
                    },
                },
            };
            var go = new GameObject("NeoTileGridRenderer object composition test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.CellSize = 2f;
                renderer.Render(
                    NeoReadOnlyTileGridPrimitive.Resolve(client, "town-grid"),
                    new List<ReadOnlyNeoTileLayerRuntime>(),
                    new[]
                    {
                        new TestObjectLayerRuntime(
                            "object-layer",
                            "Objects",
                            ObjectTypeId,
                            null,
                            12,
                            new[]
                            {
                                new NeoResolvedObjectInstance(
                                    "object-1",
                                    "object-layer",
                                    new Vector2Int(3, 4),
                                    new[] { new Vector2Int(3, 4) },
                                    obj,
                                    1),
                            }),
                    });

                var objectRoot = go.transform
                    .Find("Object Layer - Objects")
                    ?.Find("Object - object-1");
                Assert.IsNotNull(objectRoot);
                Assert.AreEqual(new Vector3(6f, 8f, 0f), objectRoot!.localPosition);

                var spriteRenderers = objectRoot.GetComponentsInChildren<SpriteRenderer>();
                Assert.AreEqual(2, spriteRenderers.Length);
                Assert.IsFalse(System.Array.Exists(
                    spriteRenderers,
                    spriteRenderer => spriteRenderer.sprite == parentSprite));

                var spriteChild = objectRoot.Find("Sprite Child");
                Assert.IsNotNull(spriteChild);
                var spriteChildRenderer = spriteChild!.GetComponent<SpriteRenderer>();
                Assert.AreEqual(new Vector3(2f, 1f, 0f), spriteChild.localPosition);
                Assert.AreSame(childSprite, spriteChildRenderer.sprite);
                Assert.AreEqual(4f, spriteChildRenderer.bounds.size.x, 0.0001f);
                Assert.AreEqual(2f, spriteChildRenderer.bounds.size.y, 0.0001f);

                var tileChild = objectRoot.Find("child-tile");
                Assert.IsNotNull(tileChild);
                var tileChildRenderer = tileChild!.GetComponent<SpriteRenderer>();
                Assert.AreEqual(new Vector3(3f, 1f, 0f), tileChild.localPosition);
                Assert.AreSame(tileSprite, tileChildRenderer.sprite);
                Assert.AreEqual(2f, tileChildRenderer.bounds.size.x, 0.0001f);
                Assert.AreEqual(2f, tileChildRenderer.bounds.size.y, 0.0001f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(parentSprite.texture);
                UnityEngine.Object.DestroyImmediate(parentSprite);
                UnityEngine.Object.DestroyImmediate(childSprite.texture);
                UnityEngine.Object.DestroyImmediate(childSprite);
                UnityEngine.Object.DestroyImmediate(tileSprite.texture);
                UnityEngine.Object.DestroyImmediate(tileSprite);
            }
        }

        [Test]
        public void TryResolveObjectColliderSpec_ReadsVectorColliderFields()
        {
            var source = new ObjectWithVectorCollider
            {
                Collider = new VectorCollider
                {
                    Size = new Vector2(2.5f, 3.5f),
                    Offset = new Vector2(0.25f, -0.5f),
                    IsTrigger = true,
                },
            };

            Assert.IsTrue(NeoTileGridRenderer.TryResolveObjectColliderSpec(source, out var spec));
            Assert.AreEqual(2.5f, spec.Size.x);
            Assert.AreEqual(3.5f, spec.Size.y);
            Assert.AreEqual(0.25f, spec.Offset.x);
            Assert.AreEqual(-0.5f, spec.Offset.y);
            Assert.IsTrue(spec.IsTrigger);
        }

        [Test]
        public void TryResolveObjectColliderSpec_ReadsScalarColliderFields()
        {
            var source = new ObjectWithScalarCollider
            {
                BoxCollider = new ScalarCollider
                {
                    Width = 4,
                    Height = 5.25,
                    OffsetX = -1,
                    OffsetY = 1.5f,
                    isTrigger = false,
                },
            };

            Assert.IsTrue(NeoTileGridRenderer.TryResolveObjectColliderSpec(source, out var spec));
            Assert.AreEqual(4f, spec.Size.x);
            Assert.AreEqual(5.25f, spec.Size.y);
            Assert.AreEqual(-1f, spec.Offset.x);
            Assert.AreEqual(1.5f, spec.Offset.y);
            Assert.IsFalse(spec.IsTrigger);
        }

        [Test]
        public void TryResolveObjectColliderSpec_RejectsColliderWithoutSize()
        {
            var source = new ObjectWithVectorCollider
            {
                Collider = new VectorCollider
                {
                    Offset = Vector2.one,
                },
            };

            Assert.IsFalse(NeoTileGridRenderer.TryResolveObjectColliderSpec(source, out _));
        }

        private sealed class ObjectWithVectorCollider
        {
            public VectorCollider? Collider { get; set; }
        }

        private sealed class ObjectWithScalarCollider
        {
            public ScalarCollider? BoxCollider { get; set; }
        }

        private sealed class VectorCollider
        {
            public Vector2 Size { get; set; }
            public Vector2 Offset { get; set; }
            public bool IsTrigger { get; set; }
        }

        private sealed class ScalarCollider
        {
            public int Width { get; set; }
            public double Height { get; set; }
            public long OffsetX { get; set; }
            public float OffsetY { get; set; }
            public bool isTrigger { get; set; }
        }

        private sealed class TestTile : NeoGeneratedCustomValue, INeoSmartTileSource
        {
            public TestTile(NeoClient client, NeoAttributeCustom node)
                : base(client, node, TileTypeId)
            {
            }

            public Sprite? Sprite { get; set; }
            public INeoSmartTile? SmartTile { get; set; }
        }

        private sealed class TestSmartTile : INeoSmartTile
        {
            public string DefaultCollider { get; set; } =
                NeoSmartTileOptionIds.ColliderSprite;

            public List<INeoSmartTileRule> Rules { get; } = new();

            IReadOnlyList<INeoSmartTileRule> INeoSmartTile.Rules => Rules;
        }

        private sealed class TestSmartTileRule : INeoSmartTileRule
        {
            public List<INeoSmartTileNeighbor> Neighbors { get; } = new();

            public List<Sprite> Sprites { get; } = new();

            public string Output { get; set; } = NeoSmartTileOptionIds.OutputSingle;

            public string Collider { get; set; } = NeoSmartTileOptionIds.ColliderSprite;

            public string RuleTransform { get; set; } =
                NeoSmartTileOptionIds.TransformFixed;

            public double MinAnimationSpeed { get; set; } = 1d;

            public double MaxAnimationSpeed { get; set; } = 1d;

            IReadOnlyList<INeoSmartTileNeighbor> INeoSmartTileRule.Neighbors => Neighbors;

            IReadOnlyList<Sprite> INeoSmartTileRule.Sprites => Sprites;
        }

        private sealed class TestSmartTileNeighbor : INeoSmartTileNeighbor
        {
            public Vector2Int Cell { get; set; }

            public string Condition { get; set; } = NeoSmartTileOptionIds.ConditionThis;

            public string? TileValueId { get; set; }
        }

        private sealed class MutableTestTileLayerRuntime : ReadOnlyNeoTileLayerRuntime
        {
            private readonly Dictionary<Vector2Int, NeoResolvedTileInstance> tilesByCell =
                new();

            public MutableTestTileLayerRuntime(
                string layerId,
                string displayName,
                string expectedTypeId)
                : base(layerId, displayName, expectedTypeId, null, null)
            {
            }

            public void SetTile(NeoResolvedTileInstance tile)
            {
                tilesByCell[tile.Cell] = tile;
            }

            public override IReadOnlyList<NeoResolvedTileInstance> GetTiles() =>
                new List<NeoResolvedTileInstance>(tilesByCell.Values);

            public override NeoResolvedTileInstance? GetTile(Vector2Int cell) =>
                tilesByCell.TryGetValue(cell, out var tile) ? tile : null;
        }

        private sealed class TestTileLayerLink : NeoGeneratedCustomValue
        {
            private NeoList<string>? tiles;

            public TestTileLayerLink(
                NeoClient client,
                NeoAttributeCustom node,
                bool isReadOnly = true)
                : base(client, node, TileLayerLinkTypeId, isReadOnly)
            {
            }

            public NeoList<string> Tiles =>
                tiles ??= new NeoList<string>(
                    client,
                    writableNode.Get<NeoAttributeListWritable>("Tiles"),
                    () => writableNode.GetOrCreateCollection<NeoAttributeListWritable>("Tiles"),
                    (_, __) => "",
                    item => NeoGeneratedTypesSupport.Value(item),
                    () => ThrowIfReadOnly("TestTileLayerLink.Tiles"),
                    () => IsReadOnly);

            public void ClearTiles()
            {
                Tiles.Clear();
            }
        }

        private sealed class TestTileLayerRuntime : ReadOnlyNeoTileLayerRuntime
        {
            private readonly IReadOnlyList<NeoResolvedTileInstance> tiles;

            public TestTileLayerRuntime(
                string layerId,
                string displayName,
                string expectedTypeId,
                string? sortingLayerName,
                int? sortingOrder,
                IReadOnlyList<NeoResolvedTileInstance>? tiles = null)
                : base(
                    layerId,
                    displayName,
                    expectedTypeId,
                    sortingLayerName,
                    sortingOrder)
            {
                this.tiles = tiles ?? new List<NeoResolvedTileInstance>();
            }

            public override IReadOnlyList<NeoResolvedTileInstance> GetTiles() => tiles;
        }

        private sealed class TestObjectLayerRuntime : ReadOnlyNeoObjectLayerRuntime
        {
            private readonly IReadOnlyList<NeoResolvedObjectInstance> objects;

            public TestObjectLayerRuntime(
                string layerId,
                string displayName,
                string expectedTypeId,
                string? sortingLayerName,
                int? sortingOrder,
                IReadOnlyList<NeoResolvedObjectInstance>? objects = null)
                : base(
                    layerId,
                    displayName,
                    expectedTypeId,
                    sortingLayerName,
                    sortingOrder)
            {
                this.objects = objects ?? new List<NeoResolvedObjectInstance>();
            }

            public override IReadOnlyList<NeoResolvedObjectInstance> GetObjects() =>
                objects;
        }

        private class TestGeneratedTileLayerRuntime
            : ReadOnlyNeoGeneratedTileLayer<TestTile>
        {
            public TestGeneratedTileLayerRuntime(NeoReadOnlyTileGridPrimitive primitive)
                : base(
                    primitive,
                    "background-layer",
                    "Background",
                    TileTypeId,
                    null,
                    null)
            {
            }
        }

        private sealed class ThrowingAfterInitialTileLayerRuntime
            : TestGeneratedTileLayerRuntime
        {
            public ThrowingAfterInitialTileLayerRuntime(
                NeoReadOnlyTileGridPrimitive primitive)
                : base(primitive)
            {
            }

            public int GetTilesCalls { get; private set; }
            public int GetRenderSnapshotCalls { get; private set; }
            public bool ThrowOnGetTiles { get; set; }
            public bool ThrowOnGetRenderSnapshot { get; set; }
            public bool ThrowOnGetTile { get; set; }

            internal override NeoTileLayerRenderSnapshot GetRenderSnapshot()
            {
                GetRenderSnapshotCalls += 1;
                if (ThrowOnGetRenderSnapshot)
                {
                    throw new InvalidOperationException(
                        "Live sync must not rebuild the tile layer snapshot.");
                }
                return base.GetRenderSnapshot();
            }

            public override IReadOnlyList<NeoResolvedTileInstance> GetTiles()
            {
                GetTilesCalls += 1;
                if (ThrowOnGetTiles)
                {
                    throw new InvalidOperationException(
                        "Live sync must not reconcile the whole tile layer.");
                }
                return base.GetTiles();
            }

            public override NeoResolvedTileInstance? GetTile(Vector2Int cell)
            {
                if (ThrowOnGetTile)
                {
                    throw new InvalidOperationException(
                        "Live sync should use cached source deltas for simple clears.");
                }
                return base.GetTile(cell);
            }
        }

        private sealed class TestTileGridContent : INeoTileGridContent
        {
            public TestTileGridContent(
                NeoReadOnlyTileGridPrimitive primitive,
                IReadOnlyList<ReadOnlyNeoTileLayerRuntime> tileLayers)
            {
                Primitive = primitive;
                TileLayersInOrder = tileLayers;
                ObjectLayersInOrder = Array.Empty<ReadOnlyNeoObjectLayerRuntime>();
            }

            public NeoReadOnlyTileGridPrimitive Primitive { get; }
            public IReadOnlyList<ReadOnlyNeoTileLayerRuntime> TileLayersInOrder { get; }
            public IReadOnlyList<ReadOnlyNeoObjectLayerRuntime> ObjectLayersInOrder { get; }
            public NeoTileGridRenderer? Renderer => Primitive.Renderer;
            public IDisposable OnChanged(Action<NeoTileGridChangedArgs> handler) =>
                Primitive.OnChanged(handler);
        }

        private sealed class TestComposedObject : NeoGeneratedCustomValue
        {
            public TestComposedObject(NeoClient client, NeoAttributeCustom node)
                : base(client, node, ObjectTypeId)
            {
            }

            public Sprite? Sprite { get; set; }
            public IReadOnlyList<object> Children { get; set; } = new List<object>();
        }

        private sealed class TestSpriteChild
        {
            public string Name { get; set; } = "";
            public Sprite? Sprite { get; set; }
            public Vector3 Position { get; set; }
            public Vector3 Size { get; set; } = Vector3.one;
        }

        private sealed class TestTileLayerLinkChild
        {
            public string Name { get; set; } = "";
            public IReadOnlyList<object> Tiles { get; set; } = new List<object>();
        }

        private sealed class TestTileInstanceChild
        {
            public Vector2Int Cell { get; set; }
            public NeoGeneratedCustomValue? Tile { get; set; }
        }

        private static Sprite CreateTestSprite(string name)
        {
            var texture = new Texture2D(1, 1);
            var sprite = Sprite.Create(
                texture,
                new Rect(0, 0, 1, 1),
                new Vector2(0.5f, 0.5f));
            sprite.name = name;
            return sprite;
        }

        private static void DestroyTestSprite(Sprite sprite)
        {
            UnityEngine.Object.DestroyImmediate(sprite.texture);
            UnityEngine.Object.DestroyImmediate(sprite);
        }

        private static TestSmartTile SmartTileWithInheritsNeighbor(
            Sprite connectedSprite,
            string tileValueId)
        {
            var rule = new TestSmartTileRule();
            rule.Sprites.Add(connectedSprite);
            rule.Neighbors.Add(new TestSmartTileNeighbor
            {
                Cell = new Vector2Int(1, 0),
                Condition = NeoSmartTileOptionIds.ConditionInheritsFromType,
                TileValueId = tileValueId,
            });
            var smartTile = new TestSmartTile();
            smartTile.Rules.Add(rule);
            return smartTile;
        }

        private static Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyCustomFactory>
            BuildInheritanceTileFactories()
        {
            NeoGeneratedTypesSupport.ReadOnlyCustomFactory factory =
                (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedCustomValue(
                        resolvedClient,
                        node,
                        () => new TestTile(resolvedClient, node));
            return new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyCustomFactory>
            {
                [TileTypeId] = factory,
                [BaseTileTypeId] = factory,
                [SubTileTypeId] = factory,
                [OtherTileTypeId] = factory,
            };
        }

        private static Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyCustomFactory>
            BuildReadOnlyFactories()
        {
            return new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyCustomFactory>
            {
                [TileTypeId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedCustomValue(
                        resolvedClient,
                        node,
                        () => new TestTile(resolvedClient, node)),
                [TileLayerLinkTypeId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedCustomValue(
                        resolvedClient,
                        node,
                        () => new TestTileLayerLink(resolvedClient, node)),
            };
        }

        private static Dictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>
            BuildWritableFactories()
        {
            return new Dictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>
            {
                [TileTypeId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedCustomValue(
                        resolvedClient,
                        node,
                        () => new TestTile(resolvedClient, node)),
                [TileLayerLinkTypeId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedCustomValue(
                        resolvedClient,
                        node,
                        () => new TestTileLayerLink(
                            resolvedClient,
                            node,
                            isReadOnly: false)),
            };
        }

        private static void SeedWritableTileLayerLink(NeoClient client)
        {
            client.AddSaveValue(
                "shop-floor-link",
                new ObjectAttributeValue
                {
                    id = "shop-floor-link",
                    typeId = TileLayerLinkTypeId,
                    value = new Dictionary<string, string>
                    {
                        ["TileLayer"] = "shop-floor-link-layer",
                        ["Tiles"] = "shop-floor-link-tiles",
                    },
                });
            client.AddSaveValue(
                "shop-floor-link-tiles",
                new ArrayAttributeValue
                {
                    id = "shop-floor-link-tiles",
                    value = new[] { "floor-local" },
                });
        }

        private const string GridTypeId = "grid-type";
        private const string ObjectLayerLinkTypeId = "object-layer-link-type";

        /// <summary>
        /// Values-native world fixture: the grid value's "Children" ordered
        /// list carries a (empty) direct tile layer link plus an object layer
        /// link whose placed object "shop-1" (at 10,20) carries a
        /// TileLayerLink child "shop-floor-link" projecting "floor-local"
        /// (local cell -1,2 -> grid cell 9,22) into "background-layer".
        /// </summary>
        private static ProjectData BuildTileGridProjectData()
        {
            var rootType = new CustomType
            {
                id = "root-type",
                projectId = "project-a",
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            var gridType = new CustomType
            {
                id = GridTypeId,
                projectId = "project-a",
                name = "Grid",
                schema = new Dictionary<string, string>
                {
                    ["Children"] = "grid-children-attribute",
                },
            };
            var tileType = new CustomType
            {
                id = TileTypeId,
                projectId = "project-a",
                name = "Tile",
                schema = new Dictionary<string, string>(),
            };
            var objectType = new CustomType
            {
                id = ObjectTypeId,
                projectId = "project-a",
                name = "Object",
                schema = new Dictionary<string, string>
                {
                    ["Position"] = "object-position-attribute",
                    ["Children"] = "object-children-attribute",
                },
            };
            var tileInstanceType = new CustomType
            {
                id = TileInstanceTypeId,
                projectId = "project-a",
                name = "Tile Instance",
                schema = new Dictionary<string, string>
                {
                    ["Cell"] = "tile-instance-cell-attribute",
                    ["Tile"] = "tile-instance-tile-attribute",
                },
            };
            var tileLayerLinkType = new CustomType
            {
                id = TileLayerLinkTypeId,
                projectId = "project-a",
                name = "Tile Layer Link",
                schema = new Dictionary<string, string>
                {
                    ["TileLayer"] = "tile-layer-link-layer-attribute",
                    ["Tiles"] = "tile-layer-link-tiles-attribute",
                },
            };
            var objectLayerLinkType = new CustomType
            {
                id = ObjectLayerLinkTypeId,
                projectId = "project-a",
                name = "Object Layer Link",
                schema = new Dictionary<string, string>
                {
                    ["ObjectLayer"] = "object-layer-link-layer-attribute",
                    ["Objects"] = "object-layer-link-objects-attribute",
                },
            };
            var baseTileType = new CustomType
            {
                id = BaseTileTypeId,
                projectId = "project-a",
                name = "Base Tile",
                schema = new Dictionary<string, string>(),
            };
            var subTileType = new CustomType
            {
                id = SubTileTypeId,
                projectId = "project-a",
                name = "Sub Tile",
                extendsTypeId = BaseTileTypeId,
                schema = new Dictionary<string, string>(),
            };
            var otherTileType = new CustomType
            {
                id = OtherTileTypeId,
                projectId = "project-a",
                name = "Other Tile",
                schema = new Dictionary<string, string>(),
            };
            return new ProjectData
            {
                project = new Project
                {
                    id = "project-a",
                    _id = "project-a",
                    name = "World Test",
                    rootAssetsAttributeId = "root-assets",
                    rootSaveFileAttributeId = "root-save",
                    rootSessionAttributeId = "root-session",
                },
                attributes = new Dictionary<string, NeoCompose.Runtime.Json.Attribute>
                {
                    ["root-assets"] = RootAttribute("root-assets", "root-assets-value", rootType.id),
                    ["root-save"] = RootAttribute("root-save", "root-save-value", rootType.id),
                    ["root-session"] = RootAttribute("root-session", "root-session-value", rootType.id),
                    ["grid-children-attribute"] = new ListAttribute
                    {
                        id = "grid-children-attribute",
                        projectId = "project-a",
                        name = "Children",
                        type = AttributeType.List,
                        entryAttributeId = "grid-child-entry-attribute",
                    },
                    ["grid-child-entry-attribute"] = new CustomAttribute
                    {
                        id = "grid-child-entry-attribute",
                        projectId = "project-a",
                        name = "Child",
                        type = AttributeType.Custom,
                        customTypeId = TileLayerLinkTypeId,
                    },
                    ["object-position-attribute"] = new Vector3Attribute
                    {
                        id = "object-position-attribute",
                        projectId = "project-a",
                        name = "Position",
                        type = AttributeType.Vector3,
                    },
                    ["object-children-attribute"] = new ListAttribute
                    {
                        id = "object-children-attribute",
                        projectId = "project-a",
                        name = "Children",
                        type = AttributeType.List,
                        entryAttributeId = "grid-child-entry-attribute",
                    },
                    ["tile-instance-cell-attribute"] = new Vector2IntAttribute
                    {
                        id = "tile-instance-cell-attribute",
                        projectId = "project-a",
                        name = "Cell",
                        type = AttributeType.Vector2Int,
                    },
                    ["tile-instance-tile-attribute"] = new LookupAttribute
                    {
                        id = "tile-instance-tile-attribute",
                        projectId = "project-a",
                        name = "Tile",
                        type = AttributeType.Lookup,
                        collectionAttributeId = "tile-layer-link-tiles-attribute",
                    },
                    ["tile-layer-link-layer-attribute"] = new LookupAttribute
                    {
                        id = "tile-layer-link-layer-attribute",
                        projectId = "project-a",
                        name = "TileLayer",
                        type = AttributeType.Lookup,
                        collectionAttributeId = "grid-children-attribute",
                    },
                    ["tile-layer-link-tiles-attribute"] = new ListAttribute
                    {
                        id = "tile-layer-link-tiles-attribute",
                        projectId = "project-a",
                        name = "Tiles",
                        type = AttributeType.List,
                        entryAttributeId = "tile-layer-link-tile-entry-attribute",
                        storage = "save",
                    },
                    ["tile-layer-link-tile-entry-attribute"] = new CustomAttribute
                    {
                        id = "tile-layer-link-tile-entry-attribute",
                        projectId = "project-a",
                        name = "Tile",
                        type = AttributeType.Custom,
                        customTypeId = TileInstanceTypeId,
                    },
                    ["object-layer-link-layer-attribute"] = new LookupAttribute
                    {
                        id = "object-layer-link-layer-attribute",
                        projectId = "project-a",
                        name = "ObjectLayer",
                        type = AttributeType.Lookup,
                        collectionAttributeId = "grid-children-attribute",
                    },
                    ["object-layer-link-objects-attribute"] = new ListAttribute
                    {
                        id = "object-layer-link-objects-attribute",
                        projectId = "project-a",
                        name = "Objects",
                        type = AttributeType.List,
                        entryAttributeId = "object-layer-link-object-entry-attribute",
                        listKind = NeoListKinds.Unordered,
                    },
                    ["object-layer-link-object-entry-attribute"] = new CustomAttribute
                    {
                        id = "object-layer-link-object-entry-attribute",
                        projectId = "project-a",
                        name = "Object",
                        type = AttributeType.Custom,
                        customTypeId = ObjectTypeId,
                    },
                },
                values = new Dictionary<string, AttributeValue>
                {
                    ["root-assets-value"] = ObjectValue("root-assets-value", rootType.id),
                    ["root-save-value"] = ObjectValue("root-save-value", rootType.id),
                    ["root-session-value"] = ObjectValue("root-session-value", rootType.id),
                    ["floor-tile"] = ObjectValue("floor-tile", TileTypeId),
                    ["base-tile"] = ObjectValue("base-tile", BaseTileTypeId),
                    ["sub-tile"] = ObjectValue("sub-tile", SubTileTypeId),
                    ["other-tile"] = ObjectValue("other-tile", OtherTileTypeId),
                    ["shop-object"] = ObjectValue("shop-object", ObjectTypeId),
                    ["town-grid"] = new ObjectAttributeValue
                    {
                        id = "town-grid",
                        typeId = GridTypeId,
                        value = new Dictionary<string, string>
                        {
                            ["Children"] = "town-grid-children",
                        },
                    },
                    ["town-grid-children"] = new ArrayAttributeValue
                    {
                        id = "town-grid-children",
                        value = new[] { "background-link", "objects-link" },
                    },
                    ["background-link"] = new ObjectAttributeValue
                    {
                        id = "background-link",
                        typeId = TileLayerLinkTypeId,
                        value = new Dictionary<string, string>
                        {
                            ["TileLayer"] = "background-link-layer",
                            ["Tiles"] = "background-link-tiles",
                        },
                    },
                    ["background-link-layer"] = new ArrayAttributeValue
                    {
                        id = "background-link-layer",
                        value = new[] { "background-layer" },
                    },
                    ["background-link-tiles"] = new ArrayAttributeValue
                    {
                        id = "background-link-tiles",
                        value = System.Array.Empty<string>(),
                    },
                    ["objects-link"] = new ObjectAttributeValue
                    {
                        id = "objects-link",
                        typeId = ObjectLayerLinkTypeId,
                        value = new Dictionary<string, string>
                        {
                            ["ObjectLayer"] = "objects-link-layer",
                            ["Objects"] = "objects-link-objects",
                        },
                    },
                    ["objects-link-layer"] = new ArrayAttributeValue
                    {
                        id = "objects-link-layer",
                        value = new[] { "object-layer" },
                    },
                    ["objects-link-objects"] = new ArrayAttributeValue
                    {
                        id = "objects-link-objects",
                        value = System.Array.Empty<string>(),
                    },
                    // Membership by join: shop-1 carries the Objects list
                    // value id as its containerId.
                    ["shop-1"] = new ObjectAttributeValue
                    {
                        id = "shop-1",
                        typeId = ObjectTypeId,
                        containerId = "objects-link-objects",
                        value = new Dictionary<string, string>
                        {
                            ["Position"] = "shop-1-position",
                            ["Children"] = "shop-1-children",
                        },
                    },
                    ["shop-1-position"] = new Vector3AttributeValue
                    {
                        id = "shop-1-position",
                        value = new NeoVector3Value { x = 10, y = 20, z = 0 },
                    },
                    ["shop-1-children"] = new ArrayAttributeValue
                    {
                        id = "shop-1-children",
                        value = new[] { "shop-floor-link" },
                    },
                    ["shop-floor-link"] = new ObjectAttributeValue
                    {
                        id = "shop-floor-link",
                        typeId = TileLayerLinkTypeId,
                        value = new Dictionary<string, string>
                        {
                            ["TileLayer"] = "shop-floor-link-layer",
                            ["Tiles"] = "shop-floor-link-tiles",
                        },
                    },
                    ["shop-floor-link-layer"] = new ArrayAttributeValue
                    {
                        id = "shop-floor-link-layer",
                        value = new[] { "background-layer" },
                    },
                    ["shop-floor-link-tiles"] = new ArrayAttributeValue
                    {
                        id = "shop-floor-link-tiles",
                        value = new[] { "floor-local" },
                    },
                    ["floor-local"] = new ObjectAttributeValue
                    {
                        id = "floor-local",
                        typeId = TileInstanceTypeId,
                        value = new Dictionary<string, string>
                        {
                            ["Cell"] = "floor-local-cell",
                            ["Tile"] = "floor-local-tile",
                        },
                    },
                    ["floor-local-cell"] = new Vector2AttributeValue
                    {
                        id = "floor-local-cell",
                        value = new NeoVector2Value { x = -1, y = 2 },
                    },
                    ["floor-local-tile"] = new ArrayAttributeValue
                    {
                        id = "floor-local-tile",
                        value = new[] { "floor-tile" },
                    },
                },
                types = new Dictionary<string, CustomType>
                {
                    [rootType.id] = rootType,
                    [GridTypeId] = gridType,
                    [TileTypeId] = tileType,
                    [ObjectTypeId] = objectType,
                    [TileInstanceTypeId] = tileInstanceType,
                    [TileLayerLinkTypeId] = tileLayerLinkType,
                    [ObjectLayerLinkTypeId] = objectLayerLinkType,
                    [BaseTileTypeId] = baseTileType,
                    [SubTileTypeId] = subTileType,
                    [OtherTileTypeId] = otherTileType,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
        }

        private static CustomAttribute RootAttribute(
            string id,
            string valueId,
            string customTypeId)
        {
            return new CustomAttribute
            {
                id = id,
                projectId = "project-a",
                name = id,
                type = AttributeType.Custom,
                required = true,
                valueId = valueId,
                customTypeId = customTypeId,
            };
        }

        private static ObjectAttributeValue ObjectValue(string id, string typeId)
        {
            return new ObjectAttributeValue
            {
                id = id,
                typeId = typeId,
                value = new Dictionary<string, string>(),
            };
        }
    }
}
