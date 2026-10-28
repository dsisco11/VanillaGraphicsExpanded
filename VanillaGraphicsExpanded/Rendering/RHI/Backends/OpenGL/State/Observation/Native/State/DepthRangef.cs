using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes established glDepthRangef effects through generated originating-context delivery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Assigns the domain value after generated classification and targeted withdrawal.</summary>
    [InterceptOpenGL("glDepthRangef", Withdraw = nameof(WithdrawDepthRangef), Classify = nameof(ClassifyDepthRangef))]
    internal static void ObserveDepthRangef(OpenGlStateCache cache, float a0, float a1)
    {
        float near = a0;
        float far = a1;
        cache.ObserveExternalDepthRange(near, far);
    }

    /// <summary>Classifies the represented field from native arguments and established context facts.</summary>
    internal static OpenGLObservedEffect ClassifyDepthRangef(float a0, float a1)
    {
        float near = a0;
        float far = a1;
        if (double.IsFinite(near) && double.IsFinite(far)) return OpenGLObservedEffect.Changed;
        if (!float.IsFinite(near) || !float.IsFinite(far)) return OpenGLObservedEffect.Uncertain;
        return OpenGLObservedEffect.Rejected;
    }

    /// <summary>Withdraws only this operation's affected knowledge after uncertainty or delivery failure.</summary>
    internal static void WithdrawDepthRangef(OpenGlStateCache cache, float a0, float a1)
    {
        cache.ForgetExternalStateMutation(depth: DepthStateKnowledge.DepthRange);
    }
    #endregion
}
