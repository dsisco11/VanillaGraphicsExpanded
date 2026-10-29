using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTextureStorageMem1DEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTextureStorageMem1DEXT")]
    internal static void ObserveTextureStorageMem1DEXT(uint a0, int a1, int a2, int a3, uint a4, ulong a5)
    {
        int @texture = unchecked((int)a0);
        int @levels = unchecked((int)a1);
        ExtMemoryObject @internalFormat = unchecked((ExtMemoryObject)a2);
        int @width = unchecked((int)a3);
        int @memory = unchecked((int)a4);
        long @offset = unchecked((long)a5);
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        OpenGlStateCache.ObserveTextureDefinition(unchecked((int)@texture), 0, -1, true, false, 0, unchecked((int)@internalFormat), unchecked((int)@width), 1, 1, unchecked((int)@levels), true, 0, 0, 1, false, unchecked((int)@memory), externalMemory: true, memoryOffset: unchecked((ulong)@offset));
    }
    #endregion
}
