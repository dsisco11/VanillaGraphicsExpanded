using System;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Packs one coherent borrowed map placement and completed physical illumination.</summary>
internal sealed class CloudVolumeParameters : CpuUniformBuffer
{
    private CloudShadowOrientation solarOrientation,lunarOrientation;
    #region Public API
    /// <summary>Allocates fifteen explicitly matched std140 vectors, including independent light-space tangents.</summary>
    internal CloudVolumeParameters() : base(240) { }

    /// <summary>Identifies the discrete local underwater transport branch used instead of aerial transport.</summary>
    internal bool UsesUnderwaterTransport => System.Runtime.InteropServices.MemoryMarshal.Read<float>(Bytes.Slice(152,4))>.7f;

    /// <summary>Captures the main view medium without modifying native cloud or atmosphere state.</summary>
    internal void Capture(ICoreClientAPI api, CloudMapInput input, AtmosphereLighting lighting, CloudVolumeSettings settings)
    {
        var u = api.Render.ShaderUniforms;
        var fog = api.Ambient.BlendedFogColor;
        CaptureMap(input.Storage.Width, input.Placement, CloudMapInput.MetresPerWorldUnit, settings);
        CaptureLighting(lighting, u.CameraUnderwater,
            new(api.Ambient.BlendedFogDensity, api.Ambient.BlendedFogMin, api.Ambient.BlendedFlatFogDensity,
                api.Ambient.BlendedFlatFogYPosForShader - (float)api.World.Player.Entity.CameraPos.Y), new(fog.R, fog.G, fog.B));
    }

    /// <summary>Stages map coordinates; material coordinates remove integer wind exactly once and follow the moving grid.</summary>
    internal void CaptureMap(int width, CloudPlacement placement, float metresPerWorldUnit, CloudVolumeSettings settings)
    {
        settings.Validate();
        // Restrict conversions to a useful finite shader-arithmetic domain. Runtime uses one;
        // controlled views may vary it without underflowing or overflowing path conversion.
        if (width is < 1 or > 200 || !placement.IsFinite || !float.IsFinite(metresPerWorldUnit)
            || metresPerWorldUnit is < 1e-6f or > 1e6f)
            throw new ArgumentException("Invalid cloud volume domain.");
        WriteVector4(0, new(placement.Offset, width));
        // Periodic lattice anchoring is reduced in double precision. Center changes cancel
        // the grid shift; fractional wind already exists in Offset and must not be added again.
        WriteVector4(16, new(Anchor(placement.CenterX, placement.WindTileX, width),
            Anchor(placement.CenterZ, placement.WindTileZ, width), metresPerWorldUnit, 0));
        WriteVector4(32, new(settings.Detail ? settings.DetailAmplitude : 0, settings.Detail ? settings.EdgeWidth : 0,
            settings.NoiseScale, settings.Detail ? 1 : 0));
        WriteVector4(48, new(settings.ViewRaySampleMax, settings.LightRaySampleMax, settings.OctaveContribution, settings.OctaveOcclusion));
        WriteVector4(192, new(settings.ViewRaySampleMin, settings.ViewBudgetDistance, settings.ShadowDistance, settings.StopTransmittance));
    }

    /// <summary>Stages already attenuated lux and E/pi, plus one matching aerial decode and local-medium endpoint.</summary>
    internal void CaptureLighting(AtmosphereLighting lighting, float underwater, Vector4 fog, Vector3 color)
    {
        _ = CloudSimpleLighting.Maximum(lighting, 0);
        if (!float.IsFinite(underwater) || !float.IsFinite(fog.X) || !float.IsFinite(fog.Y)
            || !float.IsFinite(fog.Z) || !float.IsFinite(fog.W) || fog.X < 0 || !LightingCalibration.FiniteNonnegative(color))
            throw new ArgumentException("Invalid cloud volume medium.");
        WriteVector4(64, new(lighting.Solar, 0)); WriteVector4(80, new(lighting.Lunar, 0));
        WriteVector4(96, new(lighting.Environment, 0));
        Vector3 sun=Vector3.Normalize(lighting.Sun),moon=Vector3.Normalize(lighting.Moon);
        WriteVector4(112, new(sun, 0)); WriteVector4(128, new(moon, 0));
        WriteVector4(208,new(solarOrientation.Capture(sun),0));
        WriteVector4(224,new(lunarOrientation.Capture(moon),0));
        WriteVector4(144, new(lighting.Altitude, lighting.HorizonElevation, underwater, 1 / lighting.LookupStorageScale));
        WriteVector4(160, fog); WriteVector4(176, new(color, 0));
    }
    #endregion

    #region Private
    /// <summary>Keeps the 12800-unit material period precise even at large committed integer coordinates.</summary>
    private static float Anchor(int center, int wind, int width)
    {
        double value = ((double)center - wind - width * .5) * 50;
        return (float)(value - Math.Floor(value / 12800) * 12800);
    }
    #endregion
}
