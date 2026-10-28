using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glMultiTexBufferEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glMultiTexBufferEXT")]
    internal static void ObserveMultiTexBufferEXT(int a0, int a1, int a2, uint a3)
    {
        TextureUnit @texunit = unchecked((TextureUnit)a0);
        TextureTarget @target = unchecked((TextureTarget)a1);
        InternalFormat @internalformat = unchecked((InternalFormat)a2);
        int @buffer = unchecked((int)a3);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveBufferTexture(0, unchecked((int)@target), unchecked((int)@texunit), false, true, unchecked((int)@buffer), unchecked((int)@internalformat), 0, 0, true);
    }
    #endregion
}
