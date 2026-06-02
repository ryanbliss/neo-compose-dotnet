// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    public sealed class UnityVector2
    {
        public double x;
        public double y;
    }

    public sealed class UnityVector4
    {
        public double x;
        public double y;
        public double z;
        public double w;
    }

    public sealed class UnityTexture2DImportSettingsTemplate
    {
        public string id = null!;
        public string projectId = null!;
        public string name = null!;
        public string type = null!;
        public string textureType = null!;
        public string textureShape = null!;
        public bool sRGBTexture;
        public string alphaSource = null!;
        public bool alphaIsTransparency;
        public string nonPowerOfTwoScale = null!;
        public bool ignorePngGamma;
        public bool readWriteEnabled;
        public bool virtualTextureOnly;
        public bool generateMipMaps;
        public bool borderMipMaps;
        public string mipMapFiltering = null!;
        public bool mipMapsPreserveCoverage;
        public double alphaCutoffValue;
        public bool fadeOutMipMaps;
        public double mipMapFadeDistanceStart;
        public double mipMapFadeDistanceEnd;
        public int anisoLevel;
        public string wrapMode = null!;
        public string? wrapModeU;
        public string? wrapModeV;
        public string filterMode = null!;
        public int maxTextureSize;
        public string resizeAlgorithm = null!;
        public string textureCompression = null!;
        public int compressionQuality;
        public bool crunchedCompression;
        public JObject? platformSettings;
        public UnitySpriteTextureSettingsTemplate? spriteSettings;
        public JObject? normalMapSettings;
        public JObject? cookieSettings;
        public JObject? singleChannelSettings;
        public JObject? swizzle;
        public NeoTimestamp createdAt;
        public NeoTimestamp updatedAt;
    }

    public sealed class UnitySpriteTextureSettingsTemplate
    {
        public string spriteMode = null!;
        public double pixelsPerUnit;
        public string meshType = null!;
        public int extrudeEdges;
        public string pivotAlignment = null!;
        public UnityVector2 pivot = null!;
        public bool generatePhysicsShape;
        public UnitySpriteEditorSettingsTemplate spriteEditor = null!;
    }

    public sealed class UnitySpriteEditorSettingsTemplate
    {
        public UnitySpriteGridByCellSizeSliceTemplate slice = null!;
    }

    public sealed class UnitySpriteGridByCellSizeSliceTemplate
    {
        public string type = null!;
        public UnityVector2 pixelSize = null!;
        public UnityVector2 offset = null!;
        public UnityVector2 padding = null!;
        public bool keepEmptyRects;
        public string pivotAlignment = null!;
        public UnityVector2 pivot = null!;
        public UnityVector4 border = null!;
        public UnitySpriteGridNamingConvention naming = null!;
    }

    public sealed class UnitySpriteGridNamingConvention
    {
        public string pattern = null!;
        public int startIndex;
        public string order = null!;
    }

    public sealed class UnityAudioClipImportSettingsTemplate
    {
        public string id = null!;
        public string projectId = null!;
        public string name = null!;
        public bool forceToMono;
        public bool normalize;
        public bool loadInBackground;
        public bool ambisonic;
        public string loadType = null!;
        public string compressionFormat = null!;
        public double quality;
        public string sampleRateSetting = null!;
        public int? overrideSampleRate;
        public bool preloadAudioData;
        public NeoTimestamp createdAt;
        public NeoTimestamp updatedAt;
    }

    public sealed class UnitySpriteSlice
    {
        public string id = null!;
        public string name = null!;
        public UnitySpriteRect rect = null!;
        public string pivotAlignment = null!;
        public UnityVector2 pivot = null!;
        public UnityVector4 border = null!;
    }

    public sealed class UnitySpriteRect
    {
        public double x;
        public double y;
        public double width;
        public double height;
    }

    public sealed class UnitySpriteEditorSettings
    {
        public Dictionary<string, UnitySpriteSlice> slices = new();
    }
}
