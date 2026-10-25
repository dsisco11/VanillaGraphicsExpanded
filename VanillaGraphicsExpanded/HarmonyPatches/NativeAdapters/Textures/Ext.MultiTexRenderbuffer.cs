using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Textures;

/// <summary>Forwards GL.Ext.MultiTexRenderbuffer overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void MultiTexRenderbuffer(TextureUnit @texunit, TextureTarget @target, int @renderbuffer)
        => StateCache.Current.ExecuteExtMultiTexRenderbuffer(@texunit, @target, @renderbuffer);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void MultiTexRenderbuffer(TextureUnit @texunit, TextureTarget @target, uint @renderbuffer)
        => StateCache.Current.ExecuteExtMultiTexRenderbuffer(@texunit, @target, @renderbuffer);

    #endregion
}
