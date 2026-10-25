using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.TexStorage2D overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TexStorage2D(TextureTarget @target, int @levels, SizedInternalFormat @internalformat, int @width, int @height)
        => StateCache.Current.ExecuteExtTexStorage2D(@target, @levels, @internalformat, @width, @height);

    #endregion
}
