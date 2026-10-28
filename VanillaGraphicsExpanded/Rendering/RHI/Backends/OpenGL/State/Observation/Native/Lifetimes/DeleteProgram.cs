using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glDeleteProgram after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glDeleteProgram")]
    internal static void ObserveDeleteProgram(uint a0)
    {
        int id = unchecked((int)a0);
        var cache = OpenGLObservationContext.CurrentCache;

        cache.CompleteResourceDeletion(GpuResourceKind.Program, unchecked((int)id), OpenGLRenderingDeletionCalls.WasDeletedExternal(GpuResourceKind.Program, unchecked((int)id)));
    }
    #endregion
}
