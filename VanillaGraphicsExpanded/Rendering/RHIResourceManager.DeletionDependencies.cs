
namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Withdraws dependent metadata whose deleted native identity cannot safely resolve through a reused name.</summary>
internal sealed partial class RHIResourceManager
{
    #region Private
    /// <summary>Invalidates affected descriptors while preserving native retained-storage and binding semantics.</summary>
    private void InvalidateDeletionDependencies(GpuResourceKind kind, nint id, GpuResourceRegistration? deleted,
        int? readFramebuffer, int? drawFramebuffer, bool external)
    {
        foreach (var pair in metadataRegistry)
        {
            if (pair.Value is GpuResourceMetadataEntry<GpuFramebufferMetadata> framebuffer
                && framebuffer.Metadata.Attachments is { } attachments)
            {
                var remaining = attachments;
                foreach (var attachment in attachments)
                    if (attachment.Value.Kind == kind && attachment.Value.Id == id
                        && (attachment.Value.OriginalBacking is null || ReferenceEquals(attachment.Value.OriginalBacking, deleted)))
                        remaining = remaining.Remove(attachment.Key);
                if (remaining.Count == attachments.Count) continue;
                // Deletion detaches images from currently bound framebuffers. Other
                // framebuffers can retain the old object, so its reused name is not
                // sufficient to certify their descriptors.
                if (framebuffer.IsUsable && (readFramebuffer.HasValue && framebuffer.Id == readFramebuffer.Value
                    || drawFramebuffer.HasValue && framebuffer.Id == drawFramebuffer.Value))
                {
                    PublishMetadata(framebuffer, framebuffer.Metadata with { Attachments = remaining }, GpuMetadataChange.Metadata);

                }
                else if (framebuffer.IsUsable)
                {
                    // The descriptor keeps the original registration, not just its
                    // numeric name. Later attachment commands can therefore replace it.
                    foreach (var attachment in attachments.Values)
                        if (attachment.Kind == kind && attachment.Id == id && attachment.OriginalBacking is null)
                        { framebuffer.Withdraw(); break; }
                }

            }
            else if (kind == GpuResourceKind.Buffer && pair.Value is GpuResourceMetadataEntry<GpuTextureMetadata> texture
                && texture.Metadata.Buffer is { } buffer && buffer.BufferId == id)
                texture.Withdraw();
            else if (kind == GpuResourceKind.Renderbuffer
                && pair.Value is GpuResourceMetadataEntry<GpuTextureMetadata> renderbufferTexture
                && renderbufferTexture.Metadata.Renderbuffer == id)
                renderbufferTexture.Withdraw();
        }
    }
    #endregion
}
