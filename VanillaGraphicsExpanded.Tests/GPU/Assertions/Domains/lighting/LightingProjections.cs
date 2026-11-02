namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Names independently authored lighting projection contracts.</summary>
internal static class LightingProjections
{
    private static readonly GpuObservationDomain Declaration = new("lighting", [(24, 1u)]);

    #region Internal API
    /// <summary>Preserves the independent OwnedPostprocessShaderTests observation.</summary>
    internal static readonly GpuImageProjection OwnedPostprocessShaderTests_BloomGaussianFootprintHasNoSparseTapTroughs = new(Declaration, 24);
    #endregion
}
