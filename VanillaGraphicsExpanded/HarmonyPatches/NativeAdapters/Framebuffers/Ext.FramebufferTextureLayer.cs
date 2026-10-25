using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Framebuffers;

/// <summary>Routes exact Ext overloads through their command execution boundary.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void FramebufferTextureLayer(FramebufferTarget @target, FramebufferAttachment @attachment, int @texture, int @level, int @layer)
        => StateCache.Current.ExecuteExtFramebufferTextureLayer(@target, @attachment, @texture, @level, @layer);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void FramebufferTextureLayer(FramebufferTarget @target, FramebufferAttachment @attachment, uint @texture, int @level, int @layer)
        => StateCache.Current.ExecuteExtFramebufferTextureLayer(@target, @attachment, @texture, @level, @layer);
    #endregion
}
