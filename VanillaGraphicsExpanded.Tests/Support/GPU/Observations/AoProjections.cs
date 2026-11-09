
using VanillaGraphicsExpanded.Tests.Component.AmbientOcclusion.Fixtures;

namespace VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

/// <summary>Names independently authored ao projection contracts.</summary>
internal static class AoProjections
{
    private static readonly GpuObservationDomain Declaration = new("ao", [(1, 1u), (2, 1u), (14, 3u), (15, 3u)]);

    #region Internal API
    /// <summary>Preserves the independent AmbientOcclusionLightingContributionTests observation.</summary>
    internal static readonly GpuImageProjection AmbientOcclusionLightingContributionTests_GeneratedVisibilityChangesOnlyAvailableIndirectLighting_2 = new(Declaration, 1);
    /// <summary>Preserves the independent AoMinimumChildProductionTests observation.</summary>
    internal static readonly GpuImageProjection AoMinimumChildProductionTests_ValidateMasks = new(Declaration, 2);
    /// <summary>Preserves the independent AmbientOcclusionLightingFixture observation.</summary>
    internal static readonly GpuImageProjection AmbientOcclusionLightingFixture_AmbientOcclusionLightingFixture = new(Declaration, 14);
    /// <summary>Preserves the independent AmbientOcclusionLightingFixture observation.</summary>
    internal static readonly GpuImageProjection AmbientOcclusionLightingFixture_Capture = new(Declaration, 15);
    #endregion
}
