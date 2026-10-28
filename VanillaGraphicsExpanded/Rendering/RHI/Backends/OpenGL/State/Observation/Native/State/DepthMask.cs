using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes established glDepthMask effects through generated originating-context delivery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Assigns the domain value after generated classification and targeted withdrawal.</summary>
    [InterceptOpenGL("glDepthMask", Withdraw = nameof(WithdrawDepthMask))]
    internal static void ObserveDepthMask(OpenGlStateCache cache, byte enabled)
    {
        cache.ObserveExternalDepthMask(enabled != 0);
    }


    /// <summary>Withdraws only this operation's affected knowledge after uncertainty or delivery failure.</summary>
    internal static void WithdrawDepthMask(OpenGlStateCache cache, byte enabled)
    {
        cache.ForgetExternalStateMutation(depth: DepthStateKnowledge.WriteEnabled);
    }
    #endregion
}
