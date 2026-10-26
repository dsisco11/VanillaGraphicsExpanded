using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.TexImage2D overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexImage2D(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
        => OpenGLTexturesCalls.ExecuteCoreTexImage2D(@target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexImage2D<T8>(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, ref T8 @pixels)
        where T8 : struct
        => OpenGLTexturesCalls.ExecuteCoreTexImage2D<T8>(@target, @level, @internalformat, @width, @height, @border, @format, @type, ref @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexImage2D<T8>(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T8[,,] @pixels)
        where T8 : struct
        => OpenGLTexturesCalls.ExecuteCoreTexImage2D<T8>(@target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexImage2D<T8>(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T8[,] @pixels)
        where T8 : struct
        => OpenGLTexturesCalls.ExecuteCoreTexImage2D<T8>(@target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexImage2D<T8>(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T8[] @pixels)
        where T8 : struct
        => OpenGLTexturesCalls.ExecuteCoreTexImage2D<T8>(@target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);

    #endregion
}
