using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glUniformHandleui64ARB after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glUniformHandleui64ARB")]
    internal static void ObserveUniformHandleui64ARB(int a0, ulong a1)
    {
        int @location = unchecked((int)a0);
        long @value = unchecked((long)a1);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveProgramMetadata(0, false, true, unchecked((int)@location), 1, false, true);
    }
    #endregion
}
