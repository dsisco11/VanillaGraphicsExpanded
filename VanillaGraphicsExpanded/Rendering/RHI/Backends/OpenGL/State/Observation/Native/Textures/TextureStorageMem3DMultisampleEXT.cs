using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTextureStorageMem3DMultisampleEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTextureStorageMem3DMultisampleEXT")]
    internal static void ObserveTextureStorageMem3DMultisampleEXT(uint a0, int a1, int a2, int a3, int a4, int a5, byte a6, uint a7, ulong a8)
    {
        int @texture = unchecked((int)a0);
        int @samples = unchecked((int)a1);
        ExtMemoryObject @internalFormat = unchecked((ExtMemoryObject)a2);
        int @width = unchecked((int)a3);
        int @height = unchecked((int)a4);
        int @depth = unchecked((int)a5);
        bool @fixedSampleLocations = a6 != 0;
        int @memory = unchecked((int)a7);
        long @offset = unchecked((long)a8);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveTextureMetadata(unchecked((int)@texture), 0, -1, true, false, OpenGlStateCache.TextureMetadataEffect.Storage, 0);
    }
    #endregion
}
