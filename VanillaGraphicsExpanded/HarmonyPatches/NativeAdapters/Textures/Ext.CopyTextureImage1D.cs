using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.CopyTextureImage1D overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void CopyTextureImage1D(int @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @x, int @y, int @width, int @border)
        => StateCache.Current.ExecuteExtCopyTextureImage1D(@texture, @target, @level, @internalformat, @x, @y, @width, @border);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void CopyTextureImage1D(uint @texture, TextureTarget @target, int @level, InternalFormat @internalformat, int @x, int @y, int @width, int @border)
        => StateCache.Current.ExecuteExtCopyTextureImage1D(@texture, @target, @level, @internalformat, @x, @y, @width, @border);

    #endregion
}
