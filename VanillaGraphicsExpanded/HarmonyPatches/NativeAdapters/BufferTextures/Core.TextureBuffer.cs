using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferTextures;

/// <summary>Forwards GL.TextureBuffer overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureBuffer(int @texture, InternalFormat @internalformat, int @buffer)
        => OpenGLBufferTexturesCalls.ExecuteCoreTextureBuffer(@texture, @internalformat, @buffer);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureBuffer(int @texture, SizedInternalFormat @internalformat, int @buffer)
        => OpenGLBufferTexturesCalls.ExecuteCoreTextureBuffer(@texture, @internalformat, @buffer);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureBuffer(uint @texture, InternalFormat @internalformat, uint @buffer)
        => OpenGLBufferTexturesCalls.ExecuteCoreTextureBuffer(@texture, @internalformat, @buffer);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureBuffer(uint @texture, SizedInternalFormat @internalformat, uint @buffer)
        => OpenGLBufferTexturesCalls.ExecuteCoreTextureBuffer(@texture, @internalformat, @buffer);

    #endregion
}
