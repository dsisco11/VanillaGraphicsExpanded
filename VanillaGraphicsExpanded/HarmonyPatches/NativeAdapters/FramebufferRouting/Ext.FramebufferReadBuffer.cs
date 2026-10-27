using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.FramebufferRouting;

/// <summary>Forwards GL.Ext.FramebufferReadBuffer overloads through their existing OpenGlStateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void FramebufferReadBuffer(int @framebuffer, ReadBufferMode @mode) => OpenGLFramebufferRoutingCalls.ExecuteExtFramebufferReadBuffer(@framebuffer, @mode);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void FramebufferReadBuffer(uint @framebuffer, ReadBufferMode @mode) => OpenGLFramebufferRoutingCalls.ExecuteExtFramebufferReadBuffer(@framebuffer, @mode);

    #endregion
}
