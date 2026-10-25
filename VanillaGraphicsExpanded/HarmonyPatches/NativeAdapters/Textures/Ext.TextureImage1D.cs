using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.TextureImage1D overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureImage1D(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
        => StateCache.Current.ExecuteExtTextureImage1D(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureImage1D(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
        => StateCache.Current.ExecuteExtTextureImage1D(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureImage1D(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
        => StateCache.Current.ExecuteExtTextureImage1D(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureImage1D(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
        => StateCache.Current.ExecuteExtTextureImage1D(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureImage1D<T8>(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, ref T8 @pixels)
        where T8 : struct
        => StateCache.Current.ExecuteExtTextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, ref @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureImage1D<T8>(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[,,] @pixels)
        where T8 : struct
        => StateCache.Current.ExecuteExtTextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureImage1D<T8>(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[,] @pixels)
        where T8 : struct
        => StateCache.Current.ExecuteExtTextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureImage1D<T8>(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[] @pixels)
        where T8 : struct
        => StateCache.Current.ExecuteExtTextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureImage1D<T8>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, ref T8 @pixels)
        where T8 : struct
        => StateCache.Current.ExecuteExtTextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, ref @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureImage1D<T8>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[,,] @pixels)
        where T8 : struct
        => StateCache.Current.ExecuteExtTextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureImage1D<T8>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[,] @pixels)
        where T8 : struct
        => StateCache.Current.ExecuteExtTextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureImage1D<T8>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[] @pixels)
        where T8 : struct
        => StateCache.Current.ExecuteExtTextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureImage1D<T8>(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, ref T8 @pixels)
        where T8 : struct
        => StateCache.Current.ExecuteExtTextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, ref @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureImage1D<T8>(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[,,] @pixels)
        where T8 : struct
        => StateCache.Current.ExecuteExtTextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureImage1D<T8>(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[,] @pixels)
        where T8 : struct
        => StateCache.Current.ExecuteExtTextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureImage1D<T8>(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[] @pixels)
        where T8 : struct
        => StateCache.Current.ExecuteExtTextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureImage1D<T8>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, ref T8 @pixels)
        where T8 : struct
        => StateCache.Current.ExecuteExtTextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, ref @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureImage1D<T8>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[,,] @pixels)
        where T8 : struct
        => StateCache.Current.ExecuteExtTextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureImage1D<T8>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[,] @pixels)
        where T8 : struct
        => StateCache.Current.ExecuteExtTextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureImage1D<T8>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T8[] @pixels)
        where T8 : struct
        => StateCache.Current.ExecuteExtTextureImage1D<T8>(@texture, @target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    #endregion
}
