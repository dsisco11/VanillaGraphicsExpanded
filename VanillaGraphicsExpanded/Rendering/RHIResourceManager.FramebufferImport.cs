using VanillaGraphicsExpanded.Rendering.RHI;
using System;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Owns explicit imported framebuffer recovery, identity replacement and failure publication.</summary>
internal sealed partial class RHIResourceManager
{
    #region Internal API
    /// <summary>Discovers one image from reusable canonical descriptors without independently scanning native slots.</summary>
    internal static GpuFramebufferAttachment DiscoverFramebufferAttachment(GpuFramebuffer owner, FramebufferSlot slot)
    {
        try
        {
            if (!owner.HasNativeMetadata) AcquireWrapperMetadata(owner, GpuFramebufferMetadataReader.Instance);
            owner.ReconcileAttachmentMetadata(true, false);
        }
        catch { InvalidateWrapperMetadata(owner); throw; }
        foreach (var pair in owner.AttachmentImages) if (pair.Key == slot) return pair.Value;
        throw new InvalidOperationException("The requested framebuffer attachment is absent.");
    }
    /// <summary>Performs one explicit identity/discovery transaction and withdraws stale facts on every failure.</summary>
    internal static void PublishImportedFramebuffer(GpuFramebuffer owner, bool publish, int? replacementId, int width, int height)
    {
        bool replaces = owner.BeginExternalPublication(replacementId, width, height);
        bool available = false;
        bool succeeded = false;
        try
        {
            if (replaces) owner.ResourceManager?.ReplaceWrapperResource(owner);
            if (replaces)
            {
                owner.SetCreationIdentity(0);
                owner.SetCreationIdentity(replacementId!.Value, GpuResource.CaptureCreationContext(GpuResourceKind.Framebuffer));
            }
            owner.ResourceManager?.RegisterWrapperDeclaration(owner);
            if (publish)
            {
                // Association consumes canonical descriptors; only a recorded pre-install
                // identity can perform initial discovery before this publication transaction.
                var manager = owner.ResourceManager;
                var tracked = manager?.AcquireResource(GpuResourceKind.Framebuffer, owner.ResourceId, GpuFramebufferMetadataReader.Instance)
                    ?? throw new InvalidOperationException("Maintained framebuffer metadata is unavailable.");
                var candidate = tracked.Metadata;
                using var operation = new MetadataOperation<GpuFramebufferMetadata>(owner, GpuMetadataChange.Metadata);
                operation.RequireLive();
                if (owner.ResourceManager?.GetResourceRegistration(owner) is GpuResourceMetadataEntry<GpuFramebufferMetadata> entry)
                    candidate = entry.Manager.ReconcileFramebufferAttachmentModes(entry, candidate);
                var images = ResolveFramebufferAttachments(owner,
                    new System.Collections.Generic.Dictionary<FramebufferSlot, GpuFramebufferAttachment>(owner.AttachmentImages), false, candidate);
                operation.RequireLive();
                operation.Commit(candidate);
                owner.InstallAttachmentReferences(images, owner.PublishedMetadata.Attachments!);
                available = true;
            }
            // A borrower can defer pass composition without withdrawing the shared
            // framebuffer entry that its existing owner and other aliases still use.
            succeeded = true;
        }
        catch { InvalidateWrapperMetadata(owner); throw; }
        finally
        {
            // Preserve a discovery error while still informing every existing subscriber once.
            if (succeeded) owner.CompleteExternalPublication(available);
            else try { owner.CompleteExternalPublication(false); } catch { }
        }
    }
    #endregion
}
