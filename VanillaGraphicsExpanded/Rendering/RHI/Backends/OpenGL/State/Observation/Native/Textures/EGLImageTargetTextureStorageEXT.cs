using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glEGLImageTargetTextureStorageEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glEGLImageTargetTextureStorageEXT")]
    internal static void ObserveEGLImageTargetTextureStorageEXT(uint a0, nint a1, int* a2)
    {
        int @texture = unchecked((int)a0);
        IntPtr @image = unchecked((IntPtr)a1);
        int* @attrib_list = unchecked((int*)a2);
        if (image == IntPtr.Zero || !OpenGLObservationContext.TryGet(out _, out _, false)) return;
        OpenGlStateCache.ObserveTextureMetadata(unchecked((int)@texture), 0, -1, true, false, OpenGlStateCache.TextureMetadataEffect.Storage, 0);
    }
    #endregion
}
