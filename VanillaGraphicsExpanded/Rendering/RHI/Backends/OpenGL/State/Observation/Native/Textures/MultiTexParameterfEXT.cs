using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glMultiTexParameterfEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glMultiTexParameterfEXT")]
    internal static void ObserveMultiTexParameterfEXT(int a0, int a1, int a2, float a3)
    {
        TextureUnit @texunit = unchecked((TextureUnit)a0);
        TextureTarget @target = unchecked((TextureTarget)a1);
        TextureParameterName @pname = unchecked((TextureParameterName)a2);
        float @param = unchecked((float)a3);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveTextureMetadata(0, unchecked((int)@target), unchecked((int)@texunit), false, true, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (OpenGLTexturesCalls.ConvertTextureParameter(@param)) : (int?)null);
    }
    #endregion
}
