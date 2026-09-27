using System;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Estimates regional snow reflectance from seasonal mean temperature and thermal lag, without terrain observations.</summary>
internal static class AtmosphereSeasonModel
{
    internal const float BareGroundAlbedo = .1f;

    #region Seasonal approximation
    /// <summary>Uses cooling/warming asymmetry to approximate accumulation delay and persistent spring snow.</summary>
    internal static (float SnowCoverage, float GroundAlbedo) Estimate(float meanTemperature, float pastMean, float futureMean)
    {
        if (!float.IsFinite(meanTemperature) || !float.IsFinite(pastMean) || !float.IsFinite(futureMean))
            return (0, BareGroundAlbedo);
        // A bounded thermal lag makes spring snow persist at temperatures where autumn snow
        // has not accumulated yet. Reconstructing it from dates avoids frame-rate history,
        // login transients and incorrect retained state after teleporting or changing time.
        float trend = Math.Clamp((futureMean - pastMean) * .5f, -8f, 8f);
        float cold = Math.Clamp((2f - (meanTemperature - trend)) / 10f, 0f, 1f);
        float coverage = cold * cold * (3f - 2f * cold);
        // Warm snow is treated as wetter/darker than cold snow. These are broadband
        // regional assumptions, not a claim to know actual snow depth or material albedo.
        float snowAlbedo = .6f + .2f * Math.Clamp(-meanTemperature / 10f, 0f, 1f);
        return (coverage, BareGroundAlbedo + (snowAlbedo - BareGroundAlbedo) * coverage);
    }

    /// <summary>Shares a stable two-percent reflectance bucket between both transport backends and their caches.</summary>
    internal static int AlbedoBucket(float value) => (int)MathF.Round(
        Math.Clamp(float.IsFinite(value) ? value : BareGroundAlbedo, 0f, 1f) * 50f);
    #endregion
}
