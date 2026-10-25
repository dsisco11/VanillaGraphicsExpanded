using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.TextureStorage1D overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorage1D(int @texture, int @levels, InternalFormat @internalformat, int @width)
        => StateCache.Current.ExecuteCoreTextureStorage1D(@texture, @levels, @internalformat, @width);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorage1D(int @texture, int @levels, SizedInternalFormat @internalformat, int @width)
        => StateCache.Current.ExecuteCoreTextureStorage1D(@texture, @levels, @internalformat, @width);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorage1D(uint @texture, int @levels, InternalFormat @internalformat, int @width)
        => StateCache.Current.ExecuteCoreTextureStorage1D(@texture, @levels, @internalformat, @width);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorage1D(uint @texture, int @levels, SizedInternalFormat @internalformat, int @width)
        => StateCache.Current.ExecuteCoreTextureStorage1D(@texture, @levels, @internalformat, @width);

    #endregion
}
