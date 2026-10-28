using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glNamedRenderbufferStorageMultisampleCoverageEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glNamedRenderbufferStorageMultisampleCoverageEXT")]
    internal static void ObserveNamedRenderbufferStorageMultisampleCoverageEXT(uint a0, int a1, int a2, int a3, int a4, int a5)
    {
        int @renderbuffer = unchecked((int)a0);
        int @coverageSamples = unchecked((int)a1);
        int @colorSamples = unchecked((int)a2);
        InternalFormat @internalformat = unchecked((InternalFormat)a3);
        int @width = unchecked((int)a4);
        int @height = unchecked((int)a5);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveRenderbufferStorage(unchecked((int)@renderbuffer), true);
    }
    #endregion
}
