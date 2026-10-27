using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.FramebufferRouting;

/// <summary>Forwards GL.NamedFramebufferReadBuffer overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void NamedFramebufferReadBuffer(int @framebuffer, ReadBufferMode @src) => OpenGLFramebufferRoutingCalls.ExecuteCoreNamedFramebufferReadBuffer(@framebuffer, @src);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void NamedFramebufferReadBuffer(uint @framebuffer, ReadBufferMode @src) => OpenGLFramebufferRoutingCalls.ExecuteCoreNamedFramebufferReadBuffer(@framebuffer, @src);

    #endregion
}
