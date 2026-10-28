using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes established glPointParameteri effects through generated originating-context delivery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Assigns the domain value after generated classification and targeted withdrawal.</summary>
    [InterceptOpenGL("glPointParameteri", Withdraw = nameof(WithdrawPointParameteri), Classify = nameof(ClassifyPointParameteri))]
    internal static void ObservePointParameteri(OpenGlStateCache cache, int a0, int a1)
    {
        PointParameterName parameter = unchecked((PointParameterName)a0);
        int value = a1;
        cache.ObserveExternalPointParameter(parameter, value);
    }

    /// <summary>Classifies the represented field from native arguments and established context facts.</summary>
    internal static OpenGLObservedEffect ClassifyPointParameteri(int a0, int a1)
    {
        PointParameterName parameter = unchecked((PointParameterName)a0);
        int value = a1;
        if (parameter == PointParameterName.PointSpriteCoordOrigin && Enum.IsDefined((PointSpriteCoordOriginParameter)value)) return OpenGLObservedEffect.Changed;
        return OpenGLObservedEffect.Rejected;
    }

    /// <summary>Withdraws only this operation's affected knowledge after uncertainty or delivery failure.</summary>
    internal static void WithdrawPointParameteri(OpenGlStateCache cache, int a0, int a1)
    {
        cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.PointSpriteOrigin);
    }
    #endregion
}
