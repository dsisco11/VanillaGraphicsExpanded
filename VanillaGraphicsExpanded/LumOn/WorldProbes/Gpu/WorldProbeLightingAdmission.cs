using System;
using System.Numerics;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.PBR;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Admits the unscaled metadata and CPU lighting shared by both world-probe publication paths.</summary>
internal static class WorldProbeLightingAdmission
{
    #region Public API
    /// <summary>Rejects unsupported scalar storage and invalid normalization before any atlas is touched.</summary>
    internal static bool Metadata(in LumOnWorldProbeTraceResult result)
    {
        float squared = result.ShortRangeAoDirWorld.LengthSquared();
        return Unit(result.Confidence) && Unit(result.ShortRangeAoConfidence) && Unit(result.SkyIntensity)
            && float.IsFinite(result.MeanLogHitDistance) && result.MeanLogHitDistance >= 0 && result.MeanLogHitDistance <= 65504
            && float.IsFinite(result.ShortRangeAoDirWorld.X) && float.IsFinite(result.ShortRangeAoDirWorld.Y)
            && float.IsFinite(result.ShortRangeAoDirWorld.Z) && squared > 0 && float.IsFinite(squared);
    }

    /// <summary>Checks the directional projection domain without clamping source power into valid history.</summary>
    internal static bool CpuRadiance(Vector3 rgb) => LightingCalibration.FiniteNonnegative(rgb)
        && rgb.X <= LightingStorage.DirectionalMaximum && rgb.Y <= LightingStorage.DirectionalMaximum
        && rgb.Z <= LightingStorage.DirectionalMaximum;

    /// <summary>Preserves signed hit/sky distance while checking its binary16 destination.</summary>
    internal static bool Distance(float value) => float.IsFinite(value) && Math.Abs(value) <= 65504;
    #endregion

    #region Private
    /// <summary>Requires a finite dimensionless publication rather than relying on texture narrowing.</summary>
    private static bool Unit(float value) => float.IsFinite(value) && value >= 0 && value <= 1;
    #endregion
}
