using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Propagates established external depthstencil effects into the existing executed-state authority.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    #region Depth
    /// <summary>Commits the defined DepthFunc effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalDepthFunc(DepthFunction function)
    {
        depth.Comparison = function; depthKnown |= DepthStateKnowledge.Comparison;
    }

    /// <summary>Commits the defined DepthMask effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalDepthMask(bool enabled)
    {
        depth.WriteEnabled = enabled; depthKnown |= DepthStateKnowledge.WriteEnabled;
    }

    /// <summary>Commits the defined DepthRange effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalDepthRange(double near, double far)
    {
        depth.DepthRange = (Math.Clamp((double)near, 0, 1), Math.Clamp((double)far, 0, 1)); depthKnown |= DepthStateKnowledge.DepthRange;
    }

    /// <summary>Commits the defined DepthRange effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalDepthRange(float near, float far)
    {
        depth.DepthRange = (Math.Clamp((double)near, 0, 1), Math.Clamp((double)far, 0, 1)); depthKnown |= DepthStateKnowledge.DepthRange;
    }

    #endregion

    #region Stencil comparisons
    /// <summary>Commits the defined StencilFunc effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalStencilFunc(StencilFunction function, int reference, uint mask)
    {
        stencil.FrontStencilFunction = (function, Math.Max(0, reference), unchecked((uint)mask)); stencilKnown |= StencilStateKnowledge.FrontStencilFunction;
        stencil.BackStencilFunction = (function, Math.Max(0, reference), unchecked((uint)mask)); stencilKnown |= StencilStateKnowledge.BackStencilFunction;
    }

    /// <summary>Commits the defined StencilFunc effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalStencilFunc(StencilFunction function, int reference, int mask)
    {
        stencil.FrontStencilFunction = (function, Math.Max(0, reference), unchecked((uint)mask)); stencilKnown |= StencilStateKnowledge.FrontStencilFunction;
        stencil.BackStencilFunction = (function, Math.Max(0, reference), unchecked((uint)mask)); stencilKnown |= StencilStateKnowledge.BackStencilFunction;
    }

    /// <summary>Commits the defined StencilFuncSeparate effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalStencilFuncSeparate(StencilFace face, StencilFunction function, int reference, uint mask)
    {
        if (face != StencilFace.Back) { stencil.FrontStencilFunction = (function, Math.Max(0, reference), unchecked((uint)mask)); stencilKnown |= StencilStateKnowledge.FrontStencilFunction; }
        if (face != StencilFace.Front) { stencil.BackStencilFunction = (function, Math.Max(0, reference), unchecked((uint)mask)); stencilKnown |= StencilStateKnowledge.BackStencilFunction; }
    }

    /// <summary>Commits the defined StencilFuncSeparate effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalStencilFuncSeparate(StencilFace face, StencilFunction function, int reference, int mask)
    {
        if (face != StencilFace.Back) { stencil.FrontStencilFunction = (function, Math.Max(0, reference), unchecked((uint)mask)); stencilKnown |= StencilStateKnowledge.FrontStencilFunction; }
        if (face != StencilFace.Front) { stencil.BackStencilFunction = (function, Math.Max(0, reference), unchecked((uint)mask)); stencilKnown |= StencilStateKnowledge.BackStencilFunction; }
    }

    #endregion

    #region Stencil masks
    /// <summary>Commits the defined StencilMask effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalStencilMask(uint mask)
    {
        stencil.FrontStencilMask = unchecked((uint)mask); stencilKnown |= StencilStateKnowledge.FrontStencilMask;
        stencil.BackStencilMask = unchecked((uint)mask); stencilKnown |= StencilStateKnowledge.BackStencilMask;
    }

    /// <summary>Commits the defined StencilMask effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalStencilMask(int mask)
    {
        stencil.FrontStencilMask = unchecked((uint)mask); stencilKnown |= StencilStateKnowledge.FrontStencilMask;
        stencil.BackStencilMask = unchecked((uint)mask); stencilKnown |= StencilStateKnowledge.BackStencilMask;
    }

    /// <summary>Commits the defined StencilMaskSeparate effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalStencilMaskSeparate(StencilFace face, uint mask)
    {
        if (face != StencilFace.Back) { stencil.FrontStencilMask = unchecked((uint)mask); stencilKnown |= StencilStateKnowledge.FrontStencilMask; }
        if (face != StencilFace.Front) { stencil.BackStencilMask = unchecked((uint)mask); stencilKnown |= StencilStateKnowledge.BackStencilMask; }
    }

    /// <summary>Commits the defined StencilMaskSeparate effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalStencilMaskSeparate(StencilFace face, int mask)
    {
        if (face != StencilFace.Back) { stencil.FrontStencilMask = unchecked((uint)mask); stencilKnown |= StencilStateKnowledge.FrontStencilMask; }
        if (face != StencilFace.Front) { stencil.BackStencilMask = unchecked((uint)mask); stencilKnown |= StencilStateKnowledge.BackStencilMask; }
    }

    #endregion

    #region Stencil operations
    /// <summary>Commits the defined StencilOp effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalStencilOp(StencilOp fail, StencilOp depthFail, StencilOp pass)
    {
        stencil.FrontStencilOperation = (fail, depthFail, pass); stencilKnown |= StencilStateKnowledge.FrontStencilOperation;
        stencil.BackStencilOperation = (fail, depthFail, pass); stencilKnown |= StencilStateKnowledge.BackStencilOperation;
    }

    /// <summary>Commits the defined StencilOpSeparate effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalStencilOpSeparate(StencilFace face, StencilOp fail, StencilOp depthFail, StencilOp pass)
    {
        if (face != StencilFace.Back) { stencil.FrontStencilOperation = (fail, depthFail, pass); stencilKnown |= StencilStateKnowledge.FrontStencilOperation; }
        if (face != StencilFace.Front) { stencil.BackStencilOperation = (fail, depthFail, pass); stencilKnown |= StencilStateKnowledge.BackStencilOperation; }
    }
    #endregion
    #endregion
}
