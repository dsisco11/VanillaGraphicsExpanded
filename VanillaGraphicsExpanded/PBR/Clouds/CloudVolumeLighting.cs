using System;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Certifies the volume source envelope before the weather map is produced.</summary>
internal static class CloudVolumeLighting
{
    #region Public API
    /// <summary>Bounds HG(g=.6), one isotropic octave, environment and aerial/local-medium transport.</summary>
    internal static float Maximum(AtmosphereLighting lighting, float localFogMaximum, CloudVolumeSettings settings)
    {
        settings.Validate();
        _ = CloudSimpleLighting.Maximum(lighting, localFogMaximum);
        // HG reaches (1+g)/(4*pi*(1-g)^2). All transmission factors are at most one;
        // front-to-back view weights sum to at most one independently of map extinction.
        double phase = 1.6 / (4 * Math.PI * .16) + settings.OctaveContribution / (4 * Math.PI);
        double solar = Math.Max(lighting.Solar.X, Math.Max(lighting.Solar.Y, lighting.Solar.Z));
        double lunar = Math.Max(lighting.Lunar.X, Math.Max(lighting.Lunar.Y, lighting.Lunar.Z));
        double environment = Math.Max(lighting.Environment.X, Math.Max(lighting.Environment.Y, lighting.Environment.Z));
        // Primary integration has at most 384 samples. Reserve conservative arithmetic headroom
        // for transmission products and accumulation as well as the source evaluation.
        return LightingStorage.UpperBound(((solar + lunar) * phase + environment
            + lighting.ReconstructionMaximumNits + localFogMaximum) * (1 + 16384.0 / (1 << 23)));
    }
    #endregion
}
