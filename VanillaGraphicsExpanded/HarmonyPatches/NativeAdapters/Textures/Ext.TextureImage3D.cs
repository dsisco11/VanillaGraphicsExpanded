using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.TextureImage3D overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage3D(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
        => OpenGLTexturesCalls.ExecuteExtTextureImage3D(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage3D(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
        => OpenGLTexturesCalls.ExecuteExtTextureImage3D(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage3D(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
        => OpenGLTexturesCalls.ExecuteExtTextureImage3D(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage3D(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, IntPtr @pixels)
        => OpenGLTexturesCalls.ExecuteExtTextureImage3D(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage3D<T10>(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, ref T10 @pixels)
        where T10 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, ref @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage3D<T10>(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T10[,,] @pixels)
        where T10 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage3D<T10>(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T10[,] @pixels)
        where T10 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage3D<T10>(int @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T10[] @pixels)
        where T10 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage3D<T10>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, ref T10 @pixels)
        where T10 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, ref @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage3D<T10>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T10[,,] @pixels)
        where T10 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage3D<T10>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T10[,] @pixels)
        where T10 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage3D<T10>(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T10[] @pixels)
        where T10 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage3D<T10>(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, ref T10 @pixels)
        where T10 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, ref @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage3D<T10>(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T10[,,] @pixels)
        where T10 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage3D<T10>(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T10[,] @pixels)
        where T10 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage3D<T10>(uint @texture, TextureTarget @target, int @level, ExtDirectStateAccess @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T10[] @pixels)
        where T10 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage3D<T10>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, ref T10 @pixels)
        where T10 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, ref @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage3D<T10>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T10[,,] @pixels)
        where T10 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage3D<T10>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T10[,] @pixels)
        where T10 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureImage3D<T10>(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @height, int @depth, int @border, PixelFormat @format, PixelType @type, T10[] @pixels)
        where T10 : struct
        => OpenGLTexturesCalls.ExecuteExtTextureImage3D<T10>(@texture, @target, @level, @internalformat, @width, @height, @depth, @border, @format, @type, @pixels);

    #endregion
}
