namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Names independently authored observation projection contracts.</summary>
internal static class ObservationProjections
{
    private static readonly GpuObservationDomain Declaration = new("observation", [(3, 1u), (4, 1u), (5, 1u), (17, 3u), (18, 3u), (19, 1u)], orderedScalars: [17, 18]);

    #region Internal API
    /// <summary>Preserves the independent GpuImageAssertionsComparisons observation.</summary>
    internal static readonly GpuImageProjection GpuImageAssertionsComparisons_Statistics = new(Declaration, 3);
    /// <summary>Preserves the independent GpuImageAssertionsPredicates observation.</summary>
    internal static readonly GpuImageProjection GpuImageAssertionsPredicates_CompareRgba8 = new(Declaration, 4);
    /// <summary>Preserves the independent GpuImageAssertionsSelection observation.</summary>
    internal static readonly GpuImageProjection GpuImageAssertionsSelection_Brightest = new(Declaration, 5);
    /// <summary>Preserves the independent GpuImageAssertionTests observation.</summary>
    internal static readonly GpuImageProjection GpuImageAssertionTests_OrderedFloatReductionRetainsOriginalArithmetic = new(Declaration, 17);
    /// <summary>Preserves the independent GpuImageAssertionTests observation.</summary>
    internal static readonly GpuImageProjection GpuImageAssertionTests_OrderedFloatReductionRetainsOriginalArithmetic_2 = new(Declaration, 18);
    /// <summary>Preserves the independent GpuImageAssertionTests observation.</summary>
    internal static readonly GpuImageProjection GpuImageAssertionTests_HistoryProjectionsRetainCompleteIndependentImages = new(Declaration, 19);
    #endregion
}
