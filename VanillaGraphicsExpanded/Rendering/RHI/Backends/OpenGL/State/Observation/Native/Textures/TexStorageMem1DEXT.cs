using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTexStorageMem1DEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTexStorageMem1DEXT")]
    internal static void ObserveTexStorageMem1DEXT(int a0, int a1, int a2, int a3, uint a4, ulong a5)
    {
        TextureTarget @target = unchecked((TextureTarget)a0);
        int @levels = unchecked((int)a1);
        ExtMemoryObject @internalFormat = unchecked((ExtMemoryObject)a2);
        int @width = unchecked((int)a3);
        int @memory = unchecked((int)a4);
        long @offset = unchecked((long)a5);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveTextureMetadata(0, unchecked((int)@target), -1, false, false, OpenGlStateCache.TextureMetadataEffect.Storage, 0);
    }
    #endregion
}
