using System;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

/// <summary>
/// CPU-side wrapper for VgeLumOnUpsampleParamsUBO (binding 14, 32 bytes std140).
/// Used by lumon_upsample.fsh for bilateral upsampling and hole-fill parameters.
/// </summary>
public sealed class LumOnUpsampleParamsUbo : CpuUniformBuffer
{
    public const string BlockName = "VgeLumOnUpsampleParamsUBO";
    public const int UboSizeBytes = 32;

    // Byte offsets (std140 layout)
    private const int OffsetUpsampleFloats0 = 0;
    private const int OffsetUpsampleInts0 = 16;

    public LumOnUpsampleParamsUbo() : base(UboSizeBytes)
    {
    }

    /// <summary>
    /// Depth similarity sigma for bilateral upsample (float at offset 0).
    /// </summary>
    public float UpsampleDepthSigma
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetUpsampleFloats0 + 0);
        set
        {
            var (_, n, s, minConf) = UboPacking.ReadVec4(DataReadOnly, OffsetUpsampleFloats0);
            UboPacking.WriteVec4(DataWritable, OffsetUpsampleFloats0, value, n, s, minConf);
            MarkDirty(OffsetUpsampleFloats0, 16);
        }
    }

    /// <summary>
    /// Normal similarity power for bilateral upsample (float at offset 4).
    /// </summary>
    public float UpsampleNormalSigma
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetUpsampleFloats0 + 4);
        set
        {
            var (d, _, s, minConf) = UboPacking.ReadVec4(DataReadOnly, OffsetUpsampleFloats0);
            UboPacking.WriteVec4(DataWritable, OffsetUpsampleFloats0, d, value, s, minConf);
            MarkDirty(OffsetUpsampleFloats0, 16);
        }
    }

    /// <summary>
    /// Spatial kernel sigma for optional spatial denoise (float at offset 8).
    /// </summary>
    public float UpsampleSpatialSigma
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetUpsampleFloats0 + 8);
        set
        {
            var (d, n, _, minConf) = UboPacking.ReadVec4(DataReadOnly, OffsetUpsampleFloats0);
            UboPacking.WriteVec4(DataWritable, OffsetUpsampleFloats0, d, n, value, minConf);
            MarkDirty(OffsetUpsampleFloats0, 16);
        }
    }

    /// <summary>
    /// Minimum confidence (alpha) required for hole filling (float at offset 12).
    /// </summary>
    public float HoleFillMinConfidence
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetUpsampleFloats0 + 12);
        set
        {
            var (d, n, s, _) = UboPacking.ReadVec4(DataReadOnly, OffsetUpsampleFloats0);
            UboPacking.WriteVec4(DataWritable, OffsetUpsampleFloats0, d, n, s, value);
            MarkDirty(OffsetUpsampleFloats0, 16);
        }
    }

    /// <summary>
    /// Neighborhood radius in half-res pixels for hole filling (int at offset 16).
    /// </summary>
    public int HoleFillRadius
    {
        get => UboPacking.ReadInt32(DataReadOnly, OffsetUpsampleInts0 + 0);
        set
        {
            UboPacking.WriteIVec4(DataWritable, OffsetUpsampleInts0, value, 0, 0, 0);
            MarkDirty(OffsetUpsampleInts0, 16);
        }
    }
}
