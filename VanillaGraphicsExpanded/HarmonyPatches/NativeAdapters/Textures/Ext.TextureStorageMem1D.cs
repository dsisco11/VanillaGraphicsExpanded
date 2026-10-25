using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.TextureStorageMem1D overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorageMem1D(int @texture, int @levels, ExtMemoryObject @internalFormat, int @width, int @memory, long @offset)
        => StateCache.Current.ExecuteExtTextureStorageMem1D(@texture, @levels, @internalFormat, @width, @memory, @offset);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorageMem1D(int @texture, int @levels, SizedInternalFormat @internalFormat, int @width, int @memory, long @offset)
        => StateCache.Current.ExecuteExtTextureStorageMem1D(@texture, @levels, @internalFormat, @width, @memory, @offset);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorageMem1D(uint @texture, int @levels, ExtMemoryObject @internalFormat, int @width, uint @memory, ulong @offset)
        => StateCache.Current.ExecuteExtTextureStorageMem1D(@texture, @levels, @internalFormat, @width, @memory, @offset);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorageMem1D(uint @texture, int @levels, SizedInternalFormat @internalFormat, int @width, uint @memory, ulong @offset)
        => StateCache.Current.ExecuteExtTextureStorageMem1D(@texture, @levels, @internalFormat, @width, @memory, @offset);

    #endregion
}
