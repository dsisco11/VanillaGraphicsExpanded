using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Framebuffers;

/// <summary>Routes exact Core overloads through their command execution boundary.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void FramebufferTexture2D(FramebufferTarget @target, FramebufferAttachment @attachment, TextureTarget @textarget, int @texture, int @level)
        => StateCache.Current.ExecuteCoreFramebufferTexture2D(@target, @attachment, @textarget, @texture, @level);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void FramebufferTexture2D(FramebufferTarget @target, FramebufferAttachment @attachment, TextureTarget @textarget, uint @texture, int @level)
        => StateCache.Current.ExecuteCoreFramebufferTexture2D(@target, @attachment, @textarget, @texture, @level);
    #endregion
}
