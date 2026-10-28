using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes established glStencilMask effects through generated originating-context delivery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Assigns the domain value after generated classification and targeted withdrawal.</summary>
    [InterceptOpenGL("glStencilMask", Withdraw = nameof(WithdrawStencilMask))]
    internal static void ObserveStencilMask(OpenGlStateCache cache, uint a0)
    {
        uint mask = a0;
        cache.ObserveExternalStencilMask(mask);
    }


    /// <summary>Withdraws only this operation's affected knowledge after uncertainty or delivery failure.</summary>
    internal static void WithdrawStencilMask(OpenGlStateCache cache, uint a0)
    {
        cache.ForgetExternalStateMutation(stencil: (StencilStateKnowledge.FrontStencilMask | StencilStateKnowledge.BackStencilMask));
    }
    #endregion
}
