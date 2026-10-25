using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.TextureStorage3DMultisample overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorage3DMultisample(int @texture, int @samples, InternalFormat @internalformat, int @width, int @height, int @depth, bool @fixedsamplelocations)
        => StateCache.Current.ExecuteCoreTextureStorage3DMultisample(@texture, @samples, @internalformat, @width, @height, @depth, @fixedsamplelocations);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorage3DMultisample(int @texture, int @samples, SizedInternalFormat @internalformat, int @width, int @height, int @depth, bool @fixedsamplelocations)
        => StateCache.Current.ExecuteCoreTextureStorage3DMultisample(@texture, @samples, @internalformat, @width, @height, @depth, @fixedsamplelocations);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorage3DMultisample(uint @texture, int @samples, InternalFormat @internalformat, int @width, int @height, int @depth, bool @fixedsamplelocations)
        => StateCache.Current.ExecuteCoreTextureStorage3DMultisample(@texture, @samples, @internalformat, @width, @height, @depth, @fixedsamplelocations);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorage3DMultisample(uint @texture, int @samples, SizedInternalFormat @internalformat, int @width, int @height, int @depth, bool @fixedsamplelocations)
        => StateCache.Current.ExecuteCoreTextureStorage3DMultisample(@texture, @samples, @internalformat, @width, @height, @depth, @fixedsamplelocations);

    #endregion
}
