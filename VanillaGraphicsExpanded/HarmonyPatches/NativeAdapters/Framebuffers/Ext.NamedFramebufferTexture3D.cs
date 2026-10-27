using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Framebuffers;

/// <summary>Routes exact Ext overloads through their command execution boundary.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void NamedFramebufferTexture3D(int @framebuffer, FramebufferAttachment @attachment, TextureTarget @textarget, int @texture, int @level, int @zoffset)
        => StateCache.Current.ExecuteExtNamedFramebufferTexture3D(@framebuffer, @attachment, @textarget, @texture, @level, @zoffset);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void NamedFramebufferTexture3D(uint @framebuffer, FramebufferAttachment @attachment, TextureTarget @textarget, uint @texture, int @level, int @zoffset)
        => StateCache.Current.ExecuteExtNamedFramebufferTexture3D(@framebuffer, @attachment, @textarget, @texture, @level, @zoffset);
    #endregion
}
