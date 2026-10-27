using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.FramebufferRouting;

/// <summary>Forwards GL.Ext.FramebufferDrawBuffers overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void FramebufferDrawBuffers(int @framebuffer, int @n, ref DrawBufferMode @bufs) => OpenGLFramebufferRoutingCalls.ExecuteExtFramebufferDrawBuffers(@framebuffer, @n, ref @bufs);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void FramebufferDrawBuffers(int @framebuffer, int @n, DrawBufferMode[] @bufs) => OpenGLFramebufferRoutingCalls.ExecuteExtFramebufferDrawBuffers(@framebuffer, @n, @bufs);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void FramebufferDrawBuffers(int @framebuffer, int @n, DrawBufferMode* @bufs) => OpenGLFramebufferRoutingCalls.ExecuteExtFramebufferDrawBuffers(@framebuffer, @n, @bufs);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void FramebufferDrawBuffers(uint @framebuffer, int @n, ref DrawBufferMode @bufs) => OpenGLFramebufferRoutingCalls.ExecuteExtFramebufferDrawBuffers(@framebuffer, @n, ref @bufs);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void FramebufferDrawBuffers(uint @framebuffer, int @n, DrawBufferMode[] @bufs) => OpenGLFramebufferRoutingCalls.ExecuteExtFramebufferDrawBuffers(@framebuffer, @n, @bufs);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void FramebufferDrawBuffers(uint @framebuffer, int @n, DrawBufferMode* @bufs) => OpenGLFramebufferRoutingCalls.ExecuteExtFramebufferDrawBuffers(@framebuffer, @n, @bufs);

    #endregion
}
