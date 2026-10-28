using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes established glBlendFunc effects through generated originating-context delivery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Assigns the domain value after generated classification and targeted withdrawal.</summary>
    [InterceptOpenGL("glBlendFunc", Withdraw = nameof(WithdrawBlendFunc), Classify = nameof(ClassifyBlendFunc))]
    internal static void ObserveBlendFunc(OpenGlStateCache cache, int a0, int a1)
    {
        BlendingFactor source = unchecked((BlendingFactor)a0);
        BlendingFactor destination = unchecked((BlendingFactor)a1);
        cache.ObserveExternalBlendFunc(source, destination);
    }

    /// <summary>Classifies the represented field from native arguments and established context facts.</summary>
    internal static OpenGLObservedEffect ClassifyBlendFunc(int a0, int a1)
    {
        BlendingFactor source = unchecked((BlendingFactor)a0);
        BlendingFactor destination = unchecked((BlendingFactor)a1);
        if (OpenGlStateCache.AcceptsExternalBlendFactors(new GlBlendFunc((BlendingFactorSrc)source, (BlendingFactorDest)destination, (BlendingFactorSrc)source, (BlendingFactorDest)destination))) return OpenGLObservedEffect.Changed;
        return OpenGLObservedEffect.Rejected;
    }

    /// <summary>Withdraws only this operation's affected knowledge after uncertainty or delivery failure.</summary>
    internal static void WithdrawBlendFunc(OpenGlStateCache cache, int a0, int a1)
    {
        cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Factors);
    }
    #endregion
}
