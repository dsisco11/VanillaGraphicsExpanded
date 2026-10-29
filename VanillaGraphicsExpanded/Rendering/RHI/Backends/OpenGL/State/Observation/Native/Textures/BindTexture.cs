using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glBindTexture after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glBindTexture")]
    [InterceptOpenGL("glBindTextureEXT")]
    internal static void ObserveBindTexture(int a0, uint a1)
    {
        TextureTarget target = unchecked((TextureTarget)a0);
        int texture = unchecked((int)a1);
        if (!OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        bool targetValid = OpenGlStateCache.RepresentsTextureBinding(target);
        int unit = targetValid ? cache.GetActiveTextureUnit() : -1;
        bool established = texture == 0;
        if (targetValid && texture != 0)
        {
            var realization = RHIResourceManagerSystem.CurrentManager?.RealizeObservedImage(
                GpuResourceKind.Texture, texture, OpenGLImageValues.Dimension(target), implicitCreation: false);
            established = realization is GpuResourceMetadataEntry<GpuTextureMetadata> { IsRealized: true } realized
                && realized.Metadata.Target == OpenGLImageValues.Dimension(target);
            established |= cache.TryGetCachedBoundTexture(target, unit, out int prior) && prior == texture;
            var manager = RHIResourceManagerSystem.CurrentManager;
            if (!established && manager?.CanObserveNativeMetadata == true
                && manager.FindObservedResource(GpuResourceKind.Texture, texture) is GpuResourceMetadataEntry<GpuTextureMetadata> { IsUsable: true } entry)
                established = OpenGLImageValues.Dimension(target) == entry.Metadata.Target;
        }
        var origin = Integration.RenderContextRegistry.CaptureResourceContext();
        if (targetValid) cache.ForgetImageBinding(target, unit);

        cache.RecordTextureBind();
        if (targetValid && established && cache.OwnsKnowledgeFor(origin) && Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
            cache.ImageBindings(unit)[target] = texture;
    }
    #endregion
}
