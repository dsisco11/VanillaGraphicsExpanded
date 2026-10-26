using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.TextureStorage2D overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage2D(int @texture, int @levels, InternalFormat @internalformat, int @width, int @height)
        => OpenGLTexturesCalls.ExecuteCoreTextureStorage2D(@texture, @levels, @internalformat, @width, @height);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage2D(int @texture, int @levels, SizedInternalFormat @internalformat, int @width, int @height)
        => OpenGLTexturesCalls.ExecuteCoreTextureStorage2D(@texture, @levels, @internalformat, @width, @height);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage2D(uint @texture, int @levels, InternalFormat @internalformat, int @width, int @height)
        => OpenGLTexturesCalls.ExecuteCoreTextureStorage2D(@texture, @levels, @internalformat, @width, @height);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorage2D(uint @texture, int @levels, SizedInternalFormat @internalformat, int @width, int @height)
        => OpenGLTexturesCalls.ExecuteCoreTextureStorage2D(@texture, @levels, @internalformat, @width, @height);

    #endregion
}
