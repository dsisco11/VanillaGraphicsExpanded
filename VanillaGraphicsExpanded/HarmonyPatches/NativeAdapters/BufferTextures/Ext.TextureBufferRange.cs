using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferTextures;

/// <summary>Forwards GL.Ext.TextureBufferRange overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureBufferRange(int @texture, TextureTarget @target, ExtDirectStateAccess @internalformat, int @buffer, IntPtr @offset, int @size)
        => OpenGLBufferTexturesCalls.ExecuteExtTextureBufferRange(@texture, @target, @internalformat, @buffer, @offset, @size);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureBufferRange(int @texture, TextureTarget @target, ExtDirectStateAccess @internalformat, int @buffer, IntPtr @offset, IntPtr @size)
        => OpenGLBufferTexturesCalls.ExecuteExtTextureBufferRange(@texture, @target, @internalformat, @buffer, @offset, @size);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureBufferRange(int @texture, TextureTarget @target, InternalFormat @internalformat, int @buffer, IntPtr @offset, int @size)
        => OpenGLBufferTexturesCalls.ExecuteExtTextureBufferRange(@texture, @target, @internalformat, @buffer, @offset, @size);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureBufferRange(int @texture, TextureTarget @target, InternalFormat @internalformat, int @buffer, IntPtr @offset, IntPtr @size)
        => OpenGLBufferTexturesCalls.ExecuteExtTextureBufferRange(@texture, @target, @internalformat, @buffer, @offset, @size);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureBufferRange(int @texture, TextureTarget @target, SizedInternalFormat @internalformat, int @buffer, IntPtr @offset, int @size)
        => OpenGLBufferTexturesCalls.ExecuteExtTextureBufferRange(@texture, @target, @internalformat, @buffer, @offset, @size);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureBufferRange(int @texture, TextureTarget @target, SizedInternalFormat @internalformat, int @buffer, IntPtr @offset, IntPtr @size)
        => OpenGLBufferTexturesCalls.ExecuteExtTextureBufferRange(@texture, @target, @internalformat, @buffer, @offset, @size);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureBufferRange(uint @texture, TextureTarget @target, ExtDirectStateAccess @internalformat, uint @buffer, IntPtr @offset, int @size)
        => OpenGLBufferTexturesCalls.ExecuteExtTextureBufferRange(@texture, @target, @internalformat, @buffer, @offset, @size);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureBufferRange(uint @texture, TextureTarget @target, ExtDirectStateAccess @internalformat, uint @buffer, IntPtr @offset, IntPtr @size)
        => OpenGLBufferTexturesCalls.ExecuteExtTextureBufferRange(@texture, @target, @internalformat, @buffer, @offset, @size);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureBufferRange(uint @texture, TextureTarget @target, InternalFormat @internalformat, uint @buffer, IntPtr @offset, int @size)
        => OpenGLBufferTexturesCalls.ExecuteExtTextureBufferRange(@texture, @target, @internalformat, @buffer, @offset, @size);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureBufferRange(uint @texture, TextureTarget @target, InternalFormat @internalformat, uint @buffer, IntPtr @offset, IntPtr @size)
        => OpenGLBufferTexturesCalls.ExecuteExtTextureBufferRange(@texture, @target, @internalformat, @buffer, @offset, @size);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureBufferRange(uint @texture, TextureTarget @target, SizedInternalFormat @internalformat, uint @buffer, IntPtr @offset, int @size)
        => OpenGLBufferTexturesCalls.ExecuteExtTextureBufferRange(@texture, @target, @internalformat, @buffer, @offset, @size);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureBufferRange(uint @texture, TextureTarget @target, SizedInternalFormat @internalformat, uint @buffer, IntPtr @offset, IntPtr @size)
        => OpenGLBufferTexturesCalls.ExecuteExtTextureBufferRange(@texture, @target, @internalformat, @buffer, @offset, @size);

    #endregion
}
