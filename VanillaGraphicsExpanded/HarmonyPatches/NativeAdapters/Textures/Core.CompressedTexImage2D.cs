using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.CompressedTexImage2D overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTexImage2D(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, IntPtr @data)
        => OpenGLTexturesCalls.ExecuteCoreCompressedTexImage2D(@target, @level, @internalformat, @width, @height, @border, @imageSize, @data);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTexImage2D<T7>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, ref T7 @data)
        where T7 : struct
        => OpenGLTexturesCalls.ExecuteCoreCompressedTexImage2D<T7>(@target, @level, @internalformat, @width, @height, @border, @imageSize, ref @data);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTexImage2D<T7>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, T7[,,] @data)
        where T7 : struct
        => OpenGLTexturesCalls.ExecuteCoreCompressedTexImage2D<T7>(@target, @level, @internalformat, @width, @height, @border, @imageSize, @data);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTexImage2D<T7>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, T7[,] @data)
        where T7 : struct
        => OpenGLTexturesCalls.ExecuteCoreCompressedTexImage2D<T7>(@target, @level, @internalformat, @width, @height, @border, @imageSize, @data);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTexImage2D<T7>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, int @imageSize, T7[] @data)
        where T7 : struct
        => OpenGLTexturesCalls.ExecuteCoreCompressedTexImage2D<T7>(@target, @level, @internalformat, @width, @height, @border, @imageSize, @data);

    #endregion
}
