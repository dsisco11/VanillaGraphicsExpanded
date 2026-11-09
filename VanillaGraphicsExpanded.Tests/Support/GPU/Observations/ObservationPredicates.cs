namespace VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

/// <summary>Names independently authored observation predicate contracts.</summary>
internal static class ObservationPredicates
{
    private static readonly GpuObservationDomain Declaration = new("observation", [(187,3u), (8, 1u), (9, 1u), (47, 3u), (48, 1u), (49, 27u), (50, 3u), (51, 1u), (52, 3u)]);

    #region Internal API
    /// <summary>Verifies stored byte codes at integer-nominated X coordinates, independently of sampling decode.</summary>
    internal static readonly GpuAssertionPredicate StoredRedByte = new(Declaration,187);
    /// <summary>Preserves the independent GpuImageAssertionsSelection observation.</summary>
    internal static readonly GpuAssertionPredicate GpuImageAssertionsSelection_Extremum = new(Declaration, 8);
    /// <summary>Preserves the independent GpuImageAssertionsSelection observation.</summary>
    internal static readonly GpuAssertionPredicate GpuImageAssertionsSelection_Brightest_2 = new(Declaration, 9);
    /// <summary>Preserves the independent GpuImageAssertionTests observation.</summary>
    internal static readonly GpuAssertionPredicate GpuImageAssertionTests_SparsePredicatesRetainEveryNominatedPoint = new(Declaration, 47);
    /// <summary>Preserves the independent GpuImageAssertionTests observation.</summary>
    internal static readonly GpuAssertionPredicate GpuImageAssertionTests_SparsePredicatesRetainEveryNominatedPoint_2 = new(Declaration, 48);
    /// <summary>Preserves the independent GpuImageAssertionTests observation.</summary>
    internal static readonly GpuAssertionPredicate GpuImageAssertionTests_AuthoredPredicatesRetainCorrespondingImagesAndWitnesses = new(Declaration, 49);
    /// <summary>Preserves the independent GpuImageAssertionTests observation.</summary>
    internal static readonly GpuAssertionPredicate GpuImageAssertionTests_AuthoredPredicatesRetainCorrespondingImagesAndWitnesses_2 = new(Declaration, 50);
    /// <summary>Preserves the independent GpuImageAssertionTests observation.</summary>
    internal static readonly GpuAssertionPredicate GpuImageAssertionTests_AuthoredPredicatesRetainCorrespondingImagesAndWitnesses_3 = new(Declaration, 51);
    /// <summary>Preserves the independent GpuImageAssertionTests observation.</summary>
    internal static readonly GpuAssertionPredicate GpuImageAssertionTests_AuthoredPredicatesRetainCorrespondingImagesAndWitnesses_4 = new(Declaration, 52);
    #endregion
}
