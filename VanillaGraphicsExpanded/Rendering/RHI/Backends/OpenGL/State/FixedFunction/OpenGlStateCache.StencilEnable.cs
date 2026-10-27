using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns cached stencil enable transitions.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Establishes StencilTest, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetStencilTestEnabled(bool enabled)
    {
        ValidateBoundaryMutation(stencil: StencilStateKnowledge.TestEnabled, completeGraphics: true);
        if (stencilKnown.HasFlag(StencilStateKnowledge.TestEnabled) && stencil.TestEnabled == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        stencilKnown &= ~StencilStateKnowledge.TestEnabled;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) OpenGLStateEffects.Enable(EnableCap.StencilTest); else OpenGLStateEffects.Disable(EnableCap.StencilTest);
#if DEBUG
        FixedFunctionCalls++;
#endif
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        stencil.TestEnabled = enabled;
        stencilKnown |= StencilStateKnowledge.TestEnabled;
    }
    #endregion
}
