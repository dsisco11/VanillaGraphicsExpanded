using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.TextureStorage1D overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorage1D(int @texture, All @target, int @levels, ExtDirectStateAccess @internalformat, int @width)
        => StateCache.Current.ExecuteExtTextureStorage1D(@texture, @target, @levels, @internalformat, @width);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorage1D(int @texture, All @target, int @levels, SizedInternalFormat @internalformat, int @width)
        => StateCache.Current.ExecuteExtTextureStorage1D(@texture, @target, @levels, @internalformat, @width);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorage1D(int @texture, ExtDirectStateAccess @target, int @levels, ExtDirectStateAccess @internalformat, int @width)
        => StateCache.Current.ExecuteExtTextureStorage1D(@texture, @target, @levels, @internalformat, @width);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorage1D(int @texture, ExtDirectStateAccess @target, int @levels, InternalFormat @internalformat, int @width)
        => StateCache.Current.ExecuteExtTextureStorage1D(@texture, @target, @levels, @internalformat, @width);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorage1D(int @texture, ExtDirectStateAccess @target, int @levels, SizedInternalFormat @internalformat, int @width)
        => StateCache.Current.ExecuteExtTextureStorage1D(@texture, @target, @levels, @internalformat, @width);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorage1D(uint @texture, All @target, int @levels, ExtDirectStateAccess @internalformat, int @width)
        => StateCache.Current.ExecuteExtTextureStorage1D(@texture, @target, @levels, @internalformat, @width);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorage1D(uint @texture, All @target, int @levels, SizedInternalFormat @internalformat, int @width)
        => StateCache.Current.ExecuteExtTextureStorage1D(@texture, @target, @levels, @internalformat, @width);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorage1D(uint @texture, ExtDirectStateAccess @target, int @levels, ExtDirectStateAccess @internalformat, int @width)
        => StateCache.Current.ExecuteExtTextureStorage1D(@texture, @target, @levels, @internalformat, @width);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorage1D(uint @texture, ExtDirectStateAccess @target, int @levels, InternalFormat @internalformat, int @width)
        => StateCache.Current.ExecuteExtTextureStorage1D(@texture, @target, @levels, @internalformat, @width);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorage1D(uint @texture, ExtDirectStateAccess @target, int @levels, SizedInternalFormat @internalformat, int @width)
        => StateCache.Current.ExecuteExtTextureStorage1D(@texture, @target, @levels, @internalformat, @width);

    #endregion
}
