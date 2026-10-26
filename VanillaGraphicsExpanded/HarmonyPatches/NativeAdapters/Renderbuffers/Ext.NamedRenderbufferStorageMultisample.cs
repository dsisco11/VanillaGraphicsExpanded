using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Renderbuffers;

/// <summary>Forwards GL.Ext.NamedRenderbufferStorageMultisample overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedRenderbufferStorageMultisample(int @renderbuffer, int @samples, ExtDirectStateAccess @internalformat, int @width, int @height)
        => OpenGLRenderbuffersCalls.ExecuteExtNamedRenderbufferStorageMultisample(@renderbuffer, @samples, @internalformat, @width, @height);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedRenderbufferStorageMultisample(int @renderbuffer, int @samples, PixelInternalFormat @internalformat, int @width, int @height)
        => OpenGLRenderbuffersCalls.ExecuteExtNamedRenderbufferStorageMultisample(@renderbuffer, @samples, @internalformat, @width, @height);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedRenderbufferStorageMultisample(int @renderbuffer, int @samples, RenderbufferStorage @internalformat, int @width, int @height)
        => OpenGLRenderbuffersCalls.ExecuteExtNamedRenderbufferStorageMultisample(@renderbuffer, @samples, @internalformat, @width, @height);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedRenderbufferStorageMultisample(uint @renderbuffer, int @samples, ExtDirectStateAccess @internalformat, int @width, int @height)
        => OpenGLRenderbuffersCalls.ExecuteExtNamedRenderbufferStorageMultisample(@renderbuffer, @samples, @internalformat, @width, @height);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedRenderbufferStorageMultisample(uint @renderbuffer, int @samples, PixelInternalFormat @internalformat, int @width, int @height)
        => OpenGLRenderbuffersCalls.ExecuteExtNamedRenderbufferStorageMultisample(@renderbuffer, @samples, @internalformat, @width, @height);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedRenderbufferStorageMultisample(uint @renderbuffer, int @samples, RenderbufferStorage @internalformat, int @width, int @height)
        => OpenGLRenderbuffersCalls.ExecuteExtNamedRenderbufferStorageMultisample(@renderbuffer, @samples, @internalformat, @width, @height);

    #endregion
}
