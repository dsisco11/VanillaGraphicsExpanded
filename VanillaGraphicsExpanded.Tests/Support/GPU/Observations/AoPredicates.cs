namespace VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

/// <summary>Names independently authored ao predicate contracts.</summary>
internal static class AoPredicates
{
    private static readonly GpuObservationDomain Declaration = new("ao", [(1, 3u), (2, 3u), (3, 1u), (4, 27u), (5, 1u), (6, 1u), (7, 11u)]);

    #region Internal API
    /// <summary>Preserves the independent AmbientOcclusionHorizonBehaviorTests observation.</summary>
    internal static readonly GpuAssertionPredicate AmbientOcclusionHorizonBehaviorTests_ProjectionAndDistancePreserveReceiverEvidence = new(Declaration, 1);
    /// <summary>Preserves the independent AmbientOcclusionHorizonBehaviorTests observation.</summary>
    internal static readonly GpuAssertionPredicate AmbientOcclusionHorizonBehaviorTests_ProjectionAndDistancePreserveReceiverEvidence_2 = new(Declaration, 2);
    /// <summary>Preserves the independent AmbientOcclusionHorizonBehaviorTests observation.</summary>
    internal static readonly GpuAssertionPredicate AmbientOcclusionHorizonBehaviorTests_ScreenEdgesUseOnlyVisibleGeometry = new(Declaration, 3);
    /// <summary>Preserves the independent AmbientOcclusionLightingContributionTests observation.</summary>
    internal static readonly GpuAssertionPredicate AmbientOcclusionLightingContributionTests_GeneratedVisibilityChangesOnlyAvailableIndirectLighting = new(Declaration, 4);
    /// <summary>Preserves the independent AmbientOcclusionPassTests observation.</summary>
    internal static readonly GpuAssertionPredicate AmbientOcclusionPassTests_ContactTransmissionAndMotionRemainCurrentFrame = new(Declaration, 5);
    /// <summary>Preserves the independent AmbientOcclusionPassTests observation.</summary>
    internal static readonly GpuAssertionPredicate AmbientOcclusionPassTests_PerpendicularCornerAndCutoutGapsRespectGeometry = new(Declaration, 6);
    /// <summary>Preserves the independent AoMinimumChildProductionTests observation.</summary>
    internal static readonly GpuAssertionPredicate AoMinimumChildProductionTests_ValidateMasks_2 = new(Declaration, 7);
    #endregion
}
