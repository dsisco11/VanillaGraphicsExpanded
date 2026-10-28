using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes established glFrontFace effects through generated originating-context delivery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Assigns the domain value after generated classification and targeted withdrawal.</summary>
    [InterceptOpenGL("glFrontFace", Withdraw = nameof(WithdrawFrontFace), Classify = nameof(ClassifyFrontFace))]
    internal static void ObserveFrontFace(OpenGlStateCache cache, int a0)
    {
        FrontFaceDirection direction = unchecked((FrontFaceDirection)a0);
        cache.ObserveExternalFrontFace(direction);
    }

    /// <summary>Classifies the represented field from native arguments and established context facts.</summary>
    internal static OpenGLObservedEffect ClassifyFrontFace(int a0)
    {
        FrontFaceDirection direction = unchecked((FrontFaceDirection)a0);
        if (Enum.IsDefined(direction)) return OpenGLObservedEffect.Changed;
        return OpenGLObservedEffect.Rejected;
    }

    /// <summary>Withdraws only this operation's affected knowledge after uncertainty or delivery failure.</summary>
    internal static void WithdrawFrontFace(OpenGlStateCache cache, int a0)
    {
        cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.FrontFace);
    }
    #endregion
}
