using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glTextureImage1DEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glTextureImage1DEXT")]
    internal static void ObserveTextureImage1DEXT(uint a0, int a1, int a2, int a3, int a4, int a5, int a6, int a7, nint a8)
    {
        int @texture = unchecked((int)a0);
        TextureTarget @target = unchecked((TextureTarget)a1);
        int @level = unchecked((int)a2);
        ExtDirectStateAccess @internalformat = unchecked((ExtDirectStateAccess)a3);
        int @width = unchecked((int)a4);
        int @border = unchecked((int)a5);
        PixelFormat @format = unchecked((PixelFormat)a6);
        PixelType @type = unchecked((PixelType)a7);
        IntPtr @pixels = unchecked((IntPtr)a8);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveTextureMetadata(unchecked((int)@texture), unchecked((int)@target), -1, true, false, OpenGlStateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
    }
    #endregion
}
