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
            NeoGeneratedClassValue value,
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
            NeoGeneratedClassValue value,
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

        public static Sprite? ResolveSprite(NeoGeneratedClassValue value)
        {
            var exact = TryReadSpriteProperty(value, "Sprite")
                ?? TryReadSpriteProperty(value, "Image");
            if (exact != null) return exact;

            var properties = value.GetType().GetProperties(
                BindingFlags.Public | BindingFlags.Instance);
            foreach (var property in properties)
            {
                if (!IsSpriteProperty(property)) continue;
                if (!property.Name.EndsWith("Sprite", StringComparison.Ordinal)) continue;
                var sprite = TryReadSpriteProperty(value, property);
                if (sprite != null) return sprite;
            }
            foreach (var property in properties)
            {
                if (!IsSpriteProperty(property)) continue;
                var sprite = TryReadSpriteProperty(value, property);
                if (sprite != null) return sprite;
            }

            return null;
        }

        /// <summary>
        /// Whether a generated property can carry a sprite this scan should
        /// consider. Both projections are live at once and both must match.
        ///
        /// <para>A generated Sprite <em>property</em> projects
        /// <see cref="NeoSprite"/> since P42 §4.1, and its read-only
        /// counterpart projects <see cref="NeoReadOnlySprite"/> — neither is
        /// assignable to <see cref="UnityEngine.Sprite"/>, because the bridge
        /// between them is a user-defined implicit conversion operator and
        /// <see cref="Type.IsAssignableFrom"/> cannot see one. Testing only
        /// the native type silently matched nothing on every generated tile.
        /// Sprites in non-property positions — list and dictionary entries,
        /// generic slots, static members, constructor parameters — are still
        /// emitted as native <see cref="UnityEngine.Sprite"/>, so the native
        /// arm is not legacy and does not go away.</para>
        /// </summary>
        private static bool IsSpriteProperty(PropertyInfo property)
        {
            return typeof(Sprite).IsAssignableFrom(property.PropertyType)
                || typeof(NeoReadOnlySprite).IsAssignableFrom(property.PropertyType);
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
            if (!IsSpriteProperty(property)) return null;
            if (property.GetIndexParameters().Length > 0) return null;
            try
            {
                var raw = property.GetValue(source);
                if (raw is Sprite sprite) return sprite;
                // ResolveOrNull, not Resolve: a required member whose asset is
                // not synchronized is just another empty candidate here, and
                // this scan is best-effort by construction — it already
                // tolerates properties it cannot read at all.
                if (raw is NeoReadOnlySprite wrapper) return wrapper.ResolveOrNull();
                return null;
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
