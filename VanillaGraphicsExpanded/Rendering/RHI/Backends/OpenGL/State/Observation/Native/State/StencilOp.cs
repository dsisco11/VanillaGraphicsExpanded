using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes established glStencilOp effects through generated originating-context delivery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Assigns the domain value after generated classification and targeted withdrawal.</summary>
    [InterceptOpenGL("glStencilOp", Withdraw = nameof(WithdrawStencilOp), Classify = nameof(ClassifyStencilOp))]
    internal static void ObserveStencilOp(OpenGlStateCache cache, int a0, int a1, int a2)
    {
        StencilOp fail = unchecked((StencilOp)a0);
        StencilOp depthFail = unchecked((StencilOp)a1);
        StencilOp pass = unchecked((StencilOp)a2);
        cache.ObserveExternalStencilOp(fail, depthFail, pass);
    }

    /// <summary>Classifies the represented field from native arguments and established context facts.</summary>
    internal static OpenGLObservedEffect ClassifyStencilOp(int a0, int a1, int a2)
    {
        StencilOp fail = unchecked((StencilOp)a0);
        StencilOp depthFail = unchecked((StencilOp)a1);
        StencilOp pass = unchecked((StencilOp)a2);
        if (Enum.IsDefined(fail) && Enum.IsDefined(depthFail) && Enum.IsDefined(pass)) return OpenGLObservedEffect.Changed;
        return OpenGLObservedEffect.Rejected;
    }

    /// <summary>Withdraws only this operation's affected knowledge after uncertainty or delivery failure.</summary>
    internal static void WithdrawStencilOp(OpenGlStateCache cache, int a0, int a1, int a2)
    {
        cache.ForgetExternalStateMutation(stencil: (StencilStateKnowledge.FrontStencilOperation | StencilStateKnowledge.BackStencilOperation));
    }
    #endregion
}
