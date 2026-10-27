using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.EGLImageTargetTexStorage overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void EGLImageTargetTexStorage(ExtEglImageStorage @target, IntPtr @image, ref int @attrib_list)
        => OpenGLTexturesCalls.ExecuteExtEGLImageTargetTexStorage(@target, @image, ref @attrib_list);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void EGLImageTargetTexStorage(ExtEglImageStorage @target, IntPtr @image, int[] @attrib_list)
        => OpenGLTexturesCalls.ExecuteExtEGLImageTargetTexStorage(@target, @image, @attrib_list);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void EGLImageTargetTexStorage(ExtEglImageStorage @target, IntPtr @image, int* @attrib_list)
        => OpenGLTexturesCalls.ExecuteExtEGLImageTargetTexStorage(@target, @image, @attrib_list);

    #endregion
}
