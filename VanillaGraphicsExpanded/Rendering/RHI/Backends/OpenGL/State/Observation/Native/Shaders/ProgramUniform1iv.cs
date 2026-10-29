using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes equivalent native entrypoint effects after driver dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glProgramUniform1iv")]
    [InterceptOpenGL("glProgramUniform1ivEXT")]
    internal static void ObserveProgramUniform1iv(uint a0, int a1, int a2, int* a3)
    {
        int @program = unchecked((int)a0);
        int @location = unchecked((int)a1);
        int @count = unchecked((int)a2);
        int* @value = unchecked((int*)a3);
        if (count <= 0 || value == null) return;
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        OpenGlStateCache.ObserveProgramMetadata(unchecked((int)@program), true, true, unchecked((int)@location), unchecked((int)@count), false, false, values: new ReadOnlySpan<int>(value, count));
    }
    #endregion
}
