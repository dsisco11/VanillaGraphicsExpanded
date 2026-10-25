using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Framebuffers;

/// <summary>Routes exact Core overloads through their command execution boundary.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void FramebufferParameter(FramebufferTarget @target, FramebufferDefaultParameter @pname, int @param)
        => StateCache.Current.ExecuteCoreFramebufferParameter(@target, @pname, @param);
    #endregion
}
