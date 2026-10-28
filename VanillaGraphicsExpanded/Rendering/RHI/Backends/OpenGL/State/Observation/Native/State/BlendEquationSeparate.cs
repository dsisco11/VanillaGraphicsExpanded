using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes established glBlendEquationSeparate effects through generated originating-context delivery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Assigns the domain value after generated classification and targeted withdrawal.</summary>
    [InterceptOpenGL("glBlendEquationSeparate", Withdraw = nameof(WithdrawBlendEquationSeparate), Classify = nameof(ClassifyBlendEquationSeparate))]
    internal static void ObserveBlendEquationSeparate(OpenGlStateCache cache, int a0, int a1)
    {
        BlendEquationMode rgb = unchecked((BlendEquationMode)a0);
        BlendEquationMode alpha = unchecked((BlendEquationMode)a1);
        cache.ObserveExternalBlendEquationSeparate(rgb, alpha);
    }

    /// <summary>Classifies the represented field from native arguments and established context facts.</summary>
    internal static OpenGLObservedEffect ClassifyBlendEquationSeparate(int a0, int a1)
    {
        BlendEquationMode rgb = unchecked((BlendEquationMode)a0);
        BlendEquationMode alpha = unchecked((BlendEquationMode)a1);
        if (Enum.IsDefined(rgb) && Enum.IsDefined(alpha)) return OpenGLObservedEffect.Changed;
        return OpenGLObservedEffect.Rejected;
    }

    /// <summary>Withdraws only this operation's affected knowledge after uncertainty or delivery failure.</summary>
    internal static void WithdrawBlendEquationSeparate(OpenGlStateCache cache, int a0, int a1)
    {
        cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Equations);
    }
    #endregion
}
