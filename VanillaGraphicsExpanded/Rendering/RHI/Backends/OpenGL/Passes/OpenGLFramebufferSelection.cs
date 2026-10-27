using System;
using OpenTK.Graphics.OpenGL4;
using Legacy = OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns framebuffer selector dispatch for controlled integration and exact intercepted calls.</summary>
internal static class OpenGLFramebufferSelection
{
    #region Internal API
    /// <summary>Suppresses a bind only when every affected selector is known equal; commits only on original-context return.</summary>
    internal static void Bind(OpenGlStateCache cache, Legacy.FramebufferTarget target, int framebuffer)
    {
        bool unchanged = target switch
        {
            Legacy.FramebufferTarget.Framebuffer => cache.TryGetCachedCurrentFramebuffer(Legacy.FramebufferTarget.ReadFramebuffer, out int read) && read == framebuffer
                && cache.TryGetCachedCurrentFramebuffer(Legacy.FramebufferTarget.DrawFramebuffer, out int draw) && draw == framebuffer,
            Legacy.FramebufferTarget.ReadFramebuffer or Legacy.FramebufferTarget.DrawFramebuffer => cache.TryGetCachedCurrentFramebuffer(target, out int value) && value == framebuffer,
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        if (unchanged) return;
        var origin = GpuResource.CaptureCreationContext(GpuResourceKind.Framebuffer);
        cache.SetFramebufferSelectionKnowledge(target, null);
        GL.BindFramebuffer((FramebufferTarget)target, framebuffer);
        if (!GpuResource.IsCurrentResourceContext(origin)) throw new InvalidOperationException("Framebuffer selection returned in a foreign context.");
        cache.SetFramebufferSelectionKnowledge(target, framebuffer);
    }
    /// <summary>Forwards an exact engine call without converting unknown native acceptance into cache certainty.</summary>
    internal static void ExternalBind(Legacy.FramebufferTarget target, int framebuffer)
    {
        var cache = OpenGlStateCache.Current;
        cache.SetFramebufferSelectionKnowledge(target, null);
        try { Legacy.GL.BindFramebuffer(target, framebuffer); }
        finally { cache.SetFramebufferSelectionKnowledge(target, null); }
    }
    #endregion
}
