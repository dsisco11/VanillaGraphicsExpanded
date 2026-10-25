using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Renderbuffers;

/// <summary>Forwards GL.Ext.RenderbufferStorage overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void RenderbufferStorage(RenderbufferTarget @target, RenderbufferStorage @internalformat, int @width, int @height)
        => StateCache.Current.ExecuteExtRenderbufferStorage(@target, @internalformat, @width, @height);

    #endregion
}
