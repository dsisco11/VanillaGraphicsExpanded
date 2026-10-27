using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Owns explicit imported framebuffer recovery, identity replacement and failure publication.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Discovers one image from reusable canonical descriptors without independently scanning native slots.</summary>
    internal static GpuFramebufferAttachment DiscoverFramebufferAttachment(GpuFramebuffer owner, FramebufferSlot slot, bool recover)
    {
        try
        {
            if (recover) RecoverWrapperMetadata(owner, GpuFramebufferMetadataReader.Instance);
            else if (!owner.HasNativeMetadata) AcquireWrapperMetadata(owner, GpuFramebufferMetadataReader.Instance);
            owner.ReconcileAttachmentMetadata(true, recover);
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
                // Keep this framebuffer unavailable while its complete descriptor/backing set
                // is resolved. Individual reads cannot expose an intermediate usable target.
                using var operation = new MetadataOperation<GpuFramebufferMetadata>(owner, GpuMetadataChange.Metadata);
                if (!GpuFramebufferMetadataReader.Instance.TryRead(owner.ResourceId, out var candidate))
                    throw new InvalidOperationException("Framebuffer metadata is unavailable.");
                operation.RequireLive();
                if (owner.ResourceManager?.GetResourceRegistration(owner) is GpuResourceMetadataEntry<GpuFramebufferMetadata> entry)
                    candidate = entry.Manager.ReconcileFramebufferAttachmentModes(entry, candidate);
                var images = ResolveFramebufferAttachments(owner,
                    new System.Collections.Generic.Dictionary<FramebufferSlot, GpuFramebufferAttachment>(owner.AttachmentImages), true, candidate);
                operation.RequireLive();
                operation.Commit(candidate);
                owner.InstallAttachmentReferences(images, owner.PublishedMetadata.Attachments!);
                available = true;
            }
            else InvalidateWrapperMetadata(owner);
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
