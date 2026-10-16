using System;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Bounds the owned two-sided diffuse shell response independently of weather geometry or later tile commits.</summary>
internal static class CloudSimpleLighting
{
    #region Public API
    /// <summary>Includes both celestial lux/pi maxima, E/pi environment, aerial reconstruction and convex local fog.</summary>
    internal static float Maximum(AtmosphereLighting lighting, float localFogMaximum)
    {
        if (!LightingCalibration.FiniteNonnegative(lighting.Solar) || !LightingCalibration.FiniteNonnegative(lighting.Lunar)
            || !LightingCalibration.FiniteNonnegative(lighting.Environment)
            || !float.IsFinite(lighting.ReconstructionMaximumNits) || lighting.ReconstructionMaximumNits < 0
            || !float.IsFinite(localFogMaximum) || localFogMaximum < 0
            || lighting.CalibrationGeneration == 0 || lighting.LookupStorageScale != LightingStorage.PersistentScale
            || !float.IsFinite(lighting.Altitude) || !float.IsFinite(lighting.HorizonElevation)
            || !ValidDirection(lighting.Sun) || !ValidDirection(lighting.Moon))
            throw new ArgumentException("Simple clouds require one completed physical atmosphere.");
        double source = (Peak(lighting.Solar) + Peak(lighting.Lunar)) / Math.PI + Peak(lighting.Environment);
        return LightingStorage.UpperBound((source + lighting.ReconstructionMaximumNits + localFogMaximum)
            * (1 + 256.0 / (1 << 23)));
    }
    #endregion

    #region Private
    /// <summary>Validates a finite nondegenerate celestial direction before normalization.</summary>
    private static bool ValidDirection(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && float.IsFinite(value.LengthSquared()) && value.LengthSquared() > .0001f;
    /// <summary>Extracts a component bound using double arithmetic for subsequent sums.</summary>
    private static double Peak(Vector3 value) => Math.Max(value.X, Math.Max(value.Y, value.Z));
    #endregion
}
