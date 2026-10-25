using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.TextureRenderbuffer overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureRenderbuffer(int @texture, TextureTarget @target, int @renderbuffer)
        => StateCache.Current.ExecuteExtTextureRenderbuffer(@texture, @target, @renderbuffer);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void TextureRenderbuffer(uint @texture, TextureTarget @target, uint @renderbuffer)
        => StateCache.Current.ExecuteExtTextureRenderbuffer(@texture, @target, @renderbuffer);

    #endregion
}
