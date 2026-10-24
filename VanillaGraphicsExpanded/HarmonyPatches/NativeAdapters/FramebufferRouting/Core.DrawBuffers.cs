using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.FramebufferRouting;

/// <summary>Forwards GL.DrawBuffers overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void DrawBuffers(int @n, ref DrawBuffersEnum @bufs) => StateCache.Current.ExecuteCoreDrawBuffers(@n, ref @bufs);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void DrawBuffers(int @n, DrawBuffersEnum[] @bufs) => StateCache.Current.ExecuteCoreDrawBuffers(@n, @bufs);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void DrawBuffers(int @n, DrawBuffersEnum* @bufs) => StateCache.Current.ExecuteCoreDrawBuffers(@n, @bufs);

    #endregion
}
