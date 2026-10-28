using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes established glClipControl effects through generated originating-context delivery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Assigns the domain value after generated classification and targeted withdrawal.</summary>
    [InterceptOpenGL("glClipControl", Withdraw = nameof(WithdrawClipControl), Classify = nameof(ClassifyClipControl))]
    internal static void ObserveClipControl(OpenGlStateCache cache, int a0, int a1)
    {
        ClipOrigin origin = unchecked((ClipOrigin)a0);
        ClipDepthMode depth = unchecked((ClipDepthMode)a1);
        cache.ObserveExternalClipControl(origin, depth);
    }

    /// <summary>Classifies the represented field from native arguments and established context facts.</summary>
    internal static OpenGLObservedEffect ClassifyClipControl(int a0, int a1)
    {
        ClipOrigin origin = unchecked((ClipOrigin)a0);
        ClipDepthMode depth = unchecked((ClipDepthMode)a1);
        if (Enum.IsDefined(origin) && Enum.IsDefined(depth)) return OpenGLObservedEffect.Changed;
        return OpenGLObservedEffect.Rejected;
    }

    /// <summary>Withdraws only this operation's affected knowledge after uncertainty or delivery failure.</summary>
    internal static void WithdrawClipControl(OpenGlStateCache cache, int a0, int a1)
    {
        cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.ClipControl);
    }
    #endregion
}
