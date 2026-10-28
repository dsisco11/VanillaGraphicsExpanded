using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glShaderStorageBlockBinding after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glShaderStorageBlockBinding")]
    internal static void ObserveShaderStorageBlockBinding(uint a0, uint a1, uint a2)
    {
        int @program = unchecked((int)a0);
        int @storageBlockIndex = unchecked((int)a1);
        int @storageBlockBinding = unchecked((int)a2);
        if (!OpenGLObservationContext.TryGet(out _, out _)) return;
        OpenGlStateCache.ObserveProgramMetadata(unchecked((int)@program), true, false, -1, 1, false, false, ProgramInterface.ShaderStorageBlock, unchecked((int)@storageBlockIndex));
    }
    #endregion
}
