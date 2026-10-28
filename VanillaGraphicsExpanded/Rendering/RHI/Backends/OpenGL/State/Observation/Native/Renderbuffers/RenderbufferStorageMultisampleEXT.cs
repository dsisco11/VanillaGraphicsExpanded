using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glRenderbufferStorageMultisampleEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glRenderbufferStorageMultisampleEXT")]
    internal static void ObserveRenderbufferStorageMultisampleEXT(int a0, int a1, int a2, int a3, int a4)
    {
        ExtFramebufferMultisample @target = unchecked((ExtFramebufferMultisample)a0);
        int @samples = unchecked((int)a1);
        ExtFramebufferMultisample @internalformat = unchecked((ExtFramebufferMultisample)a2);
        int @width = unchecked((int)a3);
        int @height = unchecked((int)a4);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveRenderbufferStorage(unchecked((int)@target), false);
    }
    #endregion
}
