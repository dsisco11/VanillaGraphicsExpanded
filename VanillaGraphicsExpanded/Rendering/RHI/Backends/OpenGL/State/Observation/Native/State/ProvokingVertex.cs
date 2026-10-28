using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes established glProvokingVertex effects through generated originating-context delivery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Assigns the domain value after generated classification and targeted withdrawal.</summary>
    [InterceptOpenGL("glProvokingVertex", Withdraw = nameof(WithdrawProvokingVertex), Classify = nameof(ClassifyProvokingVertex))]
    internal static void ObserveProvokingVertex(OpenGlStateCache cache, int a0)
    {
        ProvokingVertexMode mode = unchecked((ProvokingVertexMode)a0);
        cache.ObserveExternalProvokingVertex(mode);
    }

    /// <summary>Classifies the represented field from native arguments and established context facts.</summary>
    internal static OpenGLObservedEffect ClassifyProvokingVertex(int a0)
    {
        ProvokingVertexMode mode = unchecked((ProvokingVertexMode)a0);
        if (Enum.IsDefined(mode)) return OpenGLObservedEffect.Changed;
        return OpenGLObservedEffect.Rejected;
    }

    /// <summary>Withdraws only this operation's affected knowledge after uncertainty or delivery failure.</summary>
    internal static void WithdrawProvokingVertex(OpenGlStateCache cache, int a0)
    {
        cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.ProvokingVertex);
    }
    #endregion
}
