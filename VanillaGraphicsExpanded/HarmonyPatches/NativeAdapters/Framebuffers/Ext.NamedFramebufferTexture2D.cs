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
    internal static void NamedFramebufferTexture2D(int @framebuffer, FramebufferAttachment @attachment, TextureTarget @textarget, int @texture, int @level)
        => OpenGLFramebufferCalls.ExecuteExtNamedFramebufferTexture2D(@framebuffer, @attachment, @textarget, @texture, @level);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void NamedFramebufferTexture2D(uint @framebuffer, FramebufferAttachment @attachment, TextureTarget @textarget, uint @texture, int @level)
        => OpenGLFramebufferCalls.ExecuteExtNamedFramebufferTexture2D(@framebuffer, @attachment, @textarget, @texture, @level);
    #endregion
}
