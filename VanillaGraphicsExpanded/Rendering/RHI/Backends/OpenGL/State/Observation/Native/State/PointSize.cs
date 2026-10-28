using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes established glPointSize effects through generated originating-context delivery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Assigns the domain value after generated classification and targeted withdrawal.</summary>
    [InterceptOpenGL("glPointSize", Withdraw = nameof(WithdrawPointSize), Classify = nameof(ClassifyPointSize))]
    internal static void ObservePointSize(OpenGlStateCache cache, float a0)
    {
        float size = a0;
        cache.ObserveExternalPointSize(size);
    }

    /// <summary>Classifies the represented field from native arguments and established context facts.</summary>
    internal static OpenGLObservedEffect ClassifyPointSize(float a0)
    {
        float size = a0;
        if (float.IsFinite(size) && size > 0) return OpenGLObservedEffect.Changed;
        if (!float.IsFinite(size)) return OpenGLObservedEffect.Uncertain;
        return OpenGLObservedEffect.Rejected;
    }

    /// <summary>Withdraws only this operation's affected knowledge after uncertainty or delivery failure.</summary>
    internal static void WithdrawPointSize(OpenGlStateCache cache, float a0)
    {
        cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.PointSize);
    }
    #endregion
}
