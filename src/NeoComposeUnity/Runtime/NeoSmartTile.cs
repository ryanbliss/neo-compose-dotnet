// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace NeoCompose.Runtime
{
    public enum NeoSmartTileNeighborKind
    {
        DontCare = 0,
        This = 1,
        NotThis = 2,
        ExactTile = 3,
        NotExactTile = 4,
        InheritsFromType = 5,
        NotInheritsFromType = 6,
        Tag = 7,
        NotTag = 8,
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

    public sealed class NeoSmartTile
    {
        public Sprite? DefaultSprite { get; set; }
        public GameObject? DefaultGameObject { get; set; }
        public Tile.ColliderType DefaultColliderType { get; set; } =
            Tile.ColliderType.Sprite;
        public List<NeoSmartTileRule> Rules { get; } = new();
    }

    public sealed class NeoSmartTileRule
    {
        public List<NeoSmartTileNeighbor> Neighbors { get; } = new();
        public List<Sprite> Sprites { get; } = new();
        public GameObject? GameObject { get; set; }
        public float MinAnimationSpeed { get; set; } = 1f;
        public float MaxAnimationSpeed { get; set; } = 1f;
        public float PerlinScale { get; set; } = 0.5f;
        public NeoSmartTileOutputMode Output { get; set; } = NeoSmartTileOutputMode.Single;
        public Tile.ColliderType ColliderType { get; set; } = Tile.ColliderType.Sprite;
        public NeoSmartTileTransformMode RuleTransform { get; set; } =
            NeoSmartTileTransformMode.Fixed;
        public NeoSmartTileTransformMode RandomTransform { get; set; } =
            NeoSmartTileTransformMode.Fixed;
    }

    public sealed class NeoSmartTileNeighbor
    {
        public NeoSmartTileNeighbor()
        {
        }

        public NeoSmartTileNeighbor(Vector3Int offset, NeoSmartTileNeighborKind kind)
        {
            Offset = offset;
            Kind = kind;
        }

        public Vector3Int Offset { get; set; }
        public NeoSmartTileNeighborKind Kind { get; set; }
        public NeoGeneratedCustomValue? Tile { get; set; }
        public string? TileValueId { get; set; }
        public string? TypeId { get; set; }
        public string? Tag { get; set; }
    }

    public interface INeoSmartTileNeighborMatcher
    {
        bool Matches(NeoSmartTileNeighbor neighbor, TileBase? other);
    }

    public sealed class NeoRuleTile : RuleTile
    {
        [SerializeField]
        private List<NeoRuleTileCustomNeighbor> serializedCustomNeighbors = new();

        [NonSerialized]
        private Dictionary<int, NeoSmartTileNeighbor>? customNeighbors;

        private INeoSmartTileNeighborMatcher? matcher;

        public void Configure(INeoSmartTileNeighborMatcher? neighborMatcher)
        {
            matcher = neighborMatcher;
            customNeighbors = null;
        }

        public int RegisterCustomNeighbor(NeoSmartTileNeighbor neighbor)
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

        private Dictionary<int, NeoSmartTileNeighbor> CustomNeighbors
        {
            get
            {
                if (customNeighbors != null) return customNeighbors;
                customNeighbors = new Dictionary<int, NeoSmartTileNeighbor>();
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
        [SerializeField]
        private string typeId = "";
        [SerializeField]
        private string tag = "";

        public int Id => id;

        public static NeoRuleTileCustomNeighbor From(
            int id,
            NeoSmartTileNeighbor neighbor)
        {
            return new NeoRuleTileCustomNeighbor
            {
                id = id,
                offset = neighbor.Offset,
                kind = neighbor.Kind,
                tileValueId = neighbor.TileValueId ?? neighbor.Tile?.valueId ?? "",
                typeId = neighbor.TypeId ?? "",
                tag = neighbor.Tag ?? "",
            };
        }

        public NeoSmartTileNeighbor ToNeighbor()
        {
            return new NeoSmartTileNeighbor(offset, kind)
            {
                TileValueId = string.IsNullOrWhiteSpace(tileValueId)
                    ? null
                    : tileValueId,
                TypeId = string.IsNullOrWhiteSpace(typeId) ? null : typeId,
                Tag = string.IsNullOrWhiteSpace(tag) ? null : tag,
            };
        }
    }

    public static class NeoSmartTileRuleTileConverter
    {
        public static NeoRuleTile ToRuleTile(
            NeoSmartTile smartTile,
            INeoSmartTileNeighborMatcher? matcher = null,
            Sprite? fallbackDefaultSprite = null)
        {
            if (smartTile == null) throw new ArgumentNullException(nameof(smartTile));

            var tile = ScriptableObject.CreateInstance<NeoRuleTile>();
            tile.name = "Neo Smart Tile";
            tile.Configure(matcher);
            tile.m_DefaultSprite = smartTile.DefaultSprite ?? fallbackDefaultSprite;
            tile.m_DefaultGameObject = smartTile.DefaultGameObject;
            tile.m_DefaultColliderType = smartTile.DefaultColliderType;

            foreach (var smartRule in smartTile.Rules)
            {
                var rule = new RuleTile.TilingRule
                {
                    m_Sprites = SpritesForRule(smartRule, tile.m_DefaultSprite),
                    m_GameObject = smartRule.GameObject,
                    m_MinAnimationSpeed = Mathf.Max(0f, smartRule.MinAnimationSpeed),
                    m_MaxAnimationSpeed = Mathf.Max(0f, smartRule.MaxAnimationSpeed),
                    m_PerlinScale = Mathf.Max(0.0001f, smartRule.PerlinScale),
                    m_Output = ToUnityOutput(smartRule.Output),
                    m_ColliderType = smartRule.ColliderType,
                    m_RandomTransform = ToUnityTransform(smartRule.RandomTransform),
                    m_RuleTransform = ToUnityTransform(smartRule.RuleTransform),
                };

                rule.ApplyNeighbors(BuildUnityNeighbors(smartRule, tile));
                tile.m_TilingRules.Add(rule);
            }

            tile.UpdateNeighborPositions();
            return tile;
        }

        public static bool TryRead(object? source, out NeoSmartTile smartTile)
        {
            smartTile = new NeoSmartTile();
            if (source == null) return false;
            if (source is NeoSmartTile typed)
            {
                smartTile = typed;
                return true;
            }

            bool foundAny = false;
            if (TryReadSprite(source, "DefaultSprite", out var defaultSprite)
                || TryReadSprite(source, "Sprite", out defaultSprite))
            {
                smartTile.DefaultSprite = defaultSprite;
                foundAny = true;
            }
            if (TryReadGameObject(source, "DefaultGameObject", out var defaultGameObject)
                || TryReadGameObject(source, "GameObject", out defaultGameObject))
            {
                smartTile.DefaultGameObject = defaultGameObject;
                foundAny = true;
            }
            if (TryReadColliderType(source, "DefaultColliderType", out var defaultCollider)
                || TryReadColliderType(source, "ColliderType", out defaultCollider))
            {
                smartTile.DefaultColliderType = defaultCollider;
                foundAny = true;
            }

            var rawRules = ReadOptionalProperty(source, "Rules")
                ?? ReadOptionalProperty(source, "TilingRules")
                ?? ReadOptionalProperty(source, "SmartTileRules");
            if (rawRules is IEnumerable enumerable)
            {
                foreach (var rawRule in enumerable)
                {
                    if (rawRule == null) continue;
                    smartTile.Rules.Add(ReadRule(rawRule));
                }
                foundAny = true;
            }

            return foundAny;
        }

        private static Dictionary<Vector3Int, int> BuildUnityNeighbors(
            NeoSmartTileRule smartRule,
            NeoRuleTile tile)
        {
            var result = new Dictionary<Vector3Int, int>();
            foreach (var neighbor in smartRule.Neighbors)
            {
                int? unityNeighbor = ToUnityNeighbor(neighbor, tile);
                if (unityNeighbor == null) continue;
                result[neighbor.Offset] = unityNeighbor.Value;
            }
            return result;
        }

        private static int? ToUnityNeighbor(NeoSmartTileNeighbor neighbor, NeoRuleTile tile)
        {
            switch (neighbor.Kind)
            {
                case NeoSmartTileNeighborKind.DontCare:
                    return null;
                case NeoSmartTileNeighborKind.This:
                    return RuleTile.TilingRuleOutput.Neighbor.This;
                case NeoSmartTileNeighborKind.NotThis:
                    return RuleTile.TilingRuleOutput.Neighbor.NotThis;
                default:
                    return tile.RegisterCustomNeighbor(neighbor);
            }
        }

        private static Sprite[] SpritesForRule(
            NeoSmartTileRule rule,
            Sprite? fallbackDefaultSprite)
        {
            if (rule.Sprites.Count > 0)
            {
                return rule.Sprites.ToArray();
            }
            var sprites = new Sprite[1];
            if (fallbackDefaultSprite != null) sprites[0] = fallbackDefaultSprite;
            return sprites;
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

        private static NeoSmartTileRule ReadRule(object source)
        {
            if (source is NeoSmartTileRule typed) return typed;

            var rule = new NeoSmartTileRule();
            AddSprites(rule.Sprites, ReadOptionalProperty(source, "Sprites"));
            if (rule.Sprites.Count == 0
                && TryReadSprite(source, "Sprite", out var sprite)
                && sprite != null)
            {
                rule.Sprites.Add(sprite);
            }

            if (TryReadGameObject(source, "GameObject", out var gameObject))
            {
                rule.GameObject = gameObject;
            }
            if (TryReadFloat(source, "MinAnimationSpeed", out var minSpeed)
                || TryReadFloat(source, "AnimationSpeed", out minSpeed))
            {
                rule.MinAnimationSpeed = minSpeed;
            }
            if (TryReadFloat(source, "MaxAnimationSpeed", out var maxSpeed)
                || TryReadFloat(source, "AnimationSpeed", out maxSpeed))
            {
                rule.MaxAnimationSpeed = maxSpeed;
            }
            if (TryReadFloat(source, "PerlinScale", out var perlinScale))
            {
                rule.PerlinScale = perlinScale;
            }
            if (TryReadEnum(source, "Output", out NeoSmartTileOutputMode output)
                || TryReadEnum(source, "OutputMode", out output)
                || TryReadEnum(source, "OutputSprite", out output))
            {
                rule.Output = output;
            }
            if (TryReadEnum(source, "RuleTransform", out NeoSmartTileTransformMode ruleTransform)
                || TryReadEnum(source, "Transform", out ruleTransform))
            {
                rule.RuleTransform = ruleTransform;
            }
            if (TryReadEnum(source, "RandomTransform", out NeoSmartTileTransformMode randomTransform))
            {
                rule.RandomTransform = randomTransform;
            }
            if (TryReadColliderType(source, "ColliderType", out var colliderType))
            {
                rule.ColliderType = colliderType;
            }

            var rawNeighbors = ReadOptionalProperty(source, "Neighbors");
            if (rawNeighbors is IEnumerable enumerable)
            {
                foreach (var rawNeighbor in enumerable)
                {
                    if (rawNeighbor == null) continue;
                    rule.Neighbors.Add(ReadNeighbor(rawNeighbor));
                }
            }

            return rule;
        }

        private static NeoSmartTileNeighbor ReadNeighbor(object source)
        {
            if (source is NeoSmartTileNeighbor typed) return typed;

            var neighbor = new NeoSmartTileNeighbor();
            if (TryReadVector3Int(source, "Offset", out var offset)
                || TryReadVector3Int(source, "Position", out offset)
                || TryReadVector3Int(source, "Cell", out offset))
            {
                neighbor.Offset = offset;
            }
            if (TryReadEnum(source, "Kind", out NeoSmartTileNeighborKind kind)
                || TryReadEnum(source, "State", out kind)
                || TryReadEnum(source, "Neighbor", out kind)
                || TryReadEnum(source, "Condition", out kind))
            {
                neighbor.Kind = kind;
            }
            neighbor.Tile = ReadOptionalProperty(source, "Tile") as NeoGeneratedCustomValue
                ?? ReadOptionalProperty(source, "TileValue") as NeoGeneratedCustomValue
                ?? ReadOptionalProperty(source, "Value") as NeoGeneratedCustomValue;
            neighbor.TileValueId = ReadOptionalProperty(source, "TileValueId") as string
                ?? ReadOptionalProperty(source, "TileId") as string
                ?? neighbor.Tile?.valueId;
            neighbor.TypeId = ReadOptionalProperty(source, "TypeId") as string
                ?? ReadOptionalProperty(source, "TileTypeId") as string
                ?? ReadOptionalProperty(source, "BaseTypeId") as string;
            neighbor.Tag = ReadOptionalProperty(source, "Tag") as string
                ?? ReadOptionalProperty(source, "Category") as string;
            return neighbor;
        }

        private static void AddSprites(List<Sprite> sprites, object? value)
        {
            if (value == null) return;
            if (value is Sprite sprite)
            {
                sprites.Add(sprite);
                return;
            }
            if (value is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item is Sprite itemSprite) sprites.Add(itemSprite);
                }
            }
        }

        private static bool TryReadSprite(
            object source,
            string propertyName,
            out Sprite? sprite)
        {
            sprite = ReadOptionalProperty(source, propertyName) as Sprite;
            return sprite != null;
        }

        private static bool TryReadGameObject(
            object source,
            string propertyName,
            out GameObject? gameObject)
        {
            gameObject = ReadOptionalProperty(source, propertyName) as GameObject;
            return gameObject != null;
        }

        private static bool TryReadVector3Int(
            object source,
            string propertyName,
            out Vector3Int value)
        {
            value = default;
            var raw = ReadOptionalProperty(source, propertyName);
            if (raw is Vector3Int vector3Int)
            {
                value = vector3Int;
                return true;
            }
            if (raw is Vector2Int vector2Int)
            {
                value = new Vector3Int(vector2Int.x, vector2Int.y, 0);
                return true;
            }
            var sdkVector3 = NeoGeneratedTypesSupport.ReadVector3IntValue(raw);
            if (sdkVector3 != null)
            {
                value = sdkVector3.Value;
                return true;
            }
            var sdkVector2 = NeoGeneratedTypesSupport.ReadVector2IntValue(raw);
            if (sdkVector2 == null) return false;
            value = new Vector3Int(sdkVector2.Value.x, sdkVector2.Value.y, 0);
            return true;
        }

        private static bool TryReadFloat(
            object source,
            string propertyName,
            out float value)
        {
            value = 0f;
            var raw = ReadOptionalProperty(source, propertyName);
            if (raw == null) return false;
            switch (raw)
            {
                case float f:
                    value = f;
                    return true;
                case double d:
                    value = (float)d;
                    return true;
                case int i:
                    value = i;
                    return true;
                case long l:
                    value = l;
                    return true;
                case decimal m:
                    value = (float)m;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryReadColliderType(
            object source,
            string propertyName,
            out Tile.ColliderType value)
        {
            value = Tile.ColliderType.Sprite;
            var raw = ReadOptionalProperty(source, propertyName);
            if (raw is Tile.ColliderType colliderType)
            {
                value = colliderType;
                return true;
            }
            if (raw is int intValue && Enum.IsDefined(typeof(Tile.ColliderType), intValue))
            {
                value = (Tile.ColliderType)intValue;
                return true;
            }
            if (raw is string stringValue)
            {
                return Enum.TryParse(stringValue, true, out value);
            }
            return false;
        }

        private static bool TryReadEnum<TEnum>(
            object source,
            string propertyName,
            out TEnum value)
            where TEnum : struct
        {
            value = default;
            var raw = ReadOptionalProperty(source, propertyName);
            if (raw is TEnum typed)
            {
                value = typed;
                return true;
            }
            if (raw is string stringValue)
            {
                string normalized = stringValue.Replace("-", "")
                    .Replace("_", "")
                    .Replace(" ", "");
                foreach (var name in Enum.GetNames(typeof(TEnum)))
                {
                    if (!string.Equals(
                        name,
                        normalized,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    value = (TEnum)Enum.Parse(typeof(TEnum), name);
                    return true;
                }
                return Enum.TryParse(stringValue, true, out value);
            }
            if (raw == null) return false;
            if (!IsIntegerLike(raw)) return false;
            int intValue = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
            if (!Enum.IsDefined(typeof(TEnum), intValue)) return false;
            value = (TEnum)Enum.ToObject(typeof(TEnum), intValue);
            return true;
        }

        private static bool IsIntegerLike(object value)
        {
            return value is byte
                || value is sbyte
                || value is short
                || value is ushort
                || value is int
                || value is uint
                || value is long
                || value is ulong;
        }

        private static object? ReadOptionalProperty(object source, string propertyName)
        {
            var properties = source.GetType().GetProperties(
                BindingFlags.Public | BindingFlags.Instance);
            foreach (var property in properties)
            {
                if (property.GetIndexParameters().Length > 0) continue;
                if (!string.Equals(
                    property.Name,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                try
                {
                    return property.GetValue(source);
                }
                catch (TargetInvocationException)
                {
                    return null;
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            }
            return null;
        }
    }
}
