using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes equivalent native entrypoint effects after driver dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTextureParameterIivEXT")]
    [InterceptOpenGL("glTextureParameterivEXT")]
    internal static void ObserveTextureParameterIivEXT(uint a0, int a1, int a2, int* a3)
    {
        int @texture = unchecked((int)a0);
        TextureTarget @target = unchecked((TextureTarget)a1);
        TextureParameterName @pname = unchecked((TextureParameterName)a2);
        int* @params = unchecked((int*)a3);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@params != null ? @params[0] : (int?)null) : (int?)null);
    }
    #endregion
}
