using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glNamedRenderbufferStorageMultisample after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glNamedRenderbufferStorageMultisample")]
    internal static void ObserveNamedRenderbufferStorageMultisample(uint a0, int a1, int a2, int a3, int a4)
    {
        int @renderbuffer = unchecked((int)a0);
        int @samples = unchecked((int)a1);
        RenderbufferStorage @internalformat = unchecked((RenderbufferStorage)a2);
        int @width = unchecked((int)a3);
        int @height = unchecked((int)a4);
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        OpenGlStateCache.ObserveRenderbufferStorage(unchecked((int)@renderbuffer), true, unchecked((int)@internalformat), unchecked((int)@width), unchecked((int)@height), unchecked((int)@samples), false, false);
    }
    #endregion
}
