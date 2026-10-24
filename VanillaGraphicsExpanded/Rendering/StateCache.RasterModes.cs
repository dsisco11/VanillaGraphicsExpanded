using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns cached raster modes transitions.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Establishes CullMode while suppressing a known identical native transition.</summary>
    internal void SetCullMode(CullFaceMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.CullMode, completeGraphics: true);
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.CullMode) && rasterizer.CullMode == mode) return;
        rasterizerKnown &= ~RasterizerStateKnowledge.CullMode;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.CullFace(mode);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.CullMode = mode;
        rasterizerKnown |= RasterizerStateKnowledge.CullMode;
    }

    /// <summary>Establishes FrontFace while suppressing a known identical native transition.</summary>
    internal void SetFrontFace(FrontFaceDirection winding)
    {
        if (!Enum.IsDefined(winding)) throw new ArgumentOutOfRangeException(nameof(winding));
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.FrontFace, completeGraphics: true);
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.FrontFace) && rasterizer.FrontFace == winding) return;
        rasterizerKnown &= ~RasterizerStateKnowledge.FrontFace;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.FrontFace(winding);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.FrontFace = winding;
        rasterizerKnown |= RasterizerStateKnowledge.FrontFace;
    }

    /// <summary>Establishes PolygonOffset while suppressing a known identical native transition.</summary>
    internal void SetPolygonOffset(float factor, float units)
    {
        if (!float.IsFinite(factor) || !float.IsFinite(units)) throw new ArgumentOutOfRangeException(nameof(factor));
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.PolygonOffset, completeGraphics: true);
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonOffset) && rasterizer.PolygonOffset == (factor, units)) return;
        rasterizerKnown &= ~RasterizerStateKnowledge.PolygonOffset;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.PolygonOffset(factor, units);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.PolygonOffset = (factor, units);
        rasterizerKnown |= RasterizerStateKnowledge.PolygonOffset;
    }

    /// <summary>Establishes the common polygon mode used by modern rendering.</summary>
    internal void SetPolygonMode(PolygonMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.PolygonModes, completeGraphics: true);
        if (rasterizerKnown.HasFlag(RasterizerStateKnowledge.PolygonModes) && rasterizer.PolygonModes == mode) return;
        rasterizerKnown &= ~RasterizerStateKnowledge.PolygonModes;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.PolygonMode(MaterialFace.FrontAndBack, mode);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.PolygonModes = mode;
        rasterizerKnown |= RasterizerStateKnowledge.PolygonModes;
    }

    /// <summary>Tracks common modes and forwards unsupported face selectors without caching their state.</summary>
    internal void SetPolygonMode(MaterialFace face, PolygonMode mode)
    {
        if (face == MaterialFace.FrontAndBack) { SetPolygonMode(mode); return; }
        RejectUnsupportedBoundaryMutation();
        // Preserve the native command and error behavior while withdrawing the common-mode fact.
        rasterizerKnown &= ~RasterizerStateKnowledge.PolygonModes;
        GL.PolygonMode(face, mode);
        FixedFunctionCalls++;
    }
    #endregion
}
