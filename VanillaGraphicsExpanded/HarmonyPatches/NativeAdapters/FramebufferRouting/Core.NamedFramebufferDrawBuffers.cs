using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.FramebufferRouting;

/// <summary>Forwards GL.NamedFramebufferDrawBuffers overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void NamedFramebufferDrawBuffers(int @framebuffer, int @n, ref DrawBuffersEnum @bufs) => OpenGLFramebufferRoutingCalls.ExecuteCoreNamedFramebufferDrawBuffers(@framebuffer, @n, ref @bufs);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void NamedFramebufferDrawBuffers(int @framebuffer, int @n, DrawBuffersEnum[] @bufs) => OpenGLFramebufferRoutingCalls.ExecuteCoreNamedFramebufferDrawBuffers(@framebuffer, @n, @bufs);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void NamedFramebufferDrawBuffers(int @framebuffer, int @n, DrawBuffersEnum* @bufs) => OpenGLFramebufferRoutingCalls.ExecuteCoreNamedFramebufferDrawBuffers(@framebuffer, @n, @bufs);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void NamedFramebufferDrawBuffers(uint @framebuffer, int @n, ref DrawBuffersEnum @bufs) => OpenGLFramebufferRoutingCalls.ExecuteCoreNamedFramebufferDrawBuffers(@framebuffer, @n, ref @bufs);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void NamedFramebufferDrawBuffers(uint @framebuffer, int @n, DrawBuffersEnum[] @bufs) => OpenGLFramebufferRoutingCalls.ExecuteCoreNamedFramebufferDrawBuffers(@framebuffer, @n, @bufs);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void NamedFramebufferDrawBuffers(uint @framebuffer, int @n, DrawBuffersEnum* @bufs) => OpenGLFramebufferRoutingCalls.ExecuteCoreNamedFramebufferDrawBuffers(@framebuffer, @n, @bufs);

    #endregion
}
