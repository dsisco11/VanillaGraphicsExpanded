using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns cached output transitions.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Establishes the fixed logic function independently of its enable state.</summary>
    internal void SetLogicOperation(LogicOp operation)
    {
        if (!Enum.IsDefined(operation)) throw new ArgumentOutOfRangeException(nameof(operation));
        ValidateCompleteMutation();
        if (outputKnown.HasFlag(OutputStateKnowledge.LogicOperation) && output.LogicOperation == operation) return;
        outputKnown &= ~OutputStateKnowledge.LogicOperation;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.LogicOp(operation); FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        output.LogicOperation = operation;
        outputKnown |= OutputStateKnowledge.LogicOperation;
    }
    #endregion
}
