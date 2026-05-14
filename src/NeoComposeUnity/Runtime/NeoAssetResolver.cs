// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using NeoCompose.Runtime.Json;
using UnityEngine;

namespace NeoCompose.Runtime
{
    internal static class NeoAssetResolver
    {
        public static Sprite? ResolveSprite(NeoAssetDatabase? database, SpriteValue? value)
        {
            if (value == null) return null;
            var path = ResolveResourcePath(database, value.fileId);
            if (path == null) return null;
            var sprites = Resources.LoadAll<Sprite>(path);
            if (sprites.Length == 0) return null;
            if (value.sliceIndex < 0 || value.sliceIndex >= sprites.Length) return null;
            return sprites[value.sliceIndex];
        }

        public static AudioClip? ResolveAudioClip(NeoAssetDatabase? database, FileValue? value)
        {
            if (value == null) return null;
            var path = ResolveResourcePath(database, value.fileId);
            return path == null ? null : Resources.Load<AudioClip>(path);
        }

        private static string? ResolveResourcePath(NeoAssetDatabase? database, string fileId)
        {
            database ??= NeoAssetDatabase.LoadDefault();
            var assetPath = database?.TryGetAssetPath(fileId);
            if (string.IsNullOrWhiteSpace(assetPath)) return null;

            var resourcesMarker = "/Resources/";
            var markerIndex = assetPath.IndexOf(resourcesMarker, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                if (assetPath.StartsWith("Assets/Resources/", StringComparison.Ordinal))
                {
                    markerIndex = "Assets".Length;
                }
                else
                {
                    return null;
                }
            }

            var resourcePath = assetPath.Substring(markerIndex + resourcesMarker.Length);
            return Path.ChangeExtension(resourcePath, null);
        }
    }
}
