namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Names independently authored atmosphere projection contracts.</summary>
internal static class AtmosphereProjections
{
    private static readonly GpuObservationDomain Declaration = new("atmosphere", [(6, 3u), (23, 1u)]);

    #region Internal API
    /// <summary>Preserves the independent CelestialGalaxyGenerationTests observation.</summary>
    internal static readonly GpuImageProjection CelestialGalaxyGenerationTests_IntegrateFace = new(Declaration, 6);
    /// <summary>Preserves the independent MoonDiskDrawTests observation.</summary>
    internal static readonly GpuImageProjection MoonDiskDrawTests_Draw = new(Declaration, 23);
    #endregion
}
