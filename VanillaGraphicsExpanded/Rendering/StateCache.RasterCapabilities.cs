using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns cached rasterizer enable transitions.</summary>
internal sealed partial class StateCache
{
    #region Public API
    #region Capability transitions
    /// <summary>Establishes DepthClamp, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetDepthClampEnabled(bool enabled)
    {
        ValidateCompleteMutation();
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.DepthClamp) && rasterizer.DepthClamp == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        rasterizerKnown &= ~RasterizerStateKnowledge.DepthClamp;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) GL.Enable(EnableCap.DepthClamp); else GL.Disable(EnableCap.DepthClamp);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.DepthClamp = enabled;
        rasterizerKnown |= RasterizerStateKnowledge.DepthClamp;
    }

    /// <summary>Establishes RasterizerDiscard, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetRasterizerDiscardEnabled(bool enabled)
    {
        ValidateCompleteMutation();
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.RasterizerDiscard) && rasterizer.RasterizerDiscard == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        rasterizerKnown &= ~RasterizerStateKnowledge.RasterizerDiscard;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) GL.Enable(EnableCap.RasterizerDiscard); else GL.Disable(EnableCap.RasterizerDiscard);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.RasterizerDiscard = enabled;
        rasterizerKnown |= RasterizerStateKnowledge.RasterizerDiscard;
    }

    /// <summary>Establishes PolygonOffsetFill, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetPolygonOffsetFillEnabled(bool enabled)
    {
        ValidateCompleteMutation();
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonOffsetFill) && rasterizer.PolygonOffsetFill == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        rasterizerKnown &= ~RasterizerStateKnowledge.PolygonOffsetFill;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) GL.Enable(EnableCap.PolygonOffsetFill); else GL.Disable(EnableCap.PolygonOffsetFill);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.PolygonOffsetFill = enabled;
        rasterizerKnown |= RasterizerStateKnowledge.PolygonOffsetFill;
    }

    /// <summary>Establishes PolygonOffsetLine, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetPolygonOffsetLineEnabled(bool enabled)
    {
        ValidateCompleteMutation();
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonOffsetLine) && rasterizer.PolygonOffsetLine == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        rasterizerKnown &= ~RasterizerStateKnowledge.PolygonOffsetLine;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) GL.Enable(EnableCap.PolygonOffsetLine); else GL.Disable(EnableCap.PolygonOffsetLine);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.PolygonOffsetLine = enabled;
        rasterizerKnown |= RasterizerStateKnowledge.PolygonOffsetLine;
    }

    /// <summary>Establishes PolygonOffsetPoint, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetPolygonOffsetPointEnabled(bool enabled)
    {
        ValidateCompleteMutation();
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonOffsetPoint) && rasterizer.PolygonOffsetPoint == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        rasterizerKnown &= ~RasterizerStateKnowledge.PolygonOffsetPoint;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) GL.Enable(EnableCap.PolygonOffsetPoint); else GL.Disable(EnableCap.PolygonOffsetPoint);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.PolygonOffsetPoint = enabled;
        rasterizerKnown |= RasterizerStateKnowledge.PolygonOffsetPoint;
    }

    /// <summary>Establishes ProgramPointSize, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetProgramPointSizeEnabled(bool enabled)
    {
        ValidateCompleteMutation();
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.ProgramPointSize) && rasterizer.ProgramPointSize == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        rasterizerKnown &= ~RasterizerStateKnowledge.ProgramPointSize;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) GL.Enable(EnableCap.ProgramPointSize); else GL.Disable(EnableCap.ProgramPointSize);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.ProgramPointSize = enabled;
        rasterizerKnown |= RasterizerStateKnowledge.ProgramPointSize;
    }
    #endregion
    #endregion
}
