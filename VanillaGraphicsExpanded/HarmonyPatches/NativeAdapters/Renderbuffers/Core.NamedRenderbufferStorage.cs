using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Renderbuffers;

/// <summary>Forwards GL.NamedRenderbufferStorage overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedRenderbufferStorage(int @renderbuffer, RenderbufferStorage @internalformat, int @width, int @height)
        => StateCache.Current.ExecuteCoreNamedRenderbufferStorage(@renderbuffer, @internalformat, @width, @height);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedRenderbufferStorage(uint @renderbuffer, RenderbufferStorage @internalformat, int @width, int @height)
        => StateCache.Current.ExecuteCoreNamedRenderbufferStorage(@renderbuffer, @internalformat, @width, @height);

    #endregion
}
