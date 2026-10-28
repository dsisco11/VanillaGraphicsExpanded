using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTextureParameteri after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTextureParameteri")]
    internal static void ObserveTextureParameteri(uint a0, int a1, int a2)
    {
        int @texture = unchecked((int)a0);
        TextureParameterName @pname = unchecked((TextureParameterName)a1);
        int @param = unchecked((int)a2);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveTextureMetadata(unchecked((int)@texture), 0, -1, true, false, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@param) : (int?)null);
    }
    #endregion
}
