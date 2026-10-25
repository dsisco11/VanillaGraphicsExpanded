using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Framebuffers;

/// <summary>Routes exact Ext overloads through their command execution boundary.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void FramebufferRenderbuffer(FramebufferTarget @target, FramebufferAttachment @attachment, RenderbufferTarget @renderbuffertarget, int @renderbuffer)
        => StateCache.Current.ExecuteExtFramebufferRenderbuffer(@target, @attachment, @renderbuffertarget, @renderbuffer);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void FramebufferRenderbuffer(FramebufferTarget @target, FramebufferAttachment @attachment, RenderbufferTarget @renderbuffertarget, uint @renderbuffer)
        => StateCache.Current.ExecuteExtFramebufferRenderbuffer(@target, @attachment, @renderbuffertarget, @renderbuffer);
    #endregion
}
