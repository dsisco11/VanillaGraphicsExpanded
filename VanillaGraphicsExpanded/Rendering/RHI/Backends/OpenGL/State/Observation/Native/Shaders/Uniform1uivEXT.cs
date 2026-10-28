using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glUniform1uivEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glUniform1uivEXT")]
    internal static void ObserveUniform1uivEXT(int a0, int a1, uint* a2)
    {
        int @location = unchecked((int)a0);
        int @count = unchecked((int)a1);
        int* @value = unchecked((int*)a2);
        _ = OpenGLObservationContext.CurrentCache;
        OpenGlStateCache.ObserveProgramMetadata(0, false, true, unchecked((int)@location), unchecked((int)@count), false, false);
    }
    #endregion
}
