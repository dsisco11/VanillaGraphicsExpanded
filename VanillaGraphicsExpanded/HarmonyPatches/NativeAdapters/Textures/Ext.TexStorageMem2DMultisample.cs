using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.TexStorageMem2DMultisample overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexStorageMem2DMultisample(TextureTarget @target, int @samples, ExtMemoryObject @internalFormat, int @width, int @height, bool @fixedSampleLocations, int @memory, long @offset)
        => OpenGLTexturesCalls.ExecuteExtTexStorageMem2DMultisample(@target, @samples, @internalFormat, @width, @height, @fixedSampleLocations, @memory, @offset);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexStorageMem2DMultisample(TextureTarget @target, int @samples, ExtMemoryObject @internalFormat, int @width, int @height, bool @fixedSampleLocations, uint @memory, ulong @offset)
        => OpenGLTexturesCalls.ExecuteExtTexStorageMem2DMultisample(@target, @samples, @internalFormat, @width, @height, @fixedSampleLocations, @memory, @offset);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexStorageMem2DMultisample(TextureTarget @target, int @samples, SizedInternalFormat @internalFormat, int @width, int @height, bool @fixedSampleLocations, int @memory, long @offset)
        => OpenGLTexturesCalls.ExecuteExtTexStorageMem2DMultisample(@target, @samples, @internalFormat, @width, @height, @fixedSampleLocations, @memory, @offset);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TexStorageMem2DMultisample(TextureTarget @target, int @samples, SizedInternalFormat @internalFormat, int @width, int @height, bool @fixedSampleLocations, uint @memory, ulong @offset)
        => OpenGLTexturesCalls.ExecuteExtTexStorageMem2DMultisample(@target, @samples, @internalFormat, @width, @height, @fixedSampleLocations, @memory, @offset);

    #endregion
}
