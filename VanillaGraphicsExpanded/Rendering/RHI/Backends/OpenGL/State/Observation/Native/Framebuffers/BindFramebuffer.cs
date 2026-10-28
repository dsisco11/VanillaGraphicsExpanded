using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glBindFramebuffer after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glBindFramebuffer")]
    internal static void ObserveBindFramebuffer(int a0, uint a1)
    {
        FramebufferTarget target = unchecked((FramebufferTarget)a0);
        int framebuffer = unchecked((int)a1);
        if (!OpenGLObservationContext.TryGet(out var cache, out _)) return;
        bool valid = target is FramebufferTarget.Framebuffer or FramebufferTarget.ReadFramebuffer or FramebufferTarget.DrawFramebuffer;
        bool established = valid && cache.HasEstablishedNativeIdentity(GpuResourceKind.Framebuffer, framebuffer);
        var origin = Integration.RenderContextRegistry.CaptureResourceContext();
        if (valid) cache.SetFramebufferSelectionKnowledge(target, null);

        if (established && cache.OwnsKnowledgeFor(origin) && Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
            cache.SetFramebufferSelectionKnowledge(target, framebuffer);
    }
    #endregion
}
