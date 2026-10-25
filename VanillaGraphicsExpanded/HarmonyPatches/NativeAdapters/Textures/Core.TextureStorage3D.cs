using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.TextureStorage3D overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorage3D(int @texture, int @levels, InternalFormat @internalformat, int @width, int @height, int @depth)
        => StateCache.Current.ExecuteCoreTextureStorage3D(@texture, @levels, @internalformat, @width, @height, @depth);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorage3D(int @texture, int @levels, SizedInternalFormat @internalformat, int @width, int @height, int @depth)
        => StateCache.Current.ExecuteCoreTextureStorage3D(@texture, @levels, @internalformat, @width, @height, @depth);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorage3D(uint @texture, int @levels, InternalFormat @internalformat, int @width, int @height, int @depth)
        => StateCache.Current.ExecuteCoreTextureStorage3D(@texture, @levels, @internalformat, @width, @height, @depth);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorage3D(uint @texture, int @levels, SizedInternalFormat @internalformat, int @width, int @height, int @depth)
        => StateCache.Current.ExecuteCoreTextureStorage3D(@texture, @levels, @internalformat, @width, @height, @depth);

    #endregion
}
