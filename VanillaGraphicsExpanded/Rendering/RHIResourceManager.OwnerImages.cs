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
        List<Exception>? failures = null;
        foreach (var reference in images.ToArray())
        {
            if (!reference.TryGetTarget(out var image) || image.IsDisposed) continue;
            var manager = owner.ResourceManager;
            var entry = manager?.GetResourceRegistration(owner);
            if (entry is not null && ReferenceEquals(entry, manager!.GetResourceRegistration(image))) continue;
            try
            {
                if (available) PublishOwnerImage(owner, image, reportMissingImages);
                else InvalidateWrapperMetadata(image);
            }
            catch (Exception error)
            {
                InvalidateWrapperMetadata(image);
                (failures ??= new()).Add(error);
            }
            finally
            {
                try { image.NotifyDependents(); }
                catch (Exception error) { (failures ??= new()).Add(error); }
            }
        }
        if (available && failures is not null) throw new AggregateException("Image dependency publication failed.", failures);
    }
    #endregion
    #region Private
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
        if (candidate.HasValue) image.HandleMetadataChanged(candidate.Value);
        else if (reportMissingImage) throw new InvalidOperationException("The selected framebuffer image metadata is unavailable.");
    }
    #endregion
}
