using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.TexImage3D overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexImage3D(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
        => OpenGLTexturesCalls.ExecuteCoreTexImage3D(@target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexImage3D<T9>(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, ref T9 @pixels)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteCoreTexImage3D<T9>(@target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, ref @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexImage3D<T9>(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T9[,,] @pixels)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteCoreTexImage3D<T9>(@target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexImage3D<T9>(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T9[,] @pixels)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteCoreTexImage3D<T9>(@target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexImage3D<T9>(TextureTarget @target, int @level, PixelInternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T9[] @pixels)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteCoreTexImage3D<T9>(@target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);

    #endregion
}
