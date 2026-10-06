using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns cached output enable transitions.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Establishes FramebufferSrgb, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetFramebufferSrgbEnabled(bool enabled)
    {
        ValidateCompleteMutation();
        if (outputKnown.HasFlag(OutputStateKnowledge.FramebufferSrgb) && output.FramebufferSrgb == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        outputKnown &= ~OutputStateKnowledge.FramebufferSrgb;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) GL.Enable(EnableCap.FramebufferSrgb); else GL.Disable(EnableCap.FramebufferSrgb);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        output.FramebufferSrgb = enabled;
        outputKnown |= OutputStateKnowledge.FramebufferSrgb;
    }

    /// <summary>Establishes Dither, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetDitherEnabled(bool enabled)
    {
        ValidateCompleteMutation();
        if (outputKnown.HasFlag(OutputStateKnowledge.Dither) && output.Dither == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        outputKnown &= ~OutputStateKnowledge.Dither;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) GL.Enable(EnableCap.Dither); else GL.Disable(EnableCap.Dither);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        output.Dither = enabled;
        outputKnown |= OutputStateKnowledge.Dither;
    }

    /// <summary>Establishes ColorLogicOp, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetColorLogicOpEnabled(bool enabled)
    {
        ValidateCompleteMutation();
        if (outputKnown.HasFlag(OutputStateKnowledge.ColorLogicOp) && output.ColorLogicOp == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        outputKnown &= ~OutputStateKnowledge.ColorLogicOp;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) GL.Enable(EnableCap.ColorLogicOp); else GL.Disable(EnableCap.ColorLogicOp);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        output.ColorLogicOp = enabled;
        outputKnown |= OutputStateKnowledge.ColorLogicOp;
    }
    #endregion
}
