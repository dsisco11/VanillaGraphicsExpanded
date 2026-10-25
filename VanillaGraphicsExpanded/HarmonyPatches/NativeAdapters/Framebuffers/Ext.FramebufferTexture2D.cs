using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Framebuffers;

/// <summary>Routes exact Ext overloads through their command execution boundary.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void FramebufferTexture2D(FramebufferTarget @target, FramebufferAttachment @attachment, TextureTarget @textarget, int @texture, int @level)
        => StateCache.Current.ExecuteExtFramebufferTexture2D(@target, @attachment, @textarget, @texture, @level);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void FramebufferTexture2D(FramebufferTarget @target, FramebufferAttachment @attachment, TextureTarget @textarget, uint @texture, int @level)
        => StateCache.Current.ExecuteExtFramebufferTexture2D(@target, @attachment, @textarget, @texture, @level);
    #endregion
}
