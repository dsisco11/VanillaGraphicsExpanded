using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

/// <summary>
/// CPU-side wrapper for VgeLumOnDebugParamsUBO (binding 14, 128 bytes std140).
/// Shared visualization parameters for independently compiled debug views.
/// </summary>
public sealed class LumOnDebugParamsUbo : CpuUniformBuffer
{
    public const string BlockName = "VgeLumOnDebugParamsUBO";
    public const int UboSizeBytes = 128;

    // Byte offsets (std140 layout)
    private const int OffsetLumonSceneInts0 = 0;
    private const int OffsetTemporalFloats0 = 64;
    private const int OffsetDebugInts0 = 80;
    private const int OffsetCompositeTintIntensity = 96;
    private const int OffsetAoStrengths = 112;

    public LumOnDebugParamsUbo() : base(UboSizeBytes)
    {
    }

    #region LumonScene Surface Cache Debug

    public int LumonSceneEnabled
    {
        get => UboPacking.ReadInt32(DataReadOnly, OffsetLumonSceneInts0 + 0);
        set
        {
            var (_, tileSize, tilesPerAxis, tilesPerAtlas) = UboPacking.ReadIVec4(DataReadOnly, OffsetLumonSceneInts0);
            WriteIntVector4(OffsetLumonSceneInts0, value, tileSize, tilesPerAxis, tilesPerAtlas);
        }
    }

    public int LumonSceneTileSizeTexels
    {
        get => UboPacking.ReadInt32(DataReadOnly, OffsetLumonSceneInts0 + 4);
        set
        {
            var (enabled, _, tilesPerAxis, tilesPerAtlas) = UboPacking.ReadIVec4(DataReadOnly, OffsetLumonSceneInts0);
            WriteIntVector4(OffsetLumonSceneInts0, enabled, value, tilesPerAxis, tilesPerAtlas);
        }
    }

    public int LumonSceneTilesPerAxis
    {
        get => UboPacking.ReadInt32(DataReadOnly, OffsetLumonSceneInts0 + 8);
        set
        {
            var (enabled, tileSize, _, tilesPerAtlas) = UboPacking.ReadIVec4(DataReadOnly, OffsetLumonSceneInts0);
            WriteIntVector4(OffsetLumonSceneInts0, enabled, tileSize, value, tilesPerAtlas);
        }
    }

    public int LumonSceneTilesPerAtlas
    {
        get => UboPacking.ReadInt32(DataReadOnly, OffsetLumonSceneInts0 + 12);
        set
        {
            var (enabled, tileSize, tilesPerAxis, _) = UboPacking.ReadIVec4(DataReadOnly, OffsetLumonSceneInts0);
            WriteIntVector4(OffsetLumonSceneInts0, enabled, tileSize, tilesPerAxis, value);
        }
    }

    #endregion

    #region Temporal Config

    public float TemporalAlpha
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetTemporalFloats0 + 0);
        set
        {
            var (_, depthReject, normalReject, _) = UboPacking.ReadVec4(DataReadOnly, OffsetTemporalFloats0);
            WriteVector4(OffsetTemporalFloats0, new(value, depthReject, normalReject, 0f));
        }
    }

    public float DepthRejectThreshold
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetTemporalFloats0 + 4);
        set
        {
            var (temporalAlpha, _, normalReject, _) = UboPacking.ReadVec4(DataReadOnly, OffsetTemporalFloats0);
            WriteVector4(OffsetTemporalFloats0, new(temporalAlpha, value, normalReject, 0f));
        }
    }

    public float NormalRejectThreshold
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetTemporalFloats0 + 8);
        set
        {
            var (temporalAlpha, depthReject, _, _) = UboPacking.ReadVec4(DataReadOnly, OffsetTemporalFloats0);
            WriteVector4(OffsetTemporalFloats0, new(temporalAlpha, depthReject, value, 0f));
        }
    }

    #endregion

    #region Debug Selection

    public int DebugMode
    {
        get => UboPacking.ReadInt32(DataReadOnly, OffsetDebugInts0 + 0);
        set
        {
            var (_, gatherAtlasSource, _, _) = UboPacking.ReadIVec4(DataReadOnly, OffsetDebugInts0);
            WriteIntVector4(OffsetDebugInts0, value, gatherAtlasSource, 0, 0);
        }
    }

    public int GatherAtlasSource
    {
        get => UboPacking.ReadInt32(DataReadOnly, OffsetDebugInts0 + 4);
        set
        {
            var (debugMode, _, _, _) = UboPacking.ReadIVec4(DataReadOnly, OffsetDebugInts0);
            WriteIntVector4(OffsetDebugInts0, debugMode, value, 0, 0);
        }
    }

    #endregion

    #region Composite Parameters

    public Vector3 IndirectTint
    {
        get
        {
            var (r, g, b, _) = UboPacking.ReadVec4(DataReadOnly, OffsetCompositeTintIntensity);
            return new Vector3(r, g, b);
        }
        set
        {
            var (_, _, _, intensity) = UboPacking.ReadVec4(DataReadOnly, OffsetCompositeTintIntensity);
            WriteVector4(OffsetCompositeTintIntensity, new(value.X, value.Y, value.Z, intensity));
        }
    }

    public float IndirectIntensity
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetCompositeTintIntensity + 12);
        set
        {
            var (r, g, b, _) = UboPacking.ReadVec4(DataReadOnly, OffsetCompositeTintIntensity);
            WriteVector4(OffsetCompositeTintIntensity, new(r, g, b, value));
        }
    }

    public float DiffuseAOStrength
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetAoStrengths + 0);
        set
        {
            var (_, spec, _, _) = UboPacking.ReadVec4(DataReadOnly, OffsetAoStrengths);
            WriteVector4(OffsetAoStrengths, new(value, spec, WorldProbeComparisonReady ? 1f : 0f, WorldProbeEffectGain));
        }
    }

    public float SpecularAOStrength
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetAoStrengths + 4);
        set
        {
            var (diff, _, _, _) = UboPacking.ReadVec4(DataReadOnly, OffsetAoStrengths);
            WriteVector4(OffsetAoStrengths, new(diff, value, WorldProbeComparisonReady ? 1f : 0f, WorldProbeEffectGain));
        }
    }

    /// <summary>Display-only amplification of the signed luminance difference.</summary>
    public float WorldProbeEffectGain
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetAoStrengths + 12);
        set
        {
            WriteVector4(OffsetAoStrengths, new(DiffuseAOStrength,
                SpecularAOStrength, WorldProbeComparisonReady ? 1f : 0f, value));
        }
    }

    /// <summary>Whether both lighting branches completed for the current frame.</summary>
    public bool WorldProbeComparisonReady
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetAoStrengths + 8) != 0f;
        set
        {
            WriteVector4(OffsetAoStrengths, new(DiffuseAOStrength, SpecularAOStrength, value ? 1f : 0f, WorldProbeEffectGain));
        }
    }

    #endregion
}
