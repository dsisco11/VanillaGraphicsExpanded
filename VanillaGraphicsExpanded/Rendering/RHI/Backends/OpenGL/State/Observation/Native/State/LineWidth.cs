using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes established glLineWidth effects through generated originating-context delivery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Assigns the domain value after generated classification and targeted withdrawal.</summary>
    [InterceptOpenGL("glLineWidth", Withdraw = nameof(WithdrawLineWidth), Classify = nameof(ClassifyLineWidth))]
    internal static void ObserveLineWidth(OpenGlStateCache cache, float width)
    {
        cache.ObserveExternalLineWidth(width);
    }

    /// <summary>Uses captured profile rules to preserve known width after a native rejection.</summary>
    internal static OpenGLObservedEffect ClassifyLineWidth(float width)
    {
        if (width <= 0) return OpenGLObservedEffect.Rejected;
        if (!float.IsFinite(width)) return OpenGLObservedEffect.Uncertain;
        if (width <= 1) return OpenGLObservedEffect.Changed;
        if (!GpuSupport.HasCachedCapabilitiesForCurrentContext) return OpenGLObservedEffect.Uncertain;
        return GpuSupport.IsForwardCompatibleContext ? OpenGLObservedEffect.Rejected : OpenGLObservedEffect.Changed;
    }

    /// <summary>Withdraws only this operation's affected knowledge after uncertainty or delivery failure.</summary>
    internal static void WithdrawLineWidth(OpenGlStateCache cache, float width)
    {
        cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.LineWidth);
    }
    #endregion
}
