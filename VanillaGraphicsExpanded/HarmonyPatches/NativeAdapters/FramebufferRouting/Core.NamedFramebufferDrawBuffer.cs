using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.FramebufferRouting;

/// <summary>Forwards GL.NamedFramebufferDrawBuffer overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void NamedFramebufferDrawBuffer(int @framebuffer, DrawBufferMode @buf) => OpenGLFramebufferRoutingCalls.ExecuteCoreNamedFramebufferDrawBuffer(@framebuffer, @buf);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void NamedFramebufferDrawBuffer(uint @framebuffer, DrawBufferMode @buf) => OpenGLFramebufferRoutingCalls.ExecuteCoreNamedFramebufferDrawBuffer(@framebuffer, @buf);

    #endregion
}
