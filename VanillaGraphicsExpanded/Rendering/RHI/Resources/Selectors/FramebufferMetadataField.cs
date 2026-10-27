namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Identifies logical FramebufferMetadataField selections independently of native API constants.</summary>
public enum FramebufferMetadataField
{
    /// <summary>Selects Width.</summary>
    Width,
    /// <summary>Selects Height.</summary>
    Height,
    /// <summary>Selects Layers.</summary>
    Layers,
    /// <summary>Selects Samples.</summary>
    Samples,
    /// <summary>Selects FixedSampleLocations.</summary>
    FixedSampleLocations,
}
