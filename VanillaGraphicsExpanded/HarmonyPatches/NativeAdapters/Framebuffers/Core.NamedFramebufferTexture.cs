using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Framebuffers;

/// <summary>Routes exact Core overloads through their command execution boundary.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void NamedFramebufferTexture(int @framebuffer, FramebufferAttachment @attachment, int @texture, int @level)
        => OpenGLFramebufferCalls.ExecuteCoreNamedFramebufferTexture(@framebuffer, @attachment, @texture, @level);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void NamedFramebufferTexture(uint @framebuffer, FramebufferAttachment @attachment, uint @texture, int @level)
        => OpenGLFramebufferCalls.ExecuteCoreNamedFramebufferTexture(@framebuffer, @attachment, @texture, @level);
    #endregion
}
