using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferTextures;

/// <summary>Forwards GL.Arb.TexBuffer overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Arb
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexBuffer(TextureTarget @target, InternalFormat @internalformat, int @buffer)
        => StateCache.Current.ExecuteArbTexBuffer(@target, @internalformat, @buffer);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexBuffer(TextureTarget @target, InternalFormat @internalformat, uint @buffer)
        => StateCache.Current.ExecuteArbTexBuffer(@target, @internalformat, @buffer);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexBuffer(TextureTarget @target, SizedInternalFormat @internalformat, int @buffer)
        => StateCache.Current.ExecuteArbTexBuffer(@target, @internalformat, @buffer);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexBuffer(TextureTarget @target, SizedInternalFormat @internalformat, uint @buffer)
        => StateCache.Current.ExecuteArbTexBuffer(@target, @internalformat, @buffer);

    #endregion
}
