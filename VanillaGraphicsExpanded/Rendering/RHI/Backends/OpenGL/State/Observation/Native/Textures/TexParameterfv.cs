using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTexParameterfv after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTexParameterfv")]
    internal static void ObserveTexParameterfv(int a0, int a1, float* a2)
    {
        TextureTarget @target = unchecked((TextureTarget)a0);
        TextureParameterName @pname = unchecked((TextureParameterName)a1);
        float* @params = unchecked((float*)a2);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveTextureMetadata(0, unchecked((int)@target), -1, false, false, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@params != null ? OpenGLTexturesCalls.ConvertTextureParameter(@params[0]) : (int?)null) : (int?)null);
    }
    #endregion
}
