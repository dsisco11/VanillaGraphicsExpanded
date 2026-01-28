namespace VanillaGraphicsExpanded.LumOn.WorldProbes;

/// <summary>
/// Phase 0 constants for the upcoming world-probe octahedral atlas payload (SH replacement).
/// </summary>
internal static class LumOnWorldProbeAtlasSpec
{
    /// <summary>
    /// Octahedral tile size (S×S) per probe for the world-probe atlas.
    /// Screen probes remain 8×8; this is intentionally separate.
    /// </summary>
    public const int OctahedralSize = 16;

    public const int DirectionCount = OctahedralSize * OctahedralSize;

    /// <summary>
    /// Default number of octahedral texels traced/uploaded per probe update (direction slicing).
    /// </summary>
    public const int DefaultTexelsPerUpdate = 32;
}

