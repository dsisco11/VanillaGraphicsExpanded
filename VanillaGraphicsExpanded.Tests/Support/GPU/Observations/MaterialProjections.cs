namespace VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

/// <summary>Names independently authored material projection contracts.</summary>
internal static class MaterialProjections
{
    private static readonly GpuObservationDomain Declaration = new("material", [(12, 1u), (13, 1u), (20, 3u), (25, 1u), (26, 3u)], orderedScalars: [26]);

    #region Internal API
    /// <summary>Preserves the independent ColorGradingLutTests observation.</summary>
    internal static readonly GpuImageProjection ColorGradingLutTests_GeneratedGalaxySurvivesPositiveContrastInFinalOutput = new(Declaration, 12);
    /// <summary>Preserves the independent ColorGradingLutTests observation.</summary>
    internal static readonly GpuImageProjection ColorGradingLutTests_GeneratedGalaxySurvivesPositiveContrastInFinalOutput_2 = new(Declaration, 13);
    /// <summary>Preserves the independent LocalExposurePrototypeTests observation.</summary>
    internal static readonly GpuImageProjection LocalExposurePrototypeTests_FilterMatchesPhysicalReferenceAcrossStorageScales = new(Declaration, 20);
    /// <summary>Preserves the independent PbrDisplayResolveTests observation.</summary>
    internal static readonly GpuImageProjection PbrDisplayResolveTests_QuantizedRampIsBalancedNeutralAndDeterministic = new(Declaration, 25);
    /// <summary>Preserves the independent PbrLumOnFullPipelineIntegrationTests observation.</summary>
    internal static readonly GpuImageProjection PbrLumOnFullPipelineIntegrationTests_ComputeAverageLuminance = new(Declaration, 26);
    #endregion
}
