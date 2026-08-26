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

        /// <summary>
        /// Rule transform enum option id. See <see cref="NeoSmartTileOptionIds"/>.
        /// </summary>
        string RuleTransform { get; }

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

        /// <summary>The relation-selected tile class.</summary>
        string? TileClassId { get; }
    }

    public enum NeoSmartTileNeighborKind
    {
        DontCare = 0,
        This = 1,
        NotThis = 2,
        ExactTile = 3,
        NotExactTile = 4,
        InheritsFromClass = 5,
        NotInheritsFromClass = 6,
    }

    public enum NeoSmartTileOutputMode
    {
        Single = 0,
        Random = 1,
        Animation = 2,
    }

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
    /// <c>src/models/classes/world-system-classes.generated.ts</c>, generated
    /// from <c>system-schema/Enums/*.neo</c> — keep both sides in sync.
    ///
    /// Every id carries the reserved <c>system_</c> prefix that P39 §1.1c
    /// gives Neo-authored records. The UUID inside is unchanged: the
    /// re-identification is a pure prefix transform, so an id from before the
    /// migration is this one with the prefix removed.
    /// </summary>
    public static class NeoSmartTileOptionIds
    {
        public const string ConditionThis = "system_fe185d9d-63c5-5f71-be71-408db18a84ee";
        public const string ConditionNotThis = "system_53438afa-206b-5bec-a202-c9b60d1ad8b6";
        public const string ConditionInheritsFromClass = "system_04d1dedf-8b84-5638-8db1-158c4243ef3f";
        public const string ConditionNotInheritsFromClass = "system_bc62a20a-a386-5224-b54b-12c00e7c576b";

        public const string OutputSingle = "system_b76ada21-68e0-5b52-bdfe-3b1f95a8c896";
        public const string OutputRandom = "system_717e141c-a3af-535e-9f30-da2a9241803d";
        public const string OutputAnimation = "system_649f99d0-c726-5f20-aeea-786a0776f53f";

        public const string ColliderNone = "system_cfca7937-a285-5b1a-97f4-6c088137a517";
        public const string ColliderSprite = "system_6cf2f768-993c-5e9c-8bcb-60e1a0d8c575";
        public const string ColliderGrid = "system_5e0f42af-fff5-5a65-9844-f11f36ae55a8";

        public const string TransformFixed = "system_3f16d1d6-ec8b-532b-ad8b-23af9ea01172";
        public const string TransformRotated = "system_d6e0f63f-910c-5e51-ac77-250c7f606664";
        public const string TransformMirrorX = "system_00edc26a-6ef4-57db-a290-f1f8300146e9";
        public const string TransformMirrorY = "system_c1c2577b-870f-5305-b206-3c70dfeb775b";
        public const string TransformMirrorXY = "system_35b53059-1836-5fac-bcde-291274dcebc7";
        public const string TransformRotatedMirror = "system_9da42749-b317-5558-8f76-0a9fcb69229d";

        public static NeoSmartTileNeighborKind ParseCondition(string condition)
        {
            switch (condition)
            {
                case ConditionThis:
                    return NeoSmartTileNeighborKind.This;
                case ConditionNotThis:
                    return NeoSmartTileNeighborKind.NotThis;
                case ConditionInheritsFromClass:
                    return NeoSmartTileNeighborKind.InheritsFromClass;
                case ConditionNotInheritsFromClass:
                    return NeoSmartTileNeighborKind.NotInheritsFromClass;
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

        public static NeoSmartTileTransformMode ParseTransform(string transform)
        {
            switch (transform)
            {
                case TransformFixed:
                    return NeoSmartTileTransformMode.Fixed;
                case TransformRotated:
                    return NeoSmartTileTransformMode.Rotated;
                case TransformMirrorX:
                    return NeoSmartTileTransformMode.MirrorX;
                case TransformMirrorY:
                    return NeoSmartTileTransformMode.MirrorY;
                case TransformMirrorXY:
                    return NeoSmartTileTransformMode.MirrorXY;
                case TransformRotatedMirror:
                    return NeoSmartTileTransformMode.RotatedMirror;
                default:
                    throw new ArgumentException(
                        "Unrecognized smart tile rule RuleTransform option id "
                            + $"'{transform}'.",
                        nameof(transform));
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
            string? tileClassId = null)
        {
            Offset = offset;
            Kind = kind;
            TileClassId = tileClassId;
        }

        public Vector3Int Offset { get; }

        public NeoSmartTileNeighborKind Kind { get; }

        public string? TileClassId { get; }
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
        private string tileClassId = "";

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
                tileClassId = neighbor.TileClassId ?? "",
            };
        }

        public NeoRuleTileNeighbor ToNeighbor()
        {
            return new NeoRuleTileNeighbor(
                offset,
                kind,
                string.IsNullOrWhiteSpace(tileClassId) ? null : tileClassId);
        }
    }

    public static class NeoSmartTileRuleTileConverter
    {
        /// <summary>
        /// Builds the Unity <see cref="RuleTile"/> for one authored smart tile.
        ///
        /// <para><paramref name="selfTileClassId"/> is the CLASS id of the tile
        /// this smart tile belongs to — the identity <c>This</c>/<c>NotThis</c>
        /// compare a neighbor against. The web's
        /// <c>ISmartTileNeighborContext</c> pins that identity to "ALWAYS the
        /// concrete tile class id", so both runtimes have to compare the same
        /// thing; see <see cref="ToUnityNeighbor"/> for why the built-in
        /// constants cannot express it. Null or empty falls back to Unity's
        /// built-in asset-identity matching, which is all an untyped caller
        /// can offer.</para>
        /// </summary>
        public static NeoRuleTile ToRuleTile(
            INeoSmartTile smartTile,
            INeoSmartTileNeighborMatcher? matcher = null,
            Sprite? fallbackDefaultSprite = null,
            string? selfTileClassId = null)
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
                    m_RuleTransform = ToUnityTransform(
                        NeoSmartTileOptionIds.ParseTransform(smartRule.RuleTransform)),
                };

                rule.ApplyNeighbors(BuildUnityNeighbors(
                    smartRule,
                    ruleIndex,
                    tile,
                    selfTileClassId));
                tile.m_TilingRules.Add(rule);
            }

            tile.UpdateNeighborPositions();
            return tile;
        }

        private static Dictionary<Vector3Int, int> BuildUnityNeighbors(
            INeoSmartTileRule smartRule,
            int ruleIndex,
            NeoRuleTile tile,
            string? selfTileClassId)
        {
            var result = new Dictionary<Vector3Int, int>();
            foreach (var neighbor in smartRule.Neighbors)
            {
                var offset = new Vector3Int(neighbor.Cell.x, neighbor.Cell.y, 0);
                result[offset] = ToUnityNeighbor(
                    neighbor,
                    ruleIndex,
                    offset,
                    tile,
                    selfTileClassId);
            }
            return result;
        }

        private static int ToUnityNeighbor(
            INeoSmartTileNeighbor neighbor,
            int ruleIndex,
            Vector3Int offset,
            NeoRuleTile tile,
            string? selfTileClassId)
        {
            var kind = NeoSmartTileOptionIds.ParseCondition(neighbor.Condition);
            if (kind == NeoSmartTileNeighborKind.This
                || kind == NeoSmartTileNeighborKind.NotThis)
            {
                // Unity's built-in This/NotThis compare TileBase REFERENCE
                // identity, and the renderer caches one TileBase per placement
                // value id. Two placements of the same tile class therefore
                // hold different instances and fail to see each other, so a
                // painted run of one tile never joins up. The web compares the
                // neighbor's CLASS id against the tile's own
                // (`ISmartTileNeighborContext.assetId`, documented as always
                // the concrete tile class id), which is what "same tile" means
                // for a definition-identified tile. Route both through the
                // Neo-owned exact-tile matcher against this tile's own class so
                // the two runtimes decide it identically; `NotThis` stays the
                // exact inverse, including the empty-neighbor case the matcher
                // answers false for.
                if (!string.IsNullOrEmpty(selfTileClassId))
                {
                    return tile.RegisterCustomNeighbor(
                        new NeoRuleTileNeighbor(
                            offset,
                            kind == NeoSmartTileNeighborKind.This
                                ? NeoSmartTileNeighborKind.ExactTile
                                : NeoSmartTileNeighborKind.NotExactTile,
                            selfTileClassId));
                }
                // No class identity to compare (an untyped or preview-only
                // caller): Unity's asset-identity matching is the only answer
                // available, and it is what this path did before.
                return kind == NeoSmartTileNeighborKind.This
                    ? RuleTile.TilingRuleOutput.Neighbor.This
                    : RuleTile.TilingRuleOutput.Neighbor.NotThis;
            }

            // ParseCondition only returns This, NotThis, or the two
            // inherits-from-class kinds, which require a relation-selected
            // tile class.
            string? tileClassId = neighbor.TileClassId;
            if (string.IsNullOrEmpty(tileClassId))
            {
                throw new InvalidOperationException(
                    $"Smart tile rule {ruleIndex} neighbor at cell "
                        + $"({neighbor.Cell.x}, {neighbor.Cell.y}) uses condition "
                        + $"'{neighbor.Condition}' but has no TileClassId.");
            }

            return tile.RegisterCustomNeighbor(
                new NeoRuleTileNeighbor(
                    offset,
                    kind,
                    tileClassId));
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

        private static RuleTile.TilingRuleOutput.Transform ToUnityTransform(
            NeoSmartTileTransformMode transform)
        {
            switch (transform)
            {
                case NeoSmartTileTransformMode.Rotated:
                    return RuleTile.TilingRuleOutput.Transform.Rotated;
                case NeoSmartTileTransformMode.MirrorX:
                    return RuleTile.TilingRuleOutput.Transform.MirrorX;
                case NeoSmartTileTransformMode.MirrorY:
                    return RuleTile.TilingRuleOutput.Transform.MirrorY;
                case NeoSmartTileTransformMode.MirrorXY:
                    return RuleTile.TilingRuleOutput.Transform.MirrorXY;
                case NeoSmartTileTransformMode.RotatedMirror:
                    return RuleTile.TilingRuleOutput.Transform.RotatedMirror;
                default:
                    return RuleTile.TilingRuleOutput.Transform.Fixed;
            }
        }
    }
}
