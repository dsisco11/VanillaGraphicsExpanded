using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

/// <summary>
/// CPU-side wrapper for VgeLumOnProbeParamsUBO (binding 14, 64 bytes std140).
/// Shared across probe-atlas shaders (gather, filter, temporal, trace, anchor, SH9).
/// </summary>
public sealed class LumOnProbeParamsUbo : CpuUniformBuffer
{
    public const string BlockName = "VgeLumOnProbeParamsUBO";
    public const int UboSizeBytes = 64;

    // Byte offsets (std140 layout)
    private const int OffsetIndirectTintIntensity = 0;
    private const int OffsetProbeFloats0 = 16;
    private const int OffsetProbeInts0 = 32;
    private const int OffsetAnchorFloats0 = 48;

    public LumOnProbeParamsUbo() : base(UboSizeBytes)
    {
    }

    /// <summary>
    /// RGB tint applied to indirect lighting (vec3 at offset 0).
    /// </summary>
    public Vector3 IndirectTint
    {
        get
        {
            var (r, g, b, _) = UboPacking.ReadVec4(DataReadOnly, OffsetIndirectTintIntensity);
            return new Vector3(r, g, b);
        }
        set
        {
            var (_, _, _, intensity) = UboPacking.ReadVec4(DataReadOnly, OffsetIndirectTintIntensity);
            WriteVector4(OffsetIndirectTintIntensity, new(value.X, value.Y, value.Z, intensity));
        }
    }

    /// <summary>
    /// Intensity multiplier for indirect lighting (float at offset 12).
    /// </summary>
    public float Intensity
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetIndirectTintIntensity + 12);
        set
        {
            var (r, g, b, _) = UboPacking.ReadVec4(DataReadOnly, OffsetIndirectTintIntensity);
            WriteVector4(OffsetIndirectTintIntensity, new(r, g, b, value));
        }
    }

    /// <summary>
    /// Base temporal blend factor (float at offset 16).
    /// </summary>
    public float TemporalAlpha
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetProbeFloats0 + 0);
        set
        {
            var (_, hitReject, hitSigma, leakThreshold) = UboPacking.ReadVec4(DataReadOnly, OffsetProbeFloats0);
            WriteVector4(OffsetProbeFloats0, new(value, hitReject, hitSigma, leakThreshold));
        }
    }

    /// <summary>
    /// Hit-distance rejection threshold for disocclusion (float at offset 20).
    /// </summary>
    public float HitDistanceRejectThreshold
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetProbeFloats0 + 4);
        set
        {
            var (temporalAlpha, _, hitSigma, leakThreshold) = UboPacking.ReadVec4(DataReadOnly, OffsetProbeFloats0);
            WriteVector4(OffsetProbeFloats0, new(temporalAlpha, value, hitSigma, leakThreshold));
        }
    }

    /// <summary>
    /// Edge-stopping sigma for hit-distance differences in filter (float at offset 24).
    /// </summary>
    public float HitDistanceSigma
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetProbeFloats0 + 8);
        set
        {
            var (temporalAlpha, hitReject, _, leakThreshold) = UboPacking.ReadVec4(DataReadOnly, OffsetProbeFloats0);
            WriteVector4(OffsetProbeFloats0, new(temporalAlpha, hitReject, value, leakThreshold));
        }
    }

    /// <summary>
    /// Leak prevention threshold for gather (float at offset 28).
    /// </summary>
    public float LeakThreshold
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetProbeFloats0 + 12);
        set
        {
            var (temporalAlpha, hitReject, hitSigma, _) = UboPacking.ReadVec4(DataReadOnly, OffsetProbeFloats0);
            WriteVector4(OffsetProbeFloats0, new(temporalAlpha, hitReject, hitSigma, value));
        }
    }

    /// <summary>
    /// Filter radius in texels for probe-atlas filter pass (int at offset 32).
    /// </summary>
    public int FilterRadius
    {
        get => UboPacking.ReadInt32(DataReadOnly, OffsetProbeInts0 + 0);
        set
        {
            var (_, sampleStride, _, _) = UboPacking.ReadIVec4(DataReadOnly, OffsetProbeInts0);
            WriteIntVector4(OffsetProbeInts0, value, sampleStride, 0, 0);
        }
    }

    /// <summary>
    /// Sample stride for hemisphere integration in gather (int at offset 36).
    /// </summary>
    public int SampleStride
    {
        get => UboPacking.ReadInt32(DataReadOnly, OffsetProbeInts0 + 4);
        set
        {
            var (filterRadius, _, _, _) = UboPacking.ReadIVec4(DataReadOnly, OffsetProbeInts0);
            WriteIntVector4(OffsetProbeInts0, filterRadius, value, 0, 0);
        }
    }

    /// <summary>
    /// Depth discontinuity threshold for probe anchor edge detection (float at offset 48).
    /// </summary>
    public float DepthDiscontinuityThreshold
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetAnchorFloats0 + 0);
        set
        {
            WriteVector4(OffsetAnchorFloats0, new(value, SuppressWorldProbeRadiance ? 1f : 0f, 0f, 0f));
        }
    }
    /// <summary>
    /// Zeros accepted world radiance without changing sample validity or fallback selection.
    /// </summary>
    public bool SuppressWorldProbeRadiance
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetAnchorFloats0 + 4) != 0f;
        set
        {
            WriteVector4(OffsetAnchorFloats0, new(DepthDiscontinuityThreshold, value ? 1f : 0f, 0f, 0f));
        }
    }

}
