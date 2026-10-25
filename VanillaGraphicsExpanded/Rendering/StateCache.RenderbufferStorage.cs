using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes renderbuffer allocation and publishes the resulting native storage facts.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Publishes measured dimensions, format and rounded sample count after a storage command.</summary>
    internal void RenderbufferStorage(GpuRenderbuffer resource, RenderbufferStorage storage, int width, int height, int samples)
    {
        RequireResourceOperationAvailable(resource.Kind, resource.ResourceId);
        using var errors = new GlDebug.ErrorScope("Renderbuffer storage allocation");
        if (GpuSupport.Graphics.SupportsArbDirectStateAccess)
        {
            if (samples > 0) GL.NamedRenderbufferStorageMultisample(resource.RenderbufferId, samples, storage, width, height);
            else GL.NamedRenderbufferStorage(resource.RenderbufferId, storage, width, height);
        }
        else
        {
            using var binding = BindRenderbufferScope(resource.RenderbufferId);
            if (samples > 0) GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, samples, storage, width, height);
            else GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, storage, width, height);
        }
        GlDebug.ThrowIfErrors("Renderbuffer storage allocation");
        GpuResourceManager.ObserveWrapperMetadata(resource, GpuRenderbufferMetadataReader.Instance, GpuMetadataChange.Storage);
    }
    #endregion
}
