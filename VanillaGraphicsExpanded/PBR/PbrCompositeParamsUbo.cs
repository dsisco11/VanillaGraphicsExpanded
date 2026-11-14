using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Packs composition controls and water source admission; camera and fog remain shared.</summary>
internal sealed class PbrCompositeParamsUbo : CpuUniformBuffer
{
    public const string BlockName = "VgePbrCompositeParamsUBO";
    private bool underwater, refractionSource, preOverlaySource, ambientOcclusion;

    #region Public API
    #region Layout
    internal const int UnderwaterOffset = 0;
    internal const int RefractionSourceOffset = 4;
    internal const int RawCameraLightEnabledOffset = 8;
    internal const int IndirectTintOffset = 16;
    internal const int AmbientOcclusionStrengthsOffset = 32;
    internal const int PreOverlaySourceOffset = 40;
    internal const int AmbientOcclusionEnabledOffset = 44;
    internal const int AtmosphereParametersOffset = 48;
    internal const int AerialSunDirectionOffset = 64;
    internal const int WaterAbsorptionOffset = 80;
    internal const int WaterScatteringOffset = 96;
    internal const int RawCameraLightOffset = 112;
    internal const int WaterSourceUpperOffset = 128;
    internal const int EnvironmentOffset = 144;
    internal const int ScatteringSunDirectionOffset = 160;
    internal const int ScatteringMoonDirectionOffset = 176;
    #endregion

    /// <summary>Byte extent of this concrete std140 layout.</summary>
    internal const int BlockSize = 192;
    /// <summary>Declares the immutable byte extent owned by this layout.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Allocates twelve std140 vectors for composition and raw camera-medium light conversion.</summary>
    public PbrCompositeParamsUbo() : base() { }

    /// <summary>Enables the clean pre-overlay pair only after successful current-frame capture.</summary>
    internal bool PreOverlaySourceEnabled
    {
        set { preOverlaySource = value; WriteFloat(PreOverlaySourceOffset, value ? 1 : 0); }
    }
    /// <summary>Controls optional pre-transport outputs without changing shared fog data.</summary>
    internal bool RefractionSourceEnabled
    {
        set { refractionSource = value; WriteFloat(RefractionSourceOffset, value ? 1 : 0); }
    }
    /// <summary>Selects engine underwater fog without applying it again to atmospheric air.</summary>
    internal void SetUnderwater(bool value)
    {
        underwater = value;
        // Medium selection must not erase the independently published raw-light convention.
        WriteFloat(UnderwaterOffset, underwater ? 1 : 0);
        WriteFloat(RefractionSourceOffset, refractionSource ? 1 : 0);
    }
    /// <summary>Publishes coordinates from the same generation as the finite-path atmosphere volumes.</summary>
    public void SetAtmosphere(Atmosphere.AtmosphereLighting? lighting)
    {
        WriteVector4(AtmosphereParametersOffset, new(lighting?.Altitude ?? .001f, lighting?.HorizonElevation ?? 0f, 0, 1 / (lighting?.LookupStorageScale ?? 1)));
        WriteVector4(AerialSunDirectionOffset, new(lighting?.Sun.X ?? 0, lighting?.Sun.Y ?? 1, lighting?.Sun.Z ?? 0, 0));
        WriteVector4(EnvironmentOffset, new(lighting?.Environment ?? Vector3.Zero, 0));
        WriteVector4(ScatteringSunDirectionOffset, new(lighting?.Sun ?? Vector3.UnitY, 0));
        WriteVector4(ScatteringMoonDirectionOffset, new(lighting?.Moon ?? Vector3.UnitY, 0));
    }
    /// <summary>Publishes boundary capture and the camera's starting water medium in SI units.</summary>
    internal void SetWaterVolume(Liquids.WaterVolumeFrame? frame)
    {
        var medium = frame?.CameraMedium;
        bool admitted = frame?.SourceUpper is { } upper && LightingCalibration.FiniteNonnegative(upper);
        WriteVector4(WaterAbsorptionOffset, new(medium?.AbsorptionPerMetre ?? Vector3.Zero, admitted ? 1 : 0));
        WriteVector4(WaterScatteringOffset, new(medium?.EffectiveScatteringPerMetre ?? Vector3.Zero, medium.HasValue ? 1 : 0));
        WriteVector4(RawCameraLightOffset, frame?.RawCameraLight ?? new Vector4(frame?.CameraScatteringSource ?? Vector3.Zero, 0));
        WriteFloat(RawCameraLightEnabledOffset, frame?.RawCameraLight.HasValue == true ? 1 : 0);
        WriteVector4(WaterSourceUpperOffset, new(frame?.SourceUpper ?? Vector3.Zero, admitted ? 1 : 0));
    }
    /// <summary>Sets the effect's indirect-lighting tint and scale.</summary>
    public (Vector3 tint, float intensity) IndirectTintAndIntensity
    {
        set { WriteVector4(IndirectTintOffset, new(value.tint, value.intensity)); }
    }
    /// <summary>Enables only a current-frame ambient-visibility publication.</summary>
    internal bool AmbientOcclusionEnabled
    {
        set { ambientOcclusion = value; WriteFloat(AmbientOcclusionEnabledOffset, value ? 1 : 0); }
    }
    /// <summary>Sets independent diffuse and specular attenuation while retaining publication flags.</summary>
    public (float diffuse, float specular) AOStrengths
    {
        set { WriteVector4(AmbientOcclusionStrengthsOffset, new(value.diffuse, value.specular, preOverlaySource ? 1 : 0, ambientOcclusion ? 1 : 0)); }
    }
    #endregion
}
