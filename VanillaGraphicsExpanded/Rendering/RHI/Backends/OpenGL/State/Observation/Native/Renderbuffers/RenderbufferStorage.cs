using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes equivalent native entrypoint effects after driver dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glRenderbufferStorage")]
    [InterceptOpenGL("glRenderbufferStorageEXT")]
    internal static void ObserveRenderbufferStorage(int a0, int a1, int a2, int a3)
    {
        RenderbufferTarget @target = unchecked((RenderbufferTarget)a0);
        RenderbufferStorage @internalformat = unchecked((RenderbufferStorage)a1);
        int @width = unchecked((int)a2);
        int @height = unchecked((int)a3);
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        OpenGlStateCache.ObserveRenderbufferStorage(unchecked((int)@target), false, unchecked((int)@internalformat), unchecked((int)@width), unchecked((int)@height), 0, false, false);
    }
    #endregion
}
