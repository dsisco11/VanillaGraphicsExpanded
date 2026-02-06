using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

/// <summary>
/// CPU-side wrapper for VgeLumOnCombineParamsUBO (binding 14, 32 bytes std140).
/// Used by lumon_combine.fsh for indirect lighting integration parameters.
/// </summary>
public sealed class LumOnCombineParamsUbo : CpuUniformBuffer
{
    public const string BlockName = "VgeLumOnCombineParamsUBO";
    public const int UboSizeBytes = 32;

    // Byte offsets (std140 layout)
    private const int OffsetIndirectTintIntensity = 0;
    private const int OffsetAoStrengths = 16;

    public LumOnCombineParamsUbo() : base(UboSizeBytes)
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
            UboPacking.WriteVec4(DataWritable, OffsetIndirectTintIntensity, value.X, value.Y, value.Z, intensity);
            MarkDirty(OffsetIndirectTintIntensity, 16);
        }
    }

    /// <summary>
    /// Global intensity multiplier for indirect lighting (float at offset 12).
    /// </summary>
    public float IndirectIntensity
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetIndirectTintIntensity + 12);
        set
        {
            var (r, g, b, _) = UboPacking.ReadVec4(DataReadOnly, OffsetIndirectTintIntensity);
            UboPacking.WriteVec4(DataWritable, OffsetIndirectTintIntensity, r, g, b, value);
            MarkDirty(OffsetIndirectTintIntensity, 16);
        }
    }

    /// <summary>
    /// Diffuse AO strength multiplier (float at offset 16).
    /// </summary>
    public float DiffuseAOStrength
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetAoStrengths + 0);
        set
        {
            var (_, spec, _, _) = UboPacking.ReadVec4(DataReadOnly, OffsetAoStrengths);
            UboPacking.WriteVec4(DataWritable, OffsetAoStrengths, value, spec, 0f, 0f);
            MarkDirty(OffsetAoStrengths, 16);
        }
    }

    /// <summary>
    /// Specular AO strength multiplier (float at offset 20).
    /// </summary>
    public float SpecularAOStrength
    {
        get => UboPacking.ReadFloat(DataReadOnly, OffsetAoStrengths + 4);
        set
        {
            var (diff, _, _, _) = UboPacking.ReadVec4(DataReadOnly, OffsetAoStrengths);
            UboPacking.WriteVec4(DataWritable, OffsetAoStrengths, diff, value, 0f, 0f);
            MarkDirty(OffsetAoStrengths, 16);
        }
    }
}
