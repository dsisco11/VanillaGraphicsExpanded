using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glBindSampler after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glBindSampler")]
    internal static void ObserveBindSampler(uint a0, uint a1)
    {
        int unit = unchecked((int)a0);
        int sampler = unchecked((int)a1);
        if (!OpenGLObservationContext.TryGet(out var cache, out _)) return;
        bool valid = unit >= 0 && unit < GpuSupport.MaxCombinedTextureImageUnits;
        bool established = valid && cache.HasEstablishedNativeIdentity(GpuResourceKind.Sampler, sampler);
        var origin = Integration.RenderContextRegistry.CaptureResourceContext();
        if (valid) cache.SetSamplerKnowledge(unit, null);

        cache.RecordSamplerBind();
        if (established && cache.OwnsKnowledgeFor(origin) && Integration.RenderContextRegistry.IsCurrentResourceContext(origin)) cache.SetSamplerKnowledge(unit, sampler);
    }
    #endregion
}
