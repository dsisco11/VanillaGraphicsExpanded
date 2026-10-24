using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.FramebufferRouting;

/// <summary>Forwards GL.Arb.DrawBuffers overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Arb
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void DrawBuffers(int @n, ref ArbDrawBuffers @bufs) => StateCache.Current.ExecuteArbDrawBuffers(@n, ref @bufs);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void DrawBuffers(int @n, ArbDrawBuffers[] @bufs) => StateCache.Current.ExecuteArbDrawBuffers(@n, @bufs);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void DrawBuffers(int @n, ArbDrawBuffers* @bufs) => StateCache.Current.ExecuteArbDrawBuffers(@n, @bufs);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void DrawBuffers(int @n, ref DrawBufferMode @bufs) => StateCache.Current.ExecuteArbDrawBuffers(@n, ref @bufs);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void DrawBuffers(int @n, DrawBufferMode[] @bufs) => StateCache.Current.ExecuteArbDrawBuffers(@n, @bufs);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void DrawBuffers(int @n, DrawBufferMode* @bufs) => StateCache.Current.ExecuteArbDrawBuffers(@n, @bufs);

    #endregion
}
