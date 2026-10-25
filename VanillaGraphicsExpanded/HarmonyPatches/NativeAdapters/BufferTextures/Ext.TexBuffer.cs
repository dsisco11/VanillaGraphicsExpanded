using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferTextures;

/// <summary>Forwards GL.Ext.TexBuffer overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexBuffer(TextureTarget @target, InternalFormat @internalformat, int @buffer)
        => StateCache.Current.ExecuteExtTexBuffer(@target, @internalformat, @buffer);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexBuffer(TextureTarget @target, InternalFormat @internalformat, uint @buffer)
        => StateCache.Current.ExecuteExtTexBuffer(@target, @internalformat, @buffer);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexBuffer(TextureTarget @target, SizedInternalFormat @internalformat, int @buffer)
        => StateCache.Current.ExecuteExtTexBuffer(@target, @internalformat, @buffer);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexBuffer(TextureTarget @target, SizedInternalFormat @internalformat, uint @buffer)
        => StateCache.Current.ExecuteExtTexBuffer(@target, @internalformat, @buffer);

    #endregion
}
