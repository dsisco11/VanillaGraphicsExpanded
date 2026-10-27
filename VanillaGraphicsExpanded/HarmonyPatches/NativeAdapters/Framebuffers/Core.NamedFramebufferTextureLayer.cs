using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Framebuffers;

/// <summary>Routes exact Core overloads through their command execution boundary.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void NamedFramebufferTextureLayer(int @framebuffer, FramebufferAttachment @attachment, int @texture, int @level, int @layer)
        => StateCache.Current.ExecuteCoreNamedFramebufferTextureLayer(@framebuffer, @attachment, @texture, @level, @layer);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void NamedFramebufferTextureLayer(uint @framebuffer, FramebufferAttachment @attachment, uint @texture, int @level, int @layer)
        => StateCache.Current.ExecuteCoreNamedFramebufferTextureLayer(@framebuffer, @attachment, @texture, @level, @layer);
    #endregion
}
