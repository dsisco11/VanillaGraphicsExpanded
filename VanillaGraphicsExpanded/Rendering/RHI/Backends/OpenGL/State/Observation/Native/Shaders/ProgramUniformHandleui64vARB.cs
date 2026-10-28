using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glProgramUniformHandleui64vARB after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glProgramUniformHandleui64vARB")]
    internal static void ObserveProgramUniformHandleui64vARB(uint a0, int a1, int a2, ulong* a3)
    {
        int @program = unchecked((int)a0);
        int @location = unchecked((int)a1);
        int @count = unchecked((int)a2);
        long* @values = unchecked((long*)a3);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveProgramMetadata(unchecked((int)@program), true, true, unchecked((int)@location), unchecked((int)@count), false, true);
    }
    #endregion
}
