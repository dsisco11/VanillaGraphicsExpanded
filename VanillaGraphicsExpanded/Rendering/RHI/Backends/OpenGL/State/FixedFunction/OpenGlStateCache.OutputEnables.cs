using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns cached output enable transitions.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Establishes FramebufferSrgb, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetFramebufferSrgbEnabled(bool enabled)
    {
        ValidateBoundaryMutation(output: OutputStateKnowledge.FramebufferSrgb, completeGraphics: true);
        if (outputKnown.HasFlag(OutputStateKnowledge.FramebufferSrgb) && output.FramebufferSrgb == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        outputKnown &= ~OutputStateKnowledge.FramebufferSrgb;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) OpenGLStateEffects.Enable(EnableCap.FramebufferSrgb); else OpenGLStateEffects.Disable(EnableCap.FramebufferSrgb);
#if DEBUG
        FixedFunctionCalls++;
#endif
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        output.FramebufferSrgb = enabled;
        outputKnown |= OutputStateKnowledge.FramebufferSrgb;
    }

    /// <summary>Establishes Dither, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetDitherEnabled(bool enabled)
    {
        ValidateBoundaryMutation(output: OutputStateKnowledge.Dither, completeGraphics: true);
        if (outputKnown.HasFlag(OutputStateKnowledge.Dither) && output.Dither == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        outputKnown &= ~OutputStateKnowledge.Dither;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) OpenGLStateEffects.Enable(EnableCap.Dither); else OpenGLStateEffects.Disable(EnableCap.Dither);
#if DEBUG
        FixedFunctionCalls++;
#endif
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        output.Dither = enabled;
        outputKnown |= OutputStateKnowledge.Dither;
    }

    /// <summary>Establishes ColorLogicOp, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetColorLogicOpEnabled(bool enabled)
    {
        ValidateBoundaryMutation(output: OutputStateKnowledge.ColorLogicOp, completeGraphics: true);
        if (outputKnown.HasFlag(OutputStateKnowledge.ColorLogicOp) && output.ColorLogicOp == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        outputKnown &= ~OutputStateKnowledge.ColorLogicOp;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) OpenGLStateEffects.Enable(EnableCap.ColorLogicOp); else OpenGLStateEffects.Disable(EnableCap.ColorLogicOp);
#if DEBUG
        FixedFunctionCalls++;
#endif
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        output.ColorLogicOp = enabled;
        outputKnown |= OutputStateKnowledge.ColorLogicOp;
    }
    #endregion
}
