using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Identifies stages referencing an executable resource.</summary>
[Flags]
public enum ShaderStageMask
{
    /// <summary>No stage references the resource.</summary>
    None = 0,
    /// <summary>The vertex stage references the resource.</summary>
    Vertex = 1,
    /// <summary>The tessellation control stage references the resource.</summary>
    TessellationControl = 2,
    /// <summary>The tessellation evaluation stage references the resource.</summary>
    TessellationEvaluation = 4,
    /// <summary>The geometry stage references the resource.</summary>
    Geometry = 8,
    /// <summary>The fragment stage references the resource.</summary>
    Fragment = 16,
    /// <summary>The compute stage references the resource.</summary>
    Compute = 32
}
