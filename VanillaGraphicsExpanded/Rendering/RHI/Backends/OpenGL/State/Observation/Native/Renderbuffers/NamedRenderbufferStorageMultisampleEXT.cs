using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glNamedRenderbufferStorageMultisampleEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glNamedRenderbufferStorageMultisampleEXT")]
    internal static void ObserveNamedRenderbufferStorageMultisampleEXT(uint a0, int a1, int a2, int a3, int a4)
    {
        int @renderbuffer = unchecked((int)a0);
        int @samples = unchecked((int)a1);
        ExtDirectStateAccess @internalformat = unchecked((ExtDirectStateAccess)a2);
        int @width = unchecked((int)a3);
        int @height = unchecked((int)a4);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveRenderbufferStorage(unchecked((int)@renderbuffer), true);
    }
    #endregion
}
