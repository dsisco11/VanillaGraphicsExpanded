using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Packs composition-specific controls into eight std140 vectors; camera and fog data remain shared.</summary>
internal sealed class PbrCompositeParamsUbo : CpuUniformBuffer
{
    public const string BlockName = "VgePbrCompositeParamsUBO";
    private bool underwater, refractionSource, preOverlaySource, ambientOcclusion;

    #region Public API
    /// <summary>Allocates the 128-byte composition contract.</summary>
    public PbrCompositeParamsUbo() : base(128) { }

    /// <summary>Enables the clean pre-overlay pair only after successful current-frame capture.</summary>
    internal bool PreOverlaySourceEnabled
    {
        set { preOverlaySource = value; WriteFloat(40, value ? 1 : 0); }
    }
    /// <summary>Controls optional pre-transport outputs without changing shared fog data.</summary>
    internal bool RefractionSourceEnabled
    {
        set { refractionSource = value; WriteFloat(4, value ? 1 : 0); }
    }
    /// <summary>Selects engine underwater fog without applying it again to atmospheric air.</summary>
    internal void SetUnderwater(bool value)
    {
        underwater = value;
        WriteVector4(0, new(underwater ? 1 : 0, refractionSource ? 1 : 0, 0, 0));
    }
    /// <summary>Publishes coordinates from the same generation as the finite-path atmosphere volumes.</summary>
    public void SetAtmosphere(Atmosphere.AtmosphereLighting? lighting)
    {
        WriteVector4(48, new(lighting?.Altitude ?? .001f, lighting?.HorizonElevation ?? 0f, 0, 0));
        WriteVector4(64, new(lighting?.Sun.X ?? 0, lighting?.Sun.Y ?? 1, lighting?.Sun.Z ?? 0, 0));
    }
    /// <summary>Publishes boundary capture and the camera's starting water medium in SI units.</summary>
    internal void SetWaterVolume(Liquids.WaterVolumeFrame? frame)
    {
        var medium = frame?.CameraMedium;
        WriteVector4(80, new(medium?.AbsorptionPerMetre ?? Vector3.Zero, frame.HasValue ? 1 : 0));
        WriteVector4(96, new(medium?.EffectiveScatteringPerMetre ?? Vector3.Zero, medium.HasValue ? 1 : 0));
        WriteVector4(112, new(frame?.CameraScatteringSource ?? Vector3.Zero, 0));
    }
    /// <summary>Sets the effect's indirect-lighting tint and scale.</summary>
    public (Vector3 tint, float intensity) IndirectTintAndIntensity
    {
        set { WriteVector4(16, new(value.tint, value.intensity)); }
    }
    /// <summary>Enables only a current-frame ambient-visibility publication.</summary>
    internal bool AmbientOcclusionEnabled
    {
        set { ambientOcclusion = value; WriteFloat(44, value ? 1 : 0); }
    }
    /// <summary>Sets independent diffuse and specular attenuation while retaining publication flags.</summary>
    public (float diffuse, float specular) AOStrengths
    {
        set { WriteVector4(32, new(value.diffuse, value.specular, preOverlaySource ? 1 : 0, ambientOcclusion ? 1 : 0)); }
    }
    #endregion
}
