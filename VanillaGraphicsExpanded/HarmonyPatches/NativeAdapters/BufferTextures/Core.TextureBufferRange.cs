using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferTextures;

/// <summary>Forwards GL.TextureBufferRange overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureBufferRange(int @texture, InternalFormat @internalformat, int @buffer, IntPtr @offset, int @size)
        => StateCache.Current.ExecuteCoreTextureBufferRange(@texture, @internalformat, @buffer, @offset, @size);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureBufferRange(int @texture, InternalFormat @internalformat, int @buffer, IntPtr @offset, IntPtr @size)
        => StateCache.Current.ExecuteCoreTextureBufferRange(@texture, @internalformat, @buffer, @offset, @size);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureBufferRange(int @texture, SizedInternalFormat @internalformat, int @buffer, IntPtr @offset, int @size)
        => StateCache.Current.ExecuteCoreTextureBufferRange(@texture, @internalformat, @buffer, @offset, @size);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureBufferRange(int @texture, SizedInternalFormat @internalformat, int @buffer, IntPtr @offset, IntPtr @size)
        => StateCache.Current.ExecuteCoreTextureBufferRange(@texture, @internalformat, @buffer, @offset, @size);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureBufferRange(uint @texture, InternalFormat @internalformat, uint @buffer, IntPtr @offset, int @size)
        => StateCache.Current.ExecuteCoreTextureBufferRange(@texture, @internalformat, @buffer, @offset, @size);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureBufferRange(uint @texture, InternalFormat @internalformat, uint @buffer, IntPtr @offset, IntPtr @size)
        => StateCache.Current.ExecuteCoreTextureBufferRange(@texture, @internalformat, @buffer, @offset, @size);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureBufferRange(uint @texture, SizedInternalFormat @internalformat, uint @buffer, IntPtr @offset, int @size)
        => StateCache.Current.ExecuteCoreTextureBufferRange(@texture, @internalformat, @buffer, @offset, @size);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureBufferRange(uint @texture, SizedInternalFormat @internalformat, uint @buffer, IntPtr @offset, IntPtr @size)
        => StateCache.Current.ExecuteCoreTextureBufferRange(@texture, @internalformat, @buffer, @offset, @size);

    #endregion
}
