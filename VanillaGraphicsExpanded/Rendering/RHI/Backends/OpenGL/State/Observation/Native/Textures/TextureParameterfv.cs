using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTextureParameterfv after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTextureParameterfv")]
    internal static void ObserveTextureParameterfv(uint a0, int a1, float* a2)
    {
        int @texture = unchecked((int)a0);
        TextureParameterName @pname = unchecked((TextureParameterName)a1);
        float* @param = unchecked((float*)a2);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveTextureMetadata(unchecked((int)@texture), 0, -1, true, false, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@param != null ? OpenGLTexturesCalls.ConvertTextureParameter(@param[0]) : (int?)null) : (int?)null);
    }
    #endregion
}
