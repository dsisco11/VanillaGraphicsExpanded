using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.TexStorage1D overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexStorage1D(TextureTarget @target, int @levels, SizedInternalFormat @internalformat, int @width)
        => StateCache.Current.ExecuteExtTexStorage1D(@target, @levels, @internalformat, @width);

    #endregion
}
