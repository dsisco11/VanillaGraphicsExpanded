using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Declares non-pipeline state effects before entering an engine interruption.</summary>
[Flags]
public enum BoundaryOperations
{
    /// <summary>Pipeline selection and resource slots alone require no additional pass-state coverage.</summary>
    None = 0,
    /// <summary>Permits pass target selection, viewport setup and typed attachment loads/clears.</summary>
    RenderPasses = 1
}
