using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes established glDepthRange effects through generated originating-context delivery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Assigns the domain value after generated classification and targeted withdrawal.</summary>
    [InterceptOpenGL("glDepthRange", Withdraw = nameof(WithdrawDepthRange), Classify = nameof(ClassifyDepthRange))]
    internal static void ObserveDepthRange(OpenGlStateCache cache, double a0, double a1)
    {
        double near = a0;
        double far = a1;
        cache.ObserveExternalDepthRange(near, far);
    }

    /// <summary>Classifies the represented field from native arguments and established context facts.</summary>
    internal static OpenGLObservedEffect ClassifyDepthRange(double a0, double a1)
    {
        double near = a0;
        double far = a1;
        if (double.IsFinite(near) && double.IsFinite(far)) return OpenGLObservedEffect.Changed;
        if (!double.IsFinite(near) || !double.IsFinite(far)) return OpenGLObservedEffect.Uncertain;
        return OpenGLObservedEffect.Rejected;
    }

    /// <summary>Withdraws only this operation's affected knowledge after uncertainty or delivery failure.</summary>
    internal static void WithdrawDepthRange(OpenGlStateCache cache, double a0, double a1)
    {
        cache.ForgetExternalStateMutation(depth: DepthStateKnowledge.DepthRange);
    }
    #endregion
}
