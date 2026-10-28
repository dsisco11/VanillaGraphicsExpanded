using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glActiveTexture after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glActiveTexture")]
    internal static void ObserveActiveTexture(int a0)
    {
        TextureUnit texture = unchecked((TextureUnit)a0);
        if (!OpenGLObservationContext.TryGet(out var cache, out _)) return;
        int unit = (int)texture - (int)TextureUnit.Texture0;
        bool valid = unit >= 0 && unit < GpuSupport.MaxCombinedTextureImageUnits;
        var origin = Integration.RenderContextRegistry.CaptureResourceContext();
        if (valid) cache.ActiveTextureKnowledge = null;

        if (valid && cache.OwnsKnowledgeFor(origin) && Integration.RenderContextRegistry.IsCurrentResourceContext(origin)) cache.ActiveTextureKnowledge = unit;
    }
    #endregion
}
