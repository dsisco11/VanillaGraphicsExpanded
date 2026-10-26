using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.TextureStorage2D overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage2D(int @texture, All @target, int @levels, ExtDirectStateAccess @internalformat, int @width, int @height)
        => OpenGLTexturesCalls.ExecuteExtTextureStorage2D(@texture, @target, @levels, @internalformat, @width, @height);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage2D(int @texture, All @target, int @levels, SizedInternalFormat @internalformat, int @width, int @height)
        => OpenGLTexturesCalls.ExecuteExtTextureStorage2D(@texture, @target, @levels, @internalformat, @width, @height);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage2D(int @texture, ExtDirectStateAccess @target, int @levels, ExtDirectStateAccess @internalformat, int @width, int @height)
        => OpenGLTexturesCalls.ExecuteExtTextureStorage2D(@texture, @target, @levels, @internalformat, @width, @height);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage2D(int @texture, ExtDirectStateAccess @target, int @levels, InternalFormat @internalformat, int @width, int @height)
        => OpenGLTexturesCalls.ExecuteExtTextureStorage2D(@texture, @target, @levels, @internalformat, @width, @height);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage2D(int @texture, ExtDirectStateAccess @target, int @levels, SizedInternalFormat @internalformat, int @width, int @height)
        => OpenGLTexturesCalls.ExecuteExtTextureStorage2D(@texture, @target, @levels, @internalformat, @width, @height);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage2D(uint @texture, All @target, int @levels, ExtDirectStateAccess @internalformat, int @width, int @height)
        => OpenGLTexturesCalls.ExecuteExtTextureStorage2D(@texture, @target, @levels, @internalformat, @width, @height);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage2D(uint @texture, All @target, int @levels, SizedInternalFormat @internalformat, int @width, int @height)
        => OpenGLTexturesCalls.ExecuteExtTextureStorage2D(@texture, @target, @levels, @internalformat, @width, @height);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage2D(uint @texture, ExtDirectStateAccess @target, int @levels, ExtDirectStateAccess @internalformat, int @width, int @height)
        => OpenGLTexturesCalls.ExecuteExtTextureStorage2D(@texture, @target, @levels, @internalformat, @width, @height);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage2D(uint @texture, ExtDirectStateAccess @target, int @levels, InternalFormat @internalformat, int @width, int @height)
        => OpenGLTexturesCalls.ExecuteExtTextureStorage2D(@texture, @target, @levels, @internalformat, @width, @height);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage2D(uint @texture, ExtDirectStateAccess @target, int @levels, SizedInternalFormat @internalformat, int @width, int @height)
        => OpenGLTexturesCalls.ExecuteExtTextureStorage2D(@texture, @target, @levels, @internalformat, @width, @height);

    #endregion
}
