using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glUniformHandleui64vARB after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glUniformHandleui64vARB")]
    internal static void ObserveUniformHandleui64vARB(int a0, int a1, ulong* a2)
    {
        int @location = unchecked((int)a0);
        int @count = unchecked((int)a1);
        long* @value = unchecked((long*)a2);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveProgramMetadata(0, false, true, unchecked((int)@location), unchecked((int)@count), false, true);
    }
    #endregion
}
