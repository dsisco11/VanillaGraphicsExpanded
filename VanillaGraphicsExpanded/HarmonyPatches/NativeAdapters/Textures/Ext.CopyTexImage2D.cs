using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.CopyTexImage2D overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void CopyTexImage2D(TextureTarget @target, int @level, InternalFormat @internalformat, int @x, int @y, int @width, int @height, int @border)
        => StateCache.Current.ExecuteExtCopyTexImage2D(@target, @level, @internalformat, @x, @y, @width, @height, @border);

    #endregion
}
