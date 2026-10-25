using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.TextureStorageMem2DMultisample overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorageMem2DMultisample(int @texture, int @samples, ExtMemoryObject @internalFormat, int @width, int @height, bool @fixedSampleLocations, int @memory, long @offset)
        => StateCache.Current.ExecuteExtTextureStorageMem2DMultisample(@texture, @samples, @internalFormat, @width, @height, @fixedSampleLocations, @memory, @offset);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorageMem2DMultisample(int @texture, int @samples, SizedInternalFormat @internalFormat, int @width, int @height, bool @fixedSampleLocations, int @memory, long @offset)
        => StateCache.Current.ExecuteExtTextureStorageMem2DMultisample(@texture, @samples, @internalFormat, @width, @height, @fixedSampleLocations, @memory, @offset);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorageMem2DMultisample(uint @texture, int @samples, ExtMemoryObject @internalFormat, int @width, int @height, bool @fixedSampleLocations, uint @memory, ulong @offset)
        => StateCache.Current.ExecuteExtTextureStorageMem2DMultisample(@texture, @samples, @internalFormat, @width, @height, @fixedSampleLocations, @memory, @offset);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureStorageMem2DMultisample(uint @texture, int @samples, SizedInternalFormat @internalFormat, int @width, int @height, bool @fixedSampleLocations, uint @memory, ulong @offset)
        => StateCache.Current.ExecuteExtTextureStorageMem2DMultisample(@texture, @samples, @internalFormat, @width, @height, @fixedSampleLocations, @memory, @offset);

    #endregion
}
