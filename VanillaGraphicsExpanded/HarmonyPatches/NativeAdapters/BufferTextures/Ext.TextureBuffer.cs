using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferTextures;

/// <summary>Forwards GL.Ext.TextureBuffer overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureBuffer(int @texture, TextureTarget @target, ExtDirectStateAccess @internalformat, int @buffer)
        => StateCache.Current.ExecuteExtTextureBuffer(@texture, @target, @internalformat, @buffer);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureBuffer(int @texture, TextureTarget @target, InternalFormat @internalformat, int @buffer)
        => StateCache.Current.ExecuteExtTextureBuffer(@texture, @target, @internalformat, @buffer);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureBuffer(int @texture, TextureTarget @target, SizedInternalFormat @internalformat, int @buffer)
        => StateCache.Current.ExecuteExtTextureBuffer(@texture, @target, @internalformat, @buffer);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureBuffer(uint @texture, TextureTarget @target, ExtDirectStateAccess @internalformat, uint @buffer)
        => StateCache.Current.ExecuteExtTextureBuffer(@texture, @target, @internalformat, @buffer);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureBuffer(uint @texture, TextureTarget @target, InternalFormat @internalformat, uint @buffer)
        => StateCache.Current.ExecuteExtTextureBuffer(@texture, @target, @internalformat, @buffer);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureBuffer(uint @texture, TextureTarget @target, SizedInternalFormat @internalformat, uint @buffer)
        => StateCache.Current.ExecuteExtTextureBuffer(@texture, @target, @internalformat, @buffer);

    #endregion
}
