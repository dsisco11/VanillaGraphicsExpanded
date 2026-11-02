namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Names independently authored water predicate contracts.</summary>
internal static class WaterPredicates
{
    private static readonly GpuObservationDomain Declaration = new("water", [(54, 3u), (55, 3u), (176, 1u), (177, 3u), (178, 27u)]);

    #region Internal API
    /// <summary>Preserves the independent LiquidSpecularTriangulationTests observation.</summary>
    internal static readonly GpuAssertionPredicate LiquidSpecularTriangulationTests_CompareFolds = new(Declaration, 54);
    /// <summary>Preserves the independent LiquidSpecularTriangulationTests observation.</summary>
    internal static readonly GpuAssertionPredicate LiquidSpecularTriangulationTests_CompareFolds_2 = new(Declaration, 55);
    /// <summary>Preserves the independent WaterMediumTests observation.</summary>
    internal static readonly GpuAssertionPredicate WaterMediumTests_HomogeneousTransportMatchesAnalyticReference = new(Declaration, 176);
    /// <summary>Preserves the independent WaterMediumTests observation.</summary>
    internal static readonly GpuAssertionPredicate WaterMediumTests_HomogeneousTransportMatchesAnalyticReference_2 = new(Declaration, 177);
    /// <summary>Preserves the independent WaterRefractionReductionTests observation.</summary>
    internal static readonly GpuAssertionPredicate WaterRefractionReductionTests_AssertAssociatedReceivers = new(Declaration, 178);
    #endregion
}
