using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Framebuffers;

/// <summary>Routes exact Ext overloads through their command execution boundary.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedFramebufferParameter(int @framebuffer, ExtDirectStateAccess @pname, int @param)
        => StateCache.Current.ExecuteExtNamedFramebufferParameter(@framebuffer, @pname, @param);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedFramebufferParameter(int @framebuffer, FramebufferDefaultParameter @pname, int @param)
        => StateCache.Current.ExecuteExtNamedFramebufferParameter(@framebuffer, @pname, @param);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedFramebufferParameter(int @framebuffer, FramebufferParameterName @pname, int @param)
        => StateCache.Current.ExecuteExtNamedFramebufferParameter(@framebuffer, @pname, @param);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedFramebufferParameter(uint @framebuffer, ExtDirectStateAccess @pname, int @param)
        => StateCache.Current.ExecuteExtNamedFramebufferParameter(@framebuffer, @pname, @param);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedFramebufferParameter(uint @framebuffer, FramebufferDefaultParameter @pname, int @param)
        => StateCache.Current.ExecuteExtNamedFramebufferParameter(@framebuffer, @pname, @param);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedFramebufferParameter(uint @framebuffer, FramebufferParameterName @pname, int @param)
        => StateCache.Current.ExecuteExtNamedFramebufferParameter(@framebuffer, @pname, @param);
    #endregion
}
