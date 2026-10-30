using VanillaGraphicsExpanded.Rendering.RHI;
using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Associates selected images with their backing entry and owns native image snapshot acquisition.</summary>
internal sealed partial class RHIResourceManager
{
    #region Internal API
    /// <summary>Acquires external texture facts once before constructing a selected image relationship.</summary>
    internal static GpuTextureMetadata AcquireExternalImageTexture(int id)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        RHICommandContext.RequireResourceOperationAvailable(GpuResourceKind.Texture, id);
        var manager = RHIResourceManagerSystem.CurrentManager;
        if (manager is not null && !manager.IsMetadataRegistryRetired)
        {
            var existing = manager.FindResource(GpuResourceKind.Texture, id) as GpuResourceMetadataEntry<GpuTextureMetadata>;
            if (existing?.Metadata.Target == TextureDimension.Buffer)
                throw new NotSupportedException("Buffer textures cannot supply framebuffer images.");
            var reader = new GpuTextureMetadataReader(existing?.Metadata.Target ?? 0);
            return manager.AcquireResource(GpuResourceKind.Texture, id, reader)?.Metadata
                ?? throw new InvalidOperationException("Native texture image metadata is unavailable.");
        }
        throw new InvalidOperationException("Image borrowing requires maintained facts or explicit pre-install discovery.");
    }

    /// <summary>Refreshes an image association after explicit owner replacement without letting imported IDs adopt reused objects.</summary>
    internal static void AcquireImageMetadata(GpuFramebufferAttachment image)
    {
        if (image.IsDisposed || image.ResourceId == 0) return;
        if (image.ResourceManager is { } manager && manager.CanAcquireWrapperMetadata(image))
        {
            var original = manager.GetResourceRegistration(image);
            var backing = image.Resource is { } resource ? manager.GetResourceRegistration(resource) : null;
            if (original is not null && (original.Id != image.ResourceId
                || backing is not null && !ReferenceEquals(original, backing))) manager.ReplaceWrapperResource(image);
            else if (original is not null)
            {
                // Existing images receive publication through their original association. A
                // failure notification must not silently reacquire and revive withdrawn facts.
                if (!manager.IsResourceMetadataAvailable(image))
                {
                    image.AssignMetadataSnapshot(default);
                    throw new InvalidOperationException("The selected framebuffer image metadata is unavailable.");
                }
                return;
            }
            // A raw alias does not itself invalidate covered canonical facts. Acquisition
            // reuses available entries or consumes an already recorded initial-discovery cause.
            GpuResourceRegistration? entry = image.Kind == GpuResourceKind.Renderbuffer
                ? manager.AcquireResource(image.Kind, image.ResourceId, GpuRenderbufferMetadataReader.Instance)
                : manager.AcquireResource(image.Kind, image.ResourceId, new GpuTextureMetadataReader(image.ImageTarget));
            if (entry is null) throw new InvalidOperationException("The selected framebuffer image metadata is unavailable.");
            if (!entry.ContainsRecipient(image)) manager.AssociateImageResource(entry, image);
            return;
        }
        GpuTextureMetadata? acquiredTexture = null;
        GpuRenderbufferMetadata? acquiredRenderbuffer = null;
        // Context-local tools have no persistent registry. The manager still owns projection
        // and snapshot assignment, so wrappers never synthesize dimensions or sample counts.
        // Withdraw the old projection before checking the retained supplied snapshot.
        DiscardUntrackedMetadata(image);
        if (image.Resource is null) RHICommandContext.RequireResourceOperationAvailable(image.Kind, image.ResourceId);
        // Typed local owners already hold command-established facts. Raw local IDs have no
        // such owner or registry guarantee and cannot authorize native discovery.
        if (image.Resource is { } owner)
        {
            if (!owner.IsValid) throw new InvalidOperationException("The selected image owner has retired.");
            if (owner is GpuResource<GpuTextureMetadata> textureOwner) acquiredTexture = RequireTextureReferenceMetadata(textureOwner);
            else if (owner is GpuResource<GpuRenderbufferMetadata> renderbufferOwner
                && TryGetRenderbufferReferenceMetadata(renderbufferOwner, out var facts)) acquiredRenderbuffer = facts;
        }
        GpuImageMetadata? projected = null;
        if (image.Kind == GpuResourceKind.Renderbuffer)
        {
            if (acquiredRenderbuffer is { } supplied) projected = ProjectRenderbufferImage(supplied, image.MetadataImageSelection);

        }
        else if (image.Kind == GpuResourceKind.Texture)
        {
            if (acquiredTexture is { } texture) projected = ProjectTextureImage(texture, image.MetadataImageSelection);

        }
        if (!projected.HasValue)
            throw new InvalidOperationException("The selected framebuffer image metadata is unavailable.");
        image.AssignMetadataSnapshot(projected.Value);
    }
    #endregion
}
