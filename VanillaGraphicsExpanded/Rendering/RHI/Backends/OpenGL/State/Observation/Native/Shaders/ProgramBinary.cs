using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glProgramBinary after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glProgramBinary")]
    internal static void ObserveProgramBinary(uint a0, int a1, nint a2, int a3)
    {
        int @program = unchecked((int)a0);
        BinaryFormat @binaryFormat = unchecked((BinaryFormat)a1);
        IntPtr @binary = unchecked((IntPtr)a2);
        int @length = unchecked((int)a3);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveProgramMetadata(unchecked((int)@program), true, false, -1, 1, true, false);
    }
    #endregion
}
