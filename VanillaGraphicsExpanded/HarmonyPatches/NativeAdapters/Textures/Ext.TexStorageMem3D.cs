using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.TexStorageMem3D overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexStorageMem3D(TextureTarget @target, int @levels, ExtMemoryObject @internalFormat, int @width, int @height, int @depth, int @memory, long @offset)
        => StateCache.Current.ExecuteExtTexStorageMem3D(@target, @levels, @internalFormat, @width, @height, @depth, @memory, @offset);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexStorageMem3D(TextureTarget @target, int @levels, ExtMemoryObject @internalFormat, int @width, int @height, int @depth, uint @memory, ulong @offset)
        => StateCache.Current.ExecuteExtTexStorageMem3D(@target, @levels, @internalFormat, @width, @height, @depth, @memory, @offset);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexStorageMem3D(TextureTarget @target, int @levels, SizedInternalFormat @internalFormat, int @width, int @height, int @depth, int @memory, long @offset)
        => StateCache.Current.ExecuteExtTexStorageMem3D(@target, @levels, @internalFormat, @width, @height, @depth, @memory, @offset);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexStorageMem3D(TextureTarget @target, int @levels, SizedInternalFormat @internalFormat, int @width, int @height, int @depth, uint @memory, ulong @offset)
        => StateCache.Current.ExecuteExtTexStorageMem3D(@target, @levels, @internalFormat, @width, @height, @depth, @memory, @offset);

    #endregion
}
