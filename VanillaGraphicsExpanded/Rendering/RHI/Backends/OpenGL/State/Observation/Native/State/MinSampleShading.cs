using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes established glMinSampleShading effects through generated originating-context delivery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Assigns the domain value after generated classification and targeted withdrawal.</summary>
    [InterceptOpenGL("glMinSampleShading", Withdraw = nameof(WithdrawMinSampleShading), Classify = nameof(ClassifyMinSampleShading))]
    internal static void ObserveMinSampleShading(OpenGlStateCache cache, float a0)
    {
        float value = a0;
        cache.ObserveExternalMinSampleShading(value);
    }

    /// <summary>Classifies the represented field from native arguments and established context facts.</summary>
    internal static OpenGLObservedEffect ClassifyMinSampleShading(float a0)
    {
        float value = a0;
        if (float.IsFinite(value)) return OpenGLObservedEffect.Changed;
        if (!float.IsFinite(value)) return OpenGLObservedEffect.Uncertain;
        return OpenGLObservedEffect.Rejected;
    }

    /// <summary>Withdraws only this operation's affected knowledge after uncertainty or delivery failure.</summary>
    internal static void WithdrawMinSampleShading(OpenGlStateCache cache, float a0)
    {
        cache.ForgetExternalStateMutation(sampling: SamplingStateKnowledge.MinimumSampleShading);
    }
    #endregion
}
