using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes established glSampleCoverage effects through generated originating-context delivery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Assigns the domain value after generated classification and targeted withdrawal.</summary>
    [InterceptOpenGL("glSampleCoverage", Withdraw = nameof(WithdrawSampleCoverage), Classify = nameof(ClassifySampleCoverage))]
    internal static void ObserveSampleCoverage(OpenGlStateCache cache, float a0, byte a1)
    {
        float value = a0;
        bool invert = a1 != 0;
        cache.ObserveExternalSampleCoverage(value, invert);
    }

    /// <summary>Classifies the represented field from native arguments and established context facts.</summary>
    internal static OpenGLObservedEffect ClassifySampleCoverage(float a0, byte a1)
    {
        float value = a0;
        if (float.IsFinite(value)) return OpenGLObservedEffect.Changed;
        if (!float.IsFinite(value)) return OpenGLObservedEffect.Uncertain;
        return OpenGLObservedEffect.Rejected;
    }

    /// <summary>Withdraws only this operation's affected knowledge after uncertainty or delivery failure.</summary>
    internal static void WithdrawSampleCoverage(OpenGlStateCache cache, float a0, byte a1)
    {
        cache.ForgetExternalStateMutation(sampling: SamplingStateKnowledge.SampleCoverage);
    }
    #endregion
}
