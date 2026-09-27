using System;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Bounded spatial and integration budgets for one atmospheric quality level.</summary>
internal readonly record struct AtmosphereScatteringBudget(int Width, int Height,
    int DirectionSamples, int RaySamples, int LightSamples)
{
    #region Quality policy
    /// <summary>Scales spatial resolution and integration counts linearly from the baseline.</summary>
    internal static AtmosphereScatteringBudget FromQuality(int quality)
    {
        quality = Math.Clamp(quality, 0, 3);
        int sampleMultiplier = quality + 1;
        return new(AtmosphereMultipleScattering.DefaultWidth * sampleMultiplier,
            AtmosphereMultipleScattering.DefaultHeight * sampleMultiplier,
            AtmosphereMultipleScattering.DefaultDirectionSamples * sampleMultiplier,
            AtmosphereMultipleScattering.DefaultRaySamples * sampleMultiplier,
            AtmosphereMultipleScattering.DefaultLightSamples * sampleMultiplier);
    }
    #endregion
}
