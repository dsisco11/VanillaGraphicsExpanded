using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.FramebufferRouting;

/// <summary>Forwards GL.Arb.DrawBuffers overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Arb
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void DrawBuffers(int @n, ref ArbDrawBuffers @bufs) => OpenGLFramebufferRoutingCalls.ExecuteArbDrawBuffers(@n, ref @bufs);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void DrawBuffers(int @n, ArbDrawBuffers[] @bufs) => OpenGLFramebufferRoutingCalls.ExecuteArbDrawBuffers(@n, @bufs);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void DrawBuffers(int @n, ArbDrawBuffers* @bufs) => OpenGLFramebufferRoutingCalls.ExecuteArbDrawBuffers(@n, @bufs);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void DrawBuffers(int @n, ref DrawBufferMode @bufs) => OpenGLFramebufferRoutingCalls.ExecuteArbDrawBuffers(@n, ref @bufs);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void DrawBuffers(int @n, DrawBufferMode[] @bufs) => OpenGLFramebufferRoutingCalls.ExecuteArbDrawBuffers(@n, @bufs);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void DrawBuffers(int @n, DrawBufferMode* @bufs) => OpenGLFramebufferRoutingCalls.ExecuteArbDrawBuffers(@n, @bufs);

    #endregion
}
