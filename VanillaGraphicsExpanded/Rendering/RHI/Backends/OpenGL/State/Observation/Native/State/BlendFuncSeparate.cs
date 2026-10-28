using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes established glBlendFuncSeparate effects through generated originating-context delivery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Assigns the domain value after generated classification and targeted withdrawal.</summary>
    [InterceptOpenGL("glBlendFuncSeparate", Withdraw = nameof(WithdrawBlendFuncSeparate), Classify = nameof(ClassifyBlendFuncSeparate))]
    internal static void ObserveBlendFuncSeparate(OpenGlStateCache cache, int a0, int a1, int a2, int a3)
    {
        BlendingFactorSrc sourceRgb = unchecked((BlendingFactorSrc)a0);
        BlendingFactorDest destinationRgb = unchecked((BlendingFactorDest)a1);
        BlendingFactorSrc sourceAlpha = unchecked((BlendingFactorSrc)a2);
        BlendingFactorDest destinationAlpha = unchecked((BlendingFactorDest)a3);
        cache.ObserveExternalBlendFuncSeparate(sourceRgb, destinationRgb, sourceAlpha, destinationAlpha);
    }

    /// <summary>Classifies the represented field from native arguments and established context facts.</summary>
    internal static OpenGLObservedEffect ClassifyBlendFuncSeparate(int a0, int a1, int a2, int a3)
    {
        BlendingFactorSrc sourceRgb = unchecked((BlendingFactorSrc)a0);
        BlendingFactorDest destinationRgb = unchecked((BlendingFactorDest)a1);
        BlendingFactorSrc sourceAlpha = unchecked((BlendingFactorSrc)a2);
        BlendingFactorDest destinationAlpha = unchecked((BlendingFactorDest)a3);
        if (OpenGlStateCache.AcceptsExternalBlendFactors(new GlBlendFunc(sourceRgb, destinationRgb, sourceAlpha, destinationAlpha))) return OpenGLObservedEffect.Changed;
        return OpenGLObservedEffect.Rejected;
    }

    /// <summary>Withdraws only this operation's affected knowledge after uncertainty or delivery failure.</summary>
    internal static void WithdrawBlendFuncSeparate(OpenGlStateCache cache, int a0, int a1, int a2, int a3)
    {
        cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Factors);
    }
    #endregion
}
