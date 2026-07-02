// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Shared tile preview/renderer asset creation helpers. Editor sync uses this
    /// to persist Tile/RuleTile assets, while the runtime renderer uses the same
    /// logic as a fallback when editor-generated assets are missing.
    /// </summary>
    public static class NeoTileAssetFactory
    {
        public static TileBase? CreateTransientTileBase(
            NeoGeneratedCustomValue value,
            INeoSmartTileNeighborMatcher? matcher = null)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var fallbackSprite = ResolveSprite(value);
            if (TryResolveSmartTile(value, out var smartTile))
            {
                return NeoSmartTileRuleTileConverter.ToRuleTile(
                    smartTile,
                    matcher,
                    fallbackSprite);
            }

            if (fallbackSprite == null) return null;
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = $"Neo Tile - {fallbackSprite.name}";
            tile.sprite = fallbackSprite;
            return tile;
        }

        public static void ConfigureRuntimeTileBase(
            TileBase tileBase,
            INeoSmartTileNeighborMatcher? matcher)
        {
            if (tileBase is NeoRuleTile neoRuleTile)
            {
                neoRuleTile.Configure(matcher);
            }
        }

        public static bool TryResolveSmartTile(
            NeoGeneratedCustomValue value,
            out INeoSmartTile smartTile)
        {
            if (value is INeoSmartTileSource source && source.SmartTile is { } resolved)
            {
                smartTile = resolved;
                return true;
            }

            smartTile = null!;
            return false;
        }

        public static Sprite? ResolveSprite(NeoGeneratedCustomValue value)
        {
            var exact = TryReadSpriteProperty(value, "Sprite")
                ?? TryReadSpriteProperty(value, "Image");
            if (exact != null) return exact;

            var properties = value.GetType().GetProperties(
                BindingFlags.Public | BindingFlags.Instance);
            foreach (var property in properties)
            {
                if (!typeof(Sprite).IsAssignableFrom(property.PropertyType)) continue;
                if (!property.Name.EndsWith("Sprite", StringComparison.Ordinal)) continue;
                var sprite = TryReadSpriteProperty(value, property);
                if (sprite != null) return sprite;
            }
            foreach (var property in properties)
            {
                if (!typeof(Sprite).IsAssignableFrom(property.PropertyType)) continue;
                var sprite = TryReadSpriteProperty(value, property);
                if (sprite != null) return sprite;
            }

            return null;
        }

        private static Sprite? TryReadSpriteProperty(object source, string propertyName)
        {
            var property = source.GetType().GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.Instance);
            return property == null ? null : TryReadSpriteProperty(source, property);
        }

        private static Sprite? TryReadSpriteProperty(object source, PropertyInfo property)
        {
            if (!typeof(Sprite).IsAssignableFrom(property.PropertyType)) return null;
            if (property.GetIndexParameters().Length > 0) return null;
            try
            {
                return property.GetValue(source) as Sprite;
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
    }
}
