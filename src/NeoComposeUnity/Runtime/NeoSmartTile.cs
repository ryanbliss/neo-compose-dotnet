// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Bridge contract implemented by generated tile classes that carry smart
    /// tile data. The code generator emits implementations of
    /// <see cref="INeoSmartTileSource"/> (and the interfaces it exposes) so the
    /// runtime can consume authored smart tile data without reflection.
    /// </summary>
    public interface INeoSmartTileSource
    {
        INeoSmartTile? SmartTile { get; }
    }

    public interface INeoSmartTile
    {
        /// <summary>
        /// Collider enum option id. See <see cref="NeoSmartTileOptionIds"/>.
        /// </summary>
        string DefaultCollider { get; }

        IReadOnlyList<INeoSmartTileRule> Rules { get; }
    }

    public interface INeoSmartTileRule
    {
        IReadOnlyList<INeoSmartTileNeighbor> Neighbors { get; }

        IReadOnlyList<Sprite> Sprites { get; }

        /// <summary>
        /// Output enum option id. See <see cref="NeoSmartTileOptionIds"/>.
        /// </summary>
        string Output { get; }

        /// <summary>
        /// Collider enum option id. See <see cref="NeoSmartTileOptionIds"/>.
        /// </summary>
        string Collider { get; }

        double MinAnimationSpeed { get; }

        double MaxAnimationSpeed { get; }
    }

    public interface INeoSmartTileNeighbor
    {
        Vector2Int Cell { get; }

        /// <summary>
        /// Condition enum option id. See <see cref="NeoSmartTileOptionIds"/>.
        /// A cell without a stored neighbor entry means "don't care", so there
        /// is no option id for that state.
        /// </summary>
        string Condition { get; }

        /// <summary>
        /// The referenced tile value id for the inherits-from-type conditions.
        /// </summary>
        string? TileValueId { get; }
    }

    public enum NeoSmartTileNeighborKind
    {
        DontCare = 0,
        This = 1,
        NotThis = 2,
        ExactTile = 3,
        NotExactTile = 4,
        InheritsFromType = 5,
        NotInheritsFromType = 6,
    }

    public enum NeoSmartTileOutputMode
    {
        Single = 0,
        Random = 1,
        Animation = 2,
    }

    /// <summary>
    /// Reserved parse target for future transform option ids. Smart tile data
    /// has no transform fields in v1; converted rules keep Unity's Fixed
    /// transforms.
    /// </summary>
    public enum NeoSmartTileTransformMode
    {
        Fixed = 0,
        Rotated = 1,
        MirrorX = 2,
        MirrorY = 3,
        MirrorXY = 4,
        RotatedMirror = 5,
    }

    /// <summary>
    /// Smart tile enum option ids authored on the web side. These exact ids
    /// are pinned in the neo-compose repo at
    /// <c>src/models/custom-types/world-system-types.ts</c> — keep both sides
    /// in sync.
    /// </summary>
    public static class NeoSmartTileOptionIds
    {
        public const string ConditionThis = "This";
        public const string ConditionNotThis = "NotThis";
        public const string ConditionInheritsFromType = "InheritsFromType";
        public const string ConditionNotInheritsFromType = "NotInheritsFromType";

        public const string OutputSingle = "Single";
        public const string OutputRandom = "Random";
        public const string OutputAnimation = "Animation";

        public const string ColliderNone = "None";
        public const string ColliderSprite = "Sprite";
        public const string ColliderGrid = "Grid";

        public static NeoSmartTileNeighborKind ParseCondition(string condition)
        {
            switch (condition)
            {
                case ConditionThis:
                    return NeoSmartTileNeighborKind.This;
                case ConditionNotThis:
                    return NeoSmartTileNeighborKind.NotThis;
                case ConditionInheritsFromType:
                    return NeoSmartTileNeighborKind.InheritsFromType;
                case ConditionNotInheritsFromType:
                    return NeoSmartTileNeighborKind.NotInheritsFromType;
                default:
                    throw new ArgumentException(
                        "Unrecognized smart tile neighbor Condition option id "
                            + $"'{condition}'.",
                        nameof(condition));
            }
        }

        public static NeoSmartTileOutputMode ParseOutput(string output)
        {
            switch (output)
            {
                case OutputSingle:
                    return NeoSmartTileOutputMode.Single;
                case OutputRandom:
                    return NeoSmartTileOutputMode.Random;
                case OutputAnimation:
                    return NeoSmartTileOutputMode.Animation;
                default:
                    throw new ArgumentException(
                        $"Unrecognized smart tile rule Output option id '{output}'.",
                        nameof(output));
            }
        }

        public static Tile.ColliderType ParseCollider(string collider)
        {
            switch (collider)
            {
                case ColliderNone:
                    return Tile.ColliderType.None;
                case ColliderSprite:
                    return Tile.ColliderType.Sprite;
                case ColliderGrid:
                    return Tile.ColliderType.Grid;
                default:
                    throw new ArgumentException(
                        $"Unrecognized smart tile Collider option id '{collider}'.",
                        nameof(collider));
            }
        }
    }

    /// <summary>
    /// A neighbor condition registered on a <see cref="NeoRuleTile"/> that
    /// Unity's built-in This/NotThis matching cannot evaluate. Matching is
    /// delegated to an <see cref="INeoSmartTileNeighborMatcher"/> supplied by
    /// the renderer.
    /// </summary>
    public sealed class NeoRuleTileNeighbor
    {
        public NeoRuleTileNeighbor(
            Vector3Int offset,
            NeoSmartTileNeighborKind kind,
            string? tileValueId = null)
        {
            Offset = offset;
            Kind = kind;
            TileValueId = tileValueId;
        }

        public Vector3Int Offset { get; }

        public NeoSmartTileNeighborKind Kind { get; }

        public string? TileValueId { get; }
    }

    public interface INeoSmartTileNeighborMatcher
    {
        bool Matches(NeoRuleTileNeighbor neighbor, TileBase? other);
    }

    public sealed class NeoRuleTile : RuleTile
    {
        [SerializeField]
        private List<NeoRuleTileCustomNeighbor> serializedCustomNeighbors = new();

        [NonSerialized]
        private Dictionary<int, NeoRuleTileNeighbor>? customNeighbors;

        private INeoSmartTileNeighborMatcher? matcher;

        public void Configure(INeoSmartTileNeighborMatcher? neighborMatcher)
        {
            matcher = neighborMatcher;
            customNeighbors = null;
        }

        public int RegisterCustomNeighbor(NeoRuleTileNeighbor neighbor)
        {
            if (neighbor == null) throw new ArgumentNullException(nameof(neighbor));
            var neighbors = CustomNeighbors;
            int id = 1000 + neighbors.Count;
            neighbors[id] = neighbor;
            serializedCustomNeighbors.Add(NeoRuleTileCustomNeighbor.From(id, neighbor));
            return id;
        }

        public override bool RuleMatch(int neighbor, TileBase other)
        {
            if (CustomNeighbors.TryGetValue(neighbor, out var customNeighbor))
            {
                return matcher?.Matches(customNeighbor, other) ?? false;
            }

            return base.RuleMatch(neighbor, other);
        }

        private Dictionary<int, NeoRuleTileNeighbor> CustomNeighbors
        {
            get
            {
                if (customNeighbors != null) return customNeighbors;
                customNeighbors = new Dictionary<int, NeoRuleTileNeighbor>();
                foreach (var entry in serializedCustomNeighbors)
                {
                    customNeighbors[entry.Id] = entry.ToNeighbor();
                }
                return customNeighbors;
            }
        }
    }

    [Serializable]
    internal sealed class NeoRuleTileCustomNeighbor
    {
        [SerializeField]
        private int id;
        [SerializeField]
        private Vector3Int offset;
        [SerializeField]
        private NeoSmartTileNeighborKind kind;
        [SerializeField]
        private string tileValueId = "";

        public int Id => id;

        public static NeoRuleTileCustomNeighbor From(
            int id,
            NeoRuleTileNeighbor neighbor)
        {
            return new NeoRuleTileCustomNeighbor
            {
                id = id,
                offset = neighbor.Offset,
                kind = neighbor.Kind,
                tileValueId = neighbor.TileValueId ?? "",
            };
        }

        public NeoRuleTileNeighbor ToNeighbor()
        {
            return new NeoRuleTileNeighbor(
                offset,
                kind,
                string.IsNullOrWhiteSpace(tileValueId) ? null : tileValueId);
        }
    }

    public static class NeoSmartTileRuleTileConverter
    {
        public static NeoRuleTile ToRuleTile(
            INeoSmartTile smartTile,
            INeoSmartTileNeighborMatcher? matcher = null,
            Sprite? fallbackDefaultSprite = null)
        {
            if (smartTile == null) throw new ArgumentNullException(nameof(smartTile));

            var tile = ScriptableObject.CreateInstance<NeoRuleTile>();
            tile.name = "Neo Smart Tile";
            tile.Configure(matcher);
            tile.m_DefaultSprite = fallbackDefaultSprite;
            tile.m_DefaultColliderType =
                NeoSmartTileOptionIds.ParseCollider(smartTile.DefaultCollider);

            var rules = smartTile.Rules;
            for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex += 1)
            {
                var smartRule = rules[ruleIndex];
                var rule = new RuleTile.TilingRule
                {
                    m_Sprites = SpritesForRule(smartRule.Sprites, tile.m_DefaultSprite),
                    m_MinAnimationSpeed = ToAnimationSpeed(smartRule.MinAnimationSpeed),
                    m_MaxAnimationSpeed = ToAnimationSpeed(smartRule.MaxAnimationSpeed),
                    m_Output = ToUnityOutput(
                        NeoSmartTileOptionIds.ParseOutput(smartRule.Output)),
                    m_ColliderType =
                        NeoSmartTileOptionIds.ParseCollider(smartRule.Collider),
                };

                rule.ApplyNeighbors(BuildUnityNeighbors(smartRule, ruleIndex, tile));
                tile.m_TilingRules.Add(rule);
            }

            tile.UpdateNeighborPositions();
            return tile;
        }

        private static Dictionary<Vector3Int, int> BuildUnityNeighbors(
            INeoSmartTileRule smartRule,
            int ruleIndex,
            NeoRuleTile tile)
        {
            var result = new Dictionary<Vector3Int, int>();
            foreach (var neighbor in smartRule.Neighbors)
            {
                var offset = new Vector3Int(neighbor.Cell.x, neighbor.Cell.y, 0);
                result[offset] = ToUnityNeighbor(neighbor, ruleIndex, offset, tile);
            }
            return result;
        }

        private static int ToUnityNeighbor(
            INeoSmartTileNeighbor neighbor,
            int ruleIndex,
            Vector3Int offset,
            NeoRuleTile tile)
        {
            var kind = NeoSmartTileOptionIds.ParseCondition(neighbor.Condition);
            if (kind == NeoSmartTileNeighborKind.This)
            {
                return RuleTile.TilingRuleOutput.Neighbor.This;
            }
            if (kind == NeoSmartTileNeighborKind.NotThis)
            {
                return RuleTile.TilingRuleOutput.Neighbor.NotThis;
            }

            // ParseCondition only returns This, NotThis, or the two
            // inherits-from-type kinds, which require a referenced tile value.
            if (string.IsNullOrEmpty(neighbor.TileValueId))
            {
                throw new InvalidOperationException(
                    $"Smart tile rule {ruleIndex} neighbor at cell "
                        + $"({neighbor.Cell.x}, {neighbor.Cell.y}) uses condition "
                        + $"'{neighbor.Condition}' but has no TileValueId.");
            }

            return tile.RegisterCustomNeighbor(
                new NeoRuleTileNeighbor(offset, kind, neighbor.TileValueId));
        }

        private static Sprite[] SpritesForRule(
            IReadOnlyList<Sprite> sprites,
            Sprite? fallbackDefaultSprite)
        {
            if (sprites.Count > 0)
            {
                var copied = new Sprite[sprites.Count];
                for (int index = 0; index < sprites.Count; index += 1)
                {
                    copied[index] = sprites[index];
                }
                return copied;
            }

            var fallback = new Sprite[1];
            if (fallbackDefaultSprite != null) fallback[0] = fallbackDefaultSprite;
            return fallback;
        }

        private static float ToAnimationSpeed(double value)
        {
            return Mathf.Max(0f, (float)value);
        }

        private static RuleTile.TilingRuleOutput.OutputSprite ToUnityOutput(
            NeoSmartTileOutputMode output)
        {
            switch (output)
            {
                case NeoSmartTileOutputMode.Random:
                    return RuleTile.TilingRuleOutput.OutputSprite.Random;
                case NeoSmartTileOutputMode.Animation:
                    return RuleTile.TilingRuleOutput.OutputSprite.Animation;
                default:
                    return RuleTile.TilingRuleOutput.OutputSprite.Single;
            }
        }
    }
}
