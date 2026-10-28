using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes established glLogicOp effects through generated originating-context delivery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Assigns the domain value after generated classification and targeted withdrawal.</summary>
    [InterceptOpenGL("glLogicOp", Withdraw = nameof(WithdrawLogicOp), Classify = nameof(ClassifyLogicOp))]
    internal static void ObserveLogicOp(OpenGlStateCache cache, int operation)
    {
        cache.ObserveExternalLogicOp((LogicOp)operation);
    }

    /// <summary>Invalid operation selectors are rejected without changing the established logic operation.</summary>
    internal static OpenGLObservedEffect ClassifyLogicOp(int operation) => Enum.IsDefined((LogicOp)operation)
        ? OpenGLObservedEffect.Changed : OpenGLObservedEffect.Rejected;

    /// <summary>Withdraws only this operation's affected knowledge after uncertainty or delivery failure.</summary>
    internal static void WithdrawLogicOp(OpenGlStateCache cache, int operation)
    {
        cache.ForgetExternalStateMutation(output: OutputStateKnowledge.LogicOperation);
    }
    #endregion
}
