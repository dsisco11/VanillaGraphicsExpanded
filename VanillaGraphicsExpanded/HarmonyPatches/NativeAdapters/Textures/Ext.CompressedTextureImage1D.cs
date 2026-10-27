using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.CompressedTextureImage1D overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTextureImage1D(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, IntPtr @bits)
        => OpenGLTexturesCalls.ExecuteExtCompressedTextureImage1D(@texture, @target, @level, @internalformat, @width, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTextureImage1D(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, IntPtr @bits)
        => OpenGLTexturesCalls.ExecuteExtCompressedTextureImage1D(@texture, @target, @level, @internalformat, @width, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTextureImage1D<T7>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, ref T7 @bits)
        where T7 : struct
        => OpenGLTexturesCalls.ExecuteExtCompressedTextureImage1D<T7>(@texture, @target, @level, @internalformat, @width, @border, @imageSize, ref @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTextureImage1D<T7>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, T7[,,] @bits)
        where T7 : struct
        => OpenGLTexturesCalls.ExecuteExtCompressedTextureImage1D<T7>(@texture, @target, @level, @internalformat, @width, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTextureImage1D<T7>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, T7[,] @bits)
        where T7 : struct
        => OpenGLTexturesCalls.ExecuteExtCompressedTextureImage1D<T7>(@texture, @target, @level, @internalformat, @width, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTextureImage1D<T7>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, T7[] @bits)
        where T7 : struct
        => OpenGLTexturesCalls.ExecuteExtCompressedTextureImage1D<T7>(@texture, @target, @level, @internalformat, @width, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTextureImage1D<T7>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, ref T7 @bits)
        where T7 : struct
        => OpenGLTexturesCalls.ExecuteExtCompressedTextureImage1D<T7>(@texture, @target, @level, @internalformat, @width, @border, @imageSize, ref @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTextureImage1D<T7>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, T7[,,] @bits)
        where T7 : struct
        => OpenGLTexturesCalls.ExecuteExtCompressedTextureImage1D<T7>(@texture, @target, @level, @internalformat, @width, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTextureImage1D<T7>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, T7[,] @bits)
        where T7 : struct
        => OpenGLTexturesCalls.ExecuteExtCompressedTextureImage1D<T7>(@texture, @target, @level, @internalformat, @width, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTextureImage1D<T7>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, T7[] @bits)
        where T7 : struct
        => OpenGLTexturesCalls.ExecuteExtCompressedTextureImage1D<T7>(@texture, @target, @level, @internalformat, @width, @border, @imageSize, @bits);

    #endregion
}
