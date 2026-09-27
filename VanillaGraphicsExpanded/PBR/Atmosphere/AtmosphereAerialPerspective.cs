using System;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Owns the bounded logarithmic distance grid for finite atmospheric paths.</summary>
internal static class AtmosphereAerialPerspective
{
    internal const int Depth = 24;
    internal const float DistanceScale = .001f;
    internal const float MaximumDistance = 2500f;
    private static readonly float[] ranges = CreateRanges();

    #region Distance coordinates
    /// <summary>Places exact identity and boundary samples at either end, concentrating resolution near the observer.</summary>
    internal static float Distance(int slice, float boundary) => MathF.Min(boundary, ranges[slice]);

    /// <summary>Precomputes the invariant logarithmic grid once, avoiding exponentials in each CPU ray interval.</summary>
    private static float[] CreateRanges()
    {
        var result = new float[Depth];
        for (int slice = 0; slice < Depth; slice++)
            result[slice] = DistanceScale * (MathF.Exp(MathF.Log(1f + MaximumDistance / DistanceScale) * slice / (Depth - 1)) - 1f);
        return result;
    }
    #endregion
}
