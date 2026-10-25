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
            return manager.RecoverResource(GpuResourceKind.Texture, id, reader)?.Metadata
                ?? throw new InvalidOperationException("Native texture image metadata is unavailable.");
        }
        if (!GpuTextureMetadataReader.Instance.TryRead(id, out var data))
            throw new InvalidOperationException("Native texture image metadata is unavailable.");
        return data;
    }

    /// <summary>Refreshes an image association after explicit owner replacement without letting imported IDs adopt reused objects.</summary>
    internal static void AcquireImageMetadata(GpuFramebufferAttachment image, GpuTextureMetadata? acquiredTexture = null)
    {
        if (image.IsDisposed || image.ResourceId == 0 || Integration.RenderContextRegistry.Current() == default) return;
        if (image.ResourceManager is { } manager && manager.CanAcquireWrapperMetadata(image))
        {
            var original = manager.GetResourceRegistration(image);
            var backing = image.Resource is { } resource ? manager.GetResourceRegistration(resource) : null;
            if (original is not null && (original.Id != image.ResourceId
                || backing is not null && !ReferenceEquals(original, backing))) manager.ReplaceWrapperResource(image);
            else if (original is not null) return;
            var entry = manager.FindResource(image.Kind, image.ResourceId);
            // Explicit external acquisition refreshes native facts; retained managed images
            // consume their owner's already published entry instead of issuing another read.
            bool external = image.Resource is null;
            bool bufferTexture = entry is GpuResourceMetadataEntry<GpuTextureMetadata> textureEntry
                && textureEntry.Metadata.Target == OpenTK.Graphics.OpenGL.TextureTarget.TextureBuffer;
            if (entry is null || external && !bufferTexture && !acquiredTexture.HasValue)
                entry = image.Kind == GpuResourceKind.Renderbuffer
                    ? external ? manager.RecoverResource(image.Kind, image.ResourceId, GpuRenderbufferMetadataReader.Instance) : manager.AcquireResource(image.Kind, image.ResourceId, GpuRenderbufferMetadataReader.Instance)
                    : external ? manager.RecoverResource(image.Kind, image.ResourceId, new GpuTextureMetadataReader(image.ImageTarget))
                        : manager.AcquireResource(image.Kind, image.ResourceId, new GpuTextureMetadataReader(image.ImageTarget));
            if (entry is not null) manager.AssociateImageResource(entry, image);
            return;
        }
        // Context-local tools have no persistent registry. The manager still owns projection
        // and snapshot assignment, so wrappers never synthesize dimensions or sample counts.
        // Withdraw the old projection before any read that can fail or reject this selection.
        DiscardUntrackedMetadata(image);
        StateCache.Current.RequireResourceOperationAvailable(image.Kind, image.ResourceId);
        GpuImageMetadata? projected = null;
        if (image.Kind == GpuResourceKind.Renderbuffer && GpuRenderbufferMetadataReader.Instance.TryRead(image.ResourceId, out var renderbuffer))
            projected = ProjectRenderbufferImage(renderbuffer, image.MetadataImageSelection);
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
