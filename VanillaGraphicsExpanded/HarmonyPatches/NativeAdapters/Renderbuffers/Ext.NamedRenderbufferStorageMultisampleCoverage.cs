using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Renderbuffers;

/// <summary>Forwards GL.Ext.NamedRenderbufferStorageMultisampleCoverage overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedRenderbufferStorageMultisampleCoverage(int @renderbuffer, int @coverageSamples, int @colorSamples, InternalFormat @internalformat, int @width, int @height)
        => OpenGLRenderbuffersCalls.ExecuteExtNamedRenderbufferStorageMultisampleCoverage(@renderbuffer, @coverageSamples, @colorSamples, @internalformat, @width, @height);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedRenderbufferStorageMultisampleCoverage(uint @renderbuffer, int @coverageSamples, int @colorSamples, InternalFormat @internalformat, int @width, int @height)
        => OpenGLRenderbuffersCalls.ExecuteExtNamedRenderbufferStorageMultisampleCoverage(@renderbuffer, @coverageSamples, @colorSamples, @internalformat, @width, @height);

    #endregion
}
