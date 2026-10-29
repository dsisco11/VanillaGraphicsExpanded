using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glDeleteMemoryObjectsEXT after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glDeleteMemoryObjectsEXT")]
    internal static void ObserveDeleteMemoryObjectsEXT(int a0, uint* a1)
    {
        int count = unchecked((int)a0);
        int* ids = unchecked((int*)a1);
        if (!OpenGLObservationContext.TryGet(out var cache, out _, false)) return;

        if (count <= 0 || ids == null) return;
        foreach (var value in new ReadOnlySpan<int>(ids, count))
            cache.CompleteResourceDeletion(GpuResourceKind.MemoryObject, unchecked((int)value), OpenGLAuxiliaryDeletionCalls.WasDeletedExternal(GpuResourceKind.MemoryObject, unchecked((int)value)));
    }
    #endregion
}
