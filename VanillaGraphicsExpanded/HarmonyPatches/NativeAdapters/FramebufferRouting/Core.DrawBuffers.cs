using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.FramebufferRouting;

/// <summary>Forwards GL.DrawBuffers overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void DrawBuffers(int @n, ref DrawBuffersEnum @bufs) => OpenGLFramebufferRoutingCalls.ExecuteCoreDrawBuffers(@n, ref @bufs);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void DrawBuffers(int @n, DrawBuffersEnum[] @bufs) => OpenGLFramebufferRoutingCalls.ExecuteCoreDrawBuffers(@n, @bufs);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void DrawBuffers(int @n, DrawBuffersEnum* @bufs) => OpenGLFramebufferRoutingCalls.ExecuteCoreDrawBuffers(@n, @bufs);

    #endregion
}
