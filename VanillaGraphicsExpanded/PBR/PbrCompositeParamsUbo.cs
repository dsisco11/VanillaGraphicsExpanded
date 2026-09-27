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

    /// <summary>Publishes a matching horizon source and per-metre extinction; null retains the engine-only fog path.</summary>
    public void SetAtmosphere(Atmosphere.AtmosphereLighting? lighting)
    {
        var horizon = lighting?.Horizon ?? Vector3.Zero;
        var extinction = lighting?.Extinction ?? Vector3.Zero;
        UboPacking.WriteVec4(DataWritable, 192, horizon.X, horizon.Y, horizon.Z, 0f);
        UboPacking.WriteVec4(DataWritable, 208, extinction.X, extinction.Y, extinction.Z, 0f);
        MarkDirty(192, 32);
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
            UboPacking.WriteVec4(DataWritable, OffsetFogFloats, value.fogDensity, value.fogMin, 0f, 0f);
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
