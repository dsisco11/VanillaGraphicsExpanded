using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferTextures;

/// <summary>Forwards GL.TexBuffer overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexBuffer(TextureBufferTarget @target, SizedInternalFormat @internalformat, int @buffer)
        => StateCache.Current.ExecuteCoreTexBuffer(@target, @internalformat, @buffer);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexBuffer(TextureBufferTarget @target, SizedInternalFormat @internalformat, uint @buffer)
        => StateCache.Current.ExecuteCoreTexBuffer(@target, @internalformat, @buffer);

    #endregion
}
