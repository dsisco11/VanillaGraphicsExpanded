using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes established glStencilFunc effects through generated originating-context delivery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Assigns the domain value after generated classification and targeted withdrawal.</summary>
    [InterceptOpenGL("glStencilFunc", Withdraw = nameof(WithdrawStencilFunc), Classify = nameof(ClassifyStencilFunc))]
    internal static void ObserveStencilFunc(OpenGlStateCache cache, int a0, int a1, uint a2)
    {
        StencilFunction function = unchecked((StencilFunction)a0);
        int reference = a1;
        uint mask = a2;
        cache.ObserveExternalStencilFunc(function, reference, mask);
    }

    /// <summary>Classifies the represented field from native arguments and established context facts.</summary>
    internal static OpenGLObservedEffect ClassifyStencilFunc(int a0, int a1, uint a2)
    {
        StencilFunction function = unchecked((StencilFunction)a0);
        if (Enum.IsDefined(function)) return OpenGLObservedEffect.Changed;
        return OpenGLObservedEffect.Rejected;
    }

    /// <summary>Withdraws only this operation's affected knowledge after uncertainty or delivery failure.</summary>
    internal static void WithdrawStencilFunc(OpenGlStateCache cache, int a0, int a1, uint a2)
    {
        cache.ForgetExternalStateMutation(stencil: (StencilStateKnowledge.FrontStencilFunction | StencilStateKnowledge.BackStencilFunction));
    }
    #endregion
}
