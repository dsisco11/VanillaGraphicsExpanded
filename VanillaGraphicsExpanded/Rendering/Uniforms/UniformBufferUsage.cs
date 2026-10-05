namespace VanillaGraphicsExpanded.Rendering.Uniforms;

/// <summary>Declares publication lifetime independently of how often CPU contents are changed.</summary>
public enum UniformBufferUsage
{
    /// <summary>Never reuse a publication for an independent activation or dispatch.</summary>
    SingleDraw,
    /// <summary>Reuse unchanged publications within the current transient allocation epoch.</summary>
    SingleFrame,
    /// <summary>Retain unchanged publications across frames until replacement or owner retirement.</summary>
    MultiFrame
}
