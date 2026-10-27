using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.TextureStorageMem2D overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorageMem2D(int @texture, int @levels, ExtMemoryObject @internalFormat, int @width, int @height, int @memory, long @offset)
        => OpenGLTexturesCalls.ExecuteExtTextureStorageMem2D(@texture, @levels, @internalFormat, @width, @height, @memory, @offset);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorageMem2D(int @texture, int @levels, SizedInternalFormat @internalFormat, int @width, int @height, int @memory, long @offset)
        => OpenGLTexturesCalls.ExecuteExtTextureStorageMem2D(@texture, @levels, @internalFormat, @width, @height, @memory, @offset);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorageMem2D(uint @texture, int @levels, ExtMemoryObject @internalFormat, int @width, int @height, uint @memory, ulong @offset)
        => OpenGLTexturesCalls.ExecuteExtTextureStorageMem2D(@texture, @levels, @internalFormat, @width, @height, @memory, @offset);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void TextureStorageMem2D(uint @texture, int @levels, SizedInternalFormat @internalFormat, int @width, int @height, uint @memory, ulong @offset)
        => OpenGLTexturesCalls.ExecuteExtTextureStorageMem2D(@texture, @levels, @internalFormat, @width, @height, @memory, @offset);

    #endregion
}
