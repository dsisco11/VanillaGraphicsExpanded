using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns cached output transitions.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Establishes the fixed logic function independently of its enable state.</summary>
    internal void SetLogicOperation(LogicOp operation)
    {
        if (!Enum.IsDefined(operation)) throw new ArgumentOutOfRangeException(nameof(operation));
        ValidateBoundaryMutation(output: OutputStateKnowledge.LogicOperation, completeGraphics: true);
        if (outputKnown.HasFlag(OutputStateKnowledge.LogicOperation) && output.LogicOperation == operation) return;
        outputKnown &= ~OutputStateKnowledge.LogicOperation;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        OpenGLStateEffects.LogicOp(operation);
#if DEBUG
        FixedFunctionCalls++;
#endif
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        output.LogicOperation = operation;
        outputKnown |= OutputStateKnowledge.LogicOperation;
    }
    #endregion
}
