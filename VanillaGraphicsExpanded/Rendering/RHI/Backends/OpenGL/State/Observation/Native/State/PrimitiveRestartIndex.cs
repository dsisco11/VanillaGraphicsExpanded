using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes established glPrimitiveRestartIndex effects through generated originating-context delivery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Assigns the domain value after generated classification and targeted withdrawal.</summary>
    [InterceptOpenGL("glPrimitiveRestartIndex", Withdraw = nameof(WithdrawPrimitiveRestartIndex))]
    internal static void ObservePrimitiveRestartIndex(OpenGlStateCache cache, uint a0)
    {
        uint index = a0;
        cache.ObserveExternalPrimitiveRestartIndex(index);
    }


    /// <summary>Withdraws only this operation's affected knowledge after uncertainty or delivery failure.</summary>
    internal static void WithdrawPrimitiveRestartIndex(OpenGlStateCache cache, uint a0)
    {
        cache.ForgetExternalStateMutation(assembly: PrimitiveAssemblyStateKnowledge.RestartIndex);
    }
    #endregion
}
