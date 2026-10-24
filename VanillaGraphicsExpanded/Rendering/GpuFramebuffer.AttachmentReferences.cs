using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Reconciles retained image references with manager-published native attachment descriptors.</summary>
public abstract partial class GpuFramebuffer
{
    private ImmutableDictionary<FramebufferAttachment, GpuFramebufferImageMetadata>? reconciledAttachmentDescriptors;

    #region Private
    /// <summary>Reconciles retained image references with canonical descriptors without creating another native owner.</summary>
    private void ReconcileAttachmentMetadata()
    {
        if (!ownsFramebuffer && !passMetadataPublished || attachmentsDirty || metadata.Attachments is null || ReferenceEquals(metadata.Attachments, reconciledAttachmentDescriptors)) return;
        // Unavailable descriptors must not reacquire a deleted attachment through a reused name.
        if (ResourceManager?.GetResourceRegistration(this) is { MetadataRevision: > 0, MetadataAvailable: false }) return;
        foreach (var slot in new System.Collections.Generic.List<FramebufferAttachment>(attachments.Keys))
            if (!metadata.Attachments.ContainsKey(slot)) attachments.Remove(slot);
        foreach (var pair in metadata.Attachments)
        {
            var data = pair.Value;
            if (attachments.TryGetValue(pair.Key, out var current) && current.ResourceId == data.Id
                && current.Kind == data.Kind && current.MipLevel == data.MipLevel
                && current.MetadataImageSelection.Layer == data.Layer
                && current.MetadataImageSelection.Layered == data.Layered && (current.CubeFace ?? 0) == data.CubeFace) continue;
            var backing = ResourceManager?.FindResource(data.Kind, data.Id);
            var target = data.CubeFace != 0 ? data.CubeFace
                : backing is GpuResourceMetadataEntry<GpuTextureMetadata> texture ? texture.Metadata.Target : 0;
            GpuTextureMetadata? acquiredTexture = null;
            if (data.Kind == GpuResourceKind.Texture && target == 0)
            {
                // External descriptors do not encode texture dimensionality. Resolve it through
                // canonical acquisition before interpreting layer zero or whole layered images.
                acquiredTexture = GpuResourceManager.AcquireExternalImageTexture(checked((int)data.Id));
                target = acquiredTexture.Value.Target;
            }
            var image = GpuFramebufferAttachment.FromExternalImage(checked((int)data.Id), data.Kind == GpuResourceKind.Renderbuffer,
                target, data.MipLevel,
                !data.Layered && target is TextureTarget.Texture1DArray or TextureTarget.Texture2DArray
                    or TextureTarget.Texture3D or TextureTarget.TextureCubeMapArray or TextureTarget.Texture2DMultisampleArray ? data.Layer : null,
                acquiredTexture);
            image.Observe(this);
            attachments[pair.Key] = image;
        }
        reconciledAttachmentDescriptors = metadata.Attachments;
    }

    #endregion
}
