using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.CompressedTextureImage2D overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void CompressedTextureImage2D(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, IntPtr @bits)
        => StateCache.Current.ExecuteExtCompressedTextureImage2D(@texture, @target, @level, @internalformat, @width, @height, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void CompressedTextureImage2D(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, IntPtr @bits)
        => StateCache.Current.ExecuteExtCompressedTextureImage2D(@texture, @target, @level, @internalformat, @width, @height, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void CompressedTextureImage2D<T8>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, ref T8 @bits)
        where T8 : struct
        => StateCache.Current.ExecuteExtCompressedTextureImage2D<T8>(@texture, @target, @level, @internalformat, @width, @height, @border, @imageSize, ref @bits);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void CompressedTextureImage2D<T8>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, T8[,,] @bits)
        where T8 : struct
        => StateCache.Current.ExecuteExtCompressedTextureImage2D<T8>(@texture, @target, @level, @internalformat, @width, @height, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void CompressedTextureImage2D<T8>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, T8[,] @bits)
        where T8 : struct
        => StateCache.Current.ExecuteExtCompressedTextureImage2D<T8>(@texture, @target, @level, @internalformat, @width, @height, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void CompressedTextureImage2D<T8>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, T8[] @bits)
        where T8 : struct
        => StateCache.Current.ExecuteExtCompressedTextureImage2D<T8>(@texture, @target, @level, @internalformat, @width, @height, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void CompressedTextureImage2D<T8>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, ref T8 @bits)
        where T8 : struct
        => StateCache.Current.ExecuteExtCompressedTextureImage2D<T8>(@texture, @target, @level, @internalformat, @width, @height, @border, @imageSize, ref @bits);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void CompressedTextureImage2D<T8>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, T8[,,] @bits)
        where T8 : struct
        => StateCache.Current.ExecuteExtCompressedTextureImage2D<T8>(@texture, @target, @level, @internalformat, @width, @height, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void CompressedTextureImage2D<T8>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, T8[,] @bits)
        where T8 : struct
        => StateCache.Current.ExecuteExtCompressedTextureImage2D<T8>(@texture, @target, @level, @internalformat, @width, @height, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void CompressedTextureImage2D<T8>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, T8[] @bits)
        where T8 : struct
        => StateCache.Current.ExecuteExtCompressedTextureImage2D<T8>(@texture, @target, @level, @internalformat, @width, @height, @border, @imageSize, @bits);

    #endregion
}
