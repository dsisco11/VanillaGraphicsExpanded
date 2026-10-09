using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>
/// CPU-side UBO for PBR composite shader parameters.
/// Layout matches VgePbrCompositeParamsUBO in GLSL (272 bytes).
/// </summary>
internal sealed class PbrCompositeParamsUbo : CpuUniformBuffer
{
    public const string BlockName = "VgePbrCompositeParamsUBO";

    private const int OffsetInvProjection = 0;       // mat4 at 0
    private const int OffsetViewMatrix = 64;         // mat4 at 64
    private const int OffsetFogColor = 128;          // vec4 at 128
    private const int OffsetFogFloats = 144;         // vec4 at 144 (fogDensity, fogMin, 0, 0)
    private const int OffsetIndirectTintIntensity = 160; // vec4 at 160 (tint.rgb, intensity)
    private const int OffsetAOStrengths = 176;       // vec4 at 176 (diffuseAO, specularAO, preOverlaySource, ambientOcclusion)
    private bool underwater;
    private bool refractionSource;
    private bool preOverlaySource;
    private bool ambientOcclusion;
    // Total: 192 bytes

    public PbrCompositeParamsUbo() : base(272)
    {
    }

    #region Matrices

    public float[] InvProjectionMatrix
    {
        set
        {
            WriteMatrix4(OffsetInvProjection, value);
        }
    }

    public float[] ViewMatrix
    {
        set
        {
            WriteMatrix4(OffsetViewMatrix, value);
        }
    }

    #endregion

    #region Fog
    /// <summary>Enables the clean pre-overlay pair only after successful current-frame capture.</summary>
    internal bool PreOverlaySourceEnabled
    {
        set { preOverlaySource = value; WriteFloat(OffsetAOStrengths + 8, value ? 1 : 0); }
    }
    /// <summary>Controls optional pre-transport outputs in the reserved fog component.</summary>
    internal bool RefractionSourceEnabled
    {
        set { refractionSource = value; WriteFloat(OffsetFogFloats + 12, value ? 1 : 0); }
    }
    /// <summary>Publishes valid boundary capture and the camera's starting medium in SI units.</summary>
    internal void SetWaterVolume(Liquids.WaterVolumeFrame? frame)
    {
        var medium = frame?.CameraMedium;
        WriteVector4(224, new(medium?.AbsorptionPerMetre ?? Vector3.Zero, frame.HasValue ? 1 : 0));
        WriteVector4(240, new(medium?.EffectiveScatteringPerMetre ?? Vector3.Zero, medium.HasValue ? 1 : 0));
        WriteVector4(256, new(frame?.CameraScatteringSource ?? Vector3.Zero, 0));
    }

    /// <summary>Publishes coordinates from the same generation as the bound finite-path volumes.</summary>
    public void SetAtmosphere(Atmosphere.AtmosphereLighting? lighting)
    {
        WriteVector4(192, new(lighting?.Altitude ?? .001f, lighting?.HorizonElevation ?? 0f, 0, 0));
        WriteVector4(208, new(lighting?.Sun.X ?? 0, lighting?.Sun.Y ?? 1, lighting?.Sun.Z ?? 0, 0));
    }

    /// <summary>Restricts legacy engine fog to the underwater medium, independently of atmospheric availability.</summary>
    internal void SetUnderwater(bool value)
    {
        underwater = value;
        WriteFloat(OffsetFogFloats + 8, value ? 1f : 0f);

    }

    public Vector4 RgbaFogIn
    {
        set
        {
            WriteVector4(OffsetFogColor, new(value.X, value.Y, value.Z, value.W));
        }
    }

    public (float fogDensity, float fogMin) FogParams
    {
        set
        {
            WriteVector4(OffsetFogFloats, new(value.fogDensity, value.fogMin, underwater ? 1f : 0f, refractionSource ? 1f : 0f));
        }
    }

    #endregion

    #region Indirect Lighting

    public (Vector3 tint, float intensity) IndirectTintAndIntensity
    {
        set
        {
            WriteVector4(OffsetIndirectTintIntensity, new(value.tint.X, value.tint.Y, value.tint.Z, value.intensity));
        }
    }

    #endregion

    #region AO Strengths

    /// <summary>Enables only a current-frame ambient visibility publication.</summary>
    internal bool AmbientOcclusionEnabled
    {
        set { ambientOcclusion = value; WriteFloat(OffsetAOStrengths + 12, value ? 1 : 0); }
    }

    public (float diffuse, float specular) AOStrengths
    {
        set
        {
            WriteVector4(OffsetAOStrengths, new(value.diffuse, value.specular, preOverlaySource ? 1f : 0f, ambientOcclusion ? 1f : 0f));
        }
    }

    #endregion
}
