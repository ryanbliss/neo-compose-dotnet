// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace NeoCompose.Tests
{
    public class NeoSmartTileConverterTests
    {
        private readonly List<UnityEngine.Object> createdObjects = new();

        [TearDown]
        public void DestroyCreatedObjects()
        {
            foreach (var created in createdObjects)
            {
                if (created != null) UnityEngine.Object.DestroyImmediate(created);
            }
            createdObjects.Clear();
        }

        [Test]
        public void ToRuleTile_MapsThisAndNotThisToUnityBuiltInNeighbors()
        {
            var rule = new FakeSmartTileRule();
            rule.Neighbors.Add(new FakeSmartTileNeighbor
            {
                Cell = new Vector2Int(0, 1),
                Condition = NeoSmartTileOptionIds.ConditionThis,
            });
            rule.Neighbors.Add(new FakeSmartTileNeighbor
            {
                Cell = new Vector2Int(1, 0),
                Condition = NeoSmartTileOptionIds.ConditionNotThis,
            });
            var smartTile = SmartTileWithRules(rule);

            var tile = Convert(smartTile);

            var neighbors = tile.m_TilingRules[0].GetNeighbors();
            Assert.AreEqual(2, neighbors.Count);
            Assert.AreEqual(
                RuleTile.TilingRuleOutput.Neighbor.This,
                neighbors[new Vector3Int(0, 1, 0)]);
            Assert.AreEqual(
                RuleTile.TilingRuleOutput.Neighbor.NotThis,
                neighbors[new Vector3Int(1, 0, 0)]);
        }

        [Test]
        public void ToRuleTile_MapsNeighborCellsToVector3IntOffsets()
        {
            var rule = new FakeSmartTileRule();
            rule.Neighbors.Add(new FakeSmartTileNeighbor
            {
                Cell = new Vector2Int(-1, -2),
                Condition = NeoSmartTileOptionIds.ConditionThis,
            });
            rule.Neighbors.Add(new FakeSmartTileNeighbor
            {
                Cell = new Vector2Int(2, -1),
                Condition = NeoSmartTileOptionIds.ConditionNotThis,
            });
            var smartTile = SmartTileWithRules(rule);

            var tile = Convert(smartTile);

            var neighbors = tile.m_TilingRules[0].GetNeighbors();
            Assert.IsTrue(neighbors.ContainsKey(new Vector3Int(-1, -2, 0)));
            Assert.IsTrue(neighbors.ContainsKey(new Vector3Int(2, -1, 0)));
        }

        [Test]
        public void ToRuleTile_RegistersCustomNeighborForInheritsFromType()
        {
            var rule = new FakeSmartTileRule();
            rule.Neighbors.Add(new FakeSmartTileNeighbor
            {
                Cell = new Vector2Int(1, 0),
                Condition = NeoSmartTileOptionIds.ConditionInheritsFromType,
                TileValueId = "base-tile",
            });
            rule.Neighbors.Add(new FakeSmartTileNeighbor
            {
                Cell = new Vector2Int(-1, 0),
                Condition = NeoSmartTileOptionIds.ConditionNotInheritsFromType,
                TileValueId = "other-tile",
            });
            var smartTile = SmartTileWithRules(rule);
            var matcher = new RecordingNeighborMatcher();

            var tile = Convert(smartTile, matcher);

            var neighbors = tile.m_TilingRules[0].GetNeighbors();
            int inheritsId = neighbors[new Vector3Int(1, 0, 0)];
            int notInheritsId = neighbors[new Vector3Int(-1, 0, 0)];
            Assert.GreaterOrEqual(inheritsId, 1000);
            Assert.GreaterOrEqual(notInheritsId, 1000);

            tile.RuleMatch(inheritsId, null!);
            tile.RuleMatch(notInheritsId, null!);

            Assert.AreEqual(2, matcher.Observed.Count);
            var inheritsNeighbor = matcher.Observed[0];
            Assert.AreEqual(NeoSmartTileNeighborKind.InheritsFromType, inheritsNeighbor.Kind);
            Assert.AreEqual("base-tile", inheritsNeighbor.TileValueId);
            Assert.AreEqual(new Vector3Int(1, 0, 0), inheritsNeighbor.Offset);
            var notInheritsNeighbor = matcher.Observed[1];
            Assert.AreEqual(
                NeoSmartTileNeighborKind.NotInheritsFromType,
                notInheritsNeighbor.Kind);
            Assert.AreEqual("other-tile", notInheritsNeighbor.TileValueId);
            Assert.AreEqual(new Vector3Int(-1, 0, 0), notInheritsNeighbor.Offset);
        }

        [Test]
        public void ToRuleTile_MapsOutputOptionIds()
        {
            var smartTile = SmartTileWithRules(
                new FakeSmartTileRule { Output = NeoSmartTileOptionIds.OutputSingle },
                new FakeSmartTileRule { Output = NeoSmartTileOptionIds.OutputRandom },
                new FakeSmartTileRule { Output = NeoSmartTileOptionIds.OutputAnimation });

            var tile = Convert(smartTile);

            Assert.AreEqual(
                RuleTile.TilingRuleOutput.OutputSprite.Single,
                tile.m_TilingRules[0].m_Output);
            Assert.AreEqual(
                RuleTile.TilingRuleOutput.OutputSprite.Random,
                tile.m_TilingRules[1].m_Output);
            Assert.AreEqual(
                RuleTile.TilingRuleOutput.OutputSprite.Animation,
                tile.m_TilingRules[2].m_Output);
        }

        [Test]
        public void ToRuleTile_MapsColliderOptionIds()
        {
            var smartTile = SmartTileWithRules(
                new FakeSmartTileRule { Collider = NeoSmartTileOptionIds.ColliderNone },
                new FakeSmartTileRule { Collider = NeoSmartTileOptionIds.ColliderSprite },
                new FakeSmartTileRule { Collider = NeoSmartTileOptionIds.ColliderGrid });
            smartTile.DefaultCollider = NeoSmartTileOptionIds.ColliderGrid;

            var tile = Convert(smartTile);

            Assert.AreEqual(Tile.ColliderType.Grid, tile.m_DefaultColliderType);
            Assert.AreEqual(Tile.ColliderType.None, tile.m_TilingRules[0].m_ColliderType);
            Assert.AreEqual(Tile.ColliderType.Sprite, tile.m_TilingRules[1].m_ColliderType);
            Assert.AreEqual(Tile.ColliderType.Grid, tile.m_TilingRules[2].m_ColliderType);
        }

        [Test]
        public void ToRuleTile_MapsTransformOptionIds()
        {
            var smartTile = SmartTileWithRules(
                new FakeSmartTileRule
                {
                    RuleTransform = NeoSmartTileOptionIds.TransformFixed,
                },
                new FakeSmartTileRule
                {
                    RuleTransform = NeoSmartTileOptionIds.TransformRotated,
                },
                new FakeSmartTileRule
                {
                    RuleTransform = NeoSmartTileOptionIds.TransformMirrorX,
                },
                new FakeSmartTileRule
                {
                    RuleTransform = NeoSmartTileOptionIds.TransformMirrorY,
                },
                new FakeSmartTileRule
                {
                    RuleTransform = NeoSmartTileOptionIds.TransformMirrorXY,
                },
                new FakeSmartTileRule
                {
                    RuleTransform = NeoSmartTileOptionIds.TransformRotatedMirror,
                });

            var tile = Convert(smartTile);

            Assert.AreEqual(
                RuleTile.TilingRuleOutput.Transform.Fixed,
                tile.m_TilingRules[0].m_RuleTransform);
            Assert.AreEqual(
                RuleTile.TilingRuleOutput.Transform.Rotated,
                tile.m_TilingRules[1].m_RuleTransform);
            Assert.AreEqual(
                RuleTile.TilingRuleOutput.Transform.MirrorX,
                tile.m_TilingRules[2].m_RuleTransform);
            Assert.AreEqual(
                RuleTile.TilingRuleOutput.Transform.MirrorY,
                tile.m_TilingRules[3].m_RuleTransform);
            Assert.AreEqual(
                RuleTile.TilingRuleOutput.Transform.MirrorXY,
                tile.m_TilingRules[4].m_RuleTransform);
            Assert.AreEqual(
                RuleTile.TilingRuleOutput.Transform.RotatedMirror,
                tile.m_TilingRules[5].m_RuleTransform);
        }

        [Test]
        public void ToRuleTile_UnknownTransformOptionIdThrows()
        {
            var smartTile = SmartTileWithRules(new FakeSmartTileRule
            {
                RuleTransform = "Sideways",
            });

            var error = Assert.Throws<ArgumentException>(() => Convert(smartTile));
            StringAssert.Contains("Sideways", error!.Message);
            StringAssert.Contains("RuleTransform", error.Message);
        }

        [Test]
        public void ToRuleTile_UsesRuleSpritesWhenProvided()
        {
            var ruleSprite = CreateSprite("rule");
            var randomSprite = CreateSprite("random");
            var rule = new FakeSmartTileRule();
            rule.Sprites.Add(ruleSprite);
            rule.Sprites.Add(randomSprite);
            var smartTile = SmartTileWithRules(rule);

            var tile = Convert(smartTile);

            CollectionAssert.AreEqual(
                new[] { ruleSprite, randomSprite },
                tile.m_TilingRules[0].m_Sprites);
        }

        [Test]
        public void ToRuleTile_FallsBackToDefaultSpriteWhenRuleHasNoSprites()
        {
            var defaultSprite = CreateSprite("default");
            var smartTile = SmartTileWithRules(new FakeSmartTileRule());

            var tile = Convert(smartTile, fallbackDefaultSprite: defaultSprite);

            Assert.AreSame(defaultSprite, tile.m_DefaultSprite);
            CollectionAssert.AreEqual(
                new[] { defaultSprite },
                tile.m_TilingRules[0].m_Sprites);
        }

        [Test]
        public void ToRuleTile_ClampsAnimationSpeedsAndConvertsDoubles()
        {
            var smartTile = SmartTileWithRules(new FakeSmartTileRule
            {
                MinAnimationSpeed = -2.5d,
                MaxAnimationSpeed = 1.75d,
            });

            var tile = Convert(smartTile);

            Assert.AreEqual(0f, tile.m_TilingRules[0].m_MinAnimationSpeed);
            Assert.AreEqual(1.75f, tile.m_TilingRules[0].m_MaxAnimationSpeed);
        }

        [Test]
        public void ToRuleTile_UnknownConditionOptionIdThrows()
        {
            var rule = new FakeSmartTileRule();
            rule.Neighbors.Add(new FakeSmartTileNeighbor
            {
                Cell = Vector2Int.zero,
                Condition = "SortOfThis",
            });
            var smartTile = SmartTileWithRules(rule);

            var error = Assert.Throws<ArgumentException>(() => Convert(smartTile));
            StringAssert.Contains("SortOfThis", error!.Message);
            StringAssert.Contains("Condition", error.Message);
        }

        [Test]
        public void ToRuleTile_UnknownOutputOptionIdThrows()
        {
            var smartTile = SmartTileWithRules(new FakeSmartTileRule
            {
                Output = "Sometimes",
            });

            var error = Assert.Throws<ArgumentException>(() => Convert(smartTile));
            StringAssert.Contains("Sometimes", error!.Message);
            StringAssert.Contains("Output", error.Message);
        }

        [Test]
        public void ToRuleTile_UnknownColliderOptionIdThrows()
        {
            var smartTile = SmartTileWithRules(new FakeSmartTileRule());
            smartTile.DefaultCollider = "Circle";

            var error = Assert.Throws<ArgumentException>(() => Convert(smartTile));
            StringAssert.Contains("Circle", error!.Message);
            StringAssert.Contains("Collider", error.Message);
        }

        [Test]
        public void ToRuleTile_InheritsConditionWithoutTileValueIdThrows()
        {
            var rule = new FakeSmartTileRule();
            rule.Neighbors.Add(new FakeSmartTileNeighbor
            {
                Cell = new Vector2Int(1, -1),
                Condition = NeoSmartTileOptionIds.ConditionInheritsFromType,
            });
            var smartTile = SmartTileWithRules(rule);

            var error = Assert.Throws<InvalidOperationException>(() => Convert(smartTile));
            StringAssert.Contains("rule 0", error!.Message);
            StringAssert.Contains("(1, -1)", error.Message);
            StringAssert.Contains(
                NeoSmartTileOptionIds.ConditionInheritsFromType,
                error.Message);
        }

        private NeoRuleTile Convert(
            INeoSmartTile smartTile,
            INeoSmartTileNeighborMatcher? matcher = null,
            Sprite? fallbackDefaultSprite = null)
        {
            var tile = NeoSmartTileRuleTileConverter.ToRuleTile(
                smartTile,
                matcher,
                fallbackDefaultSprite);
            createdObjects.Add(tile);
            return tile;
        }

        private static FakeSmartTile SmartTileWithRules(params FakeSmartTileRule[] rules)
        {
            var smartTile = new FakeSmartTile();
            smartTile.Rules.AddRange(rules);
            return smartTile;
        }

        private Sprite CreateSprite(string name)
        {
            var texture = new Texture2D(1, 1);
            var sprite = Sprite.Create(
                texture,
                new Rect(0, 0, 1, 1),
                new Vector2(0.5f, 0.5f));
            sprite.name = name;
            createdObjects.Add(texture);
            createdObjects.Add(sprite);
            return sprite;
        }

        private sealed class FakeSmartTile : INeoSmartTile
        {
            public string DefaultCollider { get; set; } =
                NeoSmartTileOptionIds.ColliderSprite;

            public List<INeoSmartTileRule> Rules { get; } = new();

            IReadOnlyList<INeoSmartTileRule> INeoSmartTile.Rules => Rules;
        }

        private sealed class FakeSmartTileRule : INeoSmartTileRule
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

        private sealed class FakeSmartTileNeighbor : INeoSmartTileNeighbor
        {
            public Vector2Int Cell { get; set; }

            public string Condition { get; set; } = NeoSmartTileOptionIds.ConditionThis;

            public string? TileValueId { get; set; }
        }

        private sealed class RecordingNeighborMatcher : INeoSmartTileNeighborMatcher
        {
            public List<NeoRuleTileNeighbor> Observed { get; } = new();

            public bool Matches(NeoRuleTileNeighbor neighbor, TileBase? other)
            {
                Observed.Add(neighbor);
                return false;
            }
        }
    }
}
