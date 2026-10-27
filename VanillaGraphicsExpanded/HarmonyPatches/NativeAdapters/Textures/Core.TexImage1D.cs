using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.TexImage1D overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexImage1D(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
        => OpenGLTexturesCalls.ExecuteCoreTexImage1D(@target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexImage1D<T7>(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, ref T7 @pixels)
        where T7 : struct
        => OpenGLTexturesCalls.ExecuteCoreTexImage1D<T7>(@target, @level, @internalformat, @width, @border, @format, @type, ref @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexImage1D<T7>(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T7[,,] @pixels)
        where T7 : struct
        => OpenGLTexturesCalls.ExecuteCoreTexImage1D<T7>(@target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexImage1D<T7>(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T7[,] @pixels)
        where T7 : struct
        => OpenGLTexturesCalls.ExecuteCoreTexImage1D<T7>(@target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexImage1D<T7>(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @border, PixelFormat @format, PixelType @type, T7[] @pixels)
        where T7 : struct
        => OpenGLTexturesCalls.ExecuteCoreTexImage1D<T7>(@target, @level, @internalformat, @width, @border, @format, @type, @pixels);

    #endregion
}
