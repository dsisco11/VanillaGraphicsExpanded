using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes established glColorMask effects through generated originating-context delivery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Assigns the domain value after generated classification and targeted withdrawal.</summary>
    [InterceptOpenGL("glColorMask", Withdraw = nameof(WithdrawColorMask))]
    internal static void ObserveColorMask(OpenGlStateCache cache, byte a0, byte a1, byte a2, byte a3)
    {
        bool red = a0 != 0;
        bool green = a1 != 0;
        bool blue = a2 != 0;
        bool alpha = a3 != 0;
        cache.ObserveExternalColorMask(red, green, blue, alpha);
    }


    /// <summary>Withdraws only this operation's affected knowledge after uncertainty or delivery failure.</summary>
    internal static void WithdrawColorMask(OpenGlStateCache cache, byte a0, byte a1, byte a2, byte a3)
    {
        cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.WriteMask);
    }
    #endregion
}
