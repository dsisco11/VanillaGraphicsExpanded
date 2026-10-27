using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Framebuffers;

/// <summary>Routes exact Ext overloads through their command execution boundary.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void NamedFramebufferTextureFace(int @framebuffer, FramebufferAttachment @attachment, int @texture, int @level, TextureTarget @face)
        => OpenGLFramebufferCalls.ExecuteExtNamedFramebufferTextureFace(@framebuffer, @attachment, @texture, @level, @face);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void NamedFramebufferTextureFace(uint @framebuffer, FramebufferAttachment @attachment, uint @texture, int @level, TextureTarget @face)
        => OpenGLFramebufferCalls.ExecuteExtNamedFramebufferTextureFace(@framebuffer, @attachment, @texture, @level, @face);
    #endregion
}
