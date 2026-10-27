using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Framebuffers;

/// <summary>Routes exact Core overloads through their command execution boundary.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void NamedFramebufferParameter(int @framebuffer, FramebufferDefaultParameter @pname, int @param)
        => StateCache.Current.ExecuteCoreNamedFramebufferParameter(@framebuffer, @pname, @param);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void NamedFramebufferParameter(uint @framebuffer, FramebufferDefaultParameter @pname, int @param)
        => StateCache.Current.ExecuteCoreNamedFramebufferParameter(@framebuffer, @pname, @param);
    #endregion
}
