using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTextureStorageMem2DMultisampleEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTextureStorageMem2DMultisampleEXT")]
    internal static void ObserveTextureStorageMem2DMultisampleEXT(uint a0, int a1, int a2, int a3, int a4, byte a5, uint a6, ulong a7)
    {
        int @texture = unchecked((int)a0);
        int @samples = unchecked((int)a1);
        ExtMemoryObject @internalFormat = unchecked((ExtMemoryObject)a2);
        int @width = unchecked((int)a3);
        int @height = unchecked((int)a4);
        bool @fixedSampleLocations = a5 != 0;
        int @memory = unchecked((int)a6);
        long @offset = unchecked((long)a7);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveTextureMetadata(unchecked((int)@texture), 0, -1, true, false, OpenGlStateCache.TextureMetadataEffect.Storage, 0);
    }
    #endregion
}
