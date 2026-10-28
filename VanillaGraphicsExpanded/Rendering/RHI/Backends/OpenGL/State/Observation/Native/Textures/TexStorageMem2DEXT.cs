using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTexStorageMem2DEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTexStorageMem2DEXT")]
    internal static void ObserveTexStorageMem2DEXT(int a0, int a1, int a2, int a3, int a4, uint a5, ulong a6)
    {
        TextureTarget @target = unchecked((TextureTarget)a0);
        int @levels = unchecked((int)a1);
        ExtMemoryObject @internalFormat = unchecked((ExtMemoryObject)a2);
        int @width = unchecked((int)a3);
        int @height = unchecked((int)a4);
        int @memory = unchecked((int)a5);
        long @offset = unchecked((long)a6);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveTextureMetadata(0, unchecked((int)@target), -1, false, false, OpenGlStateCache.TextureMetadataEffect.Storage, 0);
    }
    #endregion
}
