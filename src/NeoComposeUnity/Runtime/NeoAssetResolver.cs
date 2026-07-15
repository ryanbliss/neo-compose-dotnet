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
            database ??= NeoAssetDatabase.LoadDefault();
            var direct = database?.TryGetSprite(value.fileId, value.sliceIndex);
            if (direct != null) return direct;

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
            database ??= NeoAssetDatabase.LoadDefault();
            var direct = database?.TryGetAudioClip(value.fileId);
            if (direct != null) return direct;

            var path = ResolveResourcePath(database, value.fileId);
            return path == null ? null : Resources.Load<AudioClip>(path);
        }

        public static SpriteValue ValueForSprite(
            NeoAssetDatabase? database,
            Sprite sprite,
            string? expectedTemplateId = null,
            string? memberName = null)
        {
            database ??= NeoAssetDatabase.LoadDefault();
            var sliceIndex = -1;
            var entry = database?.TryGetEntryForSprite(sprite, out sliceIndex);
            if (entry == null)
            {
                throw new InvalidOperationException("Sprite is not tracked by the Neo Compose asset database.");
            }

            ValidateTemplate(entry, expectedTemplateId, memberName ?? "Sprite member", "Sprite");
            return new SpriteValue { fileId = entry.FileId, sliceIndex = sliceIndex };
        }

        public static FileValue ValueForAudioClip(
            NeoAssetDatabase? database,
            AudioClip audioClip,
            string? expectedTemplateId = null,
            string? memberName = null)
        {
            database ??= NeoAssetDatabase.LoadDefault();
            var entry = database?.TryGetEntryForAudioClip(audioClip);
            if (entry == null)
            {
                throw new InvalidOperationException("AudioClip is not tracked by the Neo Compose asset database.");
            }

            ValidateTemplate(entry, expectedTemplateId, memberName ?? "Audio member", "AudioClip");
            return new FileValue { fileId = entry.FileId };
        }

        private static string? ResolveResourcePath(NeoAssetDatabase? database, string fileId)
        {
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

        private static void ValidateTemplate(
            NeoAssetDatabaseEntry entry,
            string? expectedTemplateId,
            string memberName,
            string assetKind)
        {
            if (expectedTemplateId == null || entry.TemplateId == expectedTemplateId) return;

            var actualTemplate = entry.TemplateId ?? "<none>";
            var fileName = string.IsNullOrWhiteSpace(entry.FileName)
                ? entry.FileId
                : entry.FileName;
            throw new InvalidOperationException(
                $"{assetKind} '{fileName}' does not match the Unity template required by '{memberName}'. " +
                $"Expected template id '{expectedTemplateId}', actual template id '{actualTemplate}'.");
        }
    }
}
