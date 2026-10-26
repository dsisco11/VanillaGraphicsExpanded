using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferTextures;

/// <summary>Forwards GL.TexBufferRange overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexBufferRange(TextureBufferTarget @target, SizedInternalFormat @internalformat, int @buffer, IntPtr @offset, int @size)
        => OpenGLBufferTexturesCalls.ExecuteCoreTexBufferRange(@target, @internalformat, @buffer, @offset, @size);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexBufferRange(TextureBufferTarget @target, SizedInternalFormat @internalformat, int @buffer, IntPtr @offset, IntPtr @size)
        => OpenGLBufferTexturesCalls.ExecuteCoreTexBufferRange(@target, @internalformat, @buffer, @offset, @size);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexBufferRange(TextureBufferTarget @target, SizedInternalFormat @internalformat, uint @buffer, IntPtr @offset, int @size)
        => OpenGLBufferTexturesCalls.ExecuteCoreTexBufferRange(@target, @internalformat, @buffer, @offset, @size);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexBufferRange(TextureBufferTarget @target, SizedInternalFormat @internalformat, uint @buffer, IntPtr @offset, IntPtr @size)
        => OpenGLBufferTexturesCalls.ExecuteCoreTexBufferRange(@target, @internalformat, @buffer, @offset, @size);

    #endregion
}
