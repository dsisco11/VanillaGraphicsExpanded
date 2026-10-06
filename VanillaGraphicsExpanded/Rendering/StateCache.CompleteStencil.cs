using System;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Owns independent front and back stencil transitions, including combined native aliases.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Sets stencil function for selected faces and updates all native aliases.</summary>
    internal void SetStencilFunction(StencilFace face, StencilFunction function, int reference, uint mask)
    {
        if (face is not (StencilFace.Front or StencilFace.Back or StencilFace.FrontAndBack)) throw new ArgumentOutOfRangeException(nameof(face));
        if (!Enum.IsDefined(function)) throw new ArgumentOutOfRangeException(nameof(function));
        reference = Math.Max(0, reference);
        ValidateCompleteMutation();
        bool front = face != StencilFace.Back, back = face != StencilFace.Front;
        if ((!front || completeStencil.Known.HasFlag(CompleteStencilKnowledge.FrontStencilFunction) && completeStencil.FrontStencilFunction == (function, reference, mask))
            && (!back || completeStencil.Known.HasFlag(CompleteStencilKnowledge.BackStencilFunction) && completeStencil.BackStencilFunction == (function, reference, mask))) return;
        if (front) completeStencil.Known &= ~CompleteStencilKnowledge.FrontStencilFunction;
        if (back) completeStencil.Known &= ~CompleteStencilKnowledge.BackStencilFunction;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.StencilFuncSeparate(face, function, reference, mask);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (front) { completeStencil.FrontStencilFunction = (function, reference, mask); completeStencil.Known |= CompleteStencilKnowledge.FrontStencilFunction; }
        if (back) { completeStencil.BackStencilFunction = (function, reference, mask); completeStencil.Known |= CompleteStencilKnowledge.BackStencilFunction; }
    }
    /// <summary>Sets stencil mask for selected faces and updates all native aliases.</summary>
    internal void SetStencilWriteMask(StencilFace face, uint mask)
    {
        if (face is not (StencilFace.Front or StencilFace.Back or StencilFace.FrontAndBack)) throw new ArgumentOutOfRangeException(nameof(face));
        
        ValidateCompleteMutation();
        bool front = face != StencilFace.Back, back = face != StencilFace.Front;
        if ((!front || completeStencil.Known.HasFlag(CompleteStencilKnowledge.FrontStencilMask) && completeStencil.FrontStencilMask == mask)
            && (!back || completeStencil.Known.HasFlag(CompleteStencilKnowledge.BackStencilMask) && completeStencil.BackStencilMask == mask)) return;
        if (front) completeStencil.Known &= ~CompleteStencilKnowledge.FrontStencilMask;
        if (back) completeStencil.Known &= ~CompleteStencilKnowledge.BackStencilMask;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.StencilMaskSeparate(face, mask);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (front) { completeStencil.FrontStencilMask = mask; completeStencil.Known |= CompleteStencilKnowledge.FrontStencilMask; }
        if (back) { completeStencil.BackStencilMask = mask; completeStencil.Known |= CompleteStencilKnowledge.BackStencilMask; }
    }
    /// <summary>Sets stencil operation for selected faces and updates all native aliases.</summary>
    internal void SetStencilOperation(StencilFace face, StencilOp fail, StencilOp depthFail, StencilOp pass)
    {
        if (face is not (StencilFace.Front or StencilFace.Back or StencilFace.FrontAndBack)) throw new ArgumentOutOfRangeException(nameof(face));
        if (!Enum.IsDefined(fail) || !Enum.IsDefined(depthFail) || !Enum.IsDefined(pass)) throw new ArgumentOutOfRangeException(nameof(fail));
        ValidateCompleteMutation();
        bool front = face != StencilFace.Back, back = face != StencilFace.Front;
        if ((!front || completeStencil.Known.HasFlag(CompleteStencilKnowledge.FrontStencilOperation) && completeStencil.FrontStencilOperation == (fail, depthFail, pass))
            && (!back || completeStencil.Known.HasFlag(CompleteStencilKnowledge.BackStencilOperation) && completeStencil.BackStencilOperation == (fail, depthFail, pass))) return;
        if (front) completeStencil.Known &= ~CompleteStencilKnowledge.FrontStencilOperation;
        if (back) completeStencil.Known &= ~CompleteStencilKnowledge.BackStencilOperation;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.StencilOpSeparate(face, fail, depthFail, pass);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (front) { completeStencil.FrontStencilOperation = (fail, depthFail, pass); completeStencil.Known |= CompleteStencilKnowledge.FrontStencilOperation; }
        if (back) { completeStencil.BackStencilOperation = (fail, depthFail, pass); completeStencil.Known |= CompleteStencilKnowledge.BackStencilOperation; }
    }
    #endregion
}
