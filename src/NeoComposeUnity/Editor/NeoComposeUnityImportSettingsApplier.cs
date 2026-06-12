// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace NeoCompose.Unity.Editor
{
    internal static class NeoComposeUnityImportSettingsApplier
    {
        public static void Apply(string assetPath, ProjectFile file, ProjectData projectData)
        {
            AssetDatabase.ImportAsset(assetPath);
            var importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null)
            {
                throw new InvalidOperationException($"Could not load Unity importer for {assetPath}.");
            }

            if (importer is TextureImporter textureImporter && file.unityTextureSettings != null)
            {
                ApplyTextureSettings(textureImporter, file, projectData);
                textureImporter.SaveAndReimport();
                return;
            }

            if (importer is AudioImporter audioImporter && file.unityAudioClipSettings != null)
            {
                ApplyAudioSettings(audioImporter, file, projectData);
                audioImporter.SaveAndReimport();
            }
        }

        internal static UnityTexture2DImportSettingsTemplate? ResolveTextureSettings(
            ProjectFile file,
            ProjectData projectData)
        {
            if (file.unityTextureSettings == null) return null;

            // Custom (one-off) settings: the full object rides inline with a
            // null templateId. Everything beyond the override-shape fields
            // lands in customFields — rebuild and use it directly.
            var custom = file.unityTextureSettings.customFields;
            if (string.IsNullOrEmpty(file.unityTextureSettings.templateId)
                && custom != null
                && custom.Count > 0)
            {
                var inline = new JObject();
                if (file.unityTextureSettings.type != null)
                {
                    inline["type"] = file.unityTextureSettings.type;
                }
                foreach (var pair in custom)
                {
                    inline[pair.Key] = pair.Value;
                }
                return inline.ToObject<UnityTexture2DImportSettingsTemplate>();
            }

            var resolved = ResolveSettingsObject(
                file.unityTextureSettings.templateId,
                file.unityTextureSettings.overridePaths,
                file.unityTextureSettings.values,
                projectData.textureTemplates);
            return resolved?.ToObject<UnityTexture2DImportSettingsTemplate>();
        }

        internal static UnityAudioClipImportSettingsTemplate? ResolveAudioSettings(
            ProjectFile file,
            ProjectData projectData)
        {
            if (file.unityAudioClipSettings == null) return null;

            // Custom (one-off) settings ride inline with a null templateId,
            // mirroring the texture path above.
            var custom = file.unityAudioClipSettings.customFields;
            if (string.IsNullOrEmpty(file.unityAudioClipSettings.templateId)
                && custom != null
                && custom.Count > 0)
            {
                var inline = new JObject();
                foreach (var pair in custom)
                {
                    inline[pair.Key] = pair.Value;
                }
                return inline.ToObject<UnityAudioClipImportSettingsTemplate>();
            }

            var resolved = ResolveSettingsObject(
                file.unityAudioClipSettings.templateId,
                file.unityAudioClipSettings.overridePaths,
                file.unityAudioClipSettings.values,
                projectData.audioClipTemplates);
            return resolved?.ToObject<UnityAudioClipImportSettingsTemplate>();
        }

        private static void ApplyTextureSettings(
            TextureImporter importer,
            ProjectFile file,
            ProjectData projectData)
        {
            var settings = ResolveTextureSettings(file, projectData);
            if (settings == null) return;

            importer.textureType = MapTextureType(settings.textureType);
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = settings.sRGBTexture;
            importer.alphaSource = MapAlphaSource(settings.alphaSource);
            importer.alphaIsTransparency = settings.alphaIsTransparency;
            importer.npotScale = MapNPOTScale(settings.nonPowerOfTwoScale);
            importer.ignorePngGamma = settings.ignorePngGamma;
            importer.isReadable = settings.readWriteEnabled;
            importer.vtOnly = settings.virtualTextureOnly;
            importer.mipmapEnabled = settings.generateMipMaps;
            importer.borderMipmap = settings.borderMipMaps;
            importer.mipmapFilter = MapMipMapFilter(settings.mipMapFiltering);
            importer.mipMapsPreserveCoverage = settings.mipMapsPreserveCoverage;
            importer.alphaTestReferenceValue = (float)settings.alphaCutoffValue;
            importer.fadeout = settings.fadeOutMipMaps;
            importer.mipmapFadeDistanceStart = (int)Math.Round(settings.mipMapFadeDistanceStart);
            importer.mipmapFadeDistanceEnd = (int)Math.Round(settings.mipMapFadeDistanceEnd);
            importer.anisoLevel = settings.anisoLevel;
            importer.wrapMode = MapWrapMode(settings.wrapMode);
            if (!string.IsNullOrWhiteSpace(settings.wrapModeU))
            {
                importer.wrapModeU = MapWrapMode(settings.wrapModeU);
            }
            if (!string.IsNullOrWhiteSpace(settings.wrapModeV))
            {
                importer.wrapModeV = MapWrapMode(settings.wrapModeV);
            }
            importer.filterMode = MapFilterMode(settings.filterMode);
            importer.textureCompression = MapTextureCompression(settings.textureCompression);
            importer.compressionQuality = settings.compressionQuality;
            importer.crunchedCompression = settings.crunchedCompression;

            ApplyTexturePlatformSettings(importer, settings.platformSettings);
            ApplySpriteSettings(importer, settings.spriteSettings);
        }

        private static void ApplyAudioSettings(
            AudioImporter importer,
            ProjectFile file,
            ProjectData projectData)
        {
            var settings = ResolveAudioSettings(file, projectData);
            if (settings == null) return;

            importer.forceToMono = settings.forceToMono;
            importer.loadInBackground = settings.loadInBackground;
            importer.ambisonic = settings.ambisonic;
            var sampleSettings = importer.defaultSampleSettings;
            sampleSettings.loadType = MapAudioClipLoadType(settings.loadType);
            sampleSettings.compressionFormat = MapAudioCompressionFormat(settings.compressionFormat);
            sampleSettings.quality = (float)settings.quality;
            sampleSettings.sampleRateSetting = MapAudioSampleRateSetting(settings.sampleRateSetting);
            sampleSettings.preloadAudioData = settings.preloadAudioData;
            if (settings.overrideSampleRate.HasValue)
            {
                sampleSettings.sampleRateOverride = (uint)Math.Max(0, settings.overrideSampleRate.Value);
            }
            importer.defaultSampleSettings = sampleSettings;
        }

        private static JObject? ResolveSettingsObject<TTemplate>(
            string? templateId,
            string[]? overridePaths,
            JObject? values,
            Dictionary<string, TTemplate> templates)
            where TTemplate : class
        {
            JObject? resolved = null;
            if (templateId != null &&
                templateId.Trim().Length > 0 &&
                templates.TryGetValue(templateId, out var template))
            {
                resolved = JObject.FromObject(template);
            }

            if (values != null && resolved == null)
            {
                resolved = new JObject();
            }

            if (values == null || resolved == null)
            {
                return resolved;
            }

            foreach (var path in overridePaths ?? Array.Empty<string>())
            {
                var token = values.SelectToken(path);
                if (token != null)
                {
                    SetToken(resolved, path, token.DeepClone());
                }
            }

            return resolved;
        }

        private static void SetToken(JObject target, string path, JToken value)
        {
            var parts = path.Split('.');
            JObject current = target;
            for (var index = 0; index < parts.Length - 1; index++)
            {
                var part = parts[index];
                if (current[part] is not JObject next)
                {
                    next = new JObject();
                    current[part] = next;
                }
                current = next;
            }
            current[parts[^1]] = value;
        }

        private static void ApplySpriteSettings(
            TextureImporter importer,
            UnitySpriteTextureSettingsTemplate? settings)
        {
            if (settings == null) return;

            importer.spriteImportMode = MapSpriteImportMode(settings.spriteMode);
            importer.spritePixelsPerUnit = (float)settings.pixelsPerUnit;
            importer.spritePivot = ToVector2(settings.pivot);
            var textureSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(textureSettings);
            textureSettings.spriteMeshType = MapSpriteMeshType(settings.meshType);
            textureSettings.spriteExtrude = (uint)Math.Max(0, settings.extrudeEdges);
            textureSettings.spriteAlignment = MapSpriteAlignment(settings.pivotAlignment);
            textureSettings.spriteGenerateFallbackPhysicsShape = settings.generatePhysicsShape;
            importer.SetTextureSettings(textureSettings);

            var slice = settings.spriteEditor?.slice;
            if (slice == null ||
                !string.Equals(slice.type, "grid-by-cell-size", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ApplyGridSpriteSlices(importer, slice);
        }

        private static void ApplyGridSpriteSlices(
            TextureImporter importer,
            UnitySpriteGridByCellSizeSliceTemplate slice)
        {
            var dataProvider = CreateSpriteEditorDataProvider(importer);
            var spriteEditorDataProviderType = FindType("UnityEditor.U2D.Sprites.ISpriteEditorDataProvider");
            var nameFileIdDataProviderType = FindType("UnityEditor.U2D.Sprites.ISpriteNameFileIdDataProvider");
            var spriteRectType = FindType("UnityEditor.SpriteRect");
            var spriteNameFileIdPairType = FindType("UnityEditor.SpriteNameFileIdPair");
            if (dataProvider == null ||
                spriteEditorDataProviderType == null ||
                nameFileIdDataProviderType == null ||
                spriteRectType == null ||
                spriteNameFileIdPairType == null)
            {
                throw new InvalidOperationException(
                    "Sprite slicing requires Unity's 2D Sprite Editor package APIs, but they were not available.");
            }

            spriteEditorDataProviderType.GetMethod("InitSpriteEditorDataProvider")!.Invoke(dataProvider, null);
            var descriptors = BuildGridSpriteDescriptors(importer, slice);
            var spriteRects = Array.CreateInstance(spriteRectType, descriptors.Length);
            for (var index = 0; index < descriptors.Length; index++)
            {
                var descriptor = descriptors[index];
                var spriteRect = Activator.CreateInstance(spriteRectType)!;
                spriteRectType.GetProperty("name")!.SetValue(spriteRect, descriptor.Name);
                spriteRectType.GetProperty("rect")!.SetValue(spriteRect, descriptor.Rect);
                spriteRectType.GetProperty("alignment")!.SetValue(spriteRect, descriptor.Alignment);
                spriteRectType.GetProperty("pivot")!.SetValue(spriteRect, descriptor.Pivot);
                spriteRectType.GetProperty("border")!.SetValue(spriteRect, descriptor.Border);
                spriteRectType.GetProperty("spriteID")!.SetValue(spriteRect, GUID.Generate());
                spriteRects.SetValue(spriteRect, index);
            }

            spriteEditorDataProviderType.GetMethod("SetSpriteRects")!.Invoke(dataProvider, new object[] { spriteRects });

            var getDataProvider = spriteEditorDataProviderType.GetMethod("GetDataProvider")!;
            var nameFileIdProvider = getDataProvider.MakeGenericMethod(nameFileIdDataProviderType).Invoke(dataProvider, null);
            if (nameFileIdProvider != null)
            {
                var pairs = Array.CreateInstance(spriteNameFileIdPairType, descriptors.Length);
                for (var index = 0; index < descriptors.Length; index++)
                {
                    var spriteRect = spriteRects.GetValue(index)!;
                    var spriteId = spriteRectType.GetProperty("spriteID")!.GetValue(spriteRect);
                    var pair = Activator.CreateInstance(
                        spriteNameFileIdPairType,
                        descriptors[index].Name,
                        spriteId)!;
                    pairs.SetValue(pair, index);
                }

                nameFileIdDataProviderType.GetMethod("SetNameFileIdPairs")!.Invoke(
                    nameFileIdProvider,
                    new object[] { pairs });
            }

            spriteEditorDataProviderType.GetMethod("Apply")!.Invoke(dataProvider, null);
        }

        private static object? CreateSpriteEditorDataProvider(TextureImporter importer)
        {
            var factoryType = FindType("UnityEditor.U2D.Sprites.SpriteDataProviderFactories");
            if (factoryType == null) return null;

            var factory = Activator.CreateInstance(factoryType);
            factoryType.GetMethod("Init")!.Invoke(factory, null);
            return factoryType
                .GetMethod("GetSpriteEditorDataProviderFromObject")!
                .Invoke(factory, new object[] { importer });
        }

        private static Type? FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName);
                if (type != null) return type;
            }

            return Type.GetType(fullName);
        }

        private static SpriteSliceDescriptor[] BuildGridSpriteDescriptors(
            TextureImporter importer,
            UnitySpriteGridByCellSizeSliceTemplate slice)
        {
            importer.GetSourceTextureWidthAndHeight(out var width, out var height);
            var cellWidth = Math.Max(1, (int)Math.Round(slice.pixelSize?.x ?? width));
            var cellHeight = Math.Max(1, (int)Math.Round(slice.pixelSize?.y ?? height));
            var offsetX = Math.Max(0, (int)Math.Round(slice.offset?.x ?? 0));
            var offsetY = Math.Max(0, (int)Math.Round(slice.offset?.y ?? 0));
            var paddingX = Math.Max(0, (int)Math.Round(slice.padding?.x ?? 0));
            var paddingY = Math.Max(0, (int)Math.Round(slice.padding?.y ?? 0));
            var columns = Math.Max(1, (width - offsetX + paddingX) / (cellWidth + paddingX));
            var rows = Math.Max(1, (height - offsetY + paddingY) / (cellHeight + paddingY));
            var sprites = new List<SpriteSliceDescriptor>();
            var readableTexture = slice.keepEmptyRects ? null : LoadReadableTexture(importer.assetPath);
            try
            {
                var index = 0;
                var columnMajor = string.Equals(slice.naming?.order, "column-major", StringComparison.OrdinalIgnoreCase);
                var pivotAlignment = MapSpriteAlignment(slice.pivotAlignment);
                var pivot = ToVector2(slice.pivot);
                var border = ToVector4(slice.border);

                void AddIfVisible(int row, int column)
                {
                    var x = offsetX + column * (cellWidth + paddingX);
                    var topY = offsetY + row * (cellHeight + paddingY);
                    if (x + cellWidth > width || topY + cellHeight > height) return;

                    var rect = new Rect(x, height - topY - cellHeight, cellWidth, cellHeight);
                    if (!slice.keepEmptyRects &&
                        readableTexture != null &&
                        IsTransparent(readableTexture, rect))
                    {
                        return;
                    }

                    sprites.Add(new SpriteSliceDescriptor(
                        FormatSpriteName(importer.assetPath, slice.naming, row, column, index),
                        rect,
                        pivotAlignment,
                        pivot,
                        border));
                    index++;
                }

                if (columnMajor)
                {
                    for (var column = 0; column < columns; column++)
                    {
                        for (var row = 0; row < rows; row++)
                        {
                            AddIfVisible(row, column);
                        }
                    }
                }
                else
                {
                    for (var row = 0; row < rows; row++)
                    {
                        for (var column = 0; column < columns; column++)
                        {
                            AddIfVisible(row, column);
                        }
                    }
                }

                return sprites.ToArray();
            }
            finally
            {
                if (readableTexture != null)
                {
                    UnityEngine.Object.DestroyImmediate(readableTexture);
                }
            }
        }

        private static Texture2D? LoadReadableTexture(string assetPath)
        {
            var absolutePath = Path.GetFullPath(assetPath);
            if (!File.Exists(absolutePath)) return null;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            return texture.LoadImage(File.ReadAllBytes(absolutePath)) ? texture : null;
        }

        private static bool IsTransparent(Texture2D texture, Rect rect)
        {
            var minX = Math.Max(0, (int)rect.xMin);
            var minY = Math.Max(0, (int)rect.yMin);
            var maxX = Math.Min(texture.width, (int)rect.xMax);
            var maxY = Math.Min(texture.height, (int)rect.yMax);
            for (var y = minY; y < maxY; y++)
            {
                for (var x = minX; x < maxX; x++)
                {
                    if (texture.GetPixel(x, y).a > 0.001f) return false;
                }
            }

            return true;
        }

        private static void ApplyTexturePlatformSettings(TextureImporter importer, JObject? platformSettings)
        {
            if (platformSettings == null) return;

            ApplyTexturePlatformSettings(importer, "DefaultTexturePlatform", platformSettings["default"] as JObject, false);
            ApplyTexturePlatformSettings(importer, "Standalone", platformSettings["standalone"] as JObject, true);
            ApplyTexturePlatformSettings(importer, "WebGL", platformSettings["web"] as JObject, true);
        }

        private static void ApplyTexturePlatformSettings(
            TextureImporter importer,
            string platformName,
            JObject? settings,
            bool supportsOverride)
        {
            if (settings == null) return;

            var platform = importer.GetPlatformTextureSettings(platformName);
            if (supportsOverride)
            {
                platform.overridden = settings.Value<bool?>("override") ?? false;
            }
            platform.maxTextureSize = settings.Value<int?>("maxTextureSize") ?? platform.maxTextureSize;
            platform.resizeAlgorithm = MapResizeAlgorithm(settings.Value<string?>("resizeAlgorithm"));
            platform.format = MapPlatformFormat(settings.Value<string?>("format"));
            platform.textureCompression = MapTextureCompression(settings.Value<string?>("textureCompression"));
            platform.crunchedCompression = settings.Value<bool?>("crunchedCompression") ?? false;
            platform.compressionQuality = settings.Value<int?>("compressionQuality") ?? platform.compressionQuality;
            importer.SetPlatformTextureSettings(platform);
        }

        private static string FormatSpriteName(
            string assetPath,
            UnitySpriteGridNamingConvention? naming,
            int row,
            int column,
            int index)
        {
            var fileName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            var pattern = naming?.pattern;
            if (string.IsNullOrWhiteSpace(pattern))
            {
                pattern = "{fileName}_{row}_{column}";
            }

            var startIndex = naming?.startIndex ?? 0;
            return pattern!
                .Replace("{fileName}", fileName)
                .Replace("{row}", row.ToString(CultureInfo.InvariantCulture))
                .Replace("{column}", column.ToString(CultureInfo.InvariantCulture))
                .Replace("{index}", (index + startIndex).ToString(CultureInfo.InvariantCulture));
        }

        private static TextureImporterType MapTextureType(string? value)
        {
            return value switch
            {
                "sprite" => TextureImporterType.Sprite,
                "normal-map" => TextureImporterType.NormalMap,
                "editor-gui-and-legacy-gui" => TextureImporterType.GUI,
                "cursor" => TextureImporterType.Cursor,
                "cookie" => TextureImporterType.Cookie,
                "lightmap" => TextureImporterType.Lightmap,
                "single-channel" => TextureImporterType.SingleChannel,
                _ => TextureImporterType.Default,
            };
        }

        private static TextureImporterAlphaSource MapAlphaSource(string? value)
        {
            return value switch
            {
                "none" => TextureImporterAlphaSource.None,
                "from-gray-scale" => TextureImporterAlphaSource.FromGrayScale,
                _ => TextureImporterAlphaSource.FromInput,
            };
        }

        private static TextureImporterNPOTScale MapNPOTScale(string? value)
        {
            return value switch
            {
                "to-nearest" => TextureImporterNPOTScale.ToNearest,
                "to-larger" => TextureImporterNPOTScale.ToLarger,
                "to-smaller" => TextureImporterNPOTScale.ToSmaller,
                _ => TextureImporterNPOTScale.None,
            };
        }

        private static TextureImporterMipFilter MapMipMapFilter(string? value)
        {
            return value == "kaiser" ? TextureImporterMipFilter.KaiserFilter : TextureImporterMipFilter.BoxFilter;
        }

        private static TextureWrapMode MapWrapMode(string? value)
        {
            return value switch
            {
                "repeat" => TextureWrapMode.Repeat,
                "mirror" => TextureWrapMode.Mirror,
                "mirror-once" => TextureWrapMode.MirrorOnce,
                _ => TextureWrapMode.Clamp,
            };
        }

        private static FilterMode MapFilterMode(string? value)
        {
            return value switch
            {
                "bilinear" => FilterMode.Bilinear,
                "trilinear" => FilterMode.Trilinear,
                _ => FilterMode.Point,
            };
        }

        private static TextureImporterCompression MapTextureCompression(string? value)
        {
            return value switch
            {
                "low-quality" => TextureImporterCompression.CompressedLQ,
                "normal-quality" => TextureImporterCompression.Compressed,
                "high-quality" => TextureImporterCompression.CompressedHQ,
                _ => TextureImporterCompression.Uncompressed,
            };
        }

        private static TextureResizeAlgorithm MapResizeAlgorithm(string? value)
        {
            return value == "bilinear" ? TextureResizeAlgorithm.Bilinear : TextureResizeAlgorithm.Mitchell;
        }

        private static TextureImporterFormat MapPlatformFormat(string? value)
        {
            return value switch
            {
                "rgba-compressed-dxt5-bc3" => TextureImporterFormat.DXT5,
                "rgb-compressed-dxt1-bc1" => TextureImporterFormat.DXT1,
                "rgba-32-bit" => TextureImporterFormat.RGBA32,
                "rgb-24-bit" => TextureImporterFormat.RGB24,
                _ => TextureImporterFormat.Automatic,
            };
        }

        private static SpriteImportMode MapSpriteImportMode(string? value)
        {
            return value switch
            {
                "single" => SpriteImportMode.Single,
                "polygon" => SpriteImportMode.Polygon,
                _ => SpriteImportMode.Multiple,
            };
        }

        private static SpriteMeshType MapSpriteMeshType(string? value)
        {
            return value == "full-rect" ? SpriteMeshType.FullRect : SpriteMeshType.Tight;
        }

        private static int MapSpriteAlignment(string? value)
        {
            return value switch
            {
                "top-left" => (int)SpriteAlignment.TopLeft,
                "top-center" => (int)SpriteAlignment.TopCenter,
                "top-right" => (int)SpriteAlignment.TopRight,
                "left-center" => (int)SpriteAlignment.LeftCenter,
                "right-center" => (int)SpriteAlignment.RightCenter,
                "bottom-left" => (int)SpriteAlignment.BottomLeft,
                "bottom-center" => (int)SpriteAlignment.BottomCenter,
                "bottom-right" => (int)SpriteAlignment.BottomRight,
                "custom" => (int)SpriteAlignment.Custom,
                _ => (int)SpriteAlignment.Center,
            };
        }

        private static AudioClipLoadType MapAudioClipLoadType(string? value)
        {
            return value switch
            {
                "compressed-in-memory" => AudioClipLoadType.CompressedInMemory,
                "streaming" => AudioClipLoadType.Streaming,
                _ => AudioClipLoadType.DecompressOnLoad,
            };
        }

        private static AudioCompressionFormat MapAudioCompressionFormat(string? value)
        {
            return value switch
            {
                "pcm" => AudioCompressionFormat.PCM,
                "adpcm" => AudioCompressionFormat.ADPCM,
                "mp3" => AudioCompressionFormat.MP3,
                "vorbis" => AudioCompressionFormat.Vorbis,
                _ => AudioCompressionFormat.Vorbis,
            };
        }

        private static AudioSampleRateSetting MapAudioSampleRateSetting(string? value)
        {
            return value switch
            {
                "preserve-sample-rate" => AudioSampleRateSetting.PreserveSampleRate,
                "override-sample-rate" => AudioSampleRateSetting.OverrideSampleRate,
                _ => AudioSampleRateSetting.OptimizeSampleRate,
            };
        }

        private static Vector2 ToVector2(UnityVector2? value)
        {
            return value == null ? new Vector2(0.5f, 0.5f) : new Vector2((float)value.x, (float)value.y);
        }

        private static Vector4 ToVector4(UnityVector4? value)
        {
            return value == null
                ? Vector4.zero
                : new Vector4((float)value.x, (float)value.y, (float)value.z, (float)value.w);
        }

        private sealed class SpriteSliceDescriptor
        {
            public readonly string Name;
            public readonly Rect Rect;
            public readonly SpriteAlignment Alignment;
            public readonly Vector2 Pivot;
            public readonly Vector4 Border;

            public SpriteSliceDescriptor(
                string name,
                Rect rect,
                int alignment,
                Vector2 pivot,
                Vector4 border)
            {
                Name = name;
                Rect = rect;
                Alignment = (SpriteAlignment)alignment;
                Pivot = pivot;
                Border = border;
            }
        }
    }
}
