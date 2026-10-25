using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Arb.CompressedTexImage3D overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Arb
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void CompressedTexImage3D(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, IntPtr @data)
        => StateCache.Current.ExecuteArbCompressedTexImage3D(@target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, @data);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void CompressedTexImage3D<T8>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, ref T8 @data)
        where T8 : struct
        => StateCache.Current.ExecuteArbCompressedTexImage3D<T8>(@target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, ref @data);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void CompressedTexImage3D<T8>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, T8[,,] @data)
        where T8 : struct
        => StateCache.Current.ExecuteArbCompressedTexImage3D<T8>(@target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, @data);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void CompressedTexImage3D<T8>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, T8[,] @data)
        where T8 : struct
        => StateCache.Current.ExecuteArbCompressedTexImage3D<T8>(@target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, @data);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void CompressedTexImage3D<T8>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, T8[] @data)
        where T8 : struct
        => StateCache.Current.ExecuteArbCompressedTexImage3D<T8>(@target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, @data);

    #endregion
}
