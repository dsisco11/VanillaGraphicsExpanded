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
        return RHI.RHICommandContext.Current.DiscoverFramebufferAttachment(this, slot, forceNative);
    }
    /// <summary>Reconciles retained image references with canonical descriptors without creating another native owner.</summary>
    internal void ReconcileAttachmentMetadata(bool allowUnpublishedExternal = false, bool recoverBackings = false)
    {
        if (!allowUnpublishedExternal && !ownsFramebuffer && !passMetadataPublished || attachmentsDirty || metadata.Attachments is null || !recoverBackings && ReferenceEquals(metadata.Attachments, reconciledAttachmentDescriptors)) return;
        // Unavailable descriptors must not reacquire a deleted attachment through a reused name.
        if (ResourceManager?.GetResourceRegistration(this) is { MetadataRevision: > 0, MetadataAvailable: false }) return;
        var candidate = RHI.RHICommandContext.Current.ResolveFramebufferAttachments(this, attachments, recoverBackings);
        InstallAttachmentReferences(candidate, metadata.Attachments);
    }

    /// <summary>Installs resolved domain references after their complete native descriptor set has been certified.</summary>
    internal void InstallAttachmentReferences(System.Collections.Generic.Dictionary<FramebufferAttachment, GpuFramebufferAttachment> candidate,
        ImmutableDictionary<FramebufferAttachment, GpuFramebufferImageMetadata> descriptors)
    {
        // Commit retained references only after every projection succeeds. Failed discovery
        // leaves the previous helper set intact while the caller withdraws its availability.
        attachments.Clear();
        foreach (var pair in candidate)
        {
            attachments.Add(pair.Key, pair.Value);
            pair.Value.Observe(this);
        }
        reconciledAttachmentDescriptors = descriptors;
    }

    #endregion
}
