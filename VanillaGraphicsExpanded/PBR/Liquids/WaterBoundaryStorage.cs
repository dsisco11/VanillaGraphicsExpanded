using System;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Admits camera extents that keep signed water boundary accumulation within FP32 range.</summary>
internal static class WaterBoundaryStorage
{
    #region Public API
    /// <summary>Certifies the perspective ray extent before submitting any boundary geometry.</summary>
    internal static bool CameraAdmitted(ReadOnlySpan<float> projection, float near, float far)
    {
        if (projection.Length != 16 || !float.IsFinite(near) || !float.IsFinite(far)
            || near <= .008 || far <= near) return false;
        foreach (float value in projection)
            if (!float.IsFinite(value)) return false;
        // The world pass supports ordinary and off-axis perspective cameras. Its
        // homogeneous-w bias is at most .008; account for the enlarged raster cone
        // using a conservative lower eye depth, rather than a guessed FOV ceiling.
        if (projection[0] == 0 || projection[5] == 0 || projection[11] != -1 || projection[15] != 0
            || projection[1] != 0 || projection[2] != 0 || projection[3] != 0
            || projection[4] != 0 || projection[6] != 0 || projection[7] != 0
            || projection[12] != 0 || projection[13] != 0) return false;
        if (projection[10] >= 0 || projection[14] >= 0) return false;
        double depthRange = (double)far - near;
        double expectedDepth = -((double)far + near) / depthRange;
        double expectedTranslation = -2.0 * near * far / depthRange;
        const double projectionTolerance = 16.0 / (1 << 23);
        if (Math.Abs(projection[10] - expectedDepth) > projectionTolerance * Math.Max(1, Math.Abs(expectedDepth))
            || Math.Abs(projection[14] - expectedTranslation) > projectionTolerance * Math.Max(1, Math.Abs(expectedTranslation)))
            return false;
        double minimumDepth = Math.Min(near, -(double)projection[14] / (1 - projection[10])) - .008;
        if (minimumDepth <= 0) return false;
        double horizontal = (1 + Math.Abs((double)projection[8])) / Math.Abs((double)projection[0]);
        double vertical = (1 + Math.Abs((double)projection[9])) / Math.Abs((double)projection[5]);
        double bias = 1 + .008 / minimumDepth;
        // Depth reconstruction has a .0001 denominator floor. Bound that actual
        // arithmetic instead of assuming near/far subtraction reproduces far exactly.
        double depthMaximum = Math.Max(far, 20000.0 * near * far);
        double extent = depthMaximum * Math.Sqrt(1 + horizontal * horizontal + vertical * vertical) * bias;
        // Effective scattering is at most 10,000 m^-1. Frozen P bounds the source
        // by 64,000; this extent gives at most 6.4e16 per fragment. Even a factor
        // two for additive rounding growth across 2^64 submissions stays below
        // 2.362e36, more than 140 times below FP32 max.
        // Signed optical depth and winding have still more storage headroom.
        return double.IsFinite(extent) && extent * (1 + 512.0 / (1 << 23)) <= 1e8;
    }
    #endregion
}
