using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.CompressedTextureImage3D overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTextureImage3D(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, IntPtr @bits)
        => OpenGLTexturesCalls.ExecuteExtCompressedTextureImage3D(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTextureImage3D(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, IntPtr @bits)
        => OpenGLTexturesCalls.ExecuteExtCompressedTextureImage3D(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTextureImage3D<T9>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, ref T9 @bits)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteExtCompressedTextureImage3D<T9>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, ref @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTextureImage3D<T9>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, T9[,,] @bits)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteExtCompressedTextureImage3D<T9>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTextureImage3D<T9>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, T9[,] @bits)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteExtCompressedTextureImage3D<T9>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTextureImage3D<T9>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, T9[] @bits)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteExtCompressedTextureImage3D<T9>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTextureImage3D<T9>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, ref T9 @bits)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteExtCompressedTextureImage3D<T9>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, ref @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTextureImage3D<T9>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, T9[,,] @bits)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteExtCompressedTextureImage3D<T9>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTextureImage3D<T9>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, T9[,] @bits)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteExtCompressedTextureImage3D<T9>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, @bits);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTextureImage3D<T9>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, T9[] @bits)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteExtCompressedTextureImage3D<T9>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, @bits);

    #endregion
}
