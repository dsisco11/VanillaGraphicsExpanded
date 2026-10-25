using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Renderbuffers;

/// <summary>Forwards GL.NamedRenderbufferStorageMultisample overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedRenderbufferStorageMultisample(int @renderbuffer, int @samples, RenderbufferStorage @internalformat, int @width, int @height)
        => StateCache.Current.ExecuteCoreNamedRenderbufferStorageMultisample(@renderbuffer, @samples, @internalformat, @width, @height);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedRenderbufferStorageMultisample(uint @renderbuffer, int @samples, RenderbufferStorage @internalformat, int @width, int @height)
        => StateCache.Current.ExecuteCoreNamedRenderbufferStorageMultisample(@renderbuffer, @samples, @internalformat, @width, @height);

    #endregion
}
