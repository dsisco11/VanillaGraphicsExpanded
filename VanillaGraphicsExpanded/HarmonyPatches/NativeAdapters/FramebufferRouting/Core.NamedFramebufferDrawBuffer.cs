using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.FramebufferRouting;

/// <summary>Forwards GL.NamedFramebufferDrawBuffer overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedFramebufferDrawBuffer(int @framebuffer, DrawBufferMode @buf) => StateCache.Current.ExecuteCoreNamedFramebufferDrawBuffer(@framebuffer, @buf);

    /// <summary>Forwards the exact signature to the StateCache execution boundary.</summary>
    internal static void NamedFramebufferDrawBuffer(uint @framebuffer, DrawBufferMode @buf) => StateCache.Current.ExecuteCoreNamedFramebufferDrawBuffer(@framebuffer, @buf);

    #endregion
}
