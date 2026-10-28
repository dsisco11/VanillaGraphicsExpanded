using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glRenderbufferStorageMultisample after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glRenderbufferStorageMultisample")]
    internal static void ObserveRenderbufferStorageMultisample(int a0, int a1, int a2, int a3, int a4)
    {
        RenderbufferTarget @target = unchecked((RenderbufferTarget)a0);
        int @samples = unchecked((int)a1);
        RenderbufferStorage @internalformat = unchecked((RenderbufferStorage)a2);
        int @width = unchecked((int)a3);
        int @height = unchecked((int)a4);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveRenderbufferStorage(unchecked((int)@target), false);
    }
    #endregion
}
