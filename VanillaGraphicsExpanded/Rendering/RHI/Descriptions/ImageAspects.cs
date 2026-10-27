using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Selects image aspects for explicit framebuffer transfers.</summary>
[Flags]
public enum ImageAspects
{
    /// <summary>Selects color components, including permitted destination format conversion.</summary>
    Color = 1,
    /// <summary>Selects depth values with exact source/destination representation compatibility.</summary>
    Depth = 2,
    /// <summary>Selects stencil values with exact source/destination representation compatibility.</summary>
    Stencil = 4
}
