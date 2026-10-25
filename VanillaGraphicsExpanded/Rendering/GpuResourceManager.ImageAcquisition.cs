using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Associates selected images with their backing entry and owns native image snapshot acquisition.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Acquires external texture facts once before constructing a selected image relationship.</summary>
    internal static GpuTextureMetadata AcquireExternalImageTexture(int id)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        StateCache.Current.RequireResourceOperationAvailable(GpuResourceKind.Texture, id);
        var manager = GpuResourceManagerSystem.CurrentManager;
        if (manager is not null && !manager.IsMetadataRegistryRetired)
        {
            var existing = manager.FindResource(GpuResourceKind.Texture, id) as GpuResourceMetadataEntry<GpuTextureMetadata>;
            if (existing?.Metadata.Target == TextureTarget.TextureBuffer)
                throw new NotSupportedException("Buffer textures cannot supply framebuffer images.");
            var reader = new GpuTextureMetadataReader(existing?.Metadata.Target ?? 0);
            return manager.AcquireResource(GpuResourceKind.Texture, id, reader)?.Metadata
                ?? throw new InvalidOperationException("Native texture image metadata is unavailable.");
        }
        if (!GpuTextureMetadataReader.Instance.TryRead(id, out var data))
            throw new InvalidOperationException("Native texture image metadata is unavailable.");
        return data;
    }

    /// <summary>Refreshes an image association after explicit owner replacement without letting imported IDs adopt reused objects.</summary>
    internal static void AcquireImageMetadata(GpuFramebufferAttachment image, GpuTextureMetadata? acquiredTexture = null,
        GpuRenderbufferMetadata? acquiredRenderbuffer = null)
    {
        if (image.IsDisposed || image.ResourceId == 0 || Integration.RenderContextRegistry.Current() == default) return;
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
                return;
            }
            // A raw alias does not itself invalidate covered canonical facts. Acquisition
            // reuses available entries and recovers only missing or explicitly withdrawn state.
            GpuResourceRegistration? entry = image.Kind == GpuResourceKind.Renderbuffer
                ? manager.AcquireResource(image.Kind, image.ResourceId, GpuRenderbufferMetadataReader.Instance)
                : manager.AcquireResource(image.Kind, image.ResourceId, new GpuTextureMetadataReader(image.ImageTarget));
            if (entry is null) throw new InvalidOperationException("The selected framebuffer image metadata is unavailable.");
            if (!entry.ContainsRecipient(image)) manager.AssociateImageResource(entry, image);
            return;
        }
        // Context-local tools have no persistent registry. The manager still owns projection
        // and snapshot assignment, so wrappers never synthesize dimensions or sample counts.
        // Withdraw the old projection before any read that can fail or reject this selection.
        DiscardUntrackedMetadata(image);
        StateCache.Current.RequireResourceOperationAvailable(image.Kind, image.ResourceId);
        // Typed local owners already hold command-established facts. Raw local IDs have no
        // such owner or registry guarantee and retain explicit native discovery.
        if (image.Resource is { } owner)
        {
            if (!owner.IsValid) throw new InvalidOperationException("The selected image owner has retired.");
            if (owner is GpuTexture textureOwner) acquiredTexture = textureOwner.MetadataSnapshot;
            else if (owner is GpuTextureView view) acquiredTexture = view.MetadataSnapshot;
            else if (owner is GpuRenderbuffer renderbufferOwner) acquiredRenderbuffer = renderbufferOwner.MetadataSnapshot;
        }
        GpuImageMetadata? projected = null;
        if (image.Kind == GpuResourceKind.Renderbuffer)
        {
            if (acquiredRenderbuffer is { } supplied) projected = ProjectRenderbufferImage(supplied, image.MetadataImageSelection);
            else if (GpuRenderbufferMetadataReader.Instance.TryRead(image.ResourceId, out var renderbuffer))
                projected = ProjectRenderbufferImage(renderbuffer, image.MetadataImageSelection);
        }
        else if (image.Kind == GpuResourceKind.Texture)
        {
            if (acquiredTexture is { } texture) projected = ProjectTextureImage(texture, image.MetadataImageSelection);
            else if (new GpuTextureMetadataReader(image.ImageTarget).TryRead(image.ResourceId, out texture))
                projected = ProjectTextureImage(texture, image.MetadataImageSelection);
        }
        if (!projected.HasValue)
            throw new InvalidOperationException("The selected framebuffer image metadata is unavailable.");
        image.HandleMetadataChanged(projected.Value);
    }
    #endregion
}
