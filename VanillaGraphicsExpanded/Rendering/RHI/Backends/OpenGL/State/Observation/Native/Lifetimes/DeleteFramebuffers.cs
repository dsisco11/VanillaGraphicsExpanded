using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes equivalent native entrypoint effects after driver dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glDeleteFramebuffers")]
    [InterceptOpenGL("glDeleteFramebuffersEXT")]
    internal static void ObserveDeleteFramebuffers(int a0, uint* a1)
    {
        int count = unchecked((int)a0);
        int* ids = unchecked((int*)a1);
        if (!OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        if (count > 0) foreach (var value in new ReadOnlySpan<int>(ids, count))
            cache.InterruptNativeResourceOperation(GpuResourceKind.Framebuffer, unchecked((int)value));

        if (count <= 0 || ids == null) return;
        foreach (var value in new ReadOnlySpan<int>(ids, count))
            cache.CompleteResourceDeletion(GpuResourceKind.Framebuffer, unchecked((int)value), OpenGLRenderingDeletionCalls.WasDeletedExternal(GpuResourceKind.Framebuffer, unchecked((int)value)));
    }
    #endregion
}
