using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Framebuffers;

/// <summary>Routes exact Arb overloads through their command execution boundary.</summary>
internal static unsafe partial class Arb
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void FramebufferTextureFace(FramebufferTarget @target, FramebufferAttachment @attachment, int @texture, int @level, TextureTarget @face)
        => OpenGLFramebufferCalls.ExecuteArbFramebufferTextureFace(@target, @attachment, @texture, @level, @face);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void FramebufferTextureFace(FramebufferTarget @target, FramebufferAttachment @attachment, uint @texture, int @level, TextureTarget @face)
        => OpenGLFramebufferCalls.ExecuteArbFramebufferTextureFace(@target, @attachment, @texture, @level, @face);
    #endregion
}
