using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes established glScissor effects through generated originating-context delivery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Assigns the domain value after generated classification and targeted withdrawal.</summary>
    [InterceptOpenGL("glScissor", Withdraw = nameof(WithdrawScissor), Classify = nameof(ClassifyScissor))]
    internal static void ObserveScissor(OpenGlStateCache cache, int a0, int a1, int a2, int a3)
    {
        int x = a0;
        int y = a1;
        int width = a2;
        int height = a3;
        cache.ObserveExternalScissor(x, y, width, height);
    }

    /// <summary>Classifies the represented field from native arguments and established context facts.</summary>
    internal static OpenGLObservedEffect ClassifyScissor(int a0, int a1, int a2, int a3)
    {
        int width = a2;
        int height = a3;
        if (width >= 0 && height >= 0) return OpenGLObservedEffect.Changed;
        return OpenGLObservedEffect.Rejected;
    }

    /// <summary>Withdraws only this operation's affected knowledge after uncertainty or delivery failure.</summary>
    internal static void WithdrawScissor(OpenGlStateCache cache, int a0, int a1, int a2, int a3)
    {
        cache.ForgetExternalStateMutation(dynamic: DynamicDrawStateKnowledge.Scissor);
    }
    #endregion
}
