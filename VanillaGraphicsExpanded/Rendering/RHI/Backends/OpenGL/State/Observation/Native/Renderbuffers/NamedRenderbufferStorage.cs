using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glNamedRenderbufferStorage after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glNamedRenderbufferStorage")]
    internal static void ObserveNamedRenderbufferStorage(uint a0, int a1, int a2, int a3)
    {
        int @renderbuffer = unchecked((int)a0);
        RenderbufferStorage @internalformat = unchecked((RenderbufferStorage)a1);
        int @width = unchecked((int)a2);
        int @height = unchecked((int)a3);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveRenderbufferStorage(unchecked((int)@renderbuffer), true);
    }
    #endregion
}
