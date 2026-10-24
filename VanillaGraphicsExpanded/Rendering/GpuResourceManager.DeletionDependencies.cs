using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Withdraws dependent metadata whose deleted native identity cannot safely resolve through a reused name.</summary>
internal sealed partial class GpuResourceManager
{
    #region Private
    /// <summary>Invalidates affected descriptors while preserving native retained-storage and binding semantics.</summary>
    private void InvalidateDeletionDependencies(GpuResourceKind kind, nint id)
    {
        foreach (var pair in metadataRegistry)
        {
            if (pair.Value is GpuResourceMetadataEntry<GpuFramebufferMetadata> framebuffer
                && framebuffer.Metadata.Attachments is { } attachments)
            {
                foreach (var image in attachments.Values)
                    if (image.Kind == kind && image.Id == id)
                    {
                        framebuffer.BeginRefresh(GpuMetadataChange.Metadata);
                        break;
                    }
            }
            else if (kind == GpuResourceKind.Buffer && pair.Value is GpuResourceMetadataEntry<GpuTextureMetadata> texture
                && texture.Metadata.Buffer is { } buffer && buffer.BufferId == id)
                texture.BeginRefresh(GpuMetadataChange.Metadata);
        }
    }
    #endregion
}
