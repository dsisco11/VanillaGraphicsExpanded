namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Names independently authored surface projection contracts.</summary>
internal static class SurfaceProjections
{
    private static readonly GpuObservationDomain Declaration = new("surface", [(21, 3u), (22, 3u), (27, 1u)], orderedScalars: [22]);

    #region Internal API
    /// <summary>Preserves the independent LumOnUpsampleFunctionalTests observation.</summary>
    internal static readonly GpuImageProjection LumOnUpsampleFunctionalTests_BrightnessVariance = new(Declaration, 21);
    /// <summary>Preserves the independent LumOnUpsampleFunctionalTests observation.</summary>
    internal static readonly GpuImageProjection LumOnUpsampleFunctionalTests_BrightnessVariance_2 = new(Declaration, 22);
    /// <summary>Preserves the independent SurfaceCacheLightingTermsTests observation.</summary>
    internal static readonly GpuImageProjection SurfaceCacheLightingTermsTests_CappedHistoryPreservesConstantIrradiance = new(Declaration, 27);
    #endregion
}
