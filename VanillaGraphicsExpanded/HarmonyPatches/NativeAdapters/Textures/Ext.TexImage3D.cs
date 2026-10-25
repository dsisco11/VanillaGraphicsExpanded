using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.TexImage3D overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexImage3D(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
        => StateCache.Current.ExecuteExtTexImage3D(@target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexImage3D<T9>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, ref T9 @pixels)
        where T9 : struct
        => StateCache.Current.ExecuteExtTexImage3D<T9>(@target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, ref @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexImage3D<T9>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T9[,,] @pixels)
        where T9 : struct
        => StateCache.Current.ExecuteExtTexImage3D<T9>(@target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexImage3D<T9>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T9[,] @pixels)
        where T9 : struct
        => StateCache.Current.ExecuteExtTexImage3D<T9>(@target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexImage3D<T9>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T9[] @pixels)
        where T9 : struct
        => StateCache.Current.ExecuteExtTexImage3D<T9>(@target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);

    #endregion
}
