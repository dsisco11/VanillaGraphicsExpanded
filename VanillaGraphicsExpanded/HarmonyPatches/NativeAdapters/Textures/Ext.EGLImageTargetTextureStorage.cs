using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.EGLImageTargetTextureStorage overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void EGLImageTargetTextureStorage(int @texture, IntPtr @image, ref int @attrib_list)
        => StateCache.Current.ExecuteExtEGLImageTargetTextureStorage(@texture, @image, ref @attrib_list);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void EGLImageTargetTextureStorage(int @texture, IntPtr @image, int[] @attrib_list)
        => StateCache.Current.ExecuteExtEGLImageTargetTextureStorage(@texture, @image, @attrib_list);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void EGLImageTargetTextureStorage(int @texture, IntPtr @image, int* @attrib_list)
        => StateCache.Current.ExecuteExtEGLImageTargetTextureStorage(@texture, @image, @attrib_list);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void EGLImageTargetTextureStorage(uint @texture, IntPtr @image, ref int @attrib_list)
        => StateCache.Current.ExecuteExtEGLImageTargetTextureStorage(@texture, @image, ref @attrib_list);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void EGLImageTargetTextureStorage(uint @texture, IntPtr @image, int[] @attrib_list)
        => StateCache.Current.ExecuteExtEGLImageTargetTextureStorage(@texture, @image, @attrib_list);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void EGLImageTargetTextureStorage(uint @texture, IntPtr @image, int* @attrib_list)
        => StateCache.Current.ExecuteExtEGLImageTargetTextureStorage(@texture, @image, @attrib_list);

    #endregion
}
