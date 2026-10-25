using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Framebuffers;

/// <summary>Routes exact Arb overloads through their command execution boundary.</summary>
internal static unsafe partial class Arb
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void FramebufferTexture(FramebufferTarget @target, FramebufferAttachment @attachment, int @texture, int @level)
        => StateCache.Current.ExecuteArbFramebufferTexture(@target, @attachment, @texture, @level);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void FramebufferTexture(FramebufferTarget @target, FramebufferAttachment @attachment, uint @texture, int @level)
        => StateCache.Current.ExecuteArbFramebufferTexture(@target, @attachment, @texture, @level);
    #endregion
}
