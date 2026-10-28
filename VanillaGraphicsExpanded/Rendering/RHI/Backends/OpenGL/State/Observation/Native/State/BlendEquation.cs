using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes established glBlendEquation effects through generated originating-context delivery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Assigns the domain value after generated classification and targeted withdrawal.</summary>
    [InterceptOpenGL("glBlendEquation", Withdraw = nameof(WithdrawBlendEquation), Classify = nameof(ClassifyBlendEquation))]
    internal static void ObserveBlendEquation(OpenGlStateCache cache, int a0)
    {
        BlendEquationMode mode = unchecked((BlendEquationMode)a0);
        cache.ObserveExternalBlendEquation(mode);
    }

    /// <summary>Classifies the represented field from native arguments and established context facts.</summary>
    internal static OpenGLObservedEffect ClassifyBlendEquation(int a0)
    {
        BlendEquationMode mode = unchecked((BlendEquationMode)a0);
        if (Enum.IsDefined(mode)) return OpenGLObservedEffect.Changed;
        return OpenGLObservedEffect.Rejected;
    }

    /// <summary>Withdraws only this operation's affected knowledge after uncertainty or delivery failure.</summary>
    internal static void WithdrawBlendEquation(OpenGlStateCache cache, int a0)
    {
        cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Equations);
    }
    #endregion
}
