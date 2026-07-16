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
        private const string TileClassId = "tile-class";
        private const string ObjectClassId = "object-class";
        private const string TileLayerLinkClassId = "tile-layer-link-class";
        private const string TileInstanceClassId = "tile-instance-class";
        private const string BaseTileClassId = "base-tile-class";
        private const string SubTileClassId = "sub-tile-class";
        private const string OtherTileClassId = "other-tile-class";
        private const string BackgroundLayerClassId = "background-layer-class";

        [Test]
        public void SchemaNineClassBackedLayerResolvesClassDefaultTileWithoutDefinitionValue()
        {
            var data = BuildClassBackedTileGridProjectData();
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories(),
                new Dictionary<Type, string> { [typeof(TestTile)] = TileClassId });

            var layer = primitive.BindReadOnlyTileLayer<TestAuthoredTileLayer>(
                BackgroundLayerClassId,
                new[] { TileClassId });
            var tile = layer.GetTile(new Vector2Int(2, 3));

            Assert.NotNull(tile);
            Assert.IsInstanceOf<TestTile>(tile!.Info);
            Assert.IsNull(tile.Info.valueId);
            Assert.AreEqual(TileClassId, ((TestTile)tile.Info).classId);
            Assert.AreEqual(BackgroundLayerClassId, layer.LayerClassId);
            Assert.AreEqual("background-layer-override", layer.LayerOverrideValueId);
            Assert.AreEqual("Override Background", layer.Name);
            Assert.AreEqual("Default layer description", layer.Description);
            Assert.AreEqual("background-layer-override", layer.valueId);
        }

        [Test]
        public void SchemaNineLayerWithoutOverrideUsesClassDefaults()
        {
            var data = BuildClassBackedTileGridProjectData();
            var backgroundLink = (ObjectMemberValue)data.values["background-link"];
            backgroundLink.value!.Remove("layerOverrideValueId");
            data.values.Remove("background-layer-override");
            data.values.Remove("background-layer-override-name");
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories(),
                new Dictionary<Type, string> { [typeof(TestTile)] = TileClassId });

            var layer = primitive.BindReadOnlyTileLayer<TestAuthoredTileLayer>(
                BackgroundLayerClassId,
                new[] { TileClassId });

            Assert.IsNull(layer.LayerOverrideValueId);
            Assert.IsNull(layer.valueId);
            Assert.AreEqual("Default Background", layer.Name);
            Assert.AreEqual("Default layer description", layer.Description);
        }

        [Test]
        public void SchemaNineGenericPlacementWritesClassReferenceAndEnforcesGridImports()
        {
            var data = BuildClassBackedTileGridProjectData();
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoTileGridPrimitive.ResolveForSave(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories(),
                new Dictionary<Type, string>
                {
                    [typeof(TestTile)] = TileClassId,
                    [typeof(TestOtherTile)] = OtherTileClassId,
                });
            var layer = primitive.BindWritableTileLayer<TestAuthoredTileLayer>(
                BackgroundLayerClassId,
                new[] { TileClassId });
            var lifecycle = new RecordingTileSetLifecycle();
            primitive.Lifecycle = lifecycle;

            var placed = layer.Place<TestTile>(new Vector2Int(7, 8));
            var updated = layer.Place<TestTile>(new Vector2Int(7, 8));
            var rejected = layer.Place<TestOtherTile>(new Vector2Int(9, 10));

            Assert.IsTrue(placed.Ok, placed.Message);
            Assert.IsTrue(updated.Ok, updated.Message);
            Assert.IsFalse(rejected.Ok);
            Assert.AreEqual("tile-grid-asset-not-imported", rejected.ErrorCode);
            Assert.IsNotNull(lifecycle.ExistingInstance);
            Assert.AreEqual(
                BackgroundLayerClassId,
                lifecycle.ExistingInstance!["layerClassId"]!.Value<string>());
            Assert.AreNotEqual(
                "background-link",
                lifecycle.ExistingInstance["layerClassId"]!.Value<string>());
            var resolved = layer.GetTile(new Vector2Int(7, 8));
            Assert.NotNull(resolved);
            Assert.IsInstanceOf<TestTile>(resolved!.Info);
            Assert.IsNull(resolved.Info.valueId);
        }

        [Test]
        public void TileLayerLinkPayloadsResolveThroughCurrentObjectPosition()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                [TileClassId] = (resolvedClient, node) => new TestTile(resolvedClient, node),
            };
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>());

            var tiles = primitive.GetTiles("background-layer", TileClassId);
            var candidates = primitive.GetTileCandidates<TestTile>(
                "background-layer",
                new Vector2Int(9, 22),
                TileClassId);

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
            var source = (TestTileLayerLink)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "shop-floor-link",
                readOnlyFactories,
                writableFactories)!;
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                readOnlyFactories,
                writableFactories);

            Assert.AreEqual(1, primitive.GetTiles("background-layer", TileClassId).Count);

            source.ClearTiles();

            Assert.AreEqual(0, primitive.GetTiles("background-layer", TileClassId).Count);
        }

        [Test]
        public void Render_LiveSyncClearsProjectedTilesWhenSourceTilesAreCleared()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            SeedWritableTileLayerLink(client);
            var readOnlyFactories = BuildReadOnlyFactories();
            var writableFactories = BuildWritableFactories();
            var source = (TestTileLayerLink)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "shop-floor-link",
                readOnlyFactories,
                writableFactories)!;
            var tile = (TestTile)NeoGeneratedTypesSupport.ResolveClassValue(
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
            var source = (TestTileLayerLink)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "shop-floor-link",
                readOnlyFactories,
                writableFactories)!;
            var tile = (TestTile)NeoGeneratedTypesSupport.ResolveClassValue(
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

                Assert.AreEqual(0, primitive.GetTiles("background-layer", TileClassId).Count);
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
                            TileClassId,
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
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                [TileClassId] = (resolvedClient, node) => new TestTile(resolvedClient, node),
            };
            var tile = (TestTile)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "floor-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
            var sprite = CreateTestSprite("clearable");
            tile.Sprite = sprite;
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>());
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
                            TileClassId,
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
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                [TileClassId] = (resolvedClient, node) => new TestTile(resolvedClient, node),
            };
            var tile = (TestTile)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "floor-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
            var sprite = CreateTestSprite("async-render");
            tile.Sprite = sprite;
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>());
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
                                    TileClassId,
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
                TileClassId,
                null,
                null);
        }

        [Test]
        public void Render_ShouldRenderObjectVetoSkipsMarkers_AndTryGetObjectRootFindsRendered()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                [ObjectClassId] = (resolvedClient, node) =>
                    new TestComposedObject(resolvedClient, node),
            };
            var obj = (TestComposedObject)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "shop-object",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
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
                            ObjectClassId,
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
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                [TileClassId] = (resolvedClient, node) => new TestTile(resolvedClient, node),
            };
            var tile = (TestTile)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "floor-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
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
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                [ObjectClassId] = (resolvedClient, node) =>
                    new TestComposedObject(resolvedClient, node),
            };
            var obj = (TestComposedObject)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "shop-object",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
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
            public NeoGeneratedClassValue? Tile { get; set; }
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
                    TileClassId,
                    "Assets/Neo/Generated/Tiles/floor-tile.asset",
                    "hash-1",
                    tile);

                Assert.AreSame(tile, database.TryGetTileBase("floor-tile"));
                var entry = database.TryGetTileEntry("floor-tile");
                Assert.IsNotNull(entry);
                Assert.AreEqual(TileClassId, entry!.TileClassId);
                Assert.AreEqual("hash-1", entry.ContentHash);

                var missing = database.FindMissingTileAssets(new HashSet<string>());
                Assert.AreEqual(1, missing.Length);

                database.RemoveTileAsset("floor-tile");
                Assert.IsNull(database.TryGetTileBase("floor-tile"));

                database.SetTileClassAsset(
                    TileClassId,
                    "Assets/Neo/Generated/Tiles/tile-class.asset",
                    "hash-class",
                    tile);
                Assert.AreSame(tile, database.TryGetTileBaseForClass(TileClassId));
                Assert.AreEqual(0, database.FindMissingTileAssets(new HashSet<string>()).Length);
                Assert.AreEqual(
                    1,
                    database.FindMissingTileClassAssets(new HashSet<string>()).Length);
                database.RemoveTileClassAsset(TileClassId);
                Assert.IsNull(database.TryGetTileBaseForClass(TileClassId));
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
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                [TileClassId] = (resolvedClient, node) => new TestTile(resolvedClient, node),
            };
            var tile = (TestTile)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "floor-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
            tile.Sprite = CreateTestSprite("fallback");
            var generatedTile = ScriptableObject.CreateInstance<Tile>();
            generatedTile.sprite = CreateTestSprite("editor-generated");
            var database = ScriptableObject.CreateInstance<NeoAssetDatabase>();
            database.SetTileAsset(
                "floor-tile",
                TileClassId,
                "Assets/Neo/Generated/Tiles/floor-tile.asset",
                "hash-1",
                generatedTile);
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>());
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
                            TileClassId,
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
        public void Render_PrefersEditorGeneratedTileAssetForClassDefaultPlacement()
        {
            var data = BuildClassBackedTileGridProjectData();
            var client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                BuildClassBackedReadOnlyFactories(),
                BuildClassBackedWritableFactories(),
                new Dictionary<Type, string> { [typeof(TestTile)] = TileClassId });
            var layer = primitive.BindReadOnlyTileLayer<TestAuthoredTileLayer>(
                BackgroundLayerClassId,
                new[] { TileClassId });
            var generatedTile = ScriptableObject.CreateInstance<Tile>();
            var database = ScriptableObject.CreateInstance<NeoAssetDatabase>();
            database.SetTileClassAsset(
                TileClassId,
                "Assets/Neo/Generated/Tiles/tile-class.asset",
                "hash-class",
                generatedTile);
            var go = new GameObject("NeoTileGridRenderer class asset test");

            try
            {
                var renderer = go.AddComponent<NeoTileGridRenderer>();
                renderer.AssetDatabase = database;
                renderer.Render(primitive, new[] { layer });

                var tilemap = go.GetComponentInChildren<Tilemap>();
                Assert.IsNotNull(tilemap);
                Assert.AreSame(
                    generatedTile,
                    tilemap!.GetTile(new Vector3Int(2, 3, 0)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(generatedTile);
                UnityEngine.Object.DestroyImmediate(database);
            }
        }

        [Test]
        public void Render_UsesSmartTileRuleTileWhenGeneratedTileExposesSmartTile()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                [TileClassId] = (resolvedClient, node) => new TestTile(resolvedClient, node),
            };
            var tile = (TestTile)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "floor-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
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
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>());
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
                            TileClassId,
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
            var smartTileValue = (TestTile)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "floor-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
            var neighborValue = (TestTile)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "sub-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
            var defaultSprite = CreateTestSprite("smart-default");
            var connectedSprite = CreateTestSprite("smart-connected");
            var neighborSprite = CreateTestSprite("subclass-neighbor");
            smartTileValue.Sprite = defaultSprite;
            neighborValue.Sprite = neighborSprite;
            smartTileValue.SmartTile = SmartTileWithInheritsClassNeighbor(
                connectedSprite,
                BaseTileClassId);

            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>());
            var layer = new MutableTestTileLayerRuntime(
                "background-layer",
                "Background",
                TileClassId);
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
                    "subclass-neighbor",
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
        public void Render_InheritsFromClassRuleRejectsUnrelatedNeighbor()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildTileGridProjectData());
            var factories = BuildInheritanceTileFactories();
            var smartTileValue = (TestTile)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "floor-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
            var neighborValue = (TestTile)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "other-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
            var defaultSprite = CreateTestSprite("smart-default");
            var connectedSprite = CreateTestSprite("smart-connected");
            var neighborSprite = CreateTestSprite("unrelated-neighbor");
            smartTileValue.Sprite = defaultSprite;
            neighborValue.Sprite = neighborSprite;
            smartTileValue.SmartTile = SmartTileWithInheritsClassNeighbor(
                connectedSprite,
                BaseTileClassId);

            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(
                client,
                "town-grid",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>());
            var layer = new MutableTestTileLayerRuntime(
                "background-layer",
                "Background",
                TileClassId);
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
            var factories = new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                [ObjectClassId] = (resolvedClient, node) =>
                    new TestComposedObject(resolvedClient, node),
                [TileClassId] = (resolvedClient, node) => new TestTile(resolvedClient, node),
            };
            var parentSprite = CreateTestSprite("legacy-parent");
            var childSprite = CreateTestSprite("child-object");
            var tileSprite = CreateTestSprite("child-tile");
            var obj = (TestComposedObject)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "shop-object",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
            var tile = (TestTile)NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                "floor-tile",
                factories,
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())!;
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
                            ObjectClassId,
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

        private sealed class TestTile : NeoGeneratedClassValue, INeoSmartTileSource
        {
            public TestTile(NeoClient client, NeoMemberClass node)
                : base(client, node, TileClassId)
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

        private sealed class TestSmartTileNeighbor
            : INeoSmartTileNeighbor,
              INeoSmartTileClassNeighbor
        {
            public Vector2Int Cell { get; set; }

            public string Condition { get; set; } = NeoSmartTileOptionIds.ConditionThis;

            public string? TileValueId { get; set; }

            public string? TileClassId { get; set; }
        }

        private sealed class MutableTestTileLayerRuntime : ReadOnlyNeoTileLayerRuntime
        {
            private readonly Dictionary<Vector2Int, NeoResolvedTileInstance> tilesByCell =
                new();

            public MutableTestTileLayerRuntime(
                string layerId,
                string displayName,
                string expectedClassId)
                : base(layerId, displayName, expectedClassId, null, null)
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

        private sealed class TestTileLayerLink : NeoGeneratedClassValue
        {
            private NeoList<string>? tiles;

            public TestTileLayerLink(
                NeoClient client,
                NeoMemberClass node,
                bool isReadOnly = true)
                : base(client, node, TileLayerLinkClassId, isReadOnly)
            {
            }

            public NeoList<string> Tiles =>
                tiles ??= new NeoList<string>(
                    client,
                    writableNode.Get<NeoMemberListWritable>("Tiles"),
                    () => writableNode.GetOrCreateCollection<NeoMemberListWritable>("Tiles"),
                    (_, __) => "",
                    item => NeoGeneratedTypesSupport.Value(item),
                    () => ThrowIfReadOnly("TestTileLayerLink.Tiles"),
                    () => IsReadOnly);

            public void ClearTiles()
            {
                Tiles.Clear();
            }
        }

        private sealed class TestAuthoredTileLayer : NeoGeneratedTileLayerValue
        {
            public TestAuthoredTileLayer(
                NeoClient client,
                NeoMemberClass node,
                bool isReadOnly = true)
                : base(client, node, BackgroundLayerClassId, isReadOnly)
            {
            }

            public string? Name => node.Get<NeoMemberString>("Name").Text;

            public string? Description =>
                node.Get<NeoMemberString>("Description").Text;

            public NeoPlacementResult Place<TAsset>(Vector2Int cell)
                where TAsset : class => TrySetTileClass<TAsset>(cell);
        }

        private sealed class TestOtherTile : NeoGeneratedClassValue
        {
            public TestOtherTile(NeoClient client, NeoMemberClass node)
                : base(client, node, OtherTileClassId)
            {
            }
        }

        private sealed class RecordingTileSetLifecycle : NeoTileGridLifecycle
        {
            public JObject? ExistingInstance { get; private set; }

            public override void BeforeSetTile(NeoTileSetContext context)
            {
                ExistingInstance = context.ExistingInstance;
            }
        }

        private sealed class TestTileLayerRuntime : ReadOnlyNeoTileLayerRuntime
        {
            private readonly IReadOnlyList<NeoResolvedTileInstance> tiles;

            public TestTileLayerRuntime(
                string layerId,
                string displayName,
                string expectedClassId,
                string? sortingLayerName,
                int? sortingOrder,
                IReadOnlyList<NeoResolvedTileInstance>? tiles = null)
                : base(
                    layerId,
                    displayName,
                    expectedClassId,
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
                string expectedClassId,
                string? sortingLayerName,
                int? sortingOrder,
                IReadOnlyList<NeoResolvedObjectInstance>? objects = null)
                : base(
                    layerId,
                    displayName,
                    expectedClassId,
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
                    TileClassId,
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
                IReadOnlyList<IReadOnlyNeoTileLayerRuntime> tileLayers)
            {
                Primitive = primitive;
                TileLayersInOrder = tileLayers;
                ObjectLayersInOrder = Array.Empty<IReadOnlyNeoObjectLayerRuntime>();
            }

            public NeoReadOnlyTileGridPrimitive Primitive { get; }
            public IReadOnlyList<IReadOnlyNeoTileLayerRuntime> TileLayersInOrder { get; }
            public IReadOnlyList<IReadOnlyNeoObjectLayerRuntime> ObjectLayersInOrder { get; }
            public NeoTileGridRenderer? Renderer => Primitive.Renderer;
            public IDisposable OnChanged(Action<NeoTileGridChangedArgs> handler) =>
                Primitive.OnChanged(handler);
        }

        private sealed class TestComposedObject : NeoGeneratedClassValue
        {
            public TestComposedObject(NeoClient client, NeoMemberClass node)
                : base(client, node, ObjectClassId)
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
            public NeoGeneratedClassValue? Tile { get; set; }
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
                Condition = NeoSmartTileOptionIds.ConditionInheritsFromClass,
                TileValueId = tileValueId,
            });
            var smartTile = new TestSmartTile();
            smartTile.Rules.Add(rule);
            return smartTile;
        }

        private static TestSmartTile SmartTileWithInheritsClassNeighbor(
            Sprite connectedSprite,
            string tileClassId)
        {
            var rule = new TestSmartTileRule();
            rule.Sprites.Add(connectedSprite);
            rule.Neighbors.Add(new TestSmartTileNeighbor
            {
                Cell = new Vector2Int(1, 0),
                Condition = NeoSmartTileOptionIds.ConditionInheritsFromClass,
                TileClassId = tileClassId,
            });
            var smartTile = new TestSmartTile();
            smartTile.Rules.Add(rule);
            return smartTile;
        }

        private static Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            BuildInheritanceTileFactories()
        {
            NeoGeneratedTypesSupport.ReadOnlyClassFactory factory =
                (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                        resolvedClient,
                        node,
                        () => new TestTile(resolvedClient, node));
            return new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                [TileClassId] = factory,
                [BaseTileClassId] = factory,
                [SubTileClassId] = factory,
                [OtherTileClassId] = factory,
            };
        }

        private static Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            BuildReadOnlyFactories()
        {
            return new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                [TileClassId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                        resolvedClient,
                        node,
                        () => new TestTile(resolvedClient, node)),
                [TileLayerLinkClassId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                        resolvedClient,
                        node,
                        () => new TestTileLayerLink(resolvedClient, node)),
            };
        }

        private static Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>
            BuildWritableFactories()
        {
            return new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>
            {
                [TileClassId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                        resolvedClient,
                        node,
                        () => new TestTile(resolvedClient, node)),
                [TileLayerLinkClassId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
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
                new ObjectMemberValue
                {
                    id = "shop-floor-link",
                    classId = TileLayerLinkClassId,
                    value = new Dictionary<string, string>
                    {
                        ["TileLayer"] = "shop-floor-link-layer",
                        ["Tiles"] = "shop-floor-link-tiles",
                    },
                });
            client.AddSaveValue(
                "shop-floor-link-tiles",
                new ArrayMemberValue
                {
                    id = "shop-floor-link-tiles",
                    value = new[] { "floor-local" },
                });
        }

        private const string GridClassId = "grid-class";
        private const string ObjectLayerLinkClassId = "object-layer-link-class";

        private static ProjectData BuildClassBackedTileGridProjectData()
        {
            var data = BuildTileGridProjectData();
            data.metadata = new ProjectExportMetadata
            {
                schemaVersion = 9,
                projectId = "project-a",
                versionId = "version-relations",
            };
            data.classes[BackgroundLayerClassId] = new NeoSchemaClass
            {
                id = BackgroundLayerClassId,
                projectId = "project-a",
                name = "Background Tile Layer",
                schema = new Dictionary<string, string>
                {
                    ["Name"] = "background-layer-name-member",
                    ["Description"] = "background-layer-description-member",
                },
            };
            data.members["background-layer-name-member"] = new StringMember
            {
                id = "background-layer-name-member",
                projectId = "project-a",
                name = "Name",
                kind = MemberKind.String,
                localizable = false,
                defaultValue = new StringMemberValueBase
                {
                    value = "Default Background",
                },
                createdAt = "x",
                updatedAt = "x",
            };
            data.members["background-layer-description-member"] = new StringMember
            {
                id = "background-layer-description-member",
                projectId = "project-a",
                name = "Description",
                kind = MemberKind.String,
                localizable = false,
                defaultValue = new StringMemberValueBase
                {
                    value = "Default layer description",
                },
                createdAt = "x",
                updatedAt = "x",
            };
            data.internalRecordRelations = new Dictionary<string, InternalRecordRelation>
            {
                ["relation-grid-layer"] = Relation(
                    "relation-grid-layer",
                    InternalRecordRelationKinds.WorldGridTileLayer,
                    GridClassId,
                    BackgroundLayerClassId,
                    "a0"),
                ["relation-grid-import"] = Relation(
                    "relation-grid-import",
                    InternalRecordRelationKinds.WorldGridTileImport,
                    GridClassId,
                    TileClassId),
                ["relation-tile-compatible"] = Relation(
                    "relation-tile-compatible",
                    InternalRecordRelationKinds.WorldTileCompatibleLayer,
                    TileClassId,
                    BackgroundLayerClassId),
                ["relation-link-target"] = Relation(
                    "relation-link-target",
                    InternalRecordRelationKinds.WorldTileLayerLinkTarget,
                    TileLayerLinkClassId,
                    BackgroundLayerClassId),
            };

            var backgroundLink = (ObjectMemberValue)data.values["background-link"];
            backgroundLink.value!["layerClassId"] = BackgroundLayerClassId;
            backgroundLink.value["layerOverrideValueId"] = "background-layer-override";
            backgroundLink.value.Remove("TileLayer");
            data.values["background-layer-override"] = new ObjectMemberValue
            {
                id = "background-layer-override",
                classId = BackgroundLayerClassId,
                containerId = "background-link",
                value = new Dictionary<string, string>
                {
                    ["Name"] = "background-layer-override-name",
                },
            };
            data.values["background-layer-override-name"] = new StringMemberValue
            {
                id = "background-layer-override-name",
                value = "Override Background",
            };
            ((ArrayMemberValue)data.values["background-link-tiles"]).value =
                new[] { "class-backed-placement" };
            data.values["class-backed-placement"] = new ObjectMemberValue
            {
                id = "class-backed-placement",
                classId = TileInstanceClassId,
                containerId = "background-link-tiles",
                value = new Dictionary<string, string>
                {
                    ["Cell"] = "class-backed-placement-cell",
                    ["assetClassId"] = TileClassId,
                },
            };
            data.values["class-backed-placement-cell"] = new Vector2MemberValue
            {
                id = "class-backed-placement-cell",
                value = new NeoVector2Value { x = 2, y = 3 },
            };
            return data;
        }

        private static InternalRecordRelation Relation(
            string id,
            string kind,
            string sourceClassId,
            string targetClassId,
            string? orderKey = null)
        {
            return new InternalRecordRelation
            {
                id = id,
                projectId = "project-a",
                relationKind = kind,
                sourceRecordKind = "class",
                sourceRecordId = sourceClassId,
                targetRecordKind = "class",
                targetRecordId = targetClassId,
                orderKey = orderKey,
                createdAt = "2026-01-01T00:00:00.000Z",
                updatedAt = "2026-01-01T00:00:00.000Z",
            };
        }

        private static Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            BuildClassBackedReadOnlyFactories()
        {
            return new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                [TileClassId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                        resolvedClient,
                        node,
                        () => new TestTile(resolvedClient, node)),
                [OtherTileClassId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                        resolvedClient,
                        node,
                        () => new TestOtherTile(resolvedClient, node)),
                [BackgroundLayerClassId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                        resolvedClient,
                        node,
                        () => new TestAuthoredTileLayer(resolvedClient, node)),
            };
        }

        private static Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>
            BuildClassBackedWritableFactories()
        {
            return new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>
            {
                [BackgroundLayerClassId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                        resolvedClient,
                        node,
                        () => new TestAuthoredTileLayer(
                            resolvedClient,
                            node,
                            isReadOnly: false)),
            };
        }

        /// <summary>
        /// Values-native world fixture: the grid value's "Children" ordered
        /// list carries a (empty) direct tile layer link plus an object layer
        /// link whose placed object "shop-1" (at 10,20) carries a
        /// TileLayerLink child "shop-floor-link" projecting "floor-local"
        /// (local cell -1,2 -> grid cell 9,22) into "background-layer".
        /// </summary>
        private static ProjectData BuildTileGridProjectData()
        {
            var rootClass = new NeoSchemaClass
            {
                id = "root-class",
                projectId = "project-a",
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            var gridClass = new NeoSchemaClass
            {
                id = GridClassId,
                projectId = "project-a",
                name = "Grid",
                schema = new Dictionary<string, string>
                {
                    ["Children"] = "grid-children-member",
                },
            };
            var tileClass = new NeoSchemaClass
            {
                id = TileClassId,
                projectId = "project-a",
                name = "Tile",
                schema = new Dictionary<string, string>(),
            };
            var objectClass = new NeoSchemaClass
            {
                id = ObjectClassId,
                projectId = "project-a",
                name = "Object",
                schema = new Dictionary<string, string>
                {
                    ["Position"] = "object-position-member",
                    ["Children"] = "object-children-member",
                },
            };
            var tileInstanceClass = new NeoSchemaClass
            {
                id = TileInstanceClassId,
                projectId = "project-a",
                name = "Tile Instance",
                schema = new Dictionary<string, string>
                {
                    ["Cell"] = "tile-instance-cell-member",
                    ["Tile"] = "tile-instance-tile-member",
                },
            };
            var tileLayerLinkClass = new NeoSchemaClass
            {
                id = TileLayerLinkClassId,
                projectId = "project-a",
                name = "Tile Layer Link",
                schema = new Dictionary<string, string>
                {
                    ["TileLayer"] = "tile-layer-link-layer-member",
                    ["Tiles"] = "tile-layer-link-tiles-member",
                },
            };
            var objectLayerLinkClass = new NeoSchemaClass
            {
                id = ObjectLayerLinkClassId,
                projectId = "project-a",
                name = "Object Layer Link",
                schema = new Dictionary<string, string>
                {
                    ["ObjectLayer"] = "object-layer-link-layer-member",
                    ["Objects"] = "object-layer-link-objects-member",
                },
            };
            var baseTileClass = new NeoSchemaClass
            {
                id = BaseTileClassId,
                projectId = "project-a",
                name = "Base Tile",
                schema = new Dictionary<string, string>(),
            };
            var subTileClass = new NeoSchemaClass
            {
                id = SubTileClassId,
                projectId = "project-a",
                name = "Sub Tile",
                extendsClassId = BaseTileClassId,
                schema = new Dictionary<string, string>(),
            };
            var otherTileClass = new NeoSchemaClass
            {
                id = OtherTileClassId,
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
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = new Dictionary<string, NeoCompose.Runtime.Json.Member>
                {
                    ["root-assets"] = RootMember("root-assets", "root-assets-value", rootClass.id),
                    ["root-save"] = RootMember("root-save", "root-save-value", rootClass.id),
                    ["root-session"] = RootMember("root-session", "root-session-value", rootClass.id),
                    ["grid-children-member"] = new ListMember
                    {
                        id = "grid-children-member",
                        projectId = "project-a",
                        name = "Children",
                        kind = MemberKind.List,
                        entryMemberId = "grid-child-entry-member",
                    },
                    ["grid-child-entry-member"] = new ClassMember
                    {
                        id = "grid-child-entry-member",
                        projectId = "project-a",
                        name = "Child",
                        kind = MemberKind.Class,
                        classId = TileLayerLinkClassId,
                    },
                    ["object-position-member"] = new Vector3Member
                    {
                        id = "object-position-member",
                        projectId = "project-a",
                        name = "Position",
                        kind = MemberKind.Vector3,
                    },
                    ["object-children-member"] = new ListMember
                    {
                        id = "object-children-member",
                        projectId = "project-a",
                        name = "Children",
                        kind = MemberKind.List,
                        entryMemberId = "grid-child-entry-member",
                    },
                    ["tile-instance-cell-member"] = new Vector2IntMember
                    {
                        id = "tile-instance-cell-member",
                        projectId = "project-a",
                        name = "Cell",
                        kind = MemberKind.Vector2Int,
                    },
                    ["tile-instance-tile-member"] = new LookupMember
                    {
                        id = "tile-instance-tile-member",
                        projectId = "project-a",
                        name = "Tile",
                        kind = MemberKind.Lookup,
                        collectionMemberId = "tile-layer-link-tiles-member",
                    },
                    ["tile-layer-link-layer-member"] = new LookupMember
                    {
                        id = "tile-layer-link-layer-member",
                        projectId = "project-a",
                        name = "TileLayer",
                        kind = MemberKind.Lookup,
                        collectionMemberId = "grid-children-member",
                    },
                    ["tile-layer-link-tiles-member"] = new ListMember
                    {
                        id = "tile-layer-link-tiles-member",
                        projectId = "project-a",
                        name = "Tiles",
                        kind = MemberKind.List,
                        entryMemberId = "tile-layer-link-tile-entry-member",
                        storage = "save",
                    },
                    ["tile-layer-link-tile-entry-member"] = new ClassMember
                    {
                        id = "tile-layer-link-tile-entry-member",
                        projectId = "project-a",
                        name = "Tile",
                        kind = MemberKind.Class,
                        classId = TileInstanceClassId,
                    },
                    ["object-layer-link-layer-member"] = new LookupMember
                    {
                        id = "object-layer-link-layer-member",
                        projectId = "project-a",
                        name = "ObjectLayer",
                        kind = MemberKind.Lookup,
                        collectionMemberId = "grid-children-member",
                    },
                    ["object-layer-link-objects-member"] = new ListMember
                    {
                        id = "object-layer-link-objects-member",
                        projectId = "project-a",
                        name = "Objects",
                        kind = MemberKind.List,
                        entryMemberId = "object-layer-link-object-entry-member",
                        listKind = NeoListKinds.Unordered,
                    },
                    ["object-layer-link-object-entry-member"] = new ClassMember
                    {
                        id = "object-layer-link-object-entry-member",
                        projectId = "project-a",
                        name = "Object",
                        kind = MemberKind.Class,
                        classId = ObjectClassId,
                    },
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["root-assets-value"] = ObjectValue("root-assets-value", rootClass.id),
                    ["root-save-value"] = ObjectValue("root-save-value", rootClass.id),
                    ["root-session-value"] = ObjectValue("root-session-value", rootClass.id),
                    ["floor-tile"] = ObjectValue("floor-tile", TileClassId),
                    ["base-tile"] = ObjectValue("base-tile", BaseTileClassId),
                    ["sub-tile"] = ObjectValue("sub-tile", SubTileClassId),
                    ["other-tile"] = ObjectValue("other-tile", OtherTileClassId),
                    ["shop-object"] = ObjectValue("shop-object", ObjectClassId),
                    ["town-grid"] = new ObjectMemberValue
                    {
                        id = "town-grid",
                        classId = GridClassId,
                        value = new Dictionary<string, string>
                        {
                            ["Children"] = "town-grid-children",
                        },
                    },
                    ["town-grid-children"] = new ArrayMemberValue
                    {
                        id = "town-grid-children",
                        value = new[] { "background-link", "objects-link" },
                    },
                    ["background-link"] = new ObjectMemberValue
                    {
                        id = "background-link",
                        classId = TileLayerLinkClassId,
                        value = new Dictionary<string, string>
                        {
                            ["TileLayer"] = "background-link-layer",
                            ["Tiles"] = "background-link-tiles",
                        },
                    },
                    ["background-link-layer"] = new ArrayMemberValue
                    {
                        id = "background-link-layer",
                        value = new[] { "background-layer" },
                    },
                    ["background-link-tiles"] = new ArrayMemberValue
                    {
                        id = "background-link-tiles",
                        value = System.Array.Empty<string>(),
                    },
                    ["objects-link"] = new ObjectMemberValue
                    {
                        id = "objects-link",
                        classId = ObjectLayerLinkClassId,
                        value = new Dictionary<string, string>
                        {
                            ["ObjectLayer"] = "objects-link-layer",
                            ["Objects"] = "objects-link-objects",
                        },
                    },
                    ["objects-link-layer"] = new ArrayMemberValue
                    {
                        id = "objects-link-layer",
                        value = new[] { "object-layer" },
                    },
                    ["objects-link-objects"] = new ArrayMemberValue
                    {
                        id = "objects-link-objects",
                        value = System.Array.Empty<string>(),
                    },
                    // Membership by join: shop-1 carries the Objects list
                    // value id as its containerId.
                    ["shop-1"] = new ObjectMemberValue
                    {
                        id = "shop-1",
                        classId = ObjectClassId,
                        containerId = "objects-link-objects",
                        value = new Dictionary<string, string>
                        {
                            ["Position"] = "shop-1-position",
                            ["Children"] = "shop-1-children",
                        },
                    },
                    ["shop-1-position"] = new Vector3MemberValue
                    {
                        id = "shop-1-position",
                        value = new NeoVector3Value { x = 10, y = 20, z = 0 },
                    },
                    ["shop-1-children"] = new ArrayMemberValue
                    {
                        id = "shop-1-children",
                        value = new[] { "shop-floor-link" },
                    },
                    ["shop-floor-link"] = new ObjectMemberValue
                    {
                        id = "shop-floor-link",
                        classId = TileLayerLinkClassId,
                        value = new Dictionary<string, string>
                        {
                            ["TileLayer"] = "shop-floor-link-layer",
                            ["Tiles"] = "shop-floor-link-tiles",
                        },
                    },
                    ["shop-floor-link-layer"] = new ArrayMemberValue
                    {
                        id = "shop-floor-link-layer",
                        value = new[] { "background-layer" },
                    },
                    ["shop-floor-link-tiles"] = new ArrayMemberValue
                    {
                        id = "shop-floor-link-tiles",
                        value = new[] { "floor-local" },
                    },
                    ["floor-local"] = new ObjectMemberValue
                    {
                        id = "floor-local",
                        classId = TileInstanceClassId,
                        value = new Dictionary<string, string>
                        {
                            ["Cell"] = "floor-local-cell",
                            ["Tile"] = "floor-local-tile",
                        },
                    },
                    ["floor-local-cell"] = new Vector2MemberValue
                    {
                        id = "floor-local-cell",
                        value = new NeoVector2Value { x = -1, y = 2 },
                    },
                    ["floor-local-tile"] = new ArrayMemberValue
                    {
                        id = "floor-local-tile",
                        value = new[] { "floor-tile" },
                    },
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [rootClass.id] = rootClass,
                    [GridClassId] = gridClass,
                    [TileClassId] = tileClass,
                    [ObjectClassId] = objectClass,
                    [TileInstanceClassId] = tileInstanceClass,
                    [TileLayerLinkClassId] = tileLayerLinkClass,
                    [ObjectLayerLinkClassId] = objectLayerLinkClass,
                    [BaseTileClassId] = baseTileClass,
                    [SubTileClassId] = subTileClass,
                    [OtherTileClassId] = otherTileClass,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
        }

        private static ClassMember RootMember(
            string id,
            string valueId,
            string classId)
        {
            return new ClassMember
            {
                id = id,
                projectId = "project-a",
                name = id,
                kind = MemberKind.Class,
                required = true,
                valueId = valueId,
                classId = classId,
            };
        }

        private static ObjectMemberValue ObjectValue(string id, string classId)
        {
            return new ObjectMemberValue
            {
                id = id,
                classId = classId,
                value = new Dictionary<string, string>(),
            };
        }
    }
}
