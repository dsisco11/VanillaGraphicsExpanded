using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes established glCullFace effects through generated originating-context delivery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Assigns the domain value after generated classification and targeted withdrawal.</summary>
    [InterceptOpenGL("glCullFace", Withdraw = nameof(WithdrawCullFace), Classify = nameof(ClassifyCullFace))]
    internal static void ObserveCullFace(OpenGlStateCache cache, int a0)
    {
        CullFaceMode mode = unchecked((CullFaceMode)a0);
        cache.ObserveExternalCullFace(mode);
    }

    /// <summary>Classifies the represented field from native arguments and established context facts.</summary>
    internal static OpenGLObservedEffect ClassifyCullFace(int a0)
    {
        CullFaceMode mode = unchecked((CullFaceMode)a0);
        if (Enum.IsDefined((CullFaceMode)mode)) return OpenGLObservedEffect.Changed;
        return OpenGLObservedEffect.Rejected;
    }

    /// <summary>Withdraws only this operation's affected knowledge after uncertainty or delivery failure.</summary>
    internal static void WithdrawCullFace(OpenGlStateCache cache, int a0)
    {
        cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.CullMode);
    }
    #endregion
}
