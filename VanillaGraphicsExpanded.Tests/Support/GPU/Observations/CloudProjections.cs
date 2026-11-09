
using VanillaGraphicsExpanded.Tests.Support.Clouds;

namespace VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

/// <summary>Names independently authored cloud projection contracts.</summary>
internal static class CloudProjections
{
    private static readonly GpuObservationDomain Declaration = new("cloud", [(7, 3u), (8, 1u), (9, 1u), (10, 1u), (11, 1u), (16, 1u)]);

    #region Internal API
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuImageProjection CloudStoredVolumeViewTests_RefreshSchedulesCoverOddTilesWithoutStarvation_2 = new(Declaration, 7);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuImageProjection CloudStoredVolumeViewTests_RefreshSchedulesCoverOddTilesWithoutStarvation_3 = new(Declaration, 8);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuImageProjection CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight = new(Declaration, 9);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuImageProjection CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_2 = new(Declaration, 10);
    /// <summary>Preserves the independent CloudStoredVolumeViewTests observation.</summary>
    internal static readonly GpuImageProjection CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_12 = new(Declaration, 11);
    /// <summary>Preserves the independent CloudStoredVolumeViewScene observation.</summary>
    internal static readonly GpuImageProjection CloudStoredVolumeViewScene_SeedHistory = new(Declaration, 16);
    #endregion
}
