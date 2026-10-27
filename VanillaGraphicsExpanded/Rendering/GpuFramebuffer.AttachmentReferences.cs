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
        PublishAttachmentsChanged();
    }
    /// <summary>Resolves a requested external image from one reusable canonical framebuffer discovery.</summary>
    internal GpuFramebufferAttachment DiscoverAttachment(FramebufferAttachment slot, bool forceNative)
    {
        if (forceNative) GpuResourceManager.RecoverWrapperMetadata(this, GpuFramebufferMetadataReader.Instance);
        else if (!HasNativeMetadata) GpuResourceManager.AcquireWrapperMetadata(this, GpuFramebufferMetadataReader.Instance);
        ReconcileAttachmentMetadata(allowUnpublishedExternal: true, recoverBackings: forceNative);
        return attachments.TryGetValue(slot, out var image) ? image
            : throw new System.InvalidOperationException("The requested framebuffer attachment is absent.");
    }
    #endregion

    #region Private
    /// <summary>Reconciles retained image references with canonical descriptors without creating another native owner.</summary>
    private void ReconcileAttachmentMetadata(bool allowUnpublishedExternal = false, bool recoverBackings = false)
    {
        if (!allowUnpublishedExternal && !ownsFramebuffer && !passMetadataPublished || attachmentsDirty || metadata.Attachments is null || !recoverBackings && ReferenceEquals(metadata.Attachments, reconciledAttachmentDescriptors)) return;
        // Unavailable descriptors must not reacquire a deleted attachment through a reused name.
        if (ResourceManager?.GetResourceRegistration(this) is { MetadataRevision: > 0, MetadataAvailable: false }) return;
        var candidate = new System.Collections.Generic.Dictionary<FramebufferAttachment, GpuFramebufferAttachment>(attachments);
        foreach (var slot in new System.Collections.Generic.List<FramebufferAttachment>(candidate.Keys))
            if (!metadata.Attachments.ContainsKey(slot)) candidate.Remove(slot);
        // Packed depth/stencil and repeated color selections describe the same image.
        // Preserve that relationship so combined-aspect helpers retain one reference.
        var resolved = new System.Collections.Generic.Dictionary<GpuFramebufferImageMetadata, GpuFramebufferAttachment>();
        var textures = new System.Collections.Generic.Dictionary<nint, GpuTextureMetadata>();
        var renderbuffers = new System.Collections.Generic.Dictionary<nint, GpuRenderbufferMetadata>();
        foreach (var pair in metadata.Attachments)
        {
            var data = pair.Value;
            if (resolved.TryGetValue(data, out var shared))
            {
                candidate[pair.Key] = shared;
                continue;
            }
            if (!recoverBackings && candidate.TryGetValue(pair.Key, out var current) && current.ResourceId == data.Id
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
            GpuRenderbufferMetadata? acquiredRenderbuffer = null;
            if (data.Kind == GpuResourceKind.Texture)
            {
                if (!textures.TryGetValue(data.Id, out var textureFacts))
                {
                    // Reuse one complete backing snapshot across distinct layers/faces of the same image.
                    var storageTarget = data.CubeFace != 0 ? TextureTarget.TextureCubeMap
                        : backing is GpuResourceMetadataEntry<GpuTextureMetadata> known ? known.Metadata.Target : 0;
                    textureFacts = GpuResourceManager.AcquireFramebufferTexture(checked((int)data.Id), storageTarget, recoverBackings);
                    textures.Add(data.Id, textureFacts);
                }
                acquiredTexture = textureFacts;
                target = data.CubeFace != 0 ? data.CubeFace : textureFacts.Target;
            }
            else
            {
                if (!renderbuffers.TryGetValue(data.Id, out var renderbufferFacts))
                {
                    renderbufferFacts = GpuResourceManager.AcquireFramebufferRenderbuffer(checked((int)data.Id), recoverBackings);
                    renderbuffers.Add(data.Id, renderbufferFacts);
                }
                acquiredRenderbuffer = renderbufferFacts;
            }
            bool range = target == TextureTarget.Texture2DArray && data.Mode != GpuFramebufferAttachmentMode.Ordinary
                && data.ViewCount > 0 && (data.Layered || data.Mode == GpuFramebufferAttachmentMode.Multiview);
            var image = GpuFramebufferAttachment.FromExternalImage(checked((int)data.Id), data.Kind == GpuResourceKind.Renderbuffer,
                target, data.MipLevel,
                !data.Layered && data.Mode != GpuFramebufferAttachmentMode.Multiview
                    && target is TextureTarget.Texture1DArray or TextureTarget.Texture2DArray
                    or TextureTarget.Texture3D or TextureTarget.TextureCubeMapArray or TextureTarget.Texture2DMultisampleArray ? data.Layer : null,
                acquiredTexture, range ? data.BaseView : 0, range ? data.ViewCount : 0, data.Mode, acquiredRenderbuffer);
            candidate[pair.Key] = image;
            resolved.Add(data, image);
        }
        // Commit retained references only after every projection succeeds. Failed discovery
        // leaves the previous helper set intact while the caller withdraws its availability.
        attachments.Clear();
        foreach (var pair in candidate)
        {
            attachments.Add(pair.Key, pair.Value);
            pair.Value.Observe(this);
        }
        reconciledAttachmentDescriptors = metadata.Attachments;
    }

    #endregion
}
