namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Declares independent correlated photometric sample checks.</summary>
internal static class PhotometricPredicates
{
    private static readonly GpuObservationDomain Declaration = new("photometric", [(0, 1u), (1, 1u)]);

    #region Internal API
    /// <summary>Retains the dark-end slope, chroma, monotonic and calibrated luminance assertions.</summary>
    internal static readonly GpuAssertionPredicate BlockFalloff = new(Declaration, 0);
    /// <summary>Requires strictly increasing red values across the original first-row samples.</summary>
    internal static readonly GpuAssertionPredicate IncreasingRed = new(Declaration, 1);
    #endregion
}
