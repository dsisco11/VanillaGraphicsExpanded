using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTextureParameterIuiv after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTextureParameterIuiv")]
    internal static void ObserveTextureParameterIuiv(uint a0, int a1, uint* a2)
    {
        uint @texture = unchecked((uint)a0);
        TextureParameterName @pname = unchecked((TextureParameterName)a1);
        uint* @params = unchecked((uint*)a2);
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        OpenGlStateCache.ObserveTextureMetadata(unchecked((int)@texture), 0, -1, true, false, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@params != null ? (@params[0] <= int.MaxValue ? (int?)@params[0] : null) : (int?)null) : (int?)null);
    }
    #endregion
}
