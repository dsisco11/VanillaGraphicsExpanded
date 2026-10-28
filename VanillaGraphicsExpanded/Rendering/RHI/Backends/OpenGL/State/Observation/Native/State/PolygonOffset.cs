using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes established glPolygonOffset effects through generated originating-context delivery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Assigns the domain value after generated classification and targeted withdrawal.</summary>
    [InterceptOpenGL("glPolygonOffset", Withdraw = nameof(WithdrawPolygonOffset), Classify = nameof(ClassifyPolygonOffset))]
    internal static void ObservePolygonOffset(OpenGlStateCache cache, float a0, float a1)
    {
        float factor = a0;
        float units = a1;
        cache.ObserveExternalPolygonOffset(factor, units);
    }

    /// <summary>Classifies the represented field from native arguments and established context facts.</summary>
    internal static OpenGLObservedEffect ClassifyPolygonOffset(float a0, float a1)
    {
        float factor = a0;
        float units = a1;
        if (float.IsFinite(factor) && float.IsFinite(units)) return OpenGLObservedEffect.Changed;
        if (!float.IsFinite(factor) || !float.IsFinite(units)) return OpenGLObservedEffect.Uncertain;
        return OpenGLObservedEffect.Rejected;
    }

    /// <summary>Withdraws only this operation's affected knowledge after uncertainty or delivery failure.</summary>
    internal static void WithdrawPolygonOffset(OpenGlStateCache cache, float a0, float a1)
    {
        cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.PolygonOffset);
    }
    #endregion
}
