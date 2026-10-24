using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Reconciles retained image references with manager-published native attachment descriptors.</summary>
public abstract partial class GpuFramebuffer
{
    private ImmutableDictionary<FramebufferAttachment, GpuFramebufferImageMetadata>? reconciledAttachmentDescriptors;

    #region Internal API
    /// <summary>Withdraws cached helper results without rewriting a newly observed native configuration.</summary>
    internal void OnNativeConfigurationChanged()
    {
        // Pending managed reattachment cannot overwrite the native configuration just
        // observed. Reconcile lazily so incomplete but legal intermediate configurations
        // remain representable until a consumer requires usable attachment images.
        attachmentsDirty = false;
        reconciledAttachmentDescriptors = null;
        checkedPassRevision = null;
        PublishAttachmentsChanged();
    }
    #endregion

    #region Private
    /// <summary>Reconciles retained image references with canonical descriptors without creating another native owner.</summary>
    private void ReconcileAttachmentMetadata()
    {
        if (!ownsFramebuffer && !passMetadataPublished || attachmentsDirty || metadata.Attachments is null || ReferenceEquals(metadata.Attachments, reconciledAttachmentDescriptors)) return;
        // Unavailable descriptors must not reacquire a deleted attachment through a reused name.
        if (ResourceManager?.GetResourceRegistration(this) is { MetadataRevision: > 0, MetadataAvailable: false }) return;
        foreach (var slot in new System.Collections.Generic.List<FramebufferAttachment>(attachments.Keys))
            if (!metadata.Attachments.ContainsKey(slot)) attachments.Remove(slot);
        // Packed depth/stencil and repeated color selections describe the same image.
        // Preserve that relationship so combined-aspect helpers retain one reference.
        var resolved = new System.Collections.Generic.Dictionary<GpuFramebufferImageMetadata, GpuFramebufferAttachment>();
        foreach (var pair in metadata.Attachments)
        {
            var data = pair.Value;
            if (resolved.TryGetValue(data, out var shared))
            {
                attachments[pair.Key] = shared;
                continue;
            }
            if (attachments.TryGetValue(pair.Key, out var current) && current.ResourceId == data.Id
                && current.Kind == data.Kind && current.MatchesNativeSelection(data,
                    retainAuthoredMode: ResourceManager is null || ResourceManager.IsMetadataRegistryRetired
                        || ResourceManager.GetResourceRegistration(this) is null))
            {
                resolved.Add(data, current);
                continue;
            }
            var backing = ResourceManager is { IsMetadataRegistryRetired: false } activeManager
                ? activeManager.FindResource(data.Kind, data.Id) : null;
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
            bool range = target == TextureTarget.Texture2DArray && data.Mode != GpuFramebufferAttachmentMode.Ordinary
                && data.ViewCount > 0 && (data.Layered || data.Mode == GpuFramebufferAttachmentMode.Multiview);
            var image = GpuFramebufferAttachment.FromExternalImage(checked((int)data.Id), data.Kind == GpuResourceKind.Renderbuffer,
                target, data.MipLevel,
                !data.Layered && data.Mode != GpuFramebufferAttachmentMode.Multiview
                    && target is TextureTarget.Texture1DArray or TextureTarget.Texture2DArray
                    or TextureTarget.Texture3D or TextureTarget.TextureCubeMapArray or TextureTarget.Texture2DMultisampleArray ? data.Layer : null,
                acquiredTexture, range ? data.BaseView : 0, range ? data.ViewCount : 0, data.Mode);
            image.Observe(this);
            attachments[pair.Key] = image;
            resolved.Add(data, image);
        }
        reconciledAttachmentDescriptors = metadata.Attachments;
    }

    #endregion
}
