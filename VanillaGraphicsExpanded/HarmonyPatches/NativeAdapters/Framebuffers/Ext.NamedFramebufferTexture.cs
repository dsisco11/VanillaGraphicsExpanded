using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Framebuffers;

/// <summary>Routes exact Ext overloads through their command execution boundary.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedFramebufferTexture(int @framebuffer, FramebufferAttachment @attachment, int @texture, int @level)
        => StateCache.Current.ExecuteExtNamedFramebufferTexture(@framebuffer, @attachment, @texture, @level);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedFramebufferTexture(uint @framebuffer, FramebufferAttachment @attachment, uint @texture, int @level)
        => StateCache.Current.ExecuteExtNamedFramebufferTexture(@framebuffer, @attachment, @texture, @level);
    #endregion
}
