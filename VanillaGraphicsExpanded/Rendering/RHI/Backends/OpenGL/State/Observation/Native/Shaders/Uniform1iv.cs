using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes equivalent native entrypoint effects after driver dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glUniform1iv")]
    [InterceptOpenGL("glUniform1ivARB")]
    internal static void ObserveUniform1iv(int a0, int a1, int* a2)
    {
        int @location = unchecked((int)a0);
        int @count = unchecked((int)a1);
        int* @value = unchecked((int*)a2);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveProgramMetadata(0, false, true, unchecked((int)@location), unchecked((int)@count), false, false);
    }
    #endregion
}
