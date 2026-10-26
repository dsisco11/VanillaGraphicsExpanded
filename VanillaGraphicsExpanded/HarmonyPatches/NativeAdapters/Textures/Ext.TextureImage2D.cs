using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.TextureImage2D overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage2D(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
        => OpenGLTexturesCalls.ExecuteExtTextureImage2D(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage2D(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
        => OpenGLTexturesCalls.ExecuteExtTextureImage2D(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage2D(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
        => OpenGLTexturesCalls.ExecuteExtTextureImage2D(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage2D(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
        => OpenGLTexturesCalls.ExecuteExtTextureImage2D(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage2D<T9>(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, ref T9 @pixels)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, ref @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage2D<T9>(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[,,] @pixels)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage2D<T9>(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[,] @pixels)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage2D<T9>(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[] @pixels)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage2D<T9>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, ref T9 @pixels)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, ref @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage2D<T9>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[,,] @pixels)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage2D<T9>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[,] @pixels)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage2D<T9>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[] @pixels)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage2D<T9>(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, ref T9 @pixels)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, ref @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage2D<T9>(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[,,] @pixels)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage2D<T9>(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[,] @pixels)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage2D<T9>(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[] @pixels)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage2D<T9>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, ref T9 @pixels)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, ref @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage2D<T9>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[,,] @pixels)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage2D<T9>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[,] @pixels)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage2D<T9>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @border, PixelFormat @format, PixelType @type, T9[] @pixels)
        where T9 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage2D<T9>(@texture, @target, @level, @internalformat, @width, @height, @border, @format, @type, @pixels);

    #endregion
}
