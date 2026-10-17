using System;
using System.Numerics;
using FluffyClouds;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.SceneColor;

/// <summary>Publishes the native scene adapters' actual expression bounds in physical nits.</summary>
internal readonly record struct SceneColorCompatibilityPublication(uint FrameIndex,
    float Fog, float Sky, float Stars, float Moon, float Particles, float Clouds, float CloudMap)
{
    private static readonly AccessTools.FieldRef<DefaultShaderUniforms, float> Daylight =
        AccessTools.FieldRefAccess<DefaultShaderUniforms, float>("SkyDaylight");
    private static readonly AccessTools.FieldRef<FluffyCloudsModSystem, CloudRendererMap> MapOwner =
        AccessTools.FieldRefAccess<FluffyCloudsModSystem, CloudRendererMap>("map");
    private static readonly AccessTools.FieldRef<CloudRendererMap, bool> MapEnabled =
        AccessTools.FieldRefAccess<CloudRendererMap, bool>("renderCloudMap");
    private static readonly AccessTools.FieldRef<CloudRendererMap, float> MapBrightness =
        AccessTools.FieldRefAccess<CloudRendererMap, float>("blendedGlobalCloudBrightness");

    #region Public API
    /// <summary>Includes convex adapter layers conservatively without charging every particle or overlay submission.</summary>
    internal float MaximumNits => LightingStorage.UpperBound((double)Fog + Sky + Stars + Moon
        + Particles + Clouds + CloudMap + LightingCalibration.EmissionReferenceNits);

    /// <summary>Captures concrete native owners after light collection and before scene consumers.</summary>
    internal static SceneColorCompatibilityPublication FromNative(ICoreClientAPI api,
        SceneColorFogEnvelope fog, SceneColorCloudInputs cloud)
    {
        ArgumentNullException.ThrowIfNull(api);
        uint frame = VgeFrameRenderer.Current.FrameIndex;
        if (fog.FrameIndex != frame || cloud.FrameIndex != frame)
            throw new InvalidOperationException("Native scene inputs do not belong to the current frame.");
        var render = api.Render;
        var u = render.ShaderUniforms;
        // Normalized depth and these positive clip planes certify the native
        // underwater mix's [0,1] interval. A negative fog density would extrapolate
        // the ordinary cloud mix after its separate density subtraction.
        if (!float.IsFinite(u.ZNear) || !float.IsFinite(u.ZFar) || u.ZNear <= 0 || u.ZFar < u.ZNear
            || !float.IsFinite(api.Ambient.BlendedFogDensity) || api.Ambient.BlendedFogDensity < 0)
            throw new ArgumentException("Native scene depth and fog mixing are outside their admitted domain.");
        var ambient = render.AmbientColor;
        var tint = u.WaterMurkColor;
        Vector3 ambientRgb = new(ambient.X, ambient.Y, ambient.Z);
        Vector3 tintRgb = new(tint.R, tint.G, tint.B);
        if (!LightingCalibration.FiniteNonnegative(tintRgb))
            throw new ArgumentException("Native underwater tint must be finite nonnegative authored RGB.");
        var mapSystem = api.ModLoader.GetModSystem<FluffyCloudsModSystem>();
        var map = mapSystem is null ? null : MapOwner(mapSystem);
        // Keep the native map-color allowance: the retained producer still emits it,
        // and a mid-frame ownership release can restore the native visible consumer.
        bool mapEnabled = map is not null && MapEnabled(map);
        float mapGain = mapEnabled ? Math.Max(Math.Abs(MapBrightness(map!)),
            Math.Abs(api.Ambient.BlendedCloudBrightness)) : 0;
        var publication = FromInputs(frame, fog, cloud.VertexMaximum, mapEnabled, mapGain, ambientRgb,
            Math.Max(tintRgb.X, Math.Max(tintRgb.Y, tintRgb.Z)), Daylight(u),
            u.NightVisionStrength, u.PsychedelicStrength, u.DropShadowIntensity,
            // Native Use uploads the live engine list rather than the VGE point
            // snapshot. Re-certify that exact list here, including late submissions.
            SceneColorPointLightBounds.FromEngine(u.PointLightsCount, u.PointLightColors3));
        // The owned source supplies its own physical envelope. Do not continue
        // reserving the retired native cubemap's compatibility brightness.
        if (Atmosphere.Celestial.CelestialDrawPass.IsEnabled) publication = publication with { Stars = 0 };
        return Atmosphere.Celestial.MoonDrawPass.IsEnabled ? publication with { Moon = 0 } : publication;
    }

    /// <summary>Assembles inspected native expressions and the supported normalized texture/color owner contracts.</summary>
    internal static SceneColorCompatibilityPublication FromInputs(uint frame, SceneColorFogEnvelope fog,
        float cloudVertexMaximum, bool mapEnabled, float mapBrightnessMagnitude, Vector3 ambient,
        float tintMaximum, float dayLight, float nightVision, float perception, float shadowIntensity,
        SceneColorPointLightBounds points)
    {
        if (fog.FrameIndex != frame || !points.Supported)
            throw new ArgumentException("Compatibility publication requires matching admitted source metadata.");
        // Native asset/bitmap owners upload normalized unsigned-byte celestial,
        // sky/glow, decal and packed vertex colors. Their sampled component maximum
        // is one, including replacements through those owners and linear filtering.
        float skyEndpoint = Math.Max(1, fog.AuthoredMaximum);
        float sky = SceneColorCompatibilityBounds.PhysicalSky(perception, tintMaximum, nightVision);
        float stars = SceneColorCompatibilityBounds.NightSky(1, nightVision, tintMaximum, dayLight, 0, 0, 1);
        float moon = Math.Max(
            SceneColorCompatibilityBounds.Moon(1, 1, fog.AuthoredMaximum, skyEndpoint, tintMaximum, false, 0, 1),
            SceneColorCompatibilityBounds.Moon(1, 1, fog.AuthoredMaximum, skyEndpoint, tintMaximum, true, 0, 1));
        // The particle expression also conservatively covers the native decal
        // lighting route. Packed lines/highlights and clamped quad particles/aurora
        // use the separate unit-color allowance in MaximumNits.
        float particles = SceneColorCompatibilityBounds.CubeParticles(ambient, nightVision, 0,
            1, shadowIntensity, fog.AuthoredMaximum, tintMaximum, points, 0, 1);
        float clouds = SceneColorCompatibilityBounds.Clouds(cloudVertexMaximum, skyEndpoint, perception,
            fog.AuthoredMaximum, points.CloudMaximum, nightVision, tintMaximum, 0, 1);
        // The native map has unit alpha and one fullscreen traversal submission.
        // Positive traversal/bucket weights preserve its RGB/alpha ratio, so this
        // normalized bound also covers the later volumetric cloud resolve.
        float cloudMap = mapEnabled ? SceneColorCompatibilityBounds.CloudMap(1, 1,
            mapBrightnessMagnitude, skyEndpoint, fog.AuthoredMaximum, points.CloudMapMaximum, nightVision) : 0;
        var result = new SceneColorCompatibilityPublication(frame, fog.MaximumNits, sky, stars,
            moon, particles, clouds, cloudMap);
        _ = result.MaximumNits;
        return result;
    }

    /// <summary>Rejects late native input changes that exceed the earlier immutable frame envelope.</summary>
    internal void RequireWithin(SceneColorCompatibilityPublication admitted)
    {
        if (FrameIndex != admitted.FrameIndex || Fog > admitted.Fog || Sky > admitted.Sky
            || Stars > admitted.Stars || Moon > admitted.Moon || Particles > admitted.Particles
            || Clouds > admitted.Clouds || CloudMap > admitted.CloudMap)
            throw new InvalidOperationException("Native scene input changed beyond the frozen lighting envelope.");
    }
    #endregion
}
