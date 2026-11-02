namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Names independently authored cloud predicate contracts.</summary>
internal static class CloudPredicates
{
    private static readonly GpuObservationDomain Declaration = new("cloud", [(13, 27u), (14, 1u), (15, 1u), (16, 1u), (17, 27u), (18, 3u), (19, 3u), (20, 3u), (21, 3u), (22, 11u), (23, 11u), (24, 1u), (25, 3u), (26, 3u), (27, 27u), (28, 3u), (29, 3u), (30, 3u), (31, 27u), (32, 27u), (33, 3u), (34, 3u), (35, 11u), (36, 11u), (37, 11u), (182, 27u), (183, 27u), (184, 19u), (185, 27u)]);

    #region Internal API
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudStoredVolumeViewTests_RefreshSchedulesCoverOddTilesWithoutStarvation = new(Declaration, 13);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudStoredVolumeViewTests_StoredViewUsesVoxelTransportAndNativeProducts = new(Declaration, 14);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudStoredVolumeViewTests_StoredViewUsesVoxelTransportAndNativeProducts_2 = new(Declaration, 15);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudStoredVolumeViewTests_StoredViewUsesVoxelTransportAndNativeProducts_3 = new(Declaration, 16);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_3 = new(Declaration, 17);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_4 = new(Declaration, 18);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_5 = new(Declaration, 19);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_6 = new(Declaration, 20);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_7 = new(Declaration, 21);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_8 = new(Declaration, 22);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_9 = new(Declaration, 23);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_10 = new(Declaration, 24);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_11 = new(Declaration, 25);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_13 = new(Declaration, 26);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_14 = new(Declaration, 27);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_15 = new(Declaration, 28);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_16 = new(Declaration, 29);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_17 = new(Declaration, 30);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudStoredVolumeViewTests_TemporalMotionCarriesMappedClassificationAndCurrentFrameDistance = new(Declaration, 31);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudStoredVolumeViewTests_TemporalMotionCarriesMappedClassificationAndCurrentFrameDistance_2 = new(Declaration, 32);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudStoredVolumeViewTests_TemporalMotionCarriesMappedClassificationAndCurrentFrameDistance_3 = new(Declaration, 33);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudStoredVolumeViewTests_TemporalMotionCarriesMappedClassificationAndCurrentFrameDistance_4 = new(Declaration, 34);
    /// <summary>Preserves the independent CloudTemporalFreshnessTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudTemporalFreshnessTests_ResampledFreshnessUsesPhysicalWeightsAndExpiresWithoutObservations = new(Declaration, 35);
    /// <summary>Preserves the independent CloudTemporalFreshnessTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudTemporalFreshnessTests_ResampledFreshnessUsesPhysicalWeightsAndExpiresWithoutObservations_2 = new(Declaration, 36);
    /// <summary>Preserves the independent CloudTemporalFreshnessTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudTemporalFreshnessTests_ResampledFreshnessUsesPhysicalWeightsAndExpiresWithoutObservations_3 = new(Declaration, 37);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_18 = new(Declaration, 182);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_19 = new(Declaration, 183);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_20 = new(Declaration, 184);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuAssertionPredicate CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_21 = new(Declaration, 185);
    #endregion
}
