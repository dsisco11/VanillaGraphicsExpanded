using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes equivalent native entrypoint effects after driver dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTexParameterIuiv")]
    [InterceptOpenGL("glTexParameterIuivEXT")]
    internal static void ObserveTexParameterIuiv(int a0, int a1, uint* a2)
    {
        TextureTarget @target = unchecked((TextureTarget)a0);
        TextureParameterName @pname = unchecked((TextureParameterName)a1);
        uint* @params = unchecked((uint*)a2);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveTextureMetadata(0, unchecked((int)@target), -1, false, false, OpenGlStateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname), parameterValue: @pname is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel ? (@params != null ? (@params[0] <= int.MaxValue ? (int?)@params[0] : null) : (int?)null) : (int?)null);
    }
    #endregion
}
