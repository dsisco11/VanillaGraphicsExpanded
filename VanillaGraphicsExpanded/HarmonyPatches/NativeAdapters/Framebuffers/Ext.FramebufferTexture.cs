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
    internal static void FramebufferTexture(FramebufferTarget @target, FramebufferAttachment @attachment, int @texture, int @level)
        => OpenGLFramebufferCalls.ExecuteExtFramebufferTexture(@target, @attachment, @texture, @level);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void FramebufferTexture(FramebufferTarget @target, FramebufferAttachment @attachment, uint @texture, int @level)
        => OpenGLFramebufferCalls.ExecuteExtFramebufferTexture(@target, @attachment, @texture, @level);
    #endregion
}
