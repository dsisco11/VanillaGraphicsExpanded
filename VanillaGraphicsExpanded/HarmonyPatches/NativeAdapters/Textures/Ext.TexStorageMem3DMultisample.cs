using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.TexStorageMem3DMultisample overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexStorageMem3DMultisample(TextureTarget @target, int @samples, ExtMemoryObject @internalFormat, int @width, int @height, int @depth, bool @fixedSampleLocations, int @memory, long @offset)
        => StateCache.Current.ExecuteExtTexStorageMem3DMultisample(@target, @samples, @internalFormat, @width, @height, @depth, @fixedSampleLocations, @memory, @offset);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexStorageMem3DMultisample(TextureTarget @target, int @samples, ExtMemoryObject @internalFormat, int @width, int @height, int @depth, bool @fixedSampleLocations, uint @memory, ulong @offset)
        => StateCache.Current.ExecuteExtTexStorageMem3DMultisample(@target, @samples, @internalFormat, @width, @height, @depth, @fixedSampleLocations, @memory, @offset);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexStorageMem3DMultisample(TextureTarget @target, int @samples, SizedInternalFormat @internalFormat, int @width, int @height, int @depth, bool @fixedSampleLocations, int @memory, long @offset)
        => StateCache.Current.ExecuteExtTexStorageMem3DMultisample(@target, @samples, @internalFormat, @width, @height, @depth, @fixedSampleLocations, @memory, @offset);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexStorageMem3DMultisample(TextureTarget @target, int @samples, SizedInternalFormat @internalFormat, int @width, int @height, int @depth, bool @fixedSampleLocations, uint @memory, ulong @offset)
        => StateCache.Current.ExecuteExtTexStorageMem3DMultisample(@target, @samples, @internalFormat, @width, @height, @depth, @fixedSampleLocations, @memory, @offset);

    #endregion
}
