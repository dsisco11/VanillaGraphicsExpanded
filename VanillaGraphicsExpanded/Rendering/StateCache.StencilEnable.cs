using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns cached stencil enable transitions.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Establishes StencilTest, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetStencilTestEnabled(bool enabled)
    {
        ValidateCompleteMutation();
        if (stencilKnown.HasFlag(StencilStateKnowledge.TestEnabled) && stencil.TestEnabled == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        stencilKnown &= ~StencilStateKnowledge.TestEnabled;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) GL.Enable(EnableCap.StencilTest); else GL.Disable(EnableCap.StencilTest);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        stencil.TestEnabled = enabled;
        stencilKnown |= StencilStateKnowledge.TestEnabled;
    }
    #endregion
}
