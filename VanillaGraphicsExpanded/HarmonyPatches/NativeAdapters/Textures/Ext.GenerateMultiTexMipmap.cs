using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.GenerateMultiTexMipmap overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void GenerateMultiTexMipmap(TextureUnit @texunit, TextureTarget @target)
        => StateCache.Current.ExecuteExtGenerateMultiTexMipmap(@texunit, @target);

    #endregion
}
