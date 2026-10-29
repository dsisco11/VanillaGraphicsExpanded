using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glBindRenderbuffer after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glBindRenderbuffer")]
    [InterceptOpenGL("glBindRenderbufferEXT")]
    internal static void ObserveBindRenderbuffer(int a0, uint a1)
    {
        RenderbufferTarget target = unchecked((RenderbufferTarget)a0);
        int renderbuffer = unchecked((int)a1);
        if (!OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        bool valid = target == RenderbufferTarget.Renderbuffer;
        if (valid && renderbuffer != 0)
        {
            if (RHIResourceManagerSystem.CurrentManager?.FindObservedResource(GpuResourceKind.Renderbuffer, renderbuffer) is null)
            {
                cache.RenderbufferKnowledge = null;
                return;
            }
            RHIResourceManagerSystem.CurrentManager?.RealizeObservedImage(GpuResourceKind.Renderbuffer, renderbuffer);
        }
        bool established = valid && cache.HasEstablishedNativeIdentity(GpuResourceKind.Renderbuffer, renderbuffer);
        var origin = Integration.RenderContextRegistry.CaptureResourceContext();
        if (valid) cache.RenderbufferKnowledge = null;

        if (established && cache.OwnsKnowledgeFor(origin) && Integration.RenderContextRegistry.IsCurrentResourceContext(origin)) cache.RenderbufferKnowledge = renderbuffer;
    }
    #endregion
}
