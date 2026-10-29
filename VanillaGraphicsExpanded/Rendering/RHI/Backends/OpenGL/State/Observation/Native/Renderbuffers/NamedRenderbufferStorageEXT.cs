using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glNamedRenderbufferStorageEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glNamedRenderbufferStorageEXT")]
    internal static void ObserveNamedRenderbufferStorageEXT(uint a0, int a1, int a2, int a3)
    {
        int @renderbuffer = unchecked((int)a0);
        ExtDirectStateAccess @internalformat = unchecked((ExtDirectStateAccess)a1);
        int @width = unchecked((int)a2);
        int @height = unchecked((int)a3);
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        OpenGlStateCache.ObserveRenderbufferStorage(unchecked((int)@renderbuffer), true, unchecked((int)@internalformat), unchecked((int)@width), unchecked((int)@height), 0, true, false);
    }
    #endregion
}
