namespace VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

/// <summary>Names independently authored atmosphere predicate contracts.</summary>
internal static class AtmospherePredicates
{
    private static readonly GpuObservationDomain Declaration = new("atmosphere", [(10, 27u), (12, 3u), (157, 3u), (158, 3u)]);

    #region Internal API
    /// <summary>Preserves the independent AtmosphereCelestialPublicationTests observation.</summary>
    internal static readonly GpuAssertionPredicate AtmosphereCelestialPublicationTests_ThreeBandsReconstructIndependentDirectionsAndFaintBackground = new(Declaration, 10);
    /// <summary>Preserves the independent CelestialGalaxyGenerationTests observation.</summary>
    internal static readonly GpuAssertionPredicate CelestialGalaxyGenerationTests_PackedCubePreservesSourceAndMipEnergy = new(Declaration, 12);
    /// <summary>Preserves the independent SunDiskDrawTests observation.</summary>
    internal static readonly GpuAssertionPredicate SunDiskDrawTests_VisibleDiskStorageLimitIsFiniteAndChromatic = new(Declaration, 157);
    /// <summary>Preserves the independent SunDiskDrawTests observation.</summary>
    internal static readonly GpuAssertionPredicate SunDiskDrawTests_SolarDiskIgnoresCameraTranslation = new(Declaration, 158);
    #endregion
}
