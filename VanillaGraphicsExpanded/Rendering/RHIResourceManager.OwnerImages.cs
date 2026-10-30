using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Retains weak owner-image relationships for local tools and intentional owner replacement.</summary>
internal sealed partial class RHIResourceManager
{
    private static readonly ConditionalWeakTable<GpuResource, List<WeakReference<GpuFramebufferAttachment>>> ownerImages = new();
    #region Internal API
    /// <summary>Records a weak owner relationship; canonical entry recipients remain the sole tracked publication list.</summary>
    internal static void ObserveAttachmentOwner(GpuResource owner, GpuFramebufferAttachment image)
    {
        var images = ownerImages.GetOrCreateValue(owner);
        images.RemoveAll(reference => !reference.TryGetTarget(out _));
        foreach (var reference in images)
            if (reference.TryGetTarget(out var existing) && ReferenceEquals(existing, image)) return;
        images.Add(new(image));
    }
    /// <summary>Publishes local or replaced-owner projections, skipping images already delivered by the canonical entry.</summary>
    internal static void PublishOwnerImages(GpuResource owner, bool available, bool reportMissingImages = false)
    {
        if (!ownerImages.TryGetValue(owner, out var images)) return;
        var lifecycle = owner.ResourceManager;
        using var completion = lifecycle?.BeginChangeDelivery();
        var localDeliveries = new List<Action>();
        List<Exception>? failures = null;
        foreach (var reference in images.ToArray())
        {
            if (!reference.TryGetTarget(out var image) || image.IsDisposed) continue;
            var entry = lifecycle?.GetResourceRegistration(owner);
            if (entry is not null && ReferenceEquals(entry, lifecycle!.GetResourceRegistration(image))) continue;
            var previous = image.PublishedMetadata;
            bool previouslyAvailable = ImageSnapshotAvailable(image);
            try
            {
                if (available) PublishOwnerImage(owner, image, reportMissingImages);
                else DiscardUntrackedMetadata(image);
            }
            catch (Exception error)
            {
                DiscardUntrackedMetadata(image);
                (failures ??= new()).Add(error);
            }
            bool resultingAvailability = available && image.IsValid && ImageSnapshotAvailable(image);
            var reasons = previous != image.PublishedMetadata ? ResourceChangeKind.Metadata : ResourceChangeKind.None;
            if (previouslyAvailable != resultingAvailability) reasons |= ResourceChangeKind.Availability;
            if (reportMissingImages) reasons |= ResourceChangeKind.Storage;
            if (reasons == ResourceChangeKind.None) continue;
            var resultingEntry = lifecycle?.GetResourceRegistration(image);
            var change = new RHIResourceChange(reasons, resultingAvailability,
                resultingEntry?.MetadataRevision ?? 0, resultingEntry?.StorageRevision ?? 0);
            var delivery = CaptureOwnerImageDelivery(owner, image, change, lifecycle, resultingEntry);
            if (lifecycle is not null) lifecycle.DeliverResourceChange(delivery);
            else localDeliveries.Add(delivery);
        }
        // A local owner has no manager completion scope. Finish every projection before fanout.
        foreach (var delivery in localDeliveries)
            try { delivery(); } catch (Exception error) { (failures ??= new()).Add(error); }
        if (available && failures is not null) throw new AggregateException("Image dependency publication failed.", failures);
        completion?.Complete();
    }
    #endregion
    #region Private
    /// <summary>Uses original entry authority when present, otherwise the supplied local projection's presence.</summary>
    private static bool ImageSnapshotAvailable(GpuFramebufferAttachment image) => image.ResourceManager is { } manager
        && manager.GetResourceRegistration(image) is not null ? manager.IsResourceMetadataAvailable(image) : image.HasMetadataSnapshot;

    /// <summary>Rejects stale fallback fanout after replacement, disposal, reentry or context changes.</summary>
    private static Action CaptureOwnerImageDelivery(GpuResource owner, GpuFramebufferAttachment image,
        RHIResourceChange change, RHIResourceManager? manager, GpuResourceRegistration? imageEntry)
    {
        nint ownerId = owner.ResourceId, imageId = image.ResourceId;
        bool ownerLive = owner.IsValid;
        var ownerEntry = manager?.GetResourceRegistration(owner);
        ulong revision = ownerEntry?.MetadataRevision ?? 0;
        object? storage = owner is GpuResource<GpuTextureMetadata> texture ? TextureStorageIdentity(texture) : null;
        var snapshot = image.PublishedMetadata;
        return () =>
        {
            if (image.IsDisposed || owner.ResourceId != ownerId || image.ResourceId != imageId || owner.IsValid != ownerLive
                || !ReferenceEquals(manager, owner.ResourceManager) || !ReferenceEquals(ownerEntry, manager?.GetResourceRegistration(owner))
                || !ReferenceEquals(imageEntry, manager?.GetResourceRegistration(image))
                || ownerEntry is not null && ownerEntry.MetadataRevision != revision
                || imageEntry is not null && (imageEntry.MetadataRevision != change.MetadataRevision || imageEntry.StorageRevision != change.StorageRevision)
                || image.PublishedMetadata != snapshot || (image.IsValid && ImageSnapshotAvailable(image)) != change.Available
                || owner is GpuResource<GpuTextureMetadata> current && !ReferenceEquals(storage, TextureStorageIdentity(current))) return;
            // Existing owner affinity admission rejects a replacement context without a descriptor read.
            try { owner.RequireOriginalContext(); } catch { return; }
            image.OnResourceChanged(change);
        };
    }

    /// <summary>Projects known local owner facts without reading native state or masking unexpected acquisition failures.</summary>
    private static void PublishOwnerImage(GpuResource owner, GpuFramebufferAttachment image, bool reportMissingImage)
    {
        if (owner.ResourceManager is { } manager && manager.CanAcquireWrapperMetadata(image))
        {
            AcquireImageMetadata(image);
            return;
        }
        DiscardUntrackedMetadata(image);
        GpuImageMetadata? candidate = null;
        if (owner is GpuResource<GpuTextureMetadata> texture && TryGetTextureReferenceMetadata(texture, out var textureFacts))
            candidate = ProjectTextureImage(textureFacts, image.MetadataImageSelection);
        else if (owner is GpuResource<GpuRenderbufferMetadata> renderbuffer && TryGetRenderbufferReferenceMetadata(renderbuffer, out var renderbufferFacts))
            candidate = ProjectRenderbufferImage(renderbufferFacts, image.MetadataImageSelection);
        // Missing selections are expected after storage changes. They remain empty and
        // unusable; a projection miss does not invalidate the owner's complete result.
        if (candidate.HasValue) image.AssignMetadataSnapshot(candidate.Value);
        else if (reportMissingImage) throw new InvalidOperationException("The selected framebuffer image metadata is unavailable.");
    }
    #endregion
}
