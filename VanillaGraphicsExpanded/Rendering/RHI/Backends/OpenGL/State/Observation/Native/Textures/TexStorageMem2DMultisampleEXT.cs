using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTexStorageMem2DMultisampleEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTexStorageMem2DMultisampleEXT")]
    internal static void ObserveTexStorageMem2DMultisampleEXT(int a0, int a1, int a2, int a3, int a4, byte a5, uint a6, ulong a7)
    {
        TextureTarget @target = unchecked((TextureTarget)a0);
        int @samples = unchecked((int)a1);
        ExtMemoryObject @internalFormat = unchecked((ExtMemoryObject)a2);
        int @width = unchecked((int)a3);
        int @height = unchecked((int)a4);
        bool @fixedSampleLocations = a5 != 0;
        int @memory = unchecked((int)a6);
        long @offset = unchecked((long)a7);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveTextureMetadata(0, unchecked((int)@target), -1, false, false, OpenGlStateCache.TextureMetadataEffect.Storage, 0);
    }
    #endregion
}
