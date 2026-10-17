using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Defines routing admission and pass ownership for concrete framebuffer configurations.</summary>
public abstract partial class GpuFramebuffer
{
    #region Internal API
    /// <summary>Validates immutable routing when a pass declaration is constructed, before submission.</summary>
    internal abstract void ValidatePassRouting(IReadOnlyList<DrawBuffersEnum> routing);
    /// <summary>Establishes the concrete routing policy and returns any standalone restoration owner.</summary>
    internal abstract IDisposable? BeginPassRouting(RenderPassDesc description, RenderPassSequence? sequence);
    /// <summary>Restores dynamic routing or admits the unchanged immutable configuration.</summary>
    internal abstract void RestoreDrawRouting(DrawBuffersEnum[] routing);
    /// <summary>Validates mutable target completeness or relies on a constructor-proven immutable configuration.</summary>
    internal abstract void ValidatePassCompleteness(IReadOnlyList<DrawBuffersEnum> routing);
    #endregion
}
