using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.TextureStorage3D overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage3D(int @texture, All @target, int @levels, ExtDirectStateAccess @internalformat, int @width, int @height, int @depth)
        => OpenGLTexturesCalls.ExecuteExtTextureStorage3D(@texture, @target, @levels, @internalformat, @width, @height, @depth);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage3D(int @texture, All @target, int @levels, SizedInternalFormat @internalformat, int @width, int @height, int @depth)
        => OpenGLTexturesCalls.ExecuteExtTextureStorage3D(@texture, @target, @levels, @internalformat, @width, @height, @depth);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage3D(int @texture, ExtDirectStateAccess @target, int @levels, ExtDirectStateAccess @internalformat, int @width, int @height, int @depth)
        => OpenGLTexturesCalls.ExecuteExtTextureStorage3D(@texture, @target, @levels, @internalformat, @width, @height, @depth);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage3D(int @texture, ExtDirectStateAccess @target, int @levels, InternalFormat @internalformat, int @width, int @height, int @depth)
        => OpenGLTexturesCalls.ExecuteExtTextureStorage3D(@texture, @target, @levels, @internalformat, @width, @height, @depth);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage3D(int @texture, ExtDirectStateAccess @target, int @levels, SizedInternalFormat @internalformat, int @width, int @height, int @depth)
        => OpenGLTexturesCalls.ExecuteExtTextureStorage3D(@texture, @target, @levels, @internalformat, @width, @height, @depth);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage3D(uint @texture, All @target, int @levels, ExtDirectStateAccess @internalformat, int @width, int @height, int @depth)
        => OpenGLTexturesCalls.ExecuteExtTextureStorage3D(@texture, @target, @levels, @internalformat, @width, @height, @depth);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage3D(uint @texture, All @target, int @levels, SizedInternalFormat @internalformat, int @width, int @height, int @depth)
        => OpenGLTexturesCalls.ExecuteExtTextureStorage3D(@texture, @target, @levels, @internalformat, @width, @height, @depth);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage3D(uint @texture, ExtDirectStateAccess @target, int @levels, ExtDirectStateAccess @internalformat, int @width, int @height, int @depth)
        => OpenGLTexturesCalls.ExecuteExtTextureStorage3D(@texture, @target, @levels, @internalformat, @width, @height, @depth);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage3D(uint @texture, ExtDirectStateAccess @target, int @levels, InternalFormat @internalformat, int @width, int @height, int @depth)
        => OpenGLTexturesCalls.ExecuteExtTextureStorage3D(@texture, @target, @levels, @internalformat, @width, @height, @depth);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage3D(uint @texture, ExtDirectStateAccess @target, int @levels, SizedInternalFormat @internalformat, int @width, int @height, int @depth)
        => OpenGLTexturesCalls.ExecuteExtTextureStorage3D(@texture, @target, @levels, @internalformat, @width, @height, @depth);

    #endregion
}
