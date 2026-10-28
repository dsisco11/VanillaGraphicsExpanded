using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTextureStorage3DMultisampleEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTextureStorage3DMultisampleEXT")]
    internal static void ObserveTextureStorage3DMultisampleEXT(uint a0, int a1, int a2, int a3, int a4, int a5, int a6, byte a7)
    {
        int @texture = unchecked((int)a0);
        ExtDirectStateAccess @target = unchecked((ExtDirectStateAccess)a1);
        int @samples = unchecked((int)a2);
        ExtDirectStateAccess @internalformat = unchecked((ExtDirectStateAccess)a3);
        int @width = unchecked((int)a4);
        int @height = unchecked((int)a5);
        int @depth = unchecked((int)a6);
        bool @fixedsamplelocations = a7 != 0;
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, OpenGlStateCache.TextureMetadataEffect.Storage, 0);
    }
    #endregion
}
