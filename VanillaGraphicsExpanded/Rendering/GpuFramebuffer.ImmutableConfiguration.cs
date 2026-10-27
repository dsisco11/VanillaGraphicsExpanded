using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Exposes immutable configuration certificates to the existing cache without coupling resources to pass execution.</summary>
public abstract partial class GpuFramebuffer
{
    #region Internal API
    /// <summary>Returns declared routing only for immutable configurations; mutable objects use executed cache knowledge.</summary>
    internal virtual int[] CaptureDeclaredRouting()=>throw new InvalidOperationException("This framebuffer has no immutable route.");
    /// <summary>Validates a mutation against an immutable declaration without issuing or suppressing native commands itself.</summary>
    internal virtual void ValidateDeclaredRouting(ReadOnlySpan<int> route)=>throw new InvalidOperationException("This framebuffer has no immutable configuration certificate.");
    #endregion
}
