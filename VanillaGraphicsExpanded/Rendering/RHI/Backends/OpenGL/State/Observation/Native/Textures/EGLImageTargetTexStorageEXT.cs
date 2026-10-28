using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glEGLImageTargetTexStorageEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glEGLImageTargetTexStorageEXT")]
    internal static void ObserveEGLImageTargetTexStorageEXT(int a0, nint a1, int* a2)
    {
        ExtEglImageStorage @target = unchecked((ExtEglImageStorage)a0);
        IntPtr @image = unchecked((IntPtr)a1);
        int* @attrib_list = unchecked((int*)a2);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveTextureMetadata(0, unchecked((int)@target), -1, false, false, OpenGlStateCache.TextureMetadataEffect.Storage, 0);
    }
    #endregion
}
