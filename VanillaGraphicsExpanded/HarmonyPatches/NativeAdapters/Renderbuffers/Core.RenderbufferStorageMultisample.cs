using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Renderbuffers;

/// <summary>Forwards GL.RenderbufferStorageMultisample overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void RenderbufferStorageMultisample(RenderbufferTarget @target, int @samples, RenderbufferStorage @internalformat, int @width, int @height)
        => StateCache.Current.ExecuteCoreRenderbufferStorageMultisample(@target, @samples, @internalformat, @width, @height);

    #endregion
}
