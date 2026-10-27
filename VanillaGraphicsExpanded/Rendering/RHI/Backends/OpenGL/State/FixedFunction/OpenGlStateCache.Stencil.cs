using System;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Owns independent front and back stencil transitions, including combined native aliases.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Sets stencil function for selected faces and updates all native aliases.</summary>
    internal void SetStencilFunction(StencilFace face, StencilFunction function, int reference, uint mask)
    {
        if (face is not (StencilFace.Front or StencilFace.Back or StencilFace.FrontAndBack)) throw new ArgumentOutOfRangeException(nameof(face));
        if (!Enum.IsDefined(function)) throw new ArgumentOutOfRangeException(nameof(function));
        reference = Math.Max(0, reference);
        ValidateBoundaryMutation(stencil:
            (face == StencilFace.Back ? default : StencilStateKnowledge.FrontStencilFunction)
            | (face == StencilFace.Front ? default : StencilStateKnowledge.BackStencilFunction), completeGraphics: true);
        bool front = face != StencilFace.Back, back = face != StencilFace.Front;
        if ((!front || stencilKnown.HasFlag(StencilStateKnowledge.FrontStencilFunction) && stencil.FrontStencilFunction == (function, reference, mask))
            && (!back || stencilKnown.HasFlag(StencilStateKnowledge.BackStencilFunction) && stencil.BackStencilFunction == (function, reference, mask))) return;
        if (front) stencilKnown &= ~StencilStateKnowledge.FrontStencilFunction;
        if (back) stencilKnown &= ~StencilStateKnowledge.BackStencilFunction;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        OpenGLStateEffects.StencilFuncSeparate(face, function, reference, mask);
#if DEBUG
        FixedFunctionCalls++;
#endif
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (front) { stencil.FrontStencilFunction = (function, reference, mask); stencilKnown |= StencilStateKnowledge.FrontStencilFunction; }
        if (back) { stencil.BackStencilFunction = (function, reference, mask); stencilKnown |= StencilStateKnowledge.BackStencilFunction; }
    }
    /// <summary>Sets stencil mask for selected faces and updates all native aliases.</summary>
    internal void SetStencilWriteMask(StencilFace face, uint mask)
    {
        if (face is not (StencilFace.Front or StencilFace.Back or StencilFace.FrontAndBack)) throw new ArgumentOutOfRangeException(nameof(face));
        
        ValidateBoundaryMutation(stencil:
            (face == StencilFace.Back ? default : StencilStateKnowledge.FrontStencilMask)
            | (face == StencilFace.Front ? default : StencilStateKnowledge.BackStencilMask), completeGraphics: true);
        bool front = face != StencilFace.Back, back = face != StencilFace.Front;
        if ((!front || stencilKnown.HasFlag(StencilStateKnowledge.FrontStencilMask) && stencil.FrontStencilMask == mask)
            && (!back || stencilKnown.HasFlag(StencilStateKnowledge.BackStencilMask) && stencil.BackStencilMask == mask)) return;
        if (front) stencilKnown &= ~StencilStateKnowledge.FrontStencilMask;
        if (back) stencilKnown &= ~StencilStateKnowledge.BackStencilMask;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        OpenGLStateEffects.StencilMaskSeparate(face, mask);
#if DEBUG
        FixedFunctionCalls++;
#endif
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (front) { stencil.FrontStencilMask = mask; stencilKnown |= StencilStateKnowledge.FrontStencilMask; }
        if (back) { stencil.BackStencilMask = mask; stencilKnown |= StencilStateKnowledge.BackStencilMask; }
    }
    /// <summary>Sets stencil operation for selected faces and updates all native aliases.</summary>
    internal void SetStencilOperation(StencilFace face, StencilOp fail, StencilOp depthFail, StencilOp pass)
    {
        if (face is not (StencilFace.Front or StencilFace.Back or StencilFace.FrontAndBack)) throw new ArgumentOutOfRangeException(nameof(face));
        if (!Enum.IsDefined(fail) || !Enum.IsDefined(depthFail) || !Enum.IsDefined(pass)) throw new ArgumentOutOfRangeException(nameof(fail));
        ValidateBoundaryMutation(stencil:
            (face == StencilFace.Back ? default : StencilStateKnowledge.FrontStencilOperation)
            | (face == StencilFace.Front ? default : StencilStateKnowledge.BackStencilOperation), completeGraphics: true);
        bool front = face != StencilFace.Back, back = face != StencilFace.Front;
        if ((!front || stencilKnown.HasFlag(StencilStateKnowledge.FrontStencilOperation) && stencil.FrontStencilOperation == (fail, depthFail, pass))
            && (!back || stencilKnown.HasFlag(StencilStateKnowledge.BackStencilOperation) && stencil.BackStencilOperation == (fail, depthFail, pass))) return;
        if (front) stencilKnown &= ~StencilStateKnowledge.FrontStencilOperation;
        if (back) stencilKnown &= ~StencilStateKnowledge.BackStencilOperation;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        OpenGLStateEffects.StencilOpSeparate(face, fail, depthFail, pass);
#if DEBUG
        FixedFunctionCalls++;
#endif
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (front) { stencil.FrontStencilOperation = (fail, depthFail, pass); stencilKnown |= StencilStateKnowledge.FrontStencilOperation; }
        if (back) { stencil.BackStencilOperation = (fail, depthFail, pass); stencilKnown |= StencilStateKnowledge.BackStencilOperation; }
    }
    #endregion
}
