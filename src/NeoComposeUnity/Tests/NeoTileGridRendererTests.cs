// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Tests
{
    public class NeoTileGridRendererTests
    {
        private const string TileTypeId = "tile-type";
        private const string ObjectTypeId = "object-type";

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
            Assert.AreEqual("shop-1:shop-floor-link-output:floor-local", tiles[0].InstanceId.Value);
            Assert.AreEqual("shop-1", tiles[0].SourceObjectInstanceId);
            Assert.AreEqual("shop-floor-link-output", tiles[0].SourceTileLayerLinkId);
            Assert.IsInstanceOf<TestTile>(tiles[0].Tile);
            Assert.AreEqual(1, candidates.Count);
            Assert.AreEqual(NeoTileOutputSourceKind.TileLayerLink, candidates[0].SourceKind);
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
        public void SmartTileRuleTileConverter_MapsUnityRuleTileFields()
        {
            var defaultSprite = CreateTestSprite("default");
            var ruleSprite = CreateTestSprite("rule");
            var randomSprite = CreateTestSprite("random");
            var smartTile = new NeoSmartTile
            {
                DefaultSprite = defaultSprite,
                DefaultColliderType = Tile.ColliderType.None,
            };
            var rule = new NeoSmartTileRule
            {
                Output = NeoSmartTileOutputMode.Random,
                RuleTransform = NeoSmartTileTransformMode.MirrorXY,
                RandomTransform = NeoSmartTileTransformMode.Rotated,
                ColliderType = Tile.ColliderType.Sprite,
                MinAnimationSpeed = 0.25f,
                MaxAnimationSpeed = 1.75f,
                PerlinScale = 0.8f,
            };
            rule.Sprites.Add(ruleSprite);
            rule.Sprites.Add(randomSprite);
            rule.Neighbors.Add(new NeoSmartTileNeighbor(
                new Vector3Int(0, 1, 0),
                NeoSmartTileNeighborKind.This));
            rule.Neighbors.Add(new NeoSmartTileNeighbor(
                new Vector3Int(1, 0, 0),
                NeoSmartTileNeighborKind.NotThis));
            rule.Neighbors.Add(new NeoSmartTileNeighbor(
                new Vector3Int(-1, 0, 0),
                NeoSmartTileNeighborKind.DontCare));
            smartTile.Rules.Add(rule);

            var unityTile = NeoSmartTileRuleTileConverter.ToRuleTile(smartTile);

            Assert.AreSame(defaultSprite, unityTile.m_DefaultSprite);
            Assert.AreEqual(Tile.ColliderType.None, unityTile.m_DefaultColliderType);
            Assert.AreEqual(1, unityTile.m_TilingRules.Count);
            var unityRule = unityTile.m_TilingRules[0];
            Assert.AreEqual(RuleTile.TilingRuleOutput.OutputSprite.Random, unityRule.m_Output);
            Assert.AreEqual(RuleTile.TilingRuleOutput.Transform.MirrorXY, unityRule.m_RuleTransform);
            Assert.AreEqual(RuleTile.TilingRuleOutput.Transform.Rotated, unityRule.m_RandomTransform);
            Assert.AreEqual(Tile.ColliderType.Sprite, unityRule.m_ColliderType);
            Assert.AreEqual(0.25f, unityRule.m_MinAnimationSpeed);
            Assert.AreEqual(1.75f, unityRule.m_MaxAnimationSpeed);
            Assert.AreEqual(0.8f, unityRule.m_PerlinScale);
            CollectionAssert.AreEqual(new[] { ruleSprite, randomSprite }, unityRule.m_Sprites);
            Assert.AreEqual(2, unityRule.m_Neighbors.Count);
            Assert.IsTrue(unityRule.GetNeighbors().ContainsKey(new Vector3Int(0, 1, 0)));
            Assert.IsTrue(unityRule.GetNeighbors().ContainsKey(new Vector3Int(1, 0, 0)));
            Assert.IsFalse(unityRule.GetNeighbors().ContainsKey(new Vector3Int(-1, 0, 0)));
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
            tile.Sprite = CreateTestSprite("fallback");
            tile.SmartTile = new NeoSmartTile
            {
                DefaultSprite = CreateTestSprite("smart-default"),
                DefaultColliderType = Tile.ColliderType.None,
            };
            tile.SmartTile.Rules.Add(new NeoSmartTileRule
            {
                ColliderType = Tile.ColliderType.Sprite,
                Output = NeoSmartTileOutputMode.Single,
            });

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
                Assert.IsInstanceOf<RuleTile>(renderedTile);
                var ruleTile = (RuleTile)renderedTile;
                Assert.AreSame(tile.SmartTile.DefaultSprite, ruleTile.m_DefaultSprite);
                Assert.AreEqual(Tile.ColliderType.None, ruleTile.m_DefaultColliderType);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
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

        private sealed class TestTile : NeoGeneratedCustomValue
        {
            public TestTile(NeoClient client, NeoAttributeCustom node)
                : base(client, node, TileTypeId)
            {
            }

            public Sprite? Sprite { get; set; }
            public NeoSmartTile? SmartTile { get; set; }
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

        private static ProjectData BuildTileGridProjectData()
        {
            var rootType = new CustomType
            {
                id = "root-type",
                projectId = "project-a",
                name = "Root",
                schema = new Dictionary<string, string>(),
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
                },
                values = new Dictionary<string, AttributeValue>
                {
                    ["root-assets-value"] = ObjectValue("root-assets-value", rootType.id),
                    ["root-save-value"] = ObjectValue("root-save-value", rootType.id),
                    ["root-session-value"] = ObjectValue("root-session-value", rootType.id),
                    ["floor-tile"] = ObjectValue("floor-tile", TileTypeId),
                    ["shop-object"] = ObjectValue("shop-object", ObjectTypeId),
                },
                types = new Dictionary<string, CustomType>
                {
                    [rootType.id] = rootType,
                    [TileTypeId] = tileType,
                    [ObjectTypeId] = objectType,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
                tileGridContents = new Dictionary<string, TileGridContent>
                {
                    ["town-grid"] = new TileGridContent
                    {
                        schemaVersion = 1,
                        manifest = new TileGridManifest
                        {
                            id = "manifest-1",
                            gridValueId = "town-grid",
                            schemaVersion = 1,
                            regionSize = 32,
                            layerOrder = new List<string> { "background-layer", "object-layer" },
                            importedAssets = new List<JToken>(),
                            contentHash = "manifest-hash",
                        },
                        regions = new List<TileGridRegion>
                        {
                            new TileGridRegion
                            {
                                id = "object-region-1",
                                gridValueId = "town-grid",
                                layerId = "object-layer",
                                layerKind = "object",
                                regionKey = "0,0",
                                regionX = 0,
                                regionY = 0,
                                dataSchemaVersion = 1,
                                data = JObject.Parse(@"{
                                  ""kind"": ""object"",
                                  ""instances"": [
                                    {
                                      ""id"": ""shop-1"",
                                      ""objectValueId"": ""shop-object"",
                                      ""position"": { ""x"": 10, ""y"": 20 },
                                      ""objectLayerId"": ""object-layer"",
                                      ""order"": 2
                                    }
                                  ]
                                }"),
                                contentHash = "object-region-hash",
                            },
                        },
                        tileLayerLinks = new List<TileGridLayerLinkPayload>
                        {
                            new TileGridLayerLinkPayload
                            {
                                id = "shop-floor-link-output",
                                gridValueId = "town-grid",
                                objectLayerId = "object-layer",
                                objectInstanceId = "shop-1",
                                objectValueId = "shop-object",
                                tileLayerLinkValueId = "shop-floor-link",
                                targetTileLayerId = "background-layer",
                                origin = new TileGridCell { x = 0, y = 0 },
                                order = 10,
                                tiles = new List<TileGridLayerLinkTile>
                                {
                                    new TileGridLayerLinkTile
                                    {
                                        id = "floor-local",
                                        tileValueId = "floor-tile",
                                        tileTypeId = TileTypeId,
                                        position = new TileGridCell { x = -1, y = 2 },
                                        order = 3,
                                    },
                                },
                            },
                        },
                    },
                },
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
