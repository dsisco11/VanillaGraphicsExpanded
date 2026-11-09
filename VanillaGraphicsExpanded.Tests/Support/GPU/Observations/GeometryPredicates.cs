namespace VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

/// <summary>Names independently authored geometry predicate contracts.</summary>
internal static class GeometryPredicates
{
    private static readonly GpuObservationDomain Declaration = new("geometry", [(133, 11u), (134, 3u), (173, 1u), (174, 3u), (175, 3u)]);

    #region Internal API
    /// <summary>Preserves the independent NearFieldResourceReuseTests observation.</summary>
    internal static readonly GpuAssertionPredicate NearFieldResourceReuseTests_TraceRebindsInputsAfterBranchAndVariantChanges = new(Declaration, 133);
    /// <summary>Preserves the independent NearFieldResourceReuseTests observation.</summary>
    internal static readonly GpuAssertionPredicate NearFieldResourceReuseTests_TraceRebindsInputsAfterBranchAndVariantChanges_2 = new(Declaration, 134);
    /// <summary>Preserves the independent TerrainDisplacementRasterTests observation.</summary>
    internal static readonly GpuAssertionPredicate TerrainDisplacementRasterTests_AdaptiveStagesPublishBoundedPositionAndNormal = new(Declaration, 173);
    /// <summary>Preserves the independent TerrainReliefNumericalTests observation.</summary>
    internal static readonly GpuAssertionPredicate TerrainReliefNumericalTests_ReliefUsesAuthoredMetric = new(Declaration, 174);
    /// <summary>Preserves the independent TerrainTessellationEngineDrawTests observation.</summary>
    internal static readonly GpuAssertionPredicate TerrainTessellationEngineDrawTests_PatchedEngineGroupedDrawMatchesOrdinaryIndexedDraw = new(Declaration, 175);
    #endregion
}
