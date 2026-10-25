using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Renderbuffers;

/// <summary>Forwards GL.Ext.RenderbufferStorageMultisample overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void RenderbufferStorageMultisample(ExtFramebufferMultisample @target, int @samples, ExtFramebufferMultisample @internalformat, int @width, int @height)
        => StateCache.Current.ExecuteExtRenderbufferStorageMultisample(@target, @samples, @internalformat, @width, @height);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void RenderbufferStorageMultisample(RenderbufferTarget @target, int @samples, RenderbufferStorage @internalformat, int @width, int @height)
        => StateCache.Current.ExecuteExtRenderbufferStorageMultisample(@target, @samples, @internalformat, @width, @height);

    #endregion
}
