using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes established glDepthFunc effects through generated originating-context delivery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Assigns the domain value after generated classification and targeted withdrawal.</summary>
    [InterceptOpenGL("glDepthFunc", Withdraw = nameof(WithdrawDepthFunc), Classify = nameof(ClassifyDepthFunc))]
    internal static void ObserveDepthFunc(OpenGlStateCache cache, int a0)
    {
        DepthFunction function = unchecked((DepthFunction)a0);
        cache.ObserveExternalDepthFunc(function);
    }

    /// <summary>Classifies the represented field from native arguments and established context facts.</summary>
    internal static OpenGLObservedEffect ClassifyDepthFunc(int a0)
    {
        DepthFunction function = unchecked((DepthFunction)a0);
        if (Enum.IsDefined(function)) return OpenGLObservedEffect.Changed;
        return OpenGLObservedEffect.Rejected;
    }

    /// <summary>Withdraws only this operation's affected knowledge after uncertainty or delivery failure.</summary>
    internal static void WithdrawDepthFunc(OpenGlStateCache cache, int a0)
    {
        cache.ForgetExternalStateMutation(depth: DepthStateKnowledge.Comparison);
    }
    #endregion
}
