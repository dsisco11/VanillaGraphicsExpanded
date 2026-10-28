using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTextureStorageMem3DEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTextureStorageMem3DEXT")]
    internal static void ObserveTextureStorageMem3DEXT(uint a0, int a1, int a2, int a3, int a4, int a5, uint a6, ulong a7)
    {
        int @texture = unchecked((int)a0);
        int @levels = unchecked((int)a1);
        ExtMemoryObject @internalFormat = unchecked((ExtMemoryObject)a2);
        int @width = unchecked((int)a3);
        int @height = unchecked((int)a4);
        int @depth = unchecked((int)a5);
        int @memory = unchecked((int)a6);
        long @offset = unchecked((long)a7);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveTextureMetadata(unchecked((int)@texture), 0, -1, true, false, OpenGlStateCache.TextureMetadataEffect.Storage, 0);
    }
    #endregion
}
