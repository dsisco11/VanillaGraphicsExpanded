using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.FramebufferRouting;

/// <summary>Forwards GL.Ext.FramebufferDrawBuffer overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void FramebufferDrawBuffer(int @framebuffer, DrawBufferMode @mode) => StateCache.Current.ExecuteExtFramebufferDrawBuffer(@framebuffer, @mode);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void FramebufferDrawBuffer(uint @framebuffer, DrawBufferMode @mode) => StateCache.Current.ExecuteExtFramebufferDrawBuffer(@framebuffer, @mode);

    #endregion
}
