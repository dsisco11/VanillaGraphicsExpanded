using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glDeleteSync after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glDeleteSync")]
    internal static void ObserveDeleteSync(nint a0)
    {
        IntPtr id = unchecked((IntPtr)a0);
        var cache = OpenGLObservationContext.CurrentCache;

        cache.CompleteResourceDeletion(GpuResourceKind.Sync, (nint)id, OpenGLAuxiliaryDeletionCalls.WasDeletedExternal(GpuResourceKind.Sync, (nint)id));
    }
    #endregion
}
