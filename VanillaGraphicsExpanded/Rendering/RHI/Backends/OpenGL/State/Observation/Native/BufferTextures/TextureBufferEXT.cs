using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTextureBufferEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTextureBufferEXT")]
    internal static void ObserveTextureBufferEXT(uint a0, int a1, int a2, uint a3)
    {
        int @texture = unchecked((int)a0);
        TextureTarget @target = unchecked((TextureTarget)a1);
        ExtDirectStateAccess @internalformat = unchecked((ExtDirectStateAccess)a2);
        int @buffer = unchecked((int)a3);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveBufferTexture(unchecked((int)@texture), unchecked((int)@target), 0, true, false, unchecked((int)@buffer), unchecked((int)@internalformat), 0, 0, true);
    }
    #endregion
}
