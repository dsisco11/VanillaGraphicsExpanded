using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.CompressedTexImage1D overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTexImage1D(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, IntPtr @data)
        => OpenGLTexturesCalls.ExecuteCoreCompressedTexImage1D(@target, @level, @internalformat, @width, @border, @imageSize, @data);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTexImage1D<T6>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, ref T6 @data)
        where T6 : struct
        => OpenGLTexturesCalls.ExecuteCoreCompressedTexImage1D<T6>(@target, @level, @internalformat, @width, @border, @imageSize, ref @data);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTexImage1D<T6>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, T6[,,] @data)
        where T6 : struct
        => OpenGLTexturesCalls.ExecuteCoreCompressedTexImage1D<T6>(@target, @level, @internalformat, @width, @border, @imageSize, @data);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTexImage1D<T6>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, T6[,] @data)
        where T6 : struct
        => OpenGLTexturesCalls.ExecuteCoreCompressedTexImage1D<T6>(@target, @level, @internalformat, @width, @border, @imageSize, @data);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void CompressedTexImage1D<T6>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, T6[] @data)
        where T6 : struct
        => OpenGLTexturesCalls.ExecuteCoreCompressedTexImage1D<T6>(@target, @level, @internalformat, @width, @border, @imageSize, @data);

    #endregion
}
