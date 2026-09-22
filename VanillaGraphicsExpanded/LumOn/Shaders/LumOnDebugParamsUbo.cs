using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

/// <summary>
/// CPU-side wrapper for VgeLumOnDebugParamsUBO (binding 14, 128 bytes std140).
/// Used by lumon_debug.fsh for debug visualization parameters.
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
            UboPacking.WriteIVec4(DataWritable, OffsetLumonSceneInts0, value, tileSize, tilesPerAxis, tilesPerAtlas);
            MarkDirty(OffsetLumonSceneInts0, 16);
        }
    }

    public int LumonSceneTileSizeTexels
    {
        get => UboPacking.ReadInt32(DataReadOnly, OffsetLumonSceneInts0 + 4);
        set
        {
            var (enabled, _, tilesPerAxis, tilesPerAtlas) = UboPacking.ReadIVec4(DataReadOnly, OffsetLumonSceneInts0);
            UboPacking.WriteIVec4(DataWritable, OffsetLumonSceneInts0, enabled, value, tilesPerAxis, tilesPerAtlas);
            MarkDirty(OffsetLumonSceneInts0, 16);
        }
    }

    public int LumonSceneTilesPerAxis
    {
        get => UboPacking.ReadInt32(DataReadOnly, OffsetLumonSceneInts0 + 8);
        set
        {
            var (enabled, tileSize, _, tilesPerAtlas) = UboPacking.ReadIVec4(DataReadOnly, OffsetLumonSceneInts0);
            UboPacking.WriteIVec4(DataWritable, OffsetLumonSceneInts0, enabled, tileSize, value, tilesPerAtlas);
            MarkDirty(OffsetLumonSceneInts0, 16);
        }
    }

    public int LumonSceneTilesPerAtlas
    {
        get => UboPacking.ReadInt32(DataReadOnly, OffsetLumonSceneInts0 + 12);
        set
        {
            var (enabled, tileSize, tilesPerAxis, _) = UboPacking.ReadIVec4(DataReadOnly, OffsetLumonSceneInts0);
            UboPacking.WriteIVec4(DataWritable, OffsetLumonSceneInts0, enabled, tileSize, tilesPerAxis, value);
            MarkDirty(OffsetLumonSceneInts0, 16);
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
            UboPacking.WriteVec4(DataWritable, OffsetTemporalFloats0, value, depthReject, normalReject, 0f);
            MarkDirty(OffsetTemporalFloats0, 16);
        }
    }

    public float DepthRejectThreshold
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetTemporalFloats0 + 4);
        set
        {
            var (temporalAlpha, _, normalReject, _) = UboPacking.ReadVec4(DataReadOnly, OffsetTemporalFloats0);
            UboPacking.WriteVec4(DataWritable, OffsetTemporalFloats0, temporalAlpha, value, normalReject, 0f);
            MarkDirty(OffsetTemporalFloats0, 16);
        }
    }

    public float NormalRejectThreshold
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetTemporalFloats0 + 8);
        set
        {
            var (temporalAlpha, depthReject, _, _) = UboPacking.ReadVec4(DataReadOnly, OffsetTemporalFloats0);
            UboPacking.WriteVec4(DataWritable, OffsetTemporalFloats0, temporalAlpha, depthReject, value, 0f);
            MarkDirty(OffsetTemporalFloats0, 16);
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
            UboPacking.WriteIVec4(DataWritable, OffsetDebugInts0, value, gatherAtlasSource, 0, 0);
            MarkDirty(OffsetDebugInts0, 16);
        }
    }

    public int GatherAtlasSource
    {
        get => UboPacking.ReadInt32(DataReadOnly, OffsetDebugInts0 + 4);
        set
        {
            var (debugMode, _, _, _) = UboPacking.ReadIVec4(DataReadOnly, OffsetDebugInts0);
            UboPacking.WriteIVec4(DataWritable, OffsetDebugInts0, debugMode, value, 0, 0);
            MarkDirty(OffsetDebugInts0, 16);
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
            UboPacking.WriteVec4(DataWritable, OffsetCompositeTintIntensity, value.X, value.Y, value.Z, intensity);
            MarkDirty(OffsetCompositeTintIntensity, 16);
        }
    }

    public float IndirectIntensity
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetCompositeTintIntensity + 12);
        set
        {
            var (r, g, b, _) = UboPacking.ReadVec4(DataReadOnly, OffsetCompositeTintIntensity);
            UboPacking.WriteVec4(DataWritable, OffsetCompositeTintIntensity, r, g, b, value);
            MarkDirty(OffsetCompositeTintIntensity, 16);
        }
    }

    public float DiffuseAOStrength
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetAoStrengths + 0);
        set
        {
            var (_, spec, _, _) = UboPacking.ReadVec4(DataReadOnly, OffsetAoStrengths);
            UboPacking.WriteVec4(DataWritable, OffsetAoStrengths, value, spec, WorldProbeComparisonReady ? 1f : 0f, WorldProbeEffectGain);
            MarkDirty(OffsetAoStrengths, 16);
        }
    }

    public float SpecularAOStrength
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetAoStrengths + 4);
        set
        {
            var (diff, _, _, _) = UboPacking.ReadVec4(DataReadOnly, OffsetAoStrengths);
            UboPacking.WriteVec4(DataWritable, OffsetAoStrengths, diff, value, WorldProbeComparisonReady ? 1f : 0f, WorldProbeEffectGain);
            MarkDirty(OffsetAoStrengths, 16);
        }
    }

    /// <summary>Display-only amplification of the signed luminance difference.</summary>
    public float WorldProbeEffectGain
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetAoStrengths + 12);
        set
        {
            UboPacking.WriteVec4(DataWritable, OffsetAoStrengths, DiffuseAOStrength,
                SpecularAOStrength, WorldProbeComparisonReady ? 1f : 0f, value);
            MarkDirty(OffsetAoStrengths, 16);
        }
    }

    /// <summary>Whether both lighting branches completed for the current frame.</summary>
    public bool WorldProbeComparisonReady
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetAoStrengths + 8) != 0f;
        set
        {
            UboPacking.WriteVec4(DataWritable, OffsetAoStrengths, DiffuseAOStrength, SpecularAOStrength, value ? 1f : 0f, WorldProbeEffectGain);
            MarkDirty(OffsetAoStrengths, 16);
        }
    }

    #endregion
}
