using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferTextures;

/// <summary>Forwards GL.TextureBuffer overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureBuffer(int @texture, InternalFormat @internalformat, int @buffer)
        => StateCache.Current.ExecuteCoreTextureBuffer(@texture, @internalformat, @buffer);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureBuffer(int @texture, SizedInternalFormat @internalformat, int @buffer)
        => StateCache.Current.ExecuteCoreTextureBuffer(@texture, @internalformat, @buffer);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureBuffer(uint @texture, InternalFormat @internalformat, uint @buffer)
        => StateCache.Current.ExecuteCoreTextureBuffer(@texture, @internalformat, @buffer);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureBuffer(uint @texture, SizedInternalFormat @internalformat, uint @buffer)
        => StateCache.Current.ExecuteCoreTextureBuffer(@texture, @internalformat, @buffer);

    #endregion
}
