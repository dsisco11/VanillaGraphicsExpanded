using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>
/// CPU-side UBO for PBR composite shader parameters.
/// Layout matches VgePbrCompositeParamsUBO in GLSL (224 bytes).
/// </summary>
internal sealed class PbrCompositeParamsUbo : CpuUniformBuffer
{
    public const string BlockName = "VgePbrCompositeParamsUBO";

    private const int OffsetInvProjection = 0;       // mat4 at 0
    private const int OffsetViewMatrix = 64;         // mat4 at 64
    private const int OffsetFogColor = 128;          // vec4 at 128
    private const int OffsetFogFloats = 144;         // vec4 at 144 (fogDensity, fogMin, 0, 0)
    private const int OffsetIndirectTintIntensity = 160; // vec4 at 160 (tint.rgb, intensity)
    private const int OffsetAOStrengths = 176;       // vec4 at 176 (diffuseAO, specularAO, 0, 0)
    private bool underwater;
    // Total: 192 bytes

    public PbrCompositeParamsUbo() : base(224)
    {
    }

    #region Matrices

    public float[] InvProjectionMatrix
    {
        set
        {
            UboPacking.WriteMat4(DataWritable, OffsetInvProjection, value);
            MarkDirty(OffsetInvProjection, 64);
        }
    }

    public float[] ViewMatrix
    {
        set
        {
            UboPacking.WriteMat4(DataWritable, OffsetViewMatrix, value);
            MarkDirty(OffsetViewMatrix, 64);
        }
    }

    #endregion

    #region Fog

    /// <summary>Publishes coordinates from the same generation as the bound finite-path volumes.</summary>
    public void SetAtmosphere(Atmosphere.AtmosphereLighting? lighting)
    {
        UboPacking.WriteVec4(DataWritable, 192, lighting?.Altitude ?? .001f, lighting?.HorizonElevation ?? 0f, 0, 0);
        UboPacking.WriteVec4(DataWritable, 208, lighting?.Sun.X ?? 0, lighting?.Sun.Y ?? 1, lighting?.Sun.Z ?? 0, 0);
        MarkDirty(192, 32);
    }

    /// <summary>Restricts legacy engine fog to the underwater medium, independently of atmospheric availability.</summary>
    internal void SetUnderwater(bool value)
    {
        underwater = value;
        System.Runtime.InteropServices.MemoryMarshal.Write(DataWritable.Slice(OffsetFogFloats + 8, 4), value ? 1f : 0f);
        MarkDirty(OffsetFogFloats + 8, 4);
    }

    public Vector4 RgbaFogIn
    {
        set
        {
            UboPacking.WriteVec4(DataWritable, OffsetFogColor, value.X, value.Y, value.Z, value.W);
            MarkDirty(OffsetFogColor, 16);
        }
    }

    public (float fogDensity, float fogMin) FogParams
    {
        set
        {
            UboPacking.WriteVec4(DataWritable, OffsetFogFloats, value.fogDensity, value.fogMin, underwater ? 1f : 0f, 0f);
            MarkDirty(OffsetFogFloats, 16);
        }
    }

    #endregion

    #region Indirect Lighting

    public (Vector3 tint, float intensity) IndirectTintAndIntensity
    {
        set
        {
            UboPacking.WriteVec4(DataWritable, OffsetIndirectTintIntensity, value.tint.X, value.tint.Y, value.tint.Z, value.intensity);
            MarkDirty(OffsetIndirectTintIntensity, 16);
        }
    }

    #endregion

    #region AO Strengths

    public (float diffuse, float specular) AOStrengths
    {
        set
        {
            UboPacking.WriteVec4(DataWritable, OffsetAOStrengths, value.diffuse, value.specular, 0f, 0f);
            MarkDirty(OffsetAOStrengths, 16);
        }
    }

    #endregion
}
