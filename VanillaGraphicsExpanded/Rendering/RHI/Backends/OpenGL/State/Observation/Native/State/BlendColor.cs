using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes established glBlendColor effects through generated originating-context delivery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Assigns the domain value after generated classification and targeted withdrawal.</summary>
    [InterceptOpenGL("glBlendColor", Withdraw = nameof(WithdrawBlendColor), Classify = nameof(ClassifyBlendColor))]
    internal static void ObserveBlendColor(OpenGlStateCache cache, float a0, float a1, float a2, float a3)
    {
        float red = a0;
        float green = a1;
        float blue = a2;
        float alpha = a3;
        cache.ObserveExternalBlendColor(red, green, blue, alpha);
    }

    /// <summary>Classifies the represented field from native arguments and established context facts.</summary>
    internal static OpenGLObservedEffect ClassifyBlendColor(float a0, float a1, float a2, float a3)
    {
        float red = a0;
        float green = a1;
        float blue = a2;
        float alpha = a3;
        if (float.IsFinite(red) && float.IsFinite(green) && float.IsFinite(blue) && float.IsFinite(alpha)) return OpenGLObservedEffect.Changed;
        if (!float.IsFinite(red) || !float.IsFinite(green) || !float.IsFinite(blue) || !float.IsFinite(alpha)) return OpenGLObservedEffect.Uncertain;
        return OpenGLObservedEffect.Rejected;
    }

    /// <summary>Withdraws only this operation's affected knowledge after uncertainty or delivery failure.</summary>
    internal static void WithdrawBlendColor(OpenGlStateCache cache, float a0, float a1, float a2, float a3)
    {
        cache.ForgetExternalStateMutation(dynamic: DynamicDrawStateKnowledge.BlendConstant);
    }
    #endregion
}
