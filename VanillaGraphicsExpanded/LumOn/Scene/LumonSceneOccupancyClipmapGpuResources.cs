using System;
using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene;

internal sealed class LumonSceneOccupancyClipmapGpuResources : IDisposable
{
    public const int MaxLightColors = 64;
    public const int MaxMaterialPaletteEntries = 16384;
    public const int MaxLightLevels = 33; // 0..32 inclusive

    private readonly Texture3D[] occupancyLevels;

    public int Resolution { get; }
    public int Levels { get; }

    public Texture3D[] OccupancyLevels => occupancyLevels;

    public Texture2D LightColorLut { get; }
    public Texture2D BlockLevelScalarLut { get; }
    public Texture2D SunLevelScalarLut { get; }
    public Texture2D MaterialPalette { get; }

    public LumonSceneOccupancyClipmapGpuResources(int resolution, int levels, string debugNamePrefix)
    {
        if (resolution <= 0) throw new ArgumentOutOfRangeException(nameof(resolution));
        if (levels <= 0) throw new ArgumentOutOfRangeException(nameof(levels));
        ArgumentException.ThrowIfNullOrWhiteSpace(debugNamePrefix);

        Resolution = resolution;
        Levels = levels;

        occupancyLevels = new Texture3D[levels];
        for (int i = 0; i < levels; i++)
        {
            occupancyLevels[i] = Texture3D.Create(
                width: resolution,
                height: resolution,
                depth: resolution,
                format: PixelInternalFormat.R32ui,
                filter: TextureFilterMode.Nearest,
                textureTarget: TextureTarget.Texture3D,
                debugName: $"{debugNamePrefix}.Occupancy.L{i}");
        }

        LightColorLut = Texture2D.Create(
            width: MaxLightColors,
            height: 1,
            format: PixelInternalFormat.Rgba16f,
            filter: TextureFilterMode.Nearest,
            debugName: $"{debugNamePrefix}.LightColorLut");

        BlockLevelScalarLut = Texture2D.Create(
            width: MaxLightLevels,
            height: 1,
            format: PixelInternalFormat.R16f,
            filter: TextureFilterMode.Nearest,
            debugName: $"{debugNamePrefix}.BlockLevelScalarLut");

        SunLevelScalarLut = Texture2D.Create(
            width: MaxLightLevels,
            height: 1,
            format: PixelInternalFormat.R16f,
            filter: TextureFilterMode.Nearest,
            debugName: $"{debugNamePrefix}.SunLevelScalarLut");

        MaterialPalette = Texture2D.Create(
            width: MaxMaterialPaletteEntries,
            height: 1,
            format: PixelInternalFormat.Rgba32ui,
            filter: TextureFilterMode.Nearest,
            debugName: $"{debugNamePrefix}.MaterialPalette");

        InitializeDefaults();
    }

    private void InitializeDefaults()
    {
        // Occupancy: initialize to 0 so uninitialized texels don't produce garbage hits.
        int res = Resolution;
        int expected = checked(res * res * res);
        uint[] zero = new uint[expected];
        for (int i = 0; i < occupancyLevels.Length; i++)
        {
            occupancyLevels[i].UploadDataImmediate(zero, x: 0, y: 0, z: 0, regionWidth: res, regionHeight: res, regionDepth: res, mipLevel: 0);
        }

        // Light LUT: id 0 = neutral white (fallback).
        float[] light = new float[MaxLightColors * 4];
        for (int i = 0; i < MaxLightColors; i++)
        {
            int o = i * 4;
            light[o + 0] = 1.0f;
            light[o + 1] = 1.0f;
            light[o + 2] = 1.0f;
            light[o + 3] = 1.0f;
        }
        LightColorLut.UploadDataImmediate(light);

        // Scalar LUTs: default linear 0..1 mapping of [0..32].
        float[] s = new float[MaxLightLevels];
        for (int i = 0; i < MaxLightLevels; i++)
        {
            s[i] = i / 32.0f;
        }
        BlockLevelScalarLut.UploadDataImmediate(s);
        SunLevelScalarLut.UploadDataImmediate(s);

        // Material palette: initialize to 0 (placeholder until we wire actual per-face material ids).
        int mpExpected = checked(MaxMaterialPaletteEntries * 4);
        uint[] mp = new uint[mpExpected];
        MaterialPalette.UploadDataImmediate(mp);
    }

    public void Dispose()
    {
        for (int i = 0; i < occupancyLevels.Length; i++)
        {
            occupancyLevels[i]?.Dispose();
        }

        LightColorLut.Dispose();
        BlockLevelScalarLut.Dispose();
        SunLevelScalarLut.Dispose();
        MaterialPalette.Dispose();
    }
}
