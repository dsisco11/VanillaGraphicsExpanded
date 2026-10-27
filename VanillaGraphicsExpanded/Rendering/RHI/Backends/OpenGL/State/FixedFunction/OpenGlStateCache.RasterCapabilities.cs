using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns cached rasterizer enable transitions.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    #region Capability transitions
    /// <summary>Establishes DepthClamp, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetDepthClampEnabled(bool enabled)
    {
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.DepthClamp, completeGraphics: true);
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.DepthClamp) && rasterizer.DepthClamp == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        rasterizerKnown &= ~RasterizerStateKnowledge.DepthClamp;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) OpenGLStateEffects.Enable(EnableCap.DepthClamp); else OpenGLStateEffects.Disable(EnableCap.DepthClamp);
#if DEBUG
        FixedFunctionCalls++;
#endif
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.DepthClamp = enabled;
        rasterizerKnown |= RasterizerStateKnowledge.DepthClamp;
    }

    /// <summary>Establishes RasterizerDiscard, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetRasterizerDiscardEnabled(bool enabled)
    {
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.RasterizerDiscard, completeGraphics: true);
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.RasterizerDiscard) && rasterizer.RasterizerDiscard == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        rasterizerKnown &= ~RasterizerStateKnowledge.RasterizerDiscard;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) OpenGLStateEffects.Enable(EnableCap.RasterizerDiscard); else OpenGLStateEffects.Disable(EnableCap.RasterizerDiscard);
#if DEBUG
        FixedFunctionCalls++;
#endif
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.RasterizerDiscard = enabled;
        rasterizerKnown |= RasterizerStateKnowledge.RasterizerDiscard;
    }

    /// <summary>Establishes PolygonOffsetFill, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetPolygonOffsetFillEnabled(bool enabled)
    {
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.PolygonOffsetFill, completeGraphics: true);
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonOffsetFill) && rasterizer.PolygonOffsetFill == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        rasterizerKnown &= ~RasterizerStateKnowledge.PolygonOffsetFill;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) OpenGLStateEffects.Enable(EnableCap.PolygonOffsetFill); else OpenGLStateEffects.Disable(EnableCap.PolygonOffsetFill);
#if DEBUG
        FixedFunctionCalls++;
#endif
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.PolygonOffsetFill = enabled;
        rasterizerKnown |= RasterizerStateKnowledge.PolygonOffsetFill;
    }

    /// <summary>Establishes PolygonOffsetLine, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetPolygonOffsetLineEnabled(bool enabled)
    {
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.PolygonOffsetLine, completeGraphics: true);
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonOffsetLine) && rasterizer.PolygonOffsetLine == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        rasterizerKnown &= ~RasterizerStateKnowledge.PolygonOffsetLine;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) OpenGLStateEffects.Enable(EnableCap.PolygonOffsetLine); else OpenGLStateEffects.Disable(EnableCap.PolygonOffsetLine);
#if DEBUG
        FixedFunctionCalls++;
#endif
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.PolygonOffsetLine = enabled;
        rasterizerKnown |= RasterizerStateKnowledge.PolygonOffsetLine;
    }

    /// <summary>Establishes PolygonOffsetPoint, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetPolygonOffsetPointEnabled(bool enabled)
    {
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.PolygonOffsetPoint, completeGraphics: true);
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonOffsetPoint) && rasterizer.PolygonOffsetPoint == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        rasterizerKnown &= ~RasterizerStateKnowledge.PolygonOffsetPoint;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) OpenGLStateEffects.Enable(EnableCap.PolygonOffsetPoint); else OpenGLStateEffects.Disable(EnableCap.PolygonOffsetPoint);
#if DEBUG
        FixedFunctionCalls++;
#endif
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.PolygonOffsetPoint = enabled;
        rasterizerKnown |= RasterizerStateKnowledge.PolygonOffsetPoint;
    }

    /// <summary>Establishes ProgramPointSize, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetProgramPointSizeEnabled(bool enabled)
    {
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.ProgramPointSize, completeGraphics: true);
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.ProgramPointSize) && rasterizer.ProgramPointSize == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        rasterizerKnown &= ~RasterizerStateKnowledge.ProgramPointSize;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) OpenGLStateEffects.Enable(EnableCap.ProgramPointSize); else OpenGLStateEffects.Disable(EnableCap.ProgramPointSize);
#if DEBUG
        FixedFunctionCalls++;
#endif
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.ProgramPointSize = enabled;
        rasterizerKnown |= RasterizerStateKnowledge.ProgramPointSize;
    }
    #endregion
    #endregion
}
