using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.CompressedTexImage3D overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTexImage3D(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, IntPtr @data)
        => OpenGLTexturesCalls.ExecuteCoreCompressedTexImage3D(@target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, @data);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTexImage3D<T8>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, ref T8 @data)
        where T8 : struct
        => OpenGLTexturesCalls.ExecuteCoreCompressedTexImage3D<T8>(@target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, ref @data);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTexImage3D<T8>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, T8[,,] @data)
        where T8 : struct
        => OpenGLTexturesCalls.ExecuteCoreCompressedTexImage3D<T8>(@target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, @data);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTexImage3D<T8>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, T8[,] @data)
        where T8 : struct
        => OpenGLTexturesCalls.ExecuteCoreCompressedTexImage3D<T8>(@target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, @data);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTexImage3D<T8>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, int @imageSize, T8[] @data)
        where T8 : struct
        => OpenGLTexturesCalls.ExecuteCoreCompressedTexImage3D<T8>(@target, @level, @internalformat, @width, @height, @depth, @border, @imageSize, @data);

    #endregion
}
