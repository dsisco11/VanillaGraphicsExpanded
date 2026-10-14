using System;
using System.Collections.Immutable;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Combines linear lunar transport and separately budgeted airglow with the solar generation.</summary>
internal static class AtmosphereNightLighting
{
    // Ground-level, clear zenith references. The lunar transport was evaluated at
    // the same 100,000-lux reference, so extinction is applied exactly once.
    internal const float FullMoonZenithLux = .1f;

    #region Public API
    /// <summary>Evaluates a normalized empirical lunar phase curve; phase zero supplies no direct lunar light.</summary>
    internal static float PhaseFraction(float phase)
    {
        if (!float.IsFinite(phase)) throw new ArgumentOutOfRangeException(nameof(phase));
        phase = (phase % 8 + 8) % 8;
        float angle = MathF.Abs(phase - 4) * 45;
        // The magnitude fit uses degrees and includes the full-moon opposition peak.
        float fit = MathF.Pow(10, -.4f * (.026f * angle + 4e-9f * angle * angle * angle * angle));
        float newMoon = MathF.Pow(10, -.4f * (.026f * 180 + 4e-9f * 180 * 180 * 180 * 180));
        // Remove the fit's residual at new moon; earthshine is not a separate modeled emitter.
        return Math.Clamp((fit - newMoon) / (1 - newMoon), 0, 1);
    }

    /// <summary>Publishes both scattering directions while selecting only one direct celestial emitter.</summary>
    internal static AtmosphereLighting Combine(AtmosphereLighting solar, AtmosphereLighting moon,
        AtmosphereCelestialInputs sources, float clouds)
    {
        if (solar.Width != moon.Width || solar.Height != moon.Height || solar.Altitude != moon.Altitude
            || solar.Sky.Length != moon.Sky.Length || solar.AerialRadiance.Length != moon.AerialRadiance.Length)
            throw new ArgumentException("Celestial transport must share one admitted medium and lookup grid.");
        float ratio = FullMoonZenithLux / 100000f * PhaseFraction(sources.Phase);
        Vector3 moonDirection = ratio == 0 ? sources.Moon : moon.Sun;
        var completedSources = new AtmosphereCelestialInputs(solar.Sun, moonDirection, sources.Phase);
        // This background is a bounded uniform-hemisphere approximation, not a
        // volumetric airglow model. Cloud cover suppresses it rather than generating skyglow.
        float background = Celestial.CelestialSource.AirglowLux / MathF.PI * (1 - .9f * Math.Clamp(clouds, 0, 1));
        float[] sky = Add(solar.Sky, moon.Sky, ratio);
        float[] aerial = Add(solar.AerialRadiance, moon.AerialRadiance, ratio);
        Vector3 environment = solar.Environment + moon.Environment * ratio;
        for (int y = 0; y < solar.Height; y++)
        {
            float elevation = AtmosphereSkyMapping.Elevation((float)y / (solar.Height - 1), solar.HorizonElevation);
            if (elevation < 0) continue;
            environment += new Vector3(background) * (solar.Width
                * AtmosphereSkyMapping.EnvironmentWeight(y, solar.Width, solar.Height, solar.HorizonElevation));
            for (int x = 0; x < solar.Width; x++)
                for (int channel = 0; channel < 3; channel++) sky[(y * solar.Width + x) * 4 + channel] += background;
        }
        // An isotropic background contributes the equilibrium radiance lost along
        // each finite path; extinction remains the shared dimensionless field.
        for (int index = 0; index < aerial.Length; index++)
            if ((index & 3) != 3) aerial[index] += background * solar.AerialAttenuation[index];
        return solar with
        {
            CloudCoverage = Math.Clamp(clouds, 0, 1),
            BackgroundRadiance = background,
            HasNightSources = true, Moon = moonDirection, MoonAngularRadius = completedSources.MoonAngularRadius,
            Lunar = AtmosphereModel.SolarIrradiance(moonDirection, solar.Altitude,
                1 + 7 * (MathF.Round(Math.Clamp(clouds, 0, 1) * 20) / 20), completedSources.MoonAngularRadius) * ratio,
            Environment = environment, Horizon = solar.Horizon + moon.Horizon * ratio + new Vector3(background),
            Sky = ImmutableArray.Create(sky), AerialRadiance = ImmutableArray.Create(aerial),
            SkyMoonMie = ImmutableArray.Create(Scale(moon.SkyMie, ratio)),
            AerialMoonMie = ImmutableArray.Create(Scale(moon.AerialMie, ratio))
        };
    }
    #endregion

    #region Private
    /// <summary>Adds light-bearing channels while retaining the producer's alpha metadata.</summary>
    private static float[] Add(ImmutableArray<float> sun, ImmutableArray<float> moon, float ratio)
    {
        float[] result = sun.AsSpan().ToArray();
        for (int index = 0; index < result.Length; index++)
            if ((index & 3) != 3) result[index] += moon[index] * ratio;
        return result;
    }

    /// <summary>Scales an independently reconstructed lunar scattering band without modifying alpha.</summary>
    private static float[] Scale(ImmutableArray<float> source, float ratio)
    {
        float[] result = source.AsSpan().ToArray();
        for (int index = 0; index < result.Length; index++)
            if ((index & 3) != 3) result[index] *= ratio;
        return result;
    }
    #endregion
}
