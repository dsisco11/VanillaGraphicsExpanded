using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.TexStorage1D overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexStorage1D(TextureTarget1d @target, int @levels, SizedInternalFormat @internalformat, int @width)
        => StateCache.Current.ExecuteCoreTexStorage1D(@target, @levels, @internalformat, @width);

    #endregion
}
