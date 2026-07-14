// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoTextureTemplate("66504747-cbd5-4026-9d4c-89a0644f8192")]
public sealed partial class _16x16PivotCenter
{
    public static NeoTextureTemplateSettings Settings { get; } = new()
    {
        Name = "16x16-PivotCenter",
        TextureType = NeoTextureType.Sprite,
        SrgbTexture = true,
        AlphaSource = NeoAlphaSource.InputTextureAlpha,
        AlphaIsTransparency = true,
        NonPowerOfTwoScale = NeoNonPowerOfTwoScale.None,
        IgnorePngGamma = false,
        ReadWriteEnabled = true,
        VirtualTextureOnly = false,
        GenerateMipMaps = false,
        BorderMipMaps = false,
        MipMapFiltering = NeoMipMapFiltering.Box,
        MipMapsPreserveCoverage = false,
        AlphaCutoffValue = 0.5,
        FadeOutMipMaps = false,
        MipMapFadeDistanceStart = 1,
        MipMapFadeDistanceEnd = 3,
        AnisoLevel = 1,
        WrapMode = NeoTextureWrapMode.Clamp,
        WrapModeU = null,
        WrapModeV = null,
        FilterMode = NeoTextureFilterMode.Point,
        MaxTextureSize = 2048,
        ResizeAlgorithm = NeoTextureResizeAlgorithm.Mitchell,
        TextureCompression = NeoTextureCompression.None,
        CompressionQuality = 50,
        CrunchedCompression = false,
        Platforms = new()
        {
            Default = new()
            {
                MaxTextureSize = 2048,
                ResizeAlgorithm = NeoTextureResizeAlgorithm.Mitchell,
                Format = NeoTextureFormat.Automatic,
                TextureCompression = NeoTextureCompression.None,
                CrunchedCompression = false,
                CompressionQuality = 50,
            },
            Standalone = new()
            {
                Override = false,
                MaxTextureSize = 2048,
                ResizeAlgorithm = NeoTextureResizeAlgorithm.Mitchell,
                Format = NeoTextureFormat.RgbaCompressedDxt5Bc3,
                TextureCompression = NeoTextureCompression.NormalQuality,
                CrunchedCompression = false,
                CompressionQuality = 50,
            },
            Web = new()
            {
                Override = false,
                MaxTextureSize = 2048,
                ResizeAlgorithm = NeoTextureResizeAlgorithm.Mitchell,
                Format = NeoTextureFormat.RgbaCompressedDxt5Bc3,
                TextureCompression = NeoTextureCompression.NormalQuality,
                CrunchedCompression = false,
                CompressionQuality = 50,
            },
        },
        Sprite = new()
        {
            Mode = NeoSpriteMode.Multiple,
            PixelsPerUnit = 16,
            MeshType = NeoSpriteMeshType.Tight,
            ExtrudeEdges = 1,
            PivotAlignment = NeoSpriteAlignment.Center,
            Pivot = new(0.5, 0.5),
            GeneratePhysicsShape = true,
            Slice = new()
            {
                PixelSize = new(16, 16),
                Offset = new(0, 0),
                Padding = new(0, 0),
                KeepEmptyRects = false,
                PivotAlignment = NeoSpriteAlignment.Center,
                Pivot = new(0.5, 0.5),
                Border = new(0, 0, 0, 0),
                NamingPattern = "{fileName}_{row}_{column}",
                NamingStartIndex = 0,
                ColumnMajor = false,
            },
        },
        NormalMap = new()
        {
            CreateFromGrayscale = false,
            Bumpiness = 0.25,
            Filtering = NeoNormalMapFiltering.Smooth,
            FlipGreenChannel = false,
        },
        Cookie = new() { LightType = NeoCookieLightType.Spot },
        SingleChannel = new() { Component = NeoSingleChannelComponent.Alpha },
        Swizzle = new()
        {
            R = NeoTextureSwizzle.Red,
            G = NeoTextureSwizzle.Green,
            B = NeoTextureSwizzle.Blue,
            A = NeoTextureSwizzle.Alpha,
        },
    };
}
